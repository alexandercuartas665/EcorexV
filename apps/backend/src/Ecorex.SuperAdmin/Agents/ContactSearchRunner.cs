using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Scraping;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Resultado de ejecutar una busqueda de contactos configurada.</summary>
public sealed record ContactSearchRunResult(bool Ok, int Created, string? Error);

/// <summary>
/// Ejecuta una <see cref="ContactSearchDefinition"/>: arma la instruccion segun la fuente, resuelve el
/// proveedor de IA del agente elegido, dispara <see cref="IAiStepOrchestrator"/> (el agente IA maneja el
/// navegador Colmena) y redirige las filas extraidas a <see cref="ProspectoScrapeado"/> via un sink propio.
/// </summary>
public interface IContactSearchRunner
{
    Task<ContactSearchRunResult> RunAsync(Guid searchId, CancellationToken ct = default);
}

public sealed class ContactSearchRunner : IContactSearchRunner
{
    /// <summary>Tope de corridas por fuente y dia (defensa anti-baneo de las redes). Configurable aqui.</summary>
    /// <summary>Tope de corridas por dia y red SOCIAL (defensa anti-baneo de LinkedIn/Facebook/Instagram/X).
    /// Maps/Web NO tienen tope. Configurable aqui.</summary>
    private const int DailySocialCap = 20;

    /// <summary>Fuente propia de las APERTURAS de perfil del "perfil detallado" (etapa 2b). Tienen su PROPIO
    /// contador diario (<see cref="DailySocialCap"/>) para que no consuman el cupo de las BUSQUEDAS LinkedIn:
    /// una corrida con perfil detallado abre ~8 perfiles y, si se contaran como busquedas, agotaria el tope.</summary>
    private const string LinkedInProfileSource = "LinkedInPerfil";

    /// <summary>Fuentes sociales sujetas al tope diario (las que penalizan por exceso de scraping).</summary>
    private static bool IsSocial(ContactSearchSource s) => s
        is ContactSearchSource.LinkedIn or ContactSearchSource.Facebook
        or ContactSearchSource.Instagram or ContactSearchSource.X;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAiStepOrchestrator _orchestrator;
    private readonly IScrapeFetcher _fetcher;              // GET acotado con guardas SSRF (etapa 3, sitio propio).
    private readonly IAiProviderClient _aiClient;          // resumen de empresa a partir del HTML del sitio.
    private readonly IAiProviderResolver _aiResolver;      // resuelve/descifra el proveedor elegido.
    private readonly ILogger<ContactSearchRunner> _logger; // traza de la corrida (incl. [WEB-ENRICH]).

    public ContactSearchRunner(
        IApplicationDbContext db, ITenantContext tenant, IAiStepOrchestrator orchestrator,
        IScrapeFetcher fetcher, IAiProviderClient aiClient, IAiProviderResolver aiResolver,
        ILogger<ContactSearchRunner> logger)
    {
        _db = db;
        _tenant = tenant;
        _orchestrator = orchestrator;
        _fetcher = fetcher;
        _aiClient = aiClient;
        _aiResolver = aiResolver;
        _logger = logger;
    }

    // Acota el motivo de fallo para la columna Error/UI (los errores del orquestador son cortos; tope de
    // seguridad). Nunca null cuando se llama con ok=false, para que la fila siempre muestre un motivo.
    private static string? ClipError(string? e)
        => string.IsNullOrWhiteSpace(e) ? "(sin motivo)" : (e.Length > 500 ? e[..500] : e);

