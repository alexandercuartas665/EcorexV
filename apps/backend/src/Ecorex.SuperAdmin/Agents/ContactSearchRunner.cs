using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>Fuentes sociales sujetas al tope diario (las que penalizan por exceso de scraping).</summary>
    private static bool IsSocial(ContactSearchSource s) => s
        is ContactSearchSource.LinkedIn or ContactSearchSource.Facebook
        or ContactSearchSource.Instagram or ContactSearchSource.X;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAiStepOrchestrator _orchestrator;

    public ContactSearchRunner(IApplicationDbContext db, ITenantContext tenant, IAiStepOrchestrator orchestrator)
    {
        _db = db;
        _tenant = tenant;
        _orchestrator = orchestrator;
    }

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
        var ctx = new AiStepContext(
            def.ClientId!, tenantId, instruction, Guid.Empty, AllowListFor(def.SourceType),
            MaxSteps: 25, MaxSeconds: 180, AiProviderId: providerCfg.Id, Secret: null, SinkOverride: sink,
            SessionKey: SessionKeyFor(def.SourceType));

        var outcome = await _orchestrator.RunAsync(ctx, ct);

        // Sella la ultima corrida (base del futuro programador automatico). def viene rastreado.
        def.LastRunAt = DateTimeOffset.UtcNow;
        // Registra la corrida para el tope diario por fuente (cuenta OK y fallidas: ambas tocaron la red).
        _db.ContactSearchRuns.Add(new ContactSearchRun
        {
            TenantId = tenantId,
            DefinitionId = def.Id,
            Source = source,
            RunAt = DateTimeOffset.UtcNow,
            Ok = outcome.Ok,
            Inserted = outcome.Inserted,
        });
        await _db.SaveChangesAsync(ct);

        var totalCreated = outcome.Inserted;

        // ETAPA 2 (enriquecimiento Maps -> LinkedIn): por cada EMPRESA encontrada en Maps, busca PERSONAS en
        // LinkedIn (sesion logueada) y las agrega ligadas a esa empresa. Cada empresa = una corrida LinkedIn,
        // sujeta al tope 20/dia; al alcanzarlo se corta (NO falla la corrida Maps). Cada empresa es su propio
        // AiStepContext acotado (no un run gigante) para no chocar con los topes del orquestador.
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
                    Guid.Empty, AllowListFor(ContactSearchSource.LinkedIn),
                    MaxSteps: 20, MaxSeconds: 150, AiProviderId: providerCfg.Id, Secret: null,
                    SinkOverride: liSink, SessionKey: "linkedin");
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
            }
        }

        return new(outcome.Ok, totalCreated, outcome.Ok ? null : outcome.Error);
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
            + "nombre, cargo (el headline tras el nombre) y url = la URL del perfil (/in/...). No inventes telefono "
            + "ni correo si no aparecen.");
        sb.Append("Cuando tengas los resultados, llama a 'guardar_filas' con un arreglo de objetos con las claves "
            + "nombre, cargo y url (el perfil /in/).");
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
                "Cada resultado de Google Maps es un NEGOCIO (una empresa) = el lead. Guarda UNA sola fila por "
                + "negocio con nombre = el NOMBRE DEL NEGOCIO (es la empresa; puedes repetirlo en 'empresa'). NO "
                + "inventes una persona: NO crees un segundo registro ni un 'Contacto de <negocio>', y NO rellenes "
                + "cargo/nombre de persona (Maps no trae personas). Las PERSONAS salen unicamente del enriquecimiento "
                + "en LinkedIn. Captura direccion, telefono, sitio web, metrica (estrellas/resenas) e imagen si aparecen.",
            ContactSearchSource.LinkedIn =>
                $"Estas logueado en LinkedIn. NAVEGA directamente a {lkUrl} (NO uses Google ni site:linkedin.com). "
                + "Haz scroll para cargar mas resultados. El contenido trae PERSONAS en 'PERSONAS DETECTADAS' y enlaces "
                + "de perfil (/in/): guarda UNA fila por persona con nombre, cargo (el headline tras el nombre) y "
                + "url = la URL del perfil (/in/...). No inventes telefono ni correo si no aparecen.",
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
        sb.Append("imagen_url (URL http de la foto o logo del negocio, si aparece) y ");
        sb.Append("url (URL de la ficha o pagina donde encontraste el contacto -- guardala siempre que la tengas). ");
        sb.Append("Guarda solo contactos reales con al menos un nombre.");
        return sb.ToString();
    }

    // Dominios que el agente puede visitar por fuente (defensa en profundidad del navegador Colmena).
    private static IReadOnlyList<string> AllowListFor(ContactSearchSource s) => s switch
    {
        ContactSearchSource.Maps => new[] { "google.com", "www.google.com", "maps.google.com", "google.com.co" },
        ContactSearchSource.LinkedIn => new[] { "linkedin.com", "www.linkedin.com" },
        ContactSearchSource.Instagram => new[] { "instagram.com", "www.instagram.com" },
        ContactSearchSource.Facebook => new[] { "facebook.com", "www.facebook.com" },
        ContactSearchSource.X => new[] { "x.com", "twitter.com" },
        _ => new[] { "google.com", "www.google.com", "bing.com", "www.bing.com", "duckduckgo.com" },
    };

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
            var entity = new ProspectoScrapeado
            {
                TenantId = _tenantId,
                Fuente = _fuente,
                NombreCompleto = nombre.Trim(),
                Cargo = Pick(row, "cargo", "title", "puesto", "rol"),
                Empresa = empresa,
                Ciudad = Pick(row, "ciudad", "city", "localidad", "municipio"),
                Telefono = Pick(row, "telefono", "tel", "phone", "celular", "movil"),
                Correo = Pick(row, "correo", "email", "mail", "e-mail"),
                Direccion = Pick(row, "direccion", "address", "dir", "ubicacion"),
                Metrica = Pick(row, "metrica", "rating", "resenas", "reviews", "estrellas", "seguidores", "conexiones"),
                // Solo http/https: no se persisten (ni luego se renderizan) URLs javascript:/data: del scraping.
                ImagenUrl = SafeHttpUrl(Pick(row, "imagen_url", "imagen", "foto", "image", "photo", "avatar", "logo")),
                // Sitio web PROPIO del negocio (distinto de OrigenUrl = ficha en Maps).
                SitioWeb = SafeHttpUrl(Pick(row, "sitio_web", "website", "web", "sitio", "pagina", "url_web")),
                OrigenUrl = SafeHttpUrl(Pick(row, "url", "origen", "enlace", "link", "source_url", "fuente_url", "perfil")),
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

    private static string? Pick(IReadOnlyDictionary<string, string?> row, params string[] keys)
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
    private static string? SafeHttpUrl(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { return null; }
        return Uri.TryCreate(v, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? v : null;
    }
}
