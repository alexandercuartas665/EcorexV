using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>Pedido de clasificar un lote de filas de una tabla con un agente de IA (ADR-0197).</summary>
/// <param name="TableId">Tabla (DataContainer) a clasificar; ya resuelta y tenant-scoped.</param>
/// <param name="Agente">Id o nombre del agente de IA que clasifica.</param>
/// <param name="ColumnaClave">Columna que identifica cada fila (ej. id_proceso); el agente la devuelve tal cual.</param>
/// <param name="ColumnaMarca">Columna que marca la fila como procesada (ej. clasificado_en); vacia = pendiente.</param>
/// <param name="ColumnasSalida">Columnas que el agente PUEDE escribir (ej. sector). Ninguna otra se toca.</param>
/// <param name="ColumnasEntrada">Columnas que se le envian al agente; null = todas menos la marca y las de salida.</param>
/// <param name="Limite">Filas por lote (1..MaxLimite).</param>
public sealed record ClasificarLoteRequest(
    Guid TableId, string Agente, string ColumnaClave, string ColumnaMarca,
    IReadOnlyList<string> ColumnasSalida, IReadOnlyList<string>? ColumnasEntrada, int Limite);

/// <summary>Resultado de un lote: cuantas se enviaron, cuantas se escribieron y cuantas siguen pendientes.</summary>
public sealed record ClasificarLoteResultado(
    bool Ok, string? Error, int Procesados = 0, int Actualizados = 0, int SinRespuesta = 0, int Ignorados = 0,
    int Restantes = 0, int InputTokens = 0, int OutputTokens = 0, string? Agente = null, string? Modelo = null,
    string? Codigo = null)
{
    /// <summary>Falla de CONFIGURACION (columnas, agente, proveedor): reintentar no sirve.</summary>
    public const string CodigoConfig = "config";
    /// <summary>El tenant agoto su cupo de tokens: se corta y lo pendiente queda para despues.</summary>
    public const string CodigoCupo = "cupo";
    /// <summary>Falla TRANSITORIA del proveedor (timeout, 5xx, respuesta no JSON): se puede reintentar.</summary>
    public const string CodigoTransitorio = "transitorio";

    public static ClasificarLoteResultado Falla(string error, string codigo = CodigoConfig) => new(false, error, Codigo: codigo);

    /// <summary>true si vale la pena reintentar el lote (con espera).</summary>
    public bool EsTransitorio => !Ok && Codigo == CodigoTransitorio;
}

/// <summary>Pedido de VACIAR la marca (y opcionalmente las salidas) para reclasificar (ADR-0199).</summary>
/// <param name="TableId">Tabla ya resuelta y tenant-scoped.</param>
/// <param name="ColumnaMarca">Columna marca a vaciar.</param>
/// <param name="ColumnasSalida">Columnas de salida a vaciar tambien (opcional).</param>
/// <param name="Filtro">Solo filas cuya columna (clave) tenga EXACTAMENTE ese valor (valor); AND entre ellas.
/// null/vacio = todas las filas raiz de la tabla.</param>
public sealed record LimpiarMarcaRequest(
    Guid TableId, string ColumnaMarca, IReadOnlyList<string>? ColumnasSalida,
    IReadOnlyDictionary<string, string>? Filtro);

/// <summary>Resultado de limpiar: filas alcanzadas y celdas vaciadas.</summary>
public sealed record LimpiarMarcaResultado(bool Ok, string? Error, int Filas = 0, int Celdas = 0);