    public async Task<ContactSearchRunResult> RunAsync(Guid searchId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return new(false, 0, "Sin tenant activo."); }
        var def = await _db.ContactSearchDefinitions.FirstOrDefaultAsync(d => d.Id == searchId, ct);
        if (def is null) { return new(false, 0, "La busqueda no existe."); }
        if (string.IsNullOrWhiteSpace(def.ClientId)) { return new(false, 0, "Elige un agente Colmena en la busqueda."); }
        if (def.ClassifierAiAgentId is not Guid agentId) { return new(false, 0, "Elige un agente IA en la busqueda."); }
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null) { return new(false, 0, "El agente IA elegido ya no existe."); }
        var providerCfg = await _db.AiProviderConfigs.FirstOrDefaultAsync(c => c.Provider == agent.Provider && c.IsEnabled, ct);
        if (providerCfg is null)
        {
            return new(false, 0, $"El proveedor {agent.Provider} del agente no esta habilitado (Super Admin -> Servidores de IA).");
        }

        // TOPE DIARIO SOLO PARA REDES SOCIALES: max DailySocialCap corridas/dia de esa red y tenant (penalizan
        // por exceso). Maps/Web NO tienen tope. Se cuenta por RunAt >= inicio del dia UTC (huso del tenant queda
        // para despues); el filtro de tenant lo pone el query global.
        var source = def.SourceType.ToString();
        if (IsSocial(def.SourceType))
        {
            var startOfDayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var todayRuns = await _db.ContactSearchRuns
                .CountAsync(r => r.Source == source && r.RunAt >= startOfDayUtc, ct);
            if (todayRuns >= DailySocialCap)
            {
                return new(false, 0, $"Alcanzaste el tope diario de {DailySocialCap} busquedas de {source}. Intenta manana.");
            }
        }

        var cap = def.MaxContacts <= 0 ? int.MaxValue : def.MaxContacts;
        var frase = BuildPhrase(def);
        var instruction = BuildInstruction(def, agent);
        var sink = new ProspectoSearchRowSink(_db, tenantId, def.SourceType.ToString(), cap, frase);
        // TargetContainerId no se usa (el SinkOverride escribe en ProspectoScrapeado). MaxSteps/Segundos acotados.
        // ToolAllowList son las HERRAMIENTAS que se le ofrecen al agente (navegar/leer_html/esperar), NO
        // dominios: el gate de dominios lo aplica la colmena con su allow-list de la boveda (defensa en
        // profundidad, WebView2BrowserSubAgent.IsAllowed). Antes se pasaban dominios aqui -> el orquestador
        // no los reconocia como tools y solo ofrecia guardar_filas -> el agente NO abria el navegador y
        // "extraia" de su memoria. Con estas tools el agente navega de verdad (y su ventana se ve).
        var ctx = new AiStepContext(
            def.ClientId!, tenantId, instruction, Guid.Empty, BrowserToolsForSearch,
            MaxSteps: 25, MaxSeconds: 300, AiProviderId: providerCfg.Id, Secret: null, SinkOverride: sink,
            SessionKey: SessionKeyFor(def.SourceType),
            // Barrer TODO el listado: scroll largo en cada lectura (Maps carga perezoso). Con tope 0 (sin
            // limite) + scroll alto captura todo; por eso tambien se subio MaxSeconds (el scroll tarda mas).
            ExtractScrollRounds: 20);

        var outcome = await _orchestrator.RunAsync(ctx, ct);

        // Visibilidad (A): un fallo del orquestador deja TRAZA. Antes el motivo se descartaba y el fallo quedaba
        // mudo (ok=false, 0 filas, sin rastro) cuando, p.ej., el modelo respondia texto sin llamar una tool.
        if (!outcome.Ok)
        {
            _logger.LogWarning("[CONTACT-SEARCH] etapa={Etapa} ok=false motivo={Motivo}",
                source, outcome.Error ?? "(sin motivo)");
        }

        // Sella la ultima corrida (base del futuro programador automatico). def viene rastreado.
        def.LastRunAt = DateTimeOffset.UtcNow;
        // Registra la corrida para el tope diario por fuente (cuenta OK y fallidas: ambas tocaron la red). Si
        // fallo, se PERSISTE el motivo para mostrarlo en la UI.
        _db.ContactSearchRuns.Add(new ContactSearchRun
        {
            TenantId = tenantId,
            DefinitionId = def.Id,
            Source = source,
            RunAt = DateTimeOffset.UtcNow,
            Ok = outcome.Ok,
            Inserted = outcome.Inserted,
            Error = outcome.Ok ? null : ClipError(outcome.Error),
        });
        await _db.SaveChangesAsync(ct);

        var totalCreated = outcome.Inserted;

        // ETAPA 2 (enriquecimiento Maps -> LinkedIn): por cada EMPRESA encontrada en Maps, busca PERSONAS en
        // LinkedIn (sesion logueada) y las agrega ligadas a esa empresa. Cada empresa = una corrida LinkedIn,
        // sujeta al tope 20/dia; al alcanzarlo se corta (NO falla la corrida Maps). Cada empresa es su propio
        // AiStepContext acotado (no un run gigante) para no chocar con los topes del orquestador.
        // Personas creadas en LinkedIn (con su /in/) acumuladas entre empresas, para el perfil DETALLADO (etapa 2b).
        var detailTargets = new List<(Guid Id, string Name, string InUrl)>();
        if (def.EnrichLinkedIn && def.SourceType == ContactSearchSource.Maps && outcome.Ok)
        {
            var perCompany = def.EnrichMaxPorEmpresa <= 0 ? 5 : def.EnrichMaxPorEmpresa;
            foreach (var (empresaId, empresa) in sink.CreatedCompanies)
            {
                if (ct.IsCancellationRequested) { break; }
                // Tope diario LinkedIn: se recuenta antes de CADA empresa (cada una toca la red).
                var startOfDayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
                var liToday = await _db.ContactSearchRuns
                    .CountAsync(r => r.Source == "LinkedIn" && r.RunAt >= startOfDayUtc, ct);
                if (liToday >= DailySocialCap) { break; } // se agoto el cupo LinkedIn del dia.

                // Amarre FUERTE: cada persona de esta empresa lleva EmpresaProspectoId = Id del prospecto-empresa.
                var liSink = new ProspectoSearchRowSink(
                    _db, tenantId, "LinkedIn", perCompany, $"LinkedIn: {empresa}",
                    forcedEmpresa: empresa, forcedEmpresaProspectoId: empresaId);
                var liCtx = new AiStepContext(
                    def.ClientId!, tenantId, BuildLinkedInEnrichInstruction(agent, empresa, perCompany),
                    Guid.Empty, BrowserToolsForSearch,
                    MaxSteps: 20, MaxSeconds: 300, AiProviderId: providerCfg.Id, Secret: null,
                    SinkOverride: liSink, SessionKey: "linkedin",
                    // Igual que la busqueda: scroll largo para cargar mas personas de la empresa.
                    ExtractScrollRounds: 20);
                var liOutcome = await _orchestrator.RunAsync(liCtx, ct);
                _db.ContactSearchRuns.Add(new ContactSearchRun
                {
                    TenantId = tenantId,
                    DefinitionId = def.Id,
                    Source = "LinkedIn",
                    RunAt = DateTimeOffset.UtcNow,
                    Ok = liOutcome.Ok,
                    Inserted = liOutcome.Inserted,
                });
                await _db.SaveChangesAsync(ct);
                totalCreated += liOutcome.Inserted;
                // Junta las personas de esta empresa (con /in/) para el perfil detallado (etapa 2b).
                if (def.PerfilDetallado) { detailTargets.AddRange(liSink.CreatedPeople); }
            }
        }

        // ETAPA 2b (perfil LinkedIn DETALLADO, opt-in): por cada persona creada abre su /in/, lee el perfil y la
        // IA arma un resumen amplio (about + educacion + experiencia + headline) -> PerfilDetalle. Acotado por
        // PerfilDetalladoMax y con PAUSA entre perfiles (anti-baneo). No crea filas ni toca el amarre/dedup; no
        // falla la corrida si algo sale mal. Las aperturas de perfil tienen su PROPIO cupo diario
        // (LinkedInProfileSource), separado del de las busquedas LinkedIn (C): asi no agotan el tope de busqueda.
        if (def.PerfilDetallado && def.EnrichLinkedIn && def.SourceType == ContactSearchSource.Maps
            && outcome.Ok && detailTargets.Count > 0)
        {
            var detCap = def.PerfilDetalladoMax <= 0 ? 5 : def.PerfilDetalladoMax;
            var done = 0;
            foreach (var (personaId, personaNombre, inUrl) in detailTargets)
            {
                if (ct.IsCancellationRequested || done >= detCap) { break; }
                var startOfDayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
                var liToday = await _db.ContactSearchRuns
                    .CountAsync(r => r.Source == LinkedInProfileSource && r.RunAt >= startOfDayUtc, ct);
                if (liToday >= DailySocialCap) { break; } // cupo diario de PERFILES agotado (propio, no el de busqueda).

                if (done > 0)
                {
                    // Pausa entre perfiles (anti-baneo): navegar muchos /in/ seguidos es sospechoso.
                    try { await Task.Delay(TimeSpan.FromSeconds(4), ct); } catch (OperationCanceledException) { break; }
                }
                done++;
                var detSink = new ProspectoProfileDetailSink(_db, tenantId, personaId);
                var detCtx = new AiStepContext(
                    def.ClientId!, tenantId, BuildProfileDetailInstruction(agent, personaNombre, inUrl),
                    Guid.Empty, BrowserToolsForSearch,
                    MaxSteps: 12, MaxSeconds: 180, AiProviderId: providerCfg.Id, Secret: null,
                    SinkOverride: detSink, SessionKey: "linkedin",
                    // Un perfil no es un listado: pocas rondas de scroll bastan para cargar about/experiencia.
                    ExtractScrollRounds: 6);
                await _orchestrator.RunAsync(detCtx, ct);
                _db.ContactSearchRuns.Add(new ContactSearchRun
                {
                    TenantId = tenantId,
                    DefinitionId = def.Id,
                    Source = LinkedInProfileSource, // C: cupo propio, no consume el de busquedas LinkedIn.
                    RunAt = DateTimeOffset.UtcNow,
                    Ok = true,
                    Inserted = 0,
                });
                await _db.SaveChangesAsync(ct);
            }
        }

        // ETAPA 3 (enriquecimiento Maps -> sitio web/correo): la lista de Maps NO trae web ni correo. Por cada
        // EMPRESA sin sitio web, abre su ficha de Maps (OrigenUrl) para leer el sitio web y, si lo hay, intenta
        // el correo en el sitio; ACTUALIZA el prospecto (no crea filas). Opt-in porque suma N navegaciones.
        // Maps/Web no tienen tope diario; se acota por EnrichWebMax. No falla la corrida Maps si algo sale mal.
        if (def.EnrichWebCorreo && def.SourceType == ContactSearchSource.Maps && outcome.Ok)
        {
            var webCap = def.EnrichWebMax <= 0 ? 20 : def.EnrichWebMax;
            // Proveedor para el resumen de empresa (opcional): si no resuelve, se sigue con el correo (sin resumen).
            var (aiChoice, _) = await _aiResolver.ResolveAsync(providerCfg.Id, ct);
            var processed = 0;
            foreach (var target in sink.CreatedForWebEnrich)
            {
                if (ct.IsCancellationRequested || processed >= webCap) { break; }
                processed++;
                // 1) Si NO trae sitio web, la Colmena lee la ficha de Maps (dominio permitido) para el sitio_web.
                if (!target.HasWeb)
                {
                    var webSink = new ProspectoWebEnrichSink(_db, tenantId, target.Id);
                    var webCtx = new AiStepContext(
                        def.ClientId!, tenantId,
                        BuildWebEnrichInstruction(agent, target.Name, target.OrigenUrl, def.City, def.Region, def.Country),
                        Guid.Empty, BrowserToolsForSearch,
                        MaxSteps: 12, MaxSeconds: 180, AiProviderId: providerCfg.Id, Secret: null,
                        SinkOverride: webSink, SessionKey: null,
                        // La ficha de Maps no necesita scroll largo (no es un listado): pocas rondas bastan.
                        ExtractScrollRounds: 4);
                    await _orchestrator.RunAsync(webCtx, ct);
                }
                // 2) Fetch del SERVIDOR (no la Colmena) sobre el sitio propio, con guardas anti-SSRF, para el
                //    CORREO y un RESUMEN de la empresa. Respeta la allow-list de la Colmena por diseno (no la usa).
                await EnrichCompanyFromWebsiteAsync(target.Id, aiChoice, ct);
            }
        }

        return new(outcome.Ok, totalCreated, outcome.Ok ? null : outcome.Error);
    }

    /// <summary>
    /// Enriquecimiento por FETCH DEL SERVIDOR (no la Colmena): si el prospecto-empresa tiene sitio_web y le falta
    /// correo o perfil, hace un GET acotado (IScrapeFetcher, guardas SSRF: solo http/https, bloquea privadas/
    /// loopback/link-local/metadata, timeout + tope de bytes, redirecciones re-validadas) del sitio propio,
    /// extrae un correo (mailto: o pagina de Contacto) y arma con la IA un resumen corto de la empresa. Solo
    /// rellena lo que falta; nunca lanza (best-effort).
    /// </summary>
    private async Task EnrichCompanyFromWebsiteAsync(Guid prospectoId, AiProviderChoice? aiChoice, CancellationToken ct)
    {
        var p = await _db.ProspectosScrapeados.FirstOrDefaultAsync(x => x.Id == prospectoId, ct);
        if (p is null) { return; }

        var nombre = p.NombreCompleto;
        var sitio = p.SitioWeb?.Trim();
        // Sin sitio web no hay nada que visitar, pero se DEJA TRAZA para que no haya huecos silenciosos.
        if (string.IsNullOrWhiteSpace(sitio))
        {
            _logger.LogInformation("[WEB-ENRICH] empresa=\"{Empresa}\" sitio=\"\" correos=0 telefonos=0 (sin sitio web)", nombre);
            return;
        }

        var correos = new List<string>();
        var telefonos = new List<string>();
        string? error = null;
        try
        {
            var emails = new List<string>();
            var phones = new List<string>();

            // 1) Pagina principal.
            var res = await _fetcher.FetchAsync(sitio, ct);
            // Fallback (B): la URL de Maps varia por corrida y a veces es un SUBPATH que da 404 (o un dominio con
            // error SSL). Si falla y la URL tenia ruta, se reintenta UNA vez con el dominio RAIZ (https://host/)
            // antes de rendirse; el motivo se sigue logeando.
            if ((!res.Ok || string.IsNullOrWhiteSpace(res.Body)) && TryRootUrl(sitio) is string rootUrl)
            {
                var rootRes = await _fetcher.FetchAsync(rootUrl, ct);
                if (rootRes.Ok && !string.IsNullOrWhiteSpace(rootRes.Body))
                {
                    res = rootRes;
                    sitio = rootUrl; // lo encontrado viene del dominio raiz (afecta FindContactLink y web_contacts).
                }
            }
            if (!res.Ok || string.IsNullOrWhiteSpace(res.Body))
            {
                error = res.Ok ? "sitio vacio" : (res.Error ?? "fetch fallo");
            }
            else
            {
                var html = res.Body!;
                CollectContacts(html, emails, phones);

                // 2) Pagina de "Contacto/Contact" del MISMO sitio (si existe): suele concentrar correos/telefonos.
                //    El guard SSRF re-valida el destino en cada fetch.
                var contactUrl = FindContactLink(html, sitio);
                if (contactUrl is not null)
                {
                    var r2 = await _fetcher.FetchAsync(contactUrl, ct);
                    if (r2.Ok && !string.IsNullOrWhiteSpace(r2.Body)) { CollectContacts(r2.Body!, emails, phones); }
                }

                // Correos: dedup + PRIORIZA el dominio propio del sitio (los de terceros van al final).
                correos = RankEmailsByOwnDomain(DistinctKeep(emails), sitio);
                telefonos = DistinctKeep(phones);

                var changed = false;
                // 3) Primario SOLO si esta vacio: NO pisa lo que trajo Maps. Si Maps ya trajo telefono, el del
                //    sitio NO lo reemplaza (queda en la lista completa).
                if (string.IsNullOrWhiteSpace(p.Correo) && correos.Count > 0) { p.Correo = correos[0]; changed = true; }
                if (string.IsNullOrWhiteSpace(p.Telefono) && telefonos.Count > 0) { p.Telefono = telefonos[0]; changed = true; }

                // 4) Lista COMPLETA del sitio, consultable, sin pisar Maps: data_json.web_contacts.
                if (correos.Count > 0 || telefonos.Count > 0)
                {
                    p.DataJson = MergeWebContacts(p.DataJson, sitio, correos, telefonos);
                    changed = true;
                }

                // 5) Perfil (resumen de empresa) si falta.
                if (string.IsNullOrWhiteSpace(p.Perfil) && aiChoice is not null)
                {
                    var text = HtmlToText(html);
                    if (text.Length >= 80)
                    {
                        var resumen = await SummarizeCompanyAsync(aiChoice, p.NombreCompleto, text, ct);
                        if (!string.IsNullOrWhiteSpace(resumen))
                        {
                            var r = resumen!.Trim();
                            if (r.Length > 1000) { r = r[..1000]; }
                            p.Perfil = r;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    p.Badge = ProspectoSearchRowSink.ComputeBadge(p.Metrica, p.SitioWeb);
                    await _db.SaveChangesAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Best-effort: un sitio caido/raro no tumba la corrida, pero el motivo QUEDA EN LA TRAZA (no se traga).
            error = ex.GetType().Name + ": " + ex.Message;
        }

        // Visibilidad: SIEMPRE una linea por empresa con sitio (exito o fallo), para que no haya huecos silenciosos.
        if (error is null)
        {
            _logger.LogInformation(
                "[WEB-ENRICH] empresa=\"{Empresa}\" sitio=\"{Sitio}\" correos={Correos} telefonos={Telefonos}",
                nombre, sitio, correos.Count, telefonos.Count);
        }
        else
        {
            _logger.LogWarning(
                "[WEB-ENRICH] empresa=\"{Empresa}\" sitio=\"{Sitio}\" correos={Correos} telefonos={Telefonos} (error: {Error})",
                nombre, sitio, correos.Count, telefonos.Count, error);
        }
    }

    // Recolecta correos y telefonos plausibles de un HTML (pagina principal o de Contacto) hacia las listas.
    private static void CollectContacts(string html, List<string> emails, List<string> phones)
    {
        emails.AddRange(ExtractAllEmails(html));
        phones.AddRange(ExtractPhones(html));
    }

    // Distintos preservando el ORDEN de aparicion (case-insensitive).
    internal static List<string> DistinctKeep(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var it in items)
        {
            if (!string.IsNullOrWhiteSpace(it) && seen.Add(it)) { result.Add(it); }
        }
        return result;
    }

    // Mezcla web_contacts en el data_json SIN perder lo de Maps: data_json es el row scrapeado serializado; se
    // re-escribe con el objeto { sitio, correos[], telefonos[] } agregado/actualizado. Si no es un objeto JSON
    // valido, se arranca uno nuevo (nunca lanza).
    internal static string MergeWebContacts(string? dataJson, string sitio, IReadOnlyList<string> correos, IReadOnlyList<string> telefonos)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(dataJson)
                ? new JsonObject()
                : (JsonNode.Parse(dataJson) as JsonObject) ?? new JsonObject();
        }
        catch { root = new JsonObject(); }

        root["web_contacts"] = new JsonObject
        {
            ["sitio"] = sitio,
            ["correos"] = new JsonArray(correos.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["telefonos"] = new JsonArray(telefonos.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())
        };
        return root.ToJsonString();
    }

    // Correos desde HTML: TODOS los plausibles (mailto: + texto plano), en minuscula para dedup. Filtra
    // placeholders y correos de librerias/servicios comunes (no son el de la empresa). El dedup/orden lo hace
    // DistinctKeep aguas arriba.
    private static readonly Regex MailtoRx = new(@"mailto:([^""'?\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmailRx = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    // Descarta placeholders, correos de LIBRERIAS/INFRA de terceros (no son de la empresa) y buzones automaticos.
    private static readonly string[] EmailJunk =
        { "example.com", "domain.com", "email.com", "yourdomain", "@2x", ".png", ".jpg", ".gif", ".webp",
          // Infra / anti-bot / CDNs / servicios de terceros (su correo NO es el de la empresa).
          "sentry", "sentry.io", "wixpress", "radware", "cloudflare", "akamai", "googleapis", "gstatic",
          "cloudfront", "jsdelivr", "fontawesome", "schema.org", "w3.org",
          // Buzones automaticos (no sirven para contactar).
          "noreply", "no-reply", "donotreply", "mailer-daemon", "postmaster" };

    internal static IEnumerable<string> ExtractAllEmails(string html)
    {
        foreach (Match m in MailtoRx.Matches(html))
        {
            var e = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (IsPlausibleEmail(e)) { yield return e.ToLowerInvariant(); }
        }
        foreach (Match m in EmailRx.Matches(html))
        {
            var e = m.Value.Trim();
            if (IsPlausibleEmail(e)) { yield return e.ToLowerInvariant(); }
        }
    }

    internal static bool IsPlausibleEmail(string e)
    {
        if (e.Length is < 6 or > 120 || e.Count(c => c == '@') != 1) { return false; }
        var lower = e.ToLowerInvariant();
        return !EmailJunk.Any(j => lower.Contains(j));
    }

    // Prioriza los correos cuyo dominio coincide con el del SITIO de la empresa (p.ej. @colsanitas.com para
    // colsanitas.com); los de dominio AJENO (gmail, un proveedor, etc.) quedan al FINAL. Preserva el orden
    // relativo dentro de cada grupo. Si no se puede resolver el dominio del sitio, se deja la lista tal cual.
    internal static List<string> RankEmailsByOwnDomain(List<string> emails, string sitio)
    {
        var siteDom = RegistrableDomain(HostOf(sitio));
        if (string.IsNullOrEmpty(siteDom) || emails.Count < 2) { return emails; }
        var propios = new List<string>();
        var ajenos = new List<string>();
        foreach (var e in emails)
        {
            var at = e.LastIndexOf('@');
            var dom = at >= 0 && at + 1 < e.Length ? e[(at + 1)..] : string.Empty;
            if (RegistrableDomain(dom) == siteDom) { propios.Add(e); } else { ajenos.Add(e); }
        }
        propios.AddRange(ajenos);
        return propios;
    }

    // Dominio RAIZ (https://host[:port]/) de una URL, SOLO si difiere de la original (esta tenia ruta/query/puerto);
    // null si no parsea, no es http/https, o ya ERA la raiz. Para el fallback B (reintentar el sitio base).
    internal static string? TryRootUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)
            || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }
        var root = u.GetLeftPart(UriPartial.Authority) + "/";
        // Comparar contra la forma NORMALIZADA (AbsoluteUri), para que "https://x.com" (sin "/") cuente
        // como raiz y no dispare un reintento a la misma pagina.
        return string.Equals(root, u.AbsoluteUri, StringComparison.OrdinalIgnoreCase) ? null : root;
    }

    // Host de una URL (sin "www."), en minuscula. Vacio si no parsea.
    private static string HostOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) { return string.Empty; }
        var h = u.Host.ToLowerInvariant();
        return h.StartsWith("www.", StringComparison.Ordinal) ? h[4..] : h;
    }

    // Dominio "registrable" simple (dos ultimas etiquetas: colsanitas.com, empresa.co). Suficiente para comparar
    // el correo contra el sitio sin una lista de sufijos publicos.
    private static string RegistrableDomain(string host)
    {
        host = host.ToLowerInvariant().Trim();
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] + "." + parts[^1] : host;
    }

    // Telefonos desde HTML: enlaces tel: + patrones colombianos en el texto (celular 3XX XXX XXXX, fijo con
    // indicativo 60X XXXXXXX / (60X) XXX XXXX, con o sin +57). NormalizePhone valida y deja la forma nacional
    // (10 digitos); el dedup/orden lo hace DistinctKeep aguas arriba.
    private static readonly Regex TelHrefRx =
        new(@"tel:([+0-9().\s\-]{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PhoneTextRx = new(
        @"(?:\+?57[\s.\-]?)?(?:3\d{2}[\s.\-]?\d{3}[\s.\-]?\d{4}|\(?60\d\)?[\s.\-]?\d{3}[\s.\-]?\d{4})",
        RegexOptions.Compiled);

    internal static IEnumerable<string> ExtractPhones(string html)
    {
        foreach (Match m in TelHrefRx.Matches(html))
        {
            var n = NormalizePhone(m.Groups[1].Value);
            if (n is not null) { yield return n; }
        }
        foreach (Match m in PhoneTextRx.Matches(html))
        {
            var n = NormalizePhone(m.Value);
            if (n is not null) { yield return n; }
        }
    }

    // Normaliza a la forma NACIONAL en digitos: quita el indicativo pais 57 si viene; valida que sea un numero
    // colombiano razonable (movil 10 dig que empieza por 3, fijo nuevo 10 dig que empieza por 60, o fijo viejo
    // de 7 dig venido de un tel:). Devuelve null si no calza (descarta falsos positivos por longitud).
    internal static string? NormalizePhone(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("57", StringComparison.Ordinal)) { digits = digits[2..]; }
        if (digits.Length == 10 && (digits[0] == '3' || digits.StartsWith("60", StringComparison.Ordinal))) { return digits; }
        if (digits.Length == 7) { return digits; } // fijo antiguo (normalmente de un enlace tel:)
        return null;
    }

    // Busca en el HTML un enlace a una pagina de "Contacto/Contact" del MISMO host (para reintentar el correo).
    private static readonly Regex HrefRx = new(@"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? FindContactLink(string html, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) { return null; }
        foreach (Match m in HrefRx.Matches(html))
        {
            var href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (href.Length == 0 || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith('#')) { continue; }
            var low = href.ToLowerInvariant();
            if (!low.Contains("contact") && !low.Contains("contacto")) { continue; }
            if (!Uri.TryCreate(baseUri, href, out var abs)) { continue; }
            if (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps) { continue; }
            if (!string.Equals(abs.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) { continue; } // mismo host
            return abs.AbsoluteUri;
        }
        return null;
    }

    // HTML -> texto legible: quita script/style, tags y colapsa espacios; recorta a un tope para el prompt.
    private static readonly Regex ScriptStyleRx = new(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagRx = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WsRx = new(@"\s+", RegexOptions.Compiled);

    private static string HtmlToText(string html)
    {
        var s = ScriptStyleRx.Replace(html, " ");
        s = TagRx.Replace(s, " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = WsRx.Replace(s, " ").Trim();
        return s.Length > 6000 ? s[..6000] : s;
    }

    private async Task<string?> SummarizeCompanyAsync(AiProviderChoice choice, string empresa, string siteText, CancellationToken ct)
    {
        const string system =
            "Eres un asistente que resume la actividad de una empresa a partir del texto de su sitio web. "
            + "Responde en espanol con UNA o DOS frases (max 60 palabras): que hace la empresa, su rubro y a quien "
            + "sirve. No inventes datos que no esten en el texto. No incluyas URLs, telefonos ni saludos.";
        var prompt = $"Empresa: {empresa}\n\nTexto del sitio web:\n{siteText}\n\nResumen (1-2 frases):";
        var turns = new List<AiChatTurn> { new("user", prompt) };
        try
        {
            var r = await _aiClient.CompleteAsync(choice.Provider, choice.ApiKey, choice.BaseUrl, choice.Model, system, turns, ct);
            return r.Ok ? r.Text?.Trim() : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>Frase efectiva de la busqueda (terminos + geografia), p.ej. "centros medicos Bogota
    /// Colombia". Se sella en cada prospecto (FraseBusqueda) para trazar el origen.</summary>
    private static string? BuildPhrase(ContactSearchDefinition d)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Query)) { parts.Add(d.Query!.Trim()); }
        if (!string.IsNullOrWhiteSpace(d.City)) { parts.Add(d.City!.Trim()); }
        if (!string.IsNullOrWhiteSpace(d.Region)) { parts.Add(d.Region!.Trim()); }
        if (!string.IsNullOrWhiteSpace(d.Country)) { parts.Add(d.Country!.Trim()); }
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>Instruccion de la etapa 2: buscar PERSONAS de una EMPRESA en LinkedIn (logueado).</summary>
    private static string BuildLinkedInEnrichInstruction(AiAgent agent, string empresa, int max)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt)) { sb.AppendLine(agent.SystemPrompt).AppendLine(); }
        var url = $"https://www.linkedin.com/search/results/people/?keywords={Uri.EscapeDataString(empresa)}";
        sb.AppendLine($"Estas logueado en LinkedIn. Busca PERSONAS que trabajen en la empresa \"{empresa}\".");
        sb.AppendLine($"NAVEGA directamente a {url} (NO uses Google ni site:linkedin.com). Haz scroll para cargar mas resultados.");
        sb.AppendLine($"Captura como maximo {max} personas y detente al llegar a ese numero.");
        sb.AppendLine("El contenido trae PERSONAS con enlaces de perfil (/in/). Guarda UNA fila por persona con "
            + "nombre, cargo (el headline tras el nombre), url = la URL del perfil (/in/...) y perfil = un resumen "
            + "de 1-2 frases del headline/about de la persona (que hace, area). No inventes telefono ni correo si "
            + "no aparecen.");
        sb.Append("Cuando tengas los resultados, llama a 'guardar_filas' con un arreglo de objetos con las claves "
            + "nombre, cargo, url (el perfil /in/) y perfil (el resumen).");
        return sb.ToString();
    }

    /// <summary>Instruccion de la etapa 3: abrir la ficha de Maps de UNA empresa y sacar sitio web (y correo
    /// si el sitio lo expone). Devuelve UNA sola fila con sitio_web y correo.</summary>
    private static string BuildWebEnrichInstruction(
        AiAgent agent, string empresa, string? origenUrl, string? city, string? region, string? country)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt)) { sb.AppendLine(agent.SystemPrompt).AppendLine(); }
        sb.AppendLine($"Busca el SITIO WEB y el CORREO de la empresa \"{empresa}\".");
        if (!string.IsNullOrWhiteSpace(origenUrl))
        {
            sb.AppendLine($"NAVEGA a la ficha de Google Maps de la empresa: {origenUrl}");
        }
        else
        {
            var geo = new[] { city, region, country }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim());
            var q = string.Join(" ", new[] { empresa }.Concat(geo));
            var url = $"https://www.google.com/maps/search/{Uri.EscapeDataString(q)}";
            sb.AppendLine($"NAVEGA a {url} y abre la ficha del primer resultado que coincida con la empresa.");
        }
        sb.AppendLine("En la ficha, busca el boton/enlace 'Sitio web' (Website) y toma su URL http/https = sitio_web. "
            + "Si NO hay sitio web, deja sitio_web vacio.");
        sb.AppendLine("Si la ficha MISMA muestra un correo, tomalo = correo; NO navegues fuera de Google Maps para "
            + "buscarlo (del sitio propio se encarga otro paso). Si no aparece, deja correo vacio. NO inventes datos.");
        sb.Append("Cuando termines, llama a 'guardar_filas' con UN solo objeto con las claves sitio_web y correo "
            + "(vacios si no los hallaste).");
        return sb.ToString();
    }

    /// <summary>Instruccion del perfil DETALLADO (opt-in): abrir el /in/ de una persona y resumir su perfil
    /// completo (about + educacion + experiencia + headline) en UNA fila con la clave perfil_detalle.</summary>
    private static string BuildProfileDetailInstruction(AiAgent agent, string persona, string inUrl)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt)) { sb.AppendLine(agent.SystemPrompt).AppendLine(); }
        sb.AppendLine($"Estas logueado en LinkedIn. NAVEGA al perfil de {persona}: {inUrl}");
        sb.AppendLine("Lee el perfil con leer_html. Arma un RESUMEN AMPLIO (varias frases, en espanol) que incluya, "
            + "si aparecen: el titular (headline), la seccion 'Acerca de' (about), la EDUCACION (estudios/instituciones) "
            + "y la EXPERIENCIA (empresas y cargos previos, del mas reciente al mas antiguo). No inventes: si algo no "
            + "aparece, omitelo.");
        sb.Append("Cuando termines, llama a 'guardar_filas' con UN solo objeto con la clave perfil_detalle = ese resumen.");
        return sb.ToString();
    }

    private static string BuildInstruction(ContactSearchDefinition d, AiAgent agent)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt)) { sb.AppendLine(agent.SystemPrompt).AppendLine(); }
        sb.Append("Busca contactos de negocio en ");
        sb.Append(d.SourceType switch
        {
            ContactSearchSource.Maps => "Google Maps (https://www.google.com/maps)",
            ContactSearchSource.LinkedIn => "LinkedIn",
            ContactSearchSource.Web => "un buscador web / directorios",
            ContactSearchSource.Instagram => "Instagram",
            ContactSearchSource.Facebook => "Facebook",
            ContactSearchSource.X => "X (Twitter)",
            _ => "la fuente indicada"
        });
        sb.Append(". ");
        if (!string.IsNullOrWhiteSpace(d.Query)) { sb.Append($"Terminos de busqueda: {d.Query}. "); }
        var geo = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.City)) { geo.Add(d.City!); }
        if (!string.IsNullOrWhiteSpace(d.Region)) { geo.Add(d.Region!); }
        if (!string.IsNullOrWhiteSpace(d.Country)) { geo.Add(d.Country!); }
        if (geo.Count > 0) { sb.Append($"Ubicacion: {string.Join(", ", geo)}. "); }
        if (!string.IsNullOrWhiteSpace(d.SubQuery)) { sb.Append($"Para cada resultado, ademas: {d.SubQuery}. "); }
        if (d.MaxContacts > 0) { sb.Append($"Captura como maximo {d.MaxContacts} contactos y detente al llegar a ese numero. "); }
        sb.AppendLine();
        sb.AppendLine(d.ExtractionPrompt);
        sb.AppendLine();
        // LinkedIn logueado: navegar DIRECTO a la busqueda de PERSONAS (no Google). keywords = terminos + ubicacion.
        var lkTerms = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Query)) { lkTerms.Add(d.Query!); }
        lkTerms.AddRange(geo);
        var lkUrl = $"https://www.linkedin.com/search/results/people/?keywords={Uri.EscapeDataString(string.Join(" ", lkTerms))}";

        // Guia especifica por fuente: LinkedIn descubre PERSONAS (una fila c/u); FB/IG/Maps es UN negocio (una fila).
        var guidance = d.SourceType switch
        {
            ContactSearchSource.Maps =>
                "Haz scroll hasta el fondo cargando TODOS los resultados posibles del listado antes de extraer; no "
                + "te detengas en los primeros. Cada resultado de Google Maps es un NEGOCIO (una empresa) = el lead. "
                + "Guarda UNA sola fila por negocio con nombre = el NOMBRE DEL NEGOCIO (es la empresa; puedes repetirlo "
                + "en 'empresa'). NO inventes una persona: NO crees un segundo registro ni un 'Contacto de <negocio>', y "
                + "NO rellenes cargo/nombre de persona (Maps no trae personas). Las PERSONAS salen unicamente del "
                + "enriquecimiento en LinkedIn. Captura direccion, telefono, sitio web, metrica (estrellas/resenas), "
                + "imagen y perfil (una descripcion corta de que hace el negocio / su rubro) si aparecen.",
            ContactSearchSource.LinkedIn =>
                $"Estas logueado en LinkedIn. NAVEGA directamente a {lkUrl} (NO uses Google ni site:linkedin.com). "
                + "Haz scroll para cargar mas resultados. El contenido trae PERSONAS en 'PERSONAS DETECTADAS' y enlaces "
                + "de perfil (/in/): guarda UNA fila por persona con nombre, cargo (el headline tras el nombre), "
                + "url = la URL del perfil (/in/...) y perfil = un resumen de 1-2 frases del headline/about. No inventes "
                + "telefono ni correo si no aparecen.",
            ContactSearchSource.Facebook or ContactSearchSource.Instagram =>
                "Es la pagina/perfil de UN negocio (no una lista). Guarda UNA sola fila con: nombre del negocio, empresa, "
                + "sitio web y seguidores en 'metrica', y url = la URL del perfil/pagina. Ignora el texto de los posts "
                + "(suele venir con caracteres basura anti-scraping); la firmografia de la cabecera si es confiable.",
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(guidance)) { sb.AppendLine(guidance).AppendLine(); }
        sb.Append("Cuando tengas los resultados, llama a 'guardar_filas' con un arreglo de objetos usando claves como ");
        sb.Append("nombre, empresa, cargo, telefono, correo, ciudad, metrica, ");
        sb.Append("direccion (direccion completa del negocio, de la ficha del lugar), ");
        sb.Append("sitio_web (URL del sitio web PROPIO del negocio, si aparece el enlace 'Sitio web'), ");
        sb.Append("imagen_url (URL http de la foto o logo del negocio, si aparece), ");
        sb.Append("url (URL de la ficha o pagina donde encontraste el contacto -- guardala siempre que la tengas) y ");
        sb.Append("perfil (resumen de 1-2 frases: si es una PERSONA, del headline/about; si es un NEGOCIO, que hace/rubro). ");
        sb.Append("Guarda solo contactos reales con al menos un nombre.");
        return sb.ToString();
    }

    // Herramientas de navegador que se le ofrecen al agente en TODA busqueda de contactos: navegar + leer_html
    // + esperar (solo lectura; sin evaluar_js/clic, que no hacen falta para barrer un listado). guardar_filas
    // (la salida) siempre esta. Son CLAVES DE HERRAMIENTA, no dominios: el gate de dominios lo aplica la colmena
    // con su allow-list de la boveda. internal para el test de regresion (evita re-introducir el bug de dominios).
    internal static readonly string[] BrowserToolsForSearch = { "navigate", "html", "wait" };

    // Clave de PERFIL persistente por fuente (scraping LOGUEADO). Solo las redes con "modo login" en la
    // Colmena tienen perfil; Maps/Web/X van efimeros (null) porque no requieren -ni tienen- login guardado.
    private static string? SessionKeyFor(ContactSearchSource s) => s switch
    {
        ContactSearchSource.LinkedIn => "linkedin",
        ContactSearchSource.Facebook => "facebook",
        ContactSearchSource.Instagram => "instagram",
        _ => null,
    };
}

