using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Motor minimo de importacion desde API REST (disparo manual). Hace el GET del conector con la
/// autenticacion configurada (credenciales descifradas en el servidor), interpreta el arreglo JSON
/// y crea una fila por elemento mapeando campos->columnas. El HttpClient inyectado lo registra
/// AddHttpClient en Infrastructure.
///
/// La ESCRITURA de filas/celdas (Append/Replace/Upsert) NO vive aqui: se delega en el nucleo
/// compartido <see cref="IRowIngestService"/> (doc 03 s6), el mismo que usa el importador via
/// agente. Este servicio solo aporta el origen (JSON del API) y la paginacion.
/// </summary>
public sealed class ApiImportService : IApiImportService
{
    private readonly HttpClient _http;
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _protector;
    private readonly IRowIngestService _ingest;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const int MaxFieldScan = 50;
    private const int MaxImportRows = 5000;

    public ApiImportService(HttpClient http, IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector protector, IRowIngestService ingest)
    {
        _http = http;
        _db = db;
        _tenantContext = tenantContext;
        _protector = protector;
        _ingest = ingest;
    }

    public async Task<ApiProbeResult> ProbeAsync(Guid connectorId, string? arrayPath = null, CancellationToken ct = default)
    {
        var (connector, baseUri, loadError) = await LoadConnectorAsync(connectorId, ct);
        if (connector is null || baseUri is null) { return new ApiProbeResult(false, Array.Empty<string>(), 0, arrayPath, null, loadError); }
        var (doc, error) = await FetchJsonAsync(connector, baseUri, ct);
        if (doc is null) { return new ApiProbeResult(false, Array.Empty<string>(), 0, arrayPath, null, error); }
        using (doc)
        {
            if (!TryGetArray(doc.RootElement, arrayPath, out var arr, out var detectedPath))
            {
                return new ApiProbeResult(false, Array.Empty<string>(), 0, arrayPath,
                    null, "La respuesta no contiene un arreglo JSON. Indica la ruta del arreglo si viene envuelto (ej. data).");
            }

            // Campos: union de llaves escalares de los primeros elementos objeto.
            var fields = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var scanned = 0;
            string? sample = null;
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                sample ??= Pretty(el);
                foreach (var prop in el.EnumerateObject())
                {
                    if (seen.Add(prop.Name)) { fields.Add(prop.Name); }
                }
                if (++scanned >= MaxFieldScan) { break; }
            }
            return new ApiProbeResult(true, fields, arr.GetArrayLength(), detectedPath, sample, null);
        }
    }

    public async Task<ApiImportOutcome> ImportAsync(ApiImportRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return new ApiImportOutcome(false, 0, 0, 0, 0, new[] { "Sin tenant activo." });
        }
        if (req.ColumnToField.Count == 0)
        {
            return new ApiImportOutcome(false, 0, 0, 0, 0, new[] { "Define al menos un mapeo columna -> campo." });
        }

        // Columnas escalares de la tabla destino (las de tipo relacion/submodelo no se alimentan por API en v1).
        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == req.TargetContainerId && (
                c.Type == DataContainerColumnType.Text || c.Type == DataContainerColumnType.Number ||
                c.Type == DataContainerColumnType.Decimal || c.Type == DataContainerColumnType.Date ||
                c.Type == DataContainerColumnType.Boolean))
            .ToListAsync(ct);
        if (columns.Count == 0)
        {
            return new ApiImportOutcome(false, 0, 0, 0, 0, new[] { "La tabla destino no tiene columnas escalares." });
        }
        var byId = columns.ToDictionary(c => c.Id);
        // Solo mapeos hacia columnas escalares validas y con campo no vacio.
        var mapping = req.ColumnToField
            .Where(kv => byId.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (mapping.Count == 0)
        {
            return new ApiImportOutcome(false, 0, 0, 0, 0, new[] { "Ningun mapeo apunta a una columna escalar de la tabla." });
        }

        // Upsert: la columna clave debe estar mapeada a un campo del API (el nucleo resuelve el resto).
        Guid keyColId = Guid.Empty;
        if (req.Mode == ApiImportMode.Upsert)
        {
            if (req.KeyColumnId is not Guid k || !mapping.TryGetValue(k, out _))
            {
                return new ApiImportOutcome(false, 0, 0, 0, 0, new[] { "Para Upsert elige una columna clave que este mapeada a un campo del API." });
            }
            keyColId = k;
        }

        var (connector, baseUri, loadError) = await LoadConnectorAsync(req.ConnectorId, ct);
        if (connector is null || baseUri is null) { return new ApiImportOutcome(false, 0, 0, 0, 0, new[] { loadError ?? "No se pudo leer el API." }); }

        var failed = 0;
        var errors = new List<string>();

        // Nucleo de ingesta COMPARTIDO con el importador via agente (doc 03 s6): la sesion hace el
        // Replace (vaciar), la precarga del Upsert (clave->fila) y la escritura por chunk (una pagina
        // = un chunk = un SaveChanges), igual que antes.
        var session = _ingest.CreateSession(req.TargetContainerId, tenantId, mapping, req.Mode,
            req.Mode == ApiImportMode.Upsert ? keyColId : null);
        await session.PrepareAsync(ct);

        var paging = req.Paging;
        var paginated = paging is not null && paging.Mode != PagingMode.None && paging.PageSize > 0;
        var maxPages = paginated ? Math.Max(1, paging!.MaxPages) : 1;
        var stop = false;

        for (var page = 0; page < maxPages && !stop; page++)
        {
            // Reescribe el desplazamiento/pagina + limite en el query string de la URI base.
            var uri = baseUri;
            if (paginated && paging!.Mode == PagingMode.Offset)
            {
                uri = WithQueryParam(uri, string.IsNullOrWhiteSpace(paging.OffsetParam) ? "start" : paging.OffsetParam!, paging.StartValue + page * paging.PageSize);
                if (!string.IsNullOrWhiteSpace(paging.LimitParam)) { uri = WithQueryParam(uri, paging.LimitParam!, paging.PageSize); }
            }
            else if (paginated && paging!.Mode == PagingMode.Page)
            {
                uri = WithQueryParam(uri, string.IsNullOrWhiteSpace(paging.PageParam) ? "page" : paging.PageParam!, paging.StartValue + page);
                if (!string.IsNullOrWhiteSpace(paging.LimitParam)) { uri = WithQueryParam(uri, paging.LimitParam!, paging.PageSize); }
            }

            var (doc, error) = await FetchJsonAsync(connector, uri, ct);
            if (doc is null)
            {
                if (session.Inserted + session.Updated == 0) { return new ApiImportOutcome(false, 0, 0, session.Deleted, 0, new[] { error ?? "No se pudo leer el API." }); }
                errors.Add($"Pagina {page + 1}: {error}");
                break;
            }
            using (doc)
            {
                if (!TryGetArray(doc.RootElement, req.ArrayPath, out var arr, out _))
                {
                    if (session.Inserted + session.Updated == 0) { return new ApiImportOutcome(false, 0, 0, session.Deleted, 0, new[] { "La respuesta no contiene un arreglo JSON." }); }
                    break;
                }

                // Convierte los elementos JSON de la pagina en filas (campo->valor) para el nucleo.
                var pageCount = 0;
                var rows = new List<IReadOnlyDictionary<string, string?>>();
                foreach (var el in arr.EnumerateArray())
                {
                    if (session.Inserted + session.Updated + rows.Count >= MaxImportRows)
                    {
                        errors.Add($"Se alcanzo el limite de {MaxImportRows} filas por corrida; el resto no se importo.");
                        stop = true;
                        break;
                    }
                    pageCount++;
                    if (el.ValueKind != JsonValueKind.Object) { failed++; continue; }

                    var row = new Dictionary<string, string?>(mapping.Count);
                    foreach (var field in mapping.Values.Distinct(StringComparer.Ordinal))
                    {
                        row[field] = el.TryGetProperty(field, out var pv) ? ScalarString(pv) : null;
                    }
                    rows.Add(row);
                }

                // Una pagina = un chunk = un SaveChanges (mismo comportamiento que antes).
                await session.IngestChunkAsync(rows, ct);

                // Fin de la paginacion: sin paginacion es una sola pasada; con paginacion, una pagina
                // vacia o mas corta que el tamano de pagina significa que ya no hay mas.
                if (!paginated || pageCount == 0 || pageCount < paging!.PageSize) { stop = true; }
            }
        }

        var success = session.Inserted + session.Updated + session.Deleted > 0 || failed == 0;
        return new ApiImportOutcome(success, session.Inserted, session.Updated, session.Deleted, failed, errors);
    }

    // InsertRow / DeleteAllRowsAsync se movieron al nucleo compartido IRowIngestService (doc 03 s6):
    // el import REST y el importador via agente escriben filas con la MISMA implementacion.

    // ---- Fetch + auth ----

    /// <summary>Carga el conector, valida que sea RestApi con endpoint http(s) permitido y devuelve su URI base.</summary>
    private async Task<(Domain.Entities.DataConnector? Connector, Uri? BaseUri, string? Error)> LoadConnectorAsync(Guid connectorId, CancellationToken ct)
    {
        var c = await _db.DataConnectors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == connectorId, ct);
        if (c is null) { return (null, null, "Conector no encontrado."); }
        if (c.Kind != ConnectorKind.RestApi) { return (null, null, "El conector no es de tipo API REST."); }
        if (string.IsNullOrWhiteSpace(c.EndpointUrl)) { return (null, null, "El conector no tiene endpoint configurado."); }
        if (!Uri.TryCreate(c.EndpointUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (null, null, "El endpoint debe ser una URL http(s) absoluta.");
        }
        if (IsBlockedHost(uri))
        {
            return (null, null, "El endpoint apunta a una direccion interna/no permitida.");
        }
        return (c, uri, null);
    }

    /// <summary>Hace el GET del conector sobre la URI indicada (que puede diferir de la base por paginacion).</summary>
    private async Task<(JsonDocument? Doc, string? Error)> FetchJsonAsync(Domain.Entities.DataConnector c, Uri uri, CancellationToken ct)
    {
        var method = string.IsNullOrWhiteSpace(c.HttpMethod) ? HttpMethod.Get : new HttpMethod(c.HttpMethod!.Trim().ToUpperInvariant());
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var cred = c.CredentialsEncrypted is null ? null : SafeUnprotect(c.CredentialsEncrypted);
        ApplyAuth(request, c.AuthKind, cred);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var snippet = body.Length > 300 ? body[..300] : body;
                return (null, $"El API respondio {(int)resp.StatusCode} {resp.StatusCode}. {snippet}");
            }
            try { return (JsonDocument.Parse(body), null); }
            catch (JsonException) { return (null, "La respuesta no es JSON valido."); }
        }
        catch (OperationCanceledException) { return (null, "Tiempo de espera agotado al llamar al API."); }
        catch (HttpRequestException ex) { return (null, $"Error de red al llamar al API: {ex.Message}"); }
    }

    /// <summary>Reescribe (o agrega) un parametro del query string y devuelve la URI resultante.</summary>
    private static Uri WithQueryParam(Uri baseUri, string name, int value)
    {
        var pairs = new List<string>();
        var replaced = false;
        var q = baseUri.Query.TrimStart('?');
        if (!string.IsNullOrEmpty(q))
        {
            foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                var key = eq >= 0 ? part[..eq] : part;
                if (string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase))
                {
                    pairs.Add($"{Uri.EscapeDataString(name)}={value}");
                    replaced = true;
                }
                else { pairs.Add(part); }
            }
        }
        if (!replaced) { pairs.Add($"{Uri.EscapeDataString(name)}={value}"); }
        var ub = new UriBuilder(baseUri) { Query = string.Join('&', pairs) };
        return ub.Uri;
    }

    private static void ApplyAuth(HttpRequestMessage req, ConnectorAuthKind kind, string? cred)
    {
        if (string.IsNullOrWhiteSpace(cred)) { return; }
        switch (kind)
        {
            case ConnectorAuthKind.Basic:
                // cred = "usuario:clave"; si ya viene en base64 (sin ':') se usa tal cual.
                var token = cred.Contains(':')
                    ? Convert.ToBase64String(Encoding.UTF8.GetBytes(cred))
                    : cred;
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
                break;
            case ConnectorAuthKind.Bearer:
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cred);
                break;
            case ConnectorAuthKind.ApiKey:
                // Se envia como header Authorization crudo (valor completo configurado por el usuario).
                req.Headers.TryAddWithoutValidation("Authorization", cred);
                break;
            case ConnectorAuthKind.None:
            default:
                break;
        }
    }

    private string? SafeUnprotect(string cipher)
    {
        try { return _protector.Unprotect(cipher); }
        catch { return null; }
    }

    // ---- JSON helpers ----

    private static bool TryGetArray(JsonElement root, string? arrayPath, out JsonElement array, out string? detectedPath)
    {
        array = default;
        detectedPath = null;

        if (!string.IsNullOrWhiteSpace(arrayPath))
        {
            var cur = root;
            foreach (var seg in arrayPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(seg, out var next)) { return false; }
                cur = next;
            }
            if (cur.ValueKind != JsonValueKind.Array) { return false; }
            array = cur; detectedPath = arrayPath; return true;
        }

        if (root.ValueKind == JsonValueKind.Array) { array = root; detectedPath = ""; return true; }

        if (root.ValueKind == JsonValueKind.Object)
        {
            // Envoltorios comunes, luego el primer arreglo de nivel 1.
            foreach (var candidate in new[] { "data", "items", "results", "records", "rows" })
            {
                if (root.TryGetProperty(candidate, out var el) && el.ValueKind == JsonValueKind.Array)
                {
                    array = el; detectedPath = candidate; return true;
                }
            }
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    array = prop.Value; detectedPath = prop.Name; return true;
                }
            }
        }
        return false;
    }

    private static string? ScalarString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        // Objetos/arreglos anidados: se guarda el JSON crudo para no perder informacion.
        _ => el.GetRawText()
    };

    private static string Pretty(JsonElement el)
    {
        try { return JsonSerializer.Serialize(el, new JsonSerializerOptions { WriteIndented = true }); }
        catch { return el.GetRawText(); }
    }

    private static bool IsBlockedHost(Uri uri)
    {
        var host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            if (System.Net.IPAddress.IsLoopback(ip)) { return true; }
            var b = ip.GetAddressBytes();
            if (b.Length == 4)
            {
                // 10/8, 127/8, 169.254/16, 172.16-31/12, 192.168/16
                if (b[0] == 10 || b[0] == 127) { return true; }
                if (b[0] == 169 && b[1] == 254) { return true; }
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) { return true; }
                if (b[0] == 192 && b[1] == 168) { return true; }
            }
        }
        return false;
    }
}