/// <summary>
/// RUNNER DE CLASIFICACION POR LOTES sobre un contenedor de datos (ADR-0197). Toma hasta N filas de una tabla cuya
/// columna marca esta vacia, se las envia a un agente de IA (su prompt de sistema + un contrato de salida JSON),
/// escribe las columnas de salida + la marca (fecha UTC) por la columna clave y devuelve cuantas quedan. Pensado
/// para repetirse lote a lote (no revienta cupos ni el tiempo de una llamada MCP). Reglas:
/// - Solo con el agente ENCENDIDO y el proveedor habilitado; respeta el cupo DURO del plan.
/// - Todo consumo se registra en AiUsageLog (fuente "clasificar-lote").
/// - El agente solo puede escribir filas DEL LOTE y solo las columnas de salida declaradas (no toca la entrada).
/// - Una fila que el agente no devuelve queda sin marca (se reintenta en el siguiente lote) y se reporta.
/// </summary>
public interface IContenedorClasificacionService
{
    Task<ClasificarLoteResultado> ClasificarLoteAsync(ClasificarLoteRequest request, CancellationToken ct = default);

    /// <summary>
    /// ADR-0199: VACIA la columna marca (y las de salida pedidas) de las filas raiz de la tabla (todas o las que
    /// cumplan el filtro), para que el siguiente lote las vuelva a clasificar. Pone el valor en null (no borra
    /// filas ni columnas). Un solo UPDATE en BD, tenant-scoped por el filtro global. Ojo: es un UPDATE masivo, no
    /// refresca celdas ya cargadas en el mismo contexto; reclasificar en un scope nuevo (MCP y worker ya lo hacen).
    /// </summary>
    Task<LimpiarMarcaResultado> LimpiarMarcaAsync(LimpiarMarcaRequest request, CancellationToken ct = default);
}

public sealed class ContenedorClasificacionService : IContenedorClasificacionService
{
    public const int MaxLimite = 50;

    /// <summary>Tope de cada valor enviado al modelo (descripciones largas) y del prompt completo.</summary>
    private const int MaxCharsPorCelda = 1500;
    private const int MaxPromptChars = 60_000;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;
    private readonly TimeProvider _clock;

    public ContenedorClasificacionService(IApplicationDbContext db, ITenantContext tenant, ISecretProtector secrets,
        IAiProviderClient client, IAiUsageService usage, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _secrets = secrets;
        _client = client;
        _usage = usage;
        _clock = clock;
    }

    public async Task<ClasificarLoteResultado> ClasificarLoteAsync(ClasificarLoteRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return ClasificarLoteResultado.Falla("Sin tenant activo."); }
        if (req.ColumnasSalida.Count == 0) { return ClasificarLoteResultado.Falla("Indica al menos una columna de salida (columnas_salida)."); }
        var limite = Math.Clamp(req.Limite, 1, MaxLimite);