/// <summary>
/// Sumidero de filas (IScrapeRowSink) que aterriza cada resultado extraido como
/// <see cref="ProspectoScrapeado"/> (Bolsa del Directorio, perfil Sospechoso al calificar). Se construye
/// por corrida con la fuente; ignora containerId/mapping (no hay DataContainer aqui).
/// </summary>
public sealed class ProspectoSearchRowSink : IScrapeRowSink
{
    private readonly IApplicationDbContext _db;
    private readonly Guid _tenantId;
    private readonly string _fuente;
    private readonly int _cap;
    private readonly string? _frase;         // frase efectiva de la busqueda (se sella en cada prospecto).
    private readonly string? _forcedEmpresa; // enriquecimiento LinkedIn: fuerza la empresa de cada persona.
    private readonly Guid? _forcedEmpresaProspectoId; // amarre FUERTE (self-FK) a la empresa-prospecto.
    private int _total; // acumulado entre llamadas de IngestAsync de la misma corrida.
    private readonly List<ProspectoScrapeado> _created = new(); // entidades creadas (para el enriquecimiento por empresa).

    /// <summary>Empresas (negocios) creadas en esta corrida: (Id del prospecto, Nombre), distintas por
    /// nombre. Base del enriquecimiento Maps -> LinkedIn: por cada empresa se busca personal en LinkedIn y
    /// las personas se amarran por FK (EmpresaProspectoId) al Id de la empresa. Leer DESPUES del run (los
    /// Id ya estan poblados por SaveChanges).</summary>
    public IReadOnlyList<(Guid Id, string Name)> CreatedCompanies =>
        _created.Where(e => !string.IsNullOrWhiteSpace(e.NombreCompleto))
            .GroupBy(e => e.NombreCompleto, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().Id, g.Key))
            .ToList();

    /// <summary>Empresas creadas para el enriquecimiento Maps -> sitio web/correo: (Id, Nombre, ficha de
    /// Maps, si ya trae sitio web). El runner abre la ficha (OrigenUrl) de las que NO tienen web y actualiza
    /// el prospecto con sitio web/correo. Leer DESPUES del run (Ids ya poblados por SaveChanges).</summary>
    public IReadOnlyList<(Guid Id, string Name, string? OrigenUrl, bool HasWeb)> CreatedForWebEnrich =>
        _created.Where(e => !string.IsNullOrWhiteSpace(e.NombreCompleto))
            .Select(e => (e.Id, e.NombreCompleto, e.OrigenUrl, !string.IsNullOrWhiteSpace(e.SitioWeb)))
            .ToList();

    /// <summary>Personas creadas en esta corrida con URL de perfil (/in/): (Id, Nombre, URL del perfil).
    /// Base del perfil DETALLADO (opt-in): el runner abre cada /in/ y resume about/educacion/experiencia.
    /// Leer DESPUES del run (Ids ya poblados por SaveChanges).</summary>
    public IReadOnlyList<(Guid Id, string Name, string InUrl)> CreatedPeople =>
        _created.Where(e => !string.IsNullOrWhiteSpace(e.NombreCompleto)
                            && !string.IsNullOrWhiteSpace(e.OrigenUrl)
                            && e.OrigenUrl!.Contains("/in/", StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.Id, e.NombreCompleto, e.OrigenUrl!))
            .ToList();

    public ProspectoSearchRowSink(IApplicationDbContext db, Guid tenantId, string fuente,
        int cap = int.MaxValue, string? frase = null, string? forcedEmpresa = null,
        Guid? forcedEmpresaProspectoId = null)
    {
        _db = db;
        _tenantId = tenantId;
        _fuente = fuente;
        _cap = cap <= 0 ? int.MaxValue : cap;
        _frase = string.IsNullOrWhiteSpace(frase) ? null : frase.Trim();
        _forcedEmpresa = string.IsNullOrWhiteSpace(forcedEmpresa) ? null : forcedEmpresa.Trim();
        _forcedEmpresaProspectoId = forcedEmpresaProspectoId;
    }

    public async Task<(int Inserted, int Updated, int Deleted)> IngestAsync(
        Guid containerId, Guid tenantId, string? mappingJson,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct = default)
    {
        var ins = 0;
        foreach (var row in rows)
        {
            if (_total >= _cap) { break; } // respeta el limite de contactos de la busqueda.
            var nombre = Pick(row, "nombre", "name", "nombre_completo", "negocio", "empresa", "company", "razon_social");
            if (string.IsNullOrWhiteSpace(nombre)) { continue; }
            var nombreTrim = nombre.Trim();
            // Enriquecimiento LinkedIn: la empresa la fuerza el runner (la persona pertenece a esa empresa);
            // si no, se toma de la fila (Maps/Web).
            var empresa = _forcedEmpresa ?? Pick(row, "empresa", "company", "negocio");
            var empresaKey = (empresa ?? string.Empty).Trim().ToLowerInvariant();
            var nombreKey = nombreTrim.ToLowerInvariant();
            // DE-DUP por (nombre, empresa): evita duplicados por re-run o corridas parciales (ej. la misma
            // clinica de Maps dos veces, o la misma persona de una empresa). Un mismo nombre en OTRA empresa
            // no se descarta. Se revisa lo creado en ESTA corrida y lo ya existente en la BD del tenant.
            if (_created.Any(e => string.Equals(e.NombreCompleto, nombreTrim, StringComparison.OrdinalIgnoreCase)
                    && string.Equals((e.Empresa ?? string.Empty).Trim(), (empresa ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (await _db.ProspectosScrapeados
                .AnyAsync(p => p.NombreCompleto.ToLower() == nombreKey
                    && (p.Empresa ?? "").ToLower() == empresaKey, ct))
            {
                continue;
            }
            // Metrica (estrellas/resenas) y sitio web se extraen una vez: alimentan sus columnas Y el Badge.
            var metrica = Pick(row, "metrica", "rating", "resenas", "reviews", "estrellas", "seguidores", "conexiones");
            var sitioWeb = SafeHttpUrl(Pick(row, "sitio_web", "website", "web", "sitio", "pagina", "url_web"));
            var entity = new ProspectoScrapeado
            {
                TenantId = _tenantId,
                Fuente = _fuente,
                NombreCompleto = nombre.Trim(),
                Cargo = Pick(row, "cargo", "title", "puesto", "rol"),
                // Resumen del perfil (1-2 frases): persona LinkedIn -> headline/about; empresa Maps -> que hace.
                Perfil = Pick(row, "perfil", "resumen", "about", "descripcion", "headline", "bio"),
                Empresa = empresa,
                Ciudad = Pick(row, "ciudad", "city", "localidad", "municipio"),
                Telefono = Pick(row, "telefono", "tel", "phone", "celular", "movil"),
                Correo = Pick(row, "correo", "email", "mail", "e-mail"),
                Direccion = Pick(row, "direccion", "address", "dir", "ubicacion"),
                Metrica = metrica,
                // Etiqueta de lead calculada de los datos ya extraidos (no depende de que el modelo la ponga).
                Badge = ComputeBadge(metrica, sitioWeb),
                // Solo http/https: no se persisten (ni luego se renderizan) URLs javascript:/data: del scraping.
                ImagenUrl = SafeHttpUrl(Pick(row, "imagen_url", "imagen", "foto", "image", "photo", "avatar", "logo")),
                // Sitio web PROPIO del negocio (distinto de OrigenUrl = ficha en Maps).
                SitioWeb = sitioWeb,
                // "perfil" YA NO va aqui: ahora es el resumen del perfil (columna Perfil), no la URL.
                OrigenUrl = SafeHttpUrl(Pick(row, "url", "origen", "enlace", "link", "source_url", "fuente_url")),
                // Frase efectiva con que se encontro (o "LinkedIn: <empresa>" en el enriquecimiento).
                FraseBusqueda = _frase,
                // Amarre FUERTE (self-FK) a la empresa-prospecto en el enriquecimiento LinkedIn.
                EmpresaProspectoId = _forcedEmpresaProspectoId,
                DataJson = JsonSerializer.Serialize(row),
                FechaCaptura = DateTimeOffset.UtcNow,
            };
            _db.ProspectosScrapeados.Add(entity);
            // La empresa creada (Id + nombre) alimenta el enriquecimiento por empresa (etapa 2).
            _created.Add(entity);
            ins++;
            _total++;
        }
        if (ins > 0) { await _db.SaveChangesAsync(ct); }
        return (ins, 0, 0);
    }

    /// <summary>
    /// Etiqueta de lead (Badge) calculada de forma DETERMINISTA con los datos ya extraidos (metrica + sitio
    /// web), sin depender de que el modelo la ponga (rubro acordado con el usuario):
    ///   Hot = rating &gt;= 4.5 y resenas &gt;= 20; Calificado = tiene sitio web o rating &gt;= 4.0; Nuevo = el resto.
    /// </summary>
    internal static string ComputeBadge(string? metrica, string? sitioWeb)
    {
        var (rating, reviews) = ParseMetrica(metrica);
        if (rating >= 4.5 && reviews >= 20) { return "Hot"; }
        if (!string.IsNullOrWhiteSpace(sitioWeb) || rating >= 4.0) { return "Calificado"; }
        return "Nuevo";
    }

    /// <summary>Extrae (rating, resenas) de la metrica textual de Maps. Formatos: "4.7 (12 opiniones)",
    /// "3.0(6)", "4.9 (6,690 opiniones)", "Sin opiniones", "No hay opiniones". El primer decimal es el rating;
    /// el numero entre parentesis son las resenas (se le quitan separadores de miles).</summary>
    private static (double Rating, int Reviews) ParseMetrica(string? m)
    {
        if (string.IsNullOrWhiteSpace(m)) { return (0, 0); }
        double rating = 0; int reviews = 0;
        var r = Regex.Match(m, @"(\d+([.,]\d+)?)");            // primer decimal = rating
        if (r.Success)
        {
            double.TryParse(r.Groups[1].Value.Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out rating);
        }
        var v = Regex.Match(m, @"\(([\d.,]+)");                // numero entre parentesis = resenas
        if (v.Success)
        {
            int.TryParse(v.Groups[1].Value.Replace(".", "").Replace(",", ""), out reviews);
        }
        return (rating, reviews);
    }

    internal static string? Pick(IReadOnlyDictionary<string, string?> row, params string[] keys)
    {
        foreach (var k in keys)
        {
            foreach (var kv in row)
            {
                if (string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                {
                    return kv.Value;
                }
            }
        }
        return null;
    }

    /// <summary>Devuelve la URL SOLO si es http/https absoluta; si no, null. Control de seguridad: los
    /// datos scrapeados NO deben aterrizar URLs javascript:/data:/relativas que luego se rendericen como
    /// imagen o enlace en la Bolsa.</summary>
    internal static string? SafeHttpUrl(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { return null; }
        return Uri.TryCreate(v, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? v : null;
    }
}

/// <summary>
/// Sumidero de UNA fila para el enriquecimiento Maps -> sitio web/correo (etapa 3): en vez de crear un
/// prospecto, ACTUALIZA el prospecto-empresa indicado (<see cref="_prospectoId"/>) con el sitio web y/o
/// correo que el agente saco de la ficha de Maps (y del sitio, si lo hay). Solo escribe lo que falta (no
/// pisa un dato ya presente) y recalcula el Badge. Tenant-safe: carga el prospecto por id bajo el filtro
/// global (solo alcanza el del tenant activo).
/// </summary>
public sealed class ProspectoWebEnrichSink : IScrapeRowSink
{
    private readonly IApplicationDbContext _db;
    private readonly Guid _tenantId;
    private readonly Guid _prospectoId;
    private bool _done; // una sola actualizacion por corrida de ficha (la primera fila con datos).

    public ProspectoWebEnrichSink(IApplicationDbContext db, Guid tenantId, Guid prospectoId)
    {
        _db = db;
        _tenantId = tenantId;
        _prospectoId = prospectoId;
    }

    public async Task<(int Inserted, int Updated, int Deleted)> IngestAsync(
        Guid containerId, Guid tenantId, string? mappingJson,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct = default)
    {
        if (_done) { return (0, 0, 0); }
        foreach (var row in rows)
        {
            var sitioWeb = ProspectoSearchRowSink.SafeHttpUrl(
                ProspectoSearchRowSink.Pick(row, "sitio_web", "website", "web", "sitio", "pagina", "url_web", "url"));
            var correo = CleanEmail(
                ProspectoSearchRowSink.Pick(row, "correo", "email", "mail", "e-mail"));
            if (sitioWeb is null && correo is null) { continue; } // fila vacia: sigue buscando en las siguientes.

            var p = await _db.ProspectosScrapeados.FirstOrDefaultAsync(x => x.Id == _prospectoId, ct);
            if (p is null) { _done = true; return (0, 0, 0); }

            var changed = false;
            // No pisa un dato ya presente: solo rellena lo que falta.
            if (sitioWeb is not null && string.IsNullOrWhiteSpace(p.SitioWeb)) { p.SitioWeb = sitioWeb; changed = true; }
            if (correo is not null && string.IsNullOrWhiteSpace(p.Correo)) { p.Correo = correo; changed = true; }
            if (changed)
            {
                // El sitio web influye en la etiqueta de lead: recalcula el Badge con la metrica existente.
                p.Badge = ProspectoSearchRowSink.ComputeBadge(p.Metrica, p.SitioWeb);
                await _db.SaveChangesAsync(ct);
            }
            _done = true;
            return (0, changed ? 1 : 0, 0);
        }
        return (0, 0, 0);
    }

    /// <summary>Devuelve un correo si el texto parece un email (una @ y un punto en el dominio); si no, null.
    /// Tolera un valor con prefijo "mailto:".</summary>
    private static string? CleanEmail(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { return null; }
        if (v.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) { v = v[7..].Trim(); }
        var at = v.IndexOf('@');
        if (at <= 0 || at == v.Length - 1) { return null; }
        var dom = v[(at + 1)..];
        return dom.Contains('.') && !v.Contains(' ') ? v : null;
    }
}

/// <summary>
/// Sumidero de UNA fila para el perfil LinkedIn DETALLADO (etapa 2b): ACTUALIZA el prospecto-persona
/// indicado con el resumen amplio (about + educacion + experiencia + headline) que el agente saco del /in/.
/// No pisa un PerfilDetalle ya presente. Tenant-safe por el filtro global (solo alcanza el del tenant activo).
/// </summary>
public sealed class ProspectoProfileDetailSink : IScrapeRowSink
{
    private readonly IApplicationDbContext _db;
    private readonly Guid _prospectoId;
    private bool _done; // una sola actualizacion por corrida (la primera fila con resumen).

    public ProspectoProfileDetailSink(IApplicationDbContext db, Guid tenantId, Guid prospectoId)
    {
        _db = db;
        _prospectoId = prospectoId;
    }

    public async Task<(int Inserted, int Updated, int Deleted)> IngestAsync(
        Guid containerId, Guid tenantId, string? mappingJson,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct = default)
    {
        if (_done) { return (0, 0, 0); }
        foreach (var row in rows)
        {
            var detalle = ProspectoSearchRowSink.Pick(row, "perfil_detalle", "perfil_detallado", "detalle", "resumen_detallado", "resumen");
            if (string.IsNullOrWhiteSpace(detalle)) { continue; }
            var text = detalle.Trim();
            if (text.Length > 4000) { text = text[..4000]; }

            var p = await _db.ProspectosScrapeados.FirstOrDefaultAsync(x => x.Id == _prospectoId, ct);
            if (p is null) { _done = true; return (0, 0, 0); }

            var changed = false;
            if (string.IsNullOrWhiteSpace(p.PerfilDetalle)) { p.PerfilDetalle = text; changed = true; }
            if (changed) { await _db.SaveChangesAsync(ct); }
            _done = true;
            return (0, changed ? 1 : 0, 0);
        }
        return (0, 0, 0);
    }
}
