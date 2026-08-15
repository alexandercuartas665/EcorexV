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
        var instruction = BuildInstruction(def, agent);
        var sink = new ProspectoSearchRowSink(_db, tenantId, def.SourceType.ToString(), cap);
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

        return new(outcome.Ok, outcome.Inserted, outcome.Ok ? null : outcome.Error);
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

        // Guia especifica por fuente: LinkedIn descubre PERSONAS (una fila c/u); FB/IG es UN negocio (una fila).
        var guidance = d.SourceType switch
        {
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
    private int _total; // acumulado entre llamadas de IngestAsync de la misma corrida.

    public ProspectoSearchRowSink(IApplicationDbContext db, Guid tenantId, string fuente, int cap = int.MaxValue)
    {
        _db = db;
        _tenantId = tenantId;
        _fuente = fuente;
        _cap = cap <= 0 ? int.MaxValue : cap;
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
            _db.ProspectosScrapeados.Add(new ProspectoScrapeado
            {
                TenantId = _tenantId,
                Fuente = _fuente,
                NombreCompleto = nombre.Trim(),
                Cargo = Pick(row, "cargo", "title", "puesto", "rol"),
                Empresa = Pick(row, "empresa", "company", "negocio"),
                Ciudad = Pick(row, "ciudad", "city", "localidad", "municipio"),
                Telefono = Pick(row, "telefono", "tel", "phone", "celular", "movil"),
                Correo = Pick(row, "correo", "email", "mail", "e-mail"),
                Metrica = Pick(row, "metrica", "rating", "resenas", "reviews", "estrellas", "seguidores", "conexiones"),
                // Solo http/https: no se persisten (ni luego se renderizan) URLs javascript:/data: del scraping.
                ImagenUrl = SafeHttpUrl(Pick(row, "imagen_url", "imagen", "foto", "image", "photo", "avatar", "logo")),
                OrigenUrl = SafeHttpUrl(Pick(row, "url", "origen", "enlace", "link", "source_url", "fuente_url", "perfil")),
                DataJson = JsonSerializer.Serialize(row),
                FechaCaptura = DateTimeOffset.UtcNow,
            });
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