        // ---- Columnas ----
        var cols = await ContenedorFilasPorClave.ColumnasAsync(_db, req.TableId, ct);
        string? Falta(string name) => cols.ContainsKey(name.Trim()) ? null : name;
        var faltantes = new[] { req.ColumnaClave, req.ColumnaMarca }.Concat(req.ColumnasSalida).Concat(req.ColumnasEntrada ?? [])
            .Select(Falta).Where(x => x is not null).ToList();
        if (faltantes.Count > 0)
        {
            return ClasificarLoteResultado.Falla($"La tabla no tiene las columnas: {string.Join(", ", faltantes)}. Columnas: {string.Join(", ", cols.Keys)}.");
        }
        var keyColId = cols[req.ColumnaClave.Trim()];
        var markColId = cols[req.ColumnaMarca.Trim()];
        var salida = new HashSet<string>(req.ColumnasSalida.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        if (salida.Contains(req.ColumnaClave.Trim()) || salida.Contains(req.ColumnaMarca.Trim()))
        {
            return ClasificarLoteResultado.Falla("La columna clave y la marca no pueden ser columnas de salida.");
        }
        var entrada = (req.ColumnasEntrada is { Count: > 0 }
                ? req.ColumnasEntrada.Select(s => s.Trim())
                : cols.Keys.Where(k => !salida.Contains(k) && !string.Equals(k, req.ColumnaMarca.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Append(req.ColumnaClave.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entradaIds = entrada.ToDictionary(n => cols[n], n => n);

        // ---- Agente + proveedor ----
        var agent = await ResolveAgentAsync(req.Agente, ct);
        if (agent is null) { return ClasificarLoteResultado.Falla($"No encontre el agente '{req.Agente}'."); }
        if (!agent.IsActive)
        {
            return ClasificarLoteResultado.Falla($"El agente '{agent.Name}' esta apagado: enciendelo para clasificar.");
        }
        var providerCfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == agent.Provider, ct);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return ClasificarLoteResultado.Falla($"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }
        string apiKey;
        try { apiKey = _secrets.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return ClasificarLoteResultado.Falla($"La API key del proveedor {agent.Provider} no se pudo descifrar."); }
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model! : AiProviderCatalog.For(agent.Provider).DefaultModel;

        var quota = await _usage.GetQuotaAsync(ct);
        if (quota.Hard && quota.Exceeded)
        {
            return ClasificarLoteResultado.Falla("El tenant agoto su cupo mensual de tokens de IA.", ClasificarLoteResultado.CodigoCupo);
        }

        // ---- Lote: filas raiz CON clave y SIN marca, las mas antiguas primero ----
        var pendientes = Pendientes(req.TableId, keyColId, markColId);
        var loteIds = await pendientes.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => r.Id).Take(limite).ToListAsync(ct);
        if (loteIds.Count == 0)
        {
            return new ClasificarLoteResultado(true, null, Agente: agent.Name, Modelo: model);
        }

        var entradaColIds = entradaIds.Keys.ToList();
        var celdas = await _db.DataContainerCells.AsNoTracking()
            .Where(c => loteIds.Contains(c.RowId) && entradaColIds.Contains(c.ColumnId))
            .Select(c => new { c.RowId, c.ColumnId, c.Value })
            .ToListAsync(ct);
        var porFila = celdas.GroupBy(c => c.RowId).ToDictionary(g => g.Key, g => g.ToList());

        // Registros para el modelo, respetando el tope del prompt (lo que no quepa queda para el siguiente lote).
        var registros = new List<Dictionary<string, string?>>();
        var keyToRows = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var chars = 0;
        foreach (var rowId in loteIds)
        {
            var reg = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in porFila.TryGetValue(rowId, out var l) ? l : [])
            {
                var v = c.Value;
                if (v is { Length: > MaxCharsPorCelda }) { v = v[..MaxCharsPorCelda] + "..."; }
                reg[entradaIds[c.ColumnId]] = v;
            }
            var key = reg.TryGetValue(req.ColumnaClave.Trim(), out var k) ? k?.Trim() : null;
            if (string.IsNullOrEmpty(key)) { continue; }
            var size = JsonSerializer.Serialize(reg).Length;
            if (registros.Count > 0 && chars + size > MaxPromptChars) { break; }
            chars += size;
            if (!keyToRows.TryGetValue(key, out var rows))
            {
                keyToRows[key] = rows = new List<Guid>();
                registros.Add(reg);
            }
            rows.Add(rowId);
        }

        var systemPrompt = BuildSystemPrompt(agent.SystemPrompt, req.ColumnaClave.Trim(), salida);
        var userPrompt = JsonSerializer.Serialize(registros, JsonOut);

        AiChatResult response;
        try
        {
            response = await _client.CompleteAsync(agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt,
                [new AiChatTurn("user", userPrompt)], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await SafeRecord(agent.Id, agent.Provider, model, 0, 0, false, ct);
            return ClasificarLoteResultado.Falla($"Error llamando al proveedor {agent.Provider}: {ex.Message}", ClasificarLoteResultado.CodigoTransitorio);
        }
        await SafeRecord(agent.Id, agent.Provider, model, response.InputTokens, response.OutputTokens, response.Ok, ct);
        if (!response.Ok || string.IsNullOrWhiteSpace(response.Text))
        {
            return ClasificarLoteResultado.Falla(response.Error ?? "El proveedor de IA no devolvio respuesta.", ClasificarLoteResultado.CodigoTransitorio) with
            {
                InputTokens = response.InputTokens,
                OutputTokens = response.OutputTokens
            };
        }

        var items = ParseItems(response.Text!);
        if (items is null)
        {
            return ClasificarLoteResultado.Falla("La respuesta del agente no es un arreglo JSON valido.", ClasificarLoteResultado.CodigoTransitorio) with
            {
                InputTokens = response.InputTokens,
                OutputTokens = response.OutputTokens
            };
        }

        // ---- Escribir: solo filas del lote y solo columnas de salida + la marca ----
        var marca = _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var filas = new List<IReadOnlyDictionary<string, string?>>();
        var respondidas = new HashSet<string>(StringComparer.Ordinal);
        var ignorados = 0;
        foreach (var item in items)
        {
            var fila = ContenedorFilasPorClave.ToFila(item);
            var key = fila.TryGetValue(req.ColumnaClave.Trim(), out var kv) ? kv?.Trim() : null;
            if (key is null || !keyToRows.ContainsKey(key) || !respondidas.Add(key)) { ignorados++; continue; }
            var escribir = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [req.ColumnaClave.Trim()] = key,
                [req.ColumnaMarca.Trim()] = marca
            };
            foreach (var (col, val) in fila)
            {
                if (salida.Contains(col) && val is not null) { escribir[col] = val; }
            }
            filas.Add(escribir);
        }

        var r = await ContenedorFilasPorClave.AplicarAsync(_db, tenantId, req.TableId, req.ColumnaClave.Trim(), cols, filas, crearSiFalta: false, ct);
        await _db.SaveChangesAsync(ct);

        var restantes = await Pendientes(req.TableId, keyColId, markColId).CountAsync(ct);
        return new ClasificarLoteResultado(true, null,
            Procesados: registros.Count,
            Actualizados: r.Actualizadas,
            SinRespuesta: registros.Count - respondidas.Count,
            Ignorados: ignorados,
            Restantes: restantes,
            InputTokens: response.InputTokens,
            OutputTokens: response.OutputTokens,
            Agente: agent.Name,
            Modelo: model);
    }

    public async Task<LimpiarMarcaResultado> LimpiarMarcaAsync(LimpiarMarcaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) { return new LimpiarMarcaResultado(false, "Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.ColumnaMarca)) { return new LimpiarMarcaResultado(false, "Indica la columna_marca."); }

        var cols = await ContenedorFilasPorClave.ColumnasAsync(_db, req.TableId, ct);
        var nombres = new List<string> { req.ColumnaMarca.Trim() };
        nombres.AddRange((req.ColumnasSalida ?? []).Select(s => s.Trim()).Where(s => s.Length > 0));
        var filtro = (req.Filtro ?? new Dictionary<string, string>())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        var faltantes = nombres.Concat(filtro.Keys).Where(n => !cols.ContainsKey(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (faltantes.Count > 0)
        {
            return new LimpiarMarcaResultado(false, $"La tabla no tiene las columnas: {string.Join(", ", faltantes)}. Columnas: {string.Join(", ", cols.Keys)}.");
        }
        var limpiarIds = nombres.Select(n => cols[n]).Distinct().ToList();

        // Filas raiz de la tabla que cumplen TODAS las condiciones del filtro (igualdad exacta).
        var filas = _db.DataContainerRows.Where(r => r.ContainerId == req.TableId && r.ParentRowId == null);
        foreach (var (col, val) in filtro)
        {
            var colId = cols[col];
            var v = val;
            filas = filas.Where(r => _db.DataContainerCells.Any(c => c.RowId == r.Id && c.ColumnId == colId && c.Value == v));
        }
        var filaIds = filas.Select(r => r.Id);

        var alcanzadas = await filas.CountAsync(ct);
        DateTimeOffset? ahora = _clock.GetUtcNow();
        var celdas = await _db.DataContainerCells
            .Where(c => limpiarIds.Contains(c.ColumnId) && c.Value != null && filaIds.Contains(c.RowId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Value, (string?)null)
                .SetProperty(c => c.UpdatedAt, ahora), ct);
        return new LimpiarMarcaResultado(true, null, alcanzadas, celdas);
    }

    /// <summary>Filas raiz de la tabla con valor en la clave y la marca vacia (o sin celda de marca).</summary>
    private IQueryable<Domain.Entities.DataContainerRow> Pendientes(Guid tableId, Guid keyColId, Guid markColId)
        => _db.DataContainerRows.Where(r => r.ContainerId == tableId && r.ParentRowId == null
            && _db.DataContainerCells.Any(c => c.RowId == r.Id && c.ColumnId == keyColId && c.Value != null && c.Value != "")
            && !_db.DataContainerCells.Any(c => c.RowId == r.Id && c.ColumnId == markColId && c.Value != null && c.Value != ""));

    private async Task<Domain.Entities.AiAgent?> ResolveAgentAsync(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) { return null; }
        if (Guid.TryParse(key, out var id))
        {
            return await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        }
        var name = key.Trim().ToLower();
        var hits = await _db.AiAgents.AsNoTracking().Where(a => a.Name.ToLower() == name).Take(2).ToListAsync(ct);
        return hits.Count == 1 ? hits[0] : null;
    }

    private static string BuildSystemPrompt(string agentPrompt, string keyColumn, IReadOnlySet<string> salida)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentPrompt))
        {
            sb.AppendLine(agentPrompt.Trim());
            sb.AppendLine();
        }
        sb.AppendLine("Vas a recibir un ARREGLO JSON de registros. Procesa CADA registro.");
        sb.AppendLine("Responde UNICAMENTE con un arreglo JSON (sin texto alrededor y sin bloques de codigo), con un objeto por registro.");
        sb.AppendLine($"Cada objeto DEBE incluir el campo \"{keyColumn}\" con el MISMO valor que recibiste.");
        sb.AppendLine($"Campos que se guardan: {string.Join(", ", salida)}. Cualquier otro campo se ignora.");
        return sb.ToString();
    }

    /// <summary>Extrae el arreglo de objetos de la respuesta: tolera bloques ```json, texto alrededor y un objeto
    /// envoltorio con una propiedad arreglo (ej. {"resultados":[...]}). Null si no hay JSON utilizable.</summary>
    public static List<JsonElement>? ParseItems(string text)
    {
        var t = text.Trim();
        foreach (var candidate in Candidates(t))
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var arr = root.EnumerateObject().Select(p => p.Value).FirstOrDefault(v => v.ValueKind == JsonValueKind.Array);
                    root = arr.ValueKind == JsonValueKind.Array ? arr : root;
                    if (root.ValueKind == JsonValueKind.Object) { return new List<JsonElement> { root.Clone() }; }
                }
                if (root.ValueKind != JsonValueKind.Array) { continue; }
                return root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e => e.Clone()).ToList();
            }
            catch (JsonException) { }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string t)
    {
        yield return t;
        var fence = t.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = t.IndexOf('\n', fence);
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            if (start > 0 && end > start) { yield return t[(start + 1)..end].Trim(); }
        }
        var a = t.IndexOf('[');
        var b = t.LastIndexOf(']');
        if (a >= 0 && b > a) { yield return t[a..(b + 1)]; }
        var o = t.IndexOf('{');
        var p = t.LastIndexOf('}');
        if (o >= 0 && p > o) { yield return t[o..(p + 1)]; }
    }

    private async Task SafeRecord(Guid agentId, Domain.Enums.AiProvider provider, string model, int input, int output, bool success, CancellationToken ct)
    {
        try { await _usage.RecordAsync(agentId, provider, model, input, output, "clasificar-lote", success, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* el conteo de consumo no debe tumbar el lote */ }
    }

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
