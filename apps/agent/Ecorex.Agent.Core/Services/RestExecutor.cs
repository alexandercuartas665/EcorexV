using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Ejecutor REST del agente Colmena: el equivalente HTTP del <see cref="GatewayExecutor"/> (que hace
/// SQL). Recibe un <see cref="RestFetchSpec"/> por el canal y ejecuta GET(s) de SOLO LECTURA contra la
/// fuente de la LAN/Internet, streameando <see cref="FetchResultMsg"/> en chunks igual que el Gateway.
///
/// Dos modos, ambos declarativos (nada de OCS hardcodeado):
///   - Simple: una llamada (o varias paginas) a la LISTA -> una fila por item, mapeando campos->columnas.
///   - Fan-out: por cada item de la lista un GET al DETALLE, se ubica un arreglo anidado y se emite UNA
///     fila por elemento hijo repitiendo las columnas del padre (patron OCS Inventory: computer -> software).
///
/// Parseo tolerante (mismas reglas que el import REST del servidor, mas el objeto-indexado de OCS):
/// la coleccion objetivo puede ser un arreglo, un objeto indexado por id (<c>{"1":{...},"2":{...}}</c>),
/// venir bajo <c>ArrayPath</c>, o bajo los envoltorios comunes data/items/results/records/rows.
///
/// La credencial NUNCA viaja aqui: llega en <see cref="ConnectorSpec.Secret"/> (ADR-0040) y no se loguea.
/// Su propio HttpClient (no el del hub): timeouts y ciclo de vida distintos al handshake SignalR.
/// </summary>
public sealed class RestExecutor
{
    /// <summary>Filas por chunk de FetchResult (independiente del tamano de pagina de la fuente).</summary>
    private const int ChunkRows = 500;

    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    };

    /// <summary>
    /// Costura de prueba: reemplaza el GET HTTP real por un fetcher con JSON enlatado, para probar
    /// paginacion + fan-out + aplanado + chunking sin red. null en produccion.
    /// </summary>
    private readonly Func<Uri, string, string?, string, CancellationToken, Task<JsonDocument>>? _fetchForTest;

    public RestExecutor() { }

    internal RestExecutor(Func<Uri, string, string?, string, CancellationToken, Task<JsonDocument>> fetchForTest)
    {
        _fetchForTest = fetchForTest;
    }

    /// <summary>Motores/casos que este ejecutor sabe atender: solo GET por ahora (solo-lectura).</summary>
    public static bool IsSupportedMethod(string? method) =>
        string.IsNullOrWhiteSpace(method) || method.Trim().Equals("GET", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<FetchResultMsg> ExecuteAsync(
        ConnectorSpec? connector,
        string correlationId,
        RestFetchSpec spec,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (spec is null) { throw new GatewayException("REST_NO_SPEC", "No llego RestFetchSpec para el conector RestApi."); }
        if (!IsSupportedMethod(spec.HttpMethod))
        {
            throw new GatewayException("REST_METHOD", $"El ejecutor REST es solo-lectura: solo GET (llego '{spec.HttpMethod}').");
        }
        if (string.IsNullOrWhiteSpace(spec.BaseUrl) && string.IsNullOrWhiteSpace(spec.ListPath))
        {
            throw new GatewayException("REST_NO_URL", "El RestFetchSpec no trae BaseUrl ni ListPath.");
        }

        var cred = connector?.Secret;
        var fields = BuildFieldList(spec);
        var maxRows = spec.MaxRows > 0 ? spec.MaxRows : 100000;
        var maxDetail = spec.MaxDetailCalls > 0 ? spec.MaxDetailCalls : 5000;

        using var http = new HttpClient(SharedHandler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(spec.TimeoutSeconds, 1, 600)),
        };

        var batch = new List<Dictionary<string, string?>>(ChunkRows);
        var chunkIndex = 0;
        var firstChunk = true;
        var total = 0;
        var detailCalls = 0;

        FetchResultMsg BuildChunk(bool isLast)
        {
            var msg = new FetchResultMsg(correlationId, chunkIndex, isLast, firstChunk ? fields : null,
                new List<Dictionary<string, string?>>(batch), batch.Count);
            chunkIndex++;
            firstChunk = false;
            batch = new List<Dictionary<string, string?>>(ChunkRows);
            return msg;
        }

        var paging = spec.Paging ?? new RestPagingSpec();
        var paginated = !paging.Mode.Equals("None", StringComparison.OrdinalIgnoreCase) && paging.PageSize > 0;
        var maxPages = paginated ? Math.Max(1, paging.MaxPages) : 1;
        var stop = false;

        for (var page = 0; page < maxPages && !stop; page++)
        {
            ct.ThrowIfCancellationRequested();

            var listUri = BuildPagedUri(spec.BaseUrl, spec.ListPath, paging, paginated, page);
            var listDoc = await FetchAsync(http, listUri, spec.AuthKind, cred, "REST_LIST", ct);
            using var _ = listDoc;

            var items = RestJson.Collection(listDoc.RootElement, spec.ArrayPath);
            if (items.Count == 0) { break; }

            foreach (var item in items)
            {
                if (total >= maxRows) { stop = true; break; }

                if (spec.Fanout is null)
                {
                    batch.Add(BuildRow(item.Value, spec.Fields));
                    total++;
                    if (batch.Count >= ChunkRows) { yield return BuildChunk(isLast: false); }
                    continue;
                }

                // ---- Fan-out: item de la lista -> GET detalle -> aplanado del arreglo anidado ----
                if (detailCalls >= maxDetail) { stop = true; break; }

                var id = ResolveId(item, spec.Fanout);
                if (string.IsNullOrWhiteSpace(id)) { continue; }

                var parent = BuildRow(item.Value, spec.Fanout.ParentFields);

                var detailPath = spec.Fanout.DetailPathTemplate.Replace("{id}", Uri.EscapeDataString(id));
                var detailUri = Combine(spec.BaseUrl, detailPath);
                var detailDoc = await FetchAsync(http, detailUri, spec.AuthKind, cred, "REST_DETAIL", ct);
                detailCalls++;

                using (detailDoc)
                {
                    var root = spec.Fanout.DetailUnwrapIndexed
                        ? RestJson.UnwrapIndexed(detailDoc.RootElement, spec.Fanout.ChildArrayPath)
                        : detailDoc.RootElement;

                    var children = RestJson.ChildArray(root, spec.Fanout.ChildArrayPath);
                    if (children.Count == 0)
                    {
                        // Equipo sin elementos hijos: se conserva una fila con solo las columnas del padre,
                        // para no perder el equipo. Las columnas del hijo quedan vacias.
                        batch.Add(new Dictionary<string, string?>(parent));
                        total++;
                        if (batch.Count >= ChunkRows) { yield return BuildChunk(isLast: false); }
                        continue;
                    }

                    foreach (var child in children)
                    {
                        if (total >= maxRows) { stop = true; break; }
                        var row = new Dictionary<string, string?>(parent);
                        MergeRow(row, child, spec.Fanout.ChildFields);
                        batch.Add(row);
                        total++;
                        if (batch.Count >= ChunkRows) { yield return BuildChunk(isLast: false); }
                    }
                }
            }

            // Fin de paginacion: sin paginar es una pasada; con paginacion, una pagina mas corta que el
            // tamano de pagina (o vacia) significa que ya no hay mas.
            if (!paginated || items.Count < paging.PageSize) { stop = true; }
        }

        yield return BuildChunk(isLast: true);
    }

    // ---- Filas ----

    /// <summary>Columnas emitidas, en orden: padre + hijo para fan-out; Fields para el modo simple.</summary>
    private static string[] BuildFieldList(RestFetchSpec spec)
    {
        var cols = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(IEnumerable<RestFieldMap>? maps)
        {
            if (maps is null) { return; }
            foreach (var m in maps)
            {
                if (!string.IsNullOrEmpty(m.Column) && seen.Add(m.Column)) { cols.Add(m.Column); }
            }
        }
        if (spec.Fanout is not null)
        {
            Add(spec.Fanout.ParentFields);
            Add(spec.Fanout.ChildFields);
        }
        else
        {
            Add(spec.Fields);
        }
        return cols.ToArray();
    }

    private static Dictionary<string, string?> BuildRow(JsonElement source, List<RestFieldMap>? maps)
    {
        var row = new Dictionary<string, string?>();
        MergeRow(row, source, maps);
        return row;
    }

    private static void MergeRow(Dictionary<string, string?> row, JsonElement source, List<RestFieldMap>? maps)
    {
        if (maps is null) { return; }
        foreach (var m in maps)
        {
            if (string.IsNullOrEmpty(m.Column)) { continue; }
            var value = RestJson.TryResolve(source, m.Path, out var v) ? RestJson.Scalar(v) : null;
            row[m.Column] = value ?? m.Default;
        }
    }

    private static string? ResolveId((string? Key, JsonElement Value) item, RestFanoutSpec fanout)
    {
        if (fanout.IdSource.Equals("field", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(fanout.IdField))
        {
            return RestJson.TryResolve(item.Value, fanout.IdField, out var v) ? RestJson.Scalar(v) : null;
        }
        return item.Key; // "key": la clave del objeto-indexado (OCS).
    }

    // ---- HTTP ----

    private Task<JsonDocument> FetchAsync(HttpClient http, Uri uri, string authKind, string? cred, string code, CancellationToken ct) =>
        _fetchForTest is not null
            ? _fetchForTest(uri, authKind, cred, code, ct)
            : FetchJsonAsync(http, uri, authKind, cred, code, ct);

    private static async Task<JsonDocument> FetchJsonAsync(HttpClient http, Uri uri, string authKind, string? cred, string code, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyAuth(request, authKind, cred);

        HttpResponseMessage resp;
        string body;
        try
        {
            resp = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            body = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new GatewayException(code + "_TIMEOUT", $"Tiempo de espera agotado llamando a {Safe(uri)}.", retryable: true); }
        catch (HttpRequestException ex) { throw new GatewayException(code + "_NET", $"Error de red llamando a {Safe(uri)}: {ex.Message}", retryable: true); }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var snippet = body.Length > 300 ? body[..300] : body;
                var retry = (int)resp.StatusCode >= 500 || (int)resp.StatusCode == 429;
                throw new GatewayException(code + "_HTTP", $"{Safe(uri)} respondio {(int)resp.StatusCode} {resp.StatusCode}. {snippet}", retry);
            }
            try { return JsonDocument.Parse(body); }
            catch (JsonException) { throw new GatewayException(code + "_JSON", $"La respuesta de {Safe(uri)} no es JSON valido."); }
        }
    }

    private static void ApplyAuth(HttpRequestMessage req, string authKind, string? cred)
    {
        if (string.IsNullOrWhiteSpace(cred)) { return; }
        switch ((authKind ?? "None").Trim().ToLowerInvariant())
        {
            case "basic":
                // cred = "usuario:clave"; si ya viene en base64 (sin ':') se usa tal cual.
                var token = cred.Contains(':') ? Convert.ToBase64String(Encoding.UTF8.GetBytes(cred)) : cred;
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
                break;
            case "bearer":
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cred);
                break;
            case "apikey":
                // valor completo del header Authorization configurado por el usuario.
                req.Headers.TryAddWithoutValidation("Authorization", cred);
                break;
            default:
                break;
        }
    }

    /// <summary>URL sin query para logs/errores: no filtra credenciales que pudieran ir en el query.</summary>
    private static string Safe(Uri uri) => uri.GetLeftPart(UriPartial.Path);

    // ---- URLs / paginacion ----

    /// <summary>Combina BaseUrl con un path relativo (o devuelve la URL si ya es absoluta http(s)).</summary>
    public static Uri Combine(string? baseUrl, string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var abs) &&
            (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
        {
            return abs;
        }
        if (string.IsNullOrWhiteSpace(baseUrl)) { throw new GatewayException("REST_NO_URL", "Path relativo sin BaseUrl."); }
        var b = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(b, (pathOrUrl ?? string.Empty).TrimStart('/'));
    }

    private static Uri BuildPagedUri(string? baseUrl, string listPath, RestPagingSpec paging, bool paginated, int page)
    {
        var uri = Combine(baseUrl, listPath);
        if (!paginated) { return uri; }

        if (paging.Mode.Equals("Offset", StringComparison.OrdinalIgnoreCase))
        {
            uri = WithQueryParam(uri, string.IsNullOrWhiteSpace(paging.OffsetParam) ? "start" : paging.OffsetParam, paging.StartValue + page * paging.PageSize);
            if (!string.IsNullOrWhiteSpace(paging.LimitParam)) { uri = WithQueryParam(uri, paging.LimitParam, paging.PageSize); }
        }
        else if (paging.Mode.Equals("Page", StringComparison.OrdinalIgnoreCase))
        {
            uri = WithQueryParam(uri, string.IsNullOrWhiteSpace(paging.PageParam) ? "page" : paging.PageParam, paging.StartValue + page);
            if (!string.IsNullOrWhiteSpace(paging.LimitParam)) { uri = WithQueryParam(uri, paging.LimitParam, paging.PageSize); }
        }
        return uri;
    }

    /// <summary>Reescribe (o agrega) un parametro del query string y devuelve la URI resultante.</summary>
    public static Uri WithQueryParam(Uri baseUri, string name, int value)
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
        return new UriBuilder(baseUri) { Query = string.Join('&', pairs) }.Uri;
    }
}
