using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de CONTENEDOR DE DATOS (ADR-0103): permite a un agente de IA
/// ESCRIBIR filas en un contenedor del tenant (modelo EAV data_container_rows/cells). Pensada para agentes
/// que EXTRAEN datos estructurados (ej. "Clasificador de productos y precios") y los cargan. Tenant-scoped
/// por el filtro global; resuelve el contenedor por NOMBRE (no por id). Solo INSERTA (no borra ni pisa).
/// </summary>
public interface IContenedorDatosToolset : IAgentToolset { }

public sealed class ContenedorDatosToolset : IContenedorDatosToolset
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ContenedorDatosToolset(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public string GroupKey => "contenedor-datos";
    public string GroupLabel => "Contenedor de datos";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "listar_contenedores",
            "Lista los contenedores de datos del tenant con sus columnas. Usala si no sabes el nombre exacto " +
            "del contenedor destino o el nombre de sus columnas antes de cargar filas.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new AiToolSpec(
            "agregar_filas_a_contenedor",
            "Inserta filas en un contenedor de datos del tenant. 'contenedor' es el nombre EXACTO del contenedor " +
            "(ver listar_contenedores); 'filas' es una lista de objetos: cada objeto es una fila donde cada clave " +
            "es el nombre de una COLUMNA del contenedor y su valor el dato. Las claves que no correspondan a una " +
            "columna se ignoran. Devuelve cuantas filas se cargaron.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"Nombre exacto del contenedor destino"},"filas":{"type":"array","items":{"type":"object"},"description":"Lista de filas; cada fila es un objeto columna->valor"}},"required":["contenedor","filas"],"additionalProperties":false}"""),
        new AiToolSpec(
            "actualizar_filas_por_clave",
            "ACTUALIZA filas EXISTENTES de un contenedor ubicandolas por una columna clave (no duplica). Para cada " +
            "objeto de 'filas' busca la fila cuyo valor en 'columna_clave' coincide y escribe SOLO las columnas " +
            "presentes (las demas no se tocan). Si la clave no existe la reporta en 'no_encontradas', salvo que " +
            "'crear_si_falta' sea true (entonces crea la fila). 'contenedor' es el nombre EXACTO de la tabla (ver " +
            "listar_contenedores); si el nombre se repite en varios contenedores, envia el contenedor y la 'tabla'.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"Nombre exacto de la tabla destino (o del contenedor si envias 'tabla')"},"tabla":{"type":"string","description":"Opcional: nombre de la tabla dentro del contenedor"},"columna_clave":{"type":"string","description":"Columna que identifica la fila (ej. id_proceso)"},"filas":{"type":"array","items":{"type":"object"},"description":"Lista de filas; cada fila es un objeto columna->valor que DEBE incluir la columna clave"},"crear_si_falta":{"type":"boolean","description":"true = si la clave no existe, crea la fila (default false)"}},"required":["contenedor","columna_clave","filas"],"additionalProperties":false}"""),
        new AiToolSpec(
            "cargar_productos",
            "Carga los productos extraidos en el contenedor de datos 'Productos' del tenant. 'productos' es una " +
            "lista de objetos; cada objeto es un producto con sus campos (por ejemplo categoria, marca, nombre, " +
            "referencia, especificaciones, precio, moneda, proveedor, vigencia, pagina, archivo). Cada campo se " +
            "mapea a la columna del mismo nombre; los campos que no sean columnas se ignoran. Incluye 'archivo' " +
            "con el nombre del archivo de origen cuando lo tengas. Devuelve cuantos productos se cargaron.",
            """{"type":"object","properties":{"productos":{"type":"array","items":{"type":"object"},"description":"Lista de productos (objeto campo->valor) a cargar en el contenedor 'Productos'"}},"required":["productos"],"additionalProperties":false}"""),
        new AiToolSpec(
            "consultar_productos",
            "Busca productos en el contenedor 'Productos' del tenant por texto (coincide en nombre, referencia, " +
            "marca y categoria, sin distinguir mayusculas). Devuelve para cada coincidencia todos los campos " +
            "(categoria, marca, nombre, referencia, especificaciones, precio, moneda, proveedor, vigencia, " +
            "pagina, archivo) y la fecha de actualizacion del dato. Usala para responder cuanto cuesta un articulo.",
            """{"type":"object","properties":{"texto":{"type":"string","description":"Texto a buscar (nombre/referencia/marca/categoria); vacio = todos"},"limite":{"type":"integer","description":"Maximo de resultados (default 20, tope 50)"}},"additionalProperties":false}"""),
        new AiToolSpec(
            "consultar_contenedor",
            "Busca filas en un contenedor de datos del tenant por texto (coincidencia en nombre/referencia/marca/" +
            "categoria si existen, si no en todos los campos; sin distinguir mayusculas). 'contenedor' es el nombre " +
            "EXACTO (ver listar_contenedores). Devuelve por cada coincidencia todos sus campos y la fecha de " +
            "actualizacion del dato. Usala para responder consultas de lectura sobre un contenedor.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"Nombre exacto del contenedor"},"texto":{"type":"string","description":"Texto a buscar; vacio = todas las filas"},"limite":{"type":"integer","description":"Maximo de resultados (default 20, tope 50)"}},"required":["contenedor"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch { return Err("Los argumentos no son un JSON valido."); }

        try
        {
            return toolName switch
            {
                "listar_contenedores" => await ListContainersAsync(cancellationToken),
                "agregar_filas_a_contenedor" => await AddRowsAsync(Str(args, "contenedor"), GetProp(args, "filas"), cancellationToken),
                "actualizar_filas_por_clave" => await UpdateRowsByKeyAsync(args, cancellationToken),
                // Atajo pedido por el prompt del agente Clasificador: filas -> contenedor fijo "Productos".
                "cargar_productos" => await AddRowsAsync("Productos", GetProp(args, "productos"), cancellationToken),
                // Lectura/consulta (ADR-0103 nota lectura): atajo sobre "Productos" + generico por nombre.
                "consultar_productos" => await QueryRowsAsync("Productos", Str(args, "texto"), Int(args, "limite"), cancellationToken),
                "consultar_contenedor" => await QueryRowsAsync(Str(args, "contenedor"), Str(args, "texto"), Int(args, "limite"), cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgentToolResult> ListContainersAsync(CancellationToken ct)
    {
        var containers = await _db.DataContainers.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        if (containers.Count == 0) { return Ok(new { ok = true, contenedores = Array.Empty<object>() }); }

        var ids = containers.Select(c => c.Id).ToList();
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => ids.Contains(c.ContainerId)
                && c.Type != DataContainerColumnType.Submodel && c.Type != DataContainerColumnType.RelationMany)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.ContainerId, c.Name })
            .ToListAsync(ct);
        var byContainer = cols.GroupBy(c => c.ContainerId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        return Ok(new
        {
            ok = true,
            contenedores = containers.Select(c => new
            {
                nombre = c.Name,
                columnas = byContainer.TryGetValue(c.Id, out var cc) ? cc : new List<string>()
            })
        });
    }

    /// <summary>
    /// Inserta N filas en el contenedor <paramref name="containerName"/> (por nombre, tenant-scoped). Cada item
    /// de <paramref name="filas"/> es un objeto columna->valor; las claves que no sean columnas se ignoran.
    /// Todo se guarda como texto (EAV). Solo inserta; no borra ni actualiza.
    /// </summary>
    private async Task<AgentToolResult> AddRowsAsync(string? containerName, JsonElement? filas, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tenantId) { return Err("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(containerName)) { return Err("Falta el nombre del contenedor (contenedor)."); }
        if (filas is not { } filasEl || filasEl.ValueKind != JsonValueKind.Array || filasEl.GetArrayLength() == 0)
        {
            return Err("No hay filas para cargar: envia una lista de objetos (filas/productos).");
        }

        var name = containerName.Trim();
        var container = await _db.DataContainers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower(), ct);
        if (container is null)
        {
            var nombres = await _db.DataContainers.AsNoTracking().OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(ct);
            return Err($"No existe un contenedor de datos llamado '{name}'. Contenedores disponibles: {string.Join(", ", nombres)}.");
        }

        // Columnas escalares del contenedor (se excluyen Submodel y RelationMany: no tienen celda simple).
        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == container.Id
                && c.Type != DataContainerColumnType.Submodel && c.Type != DataContainerColumnType.RelationMany)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var colByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in columns)
        {
            var key = c.Name.Trim();
            if (!colByName.ContainsKey(key)) { colByName[key] = c.Id; }
        }

        var cargados = 0;
        var celdas = 0;
        foreach (var item in filasEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { continue; }
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = container.Id };
            _db.DataContainerRows.Add(row);
            foreach (var prop in item.EnumerateObject())
            {
                if (!colByName.TryGetValue(prop.Name.Trim(), out var colId)) { continue; } // no es columna: se ignora
                _db.DataContainerCells.Add(new DataContainerCell
                {
                    TenantId = tenantId,
                    RowId = row.Id,
                    ColumnId = colId,
                    Value = CellValue(prop.Value)
                });
                celdas++;
            }
            cargados++;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, cargados, celdas, contenedor = container.Name });
    }

    /// <summary>Tope de filas por llamada a actualizar_filas_por_clave (un lote del agente, no una carga masiva).</summary>
    private const int MaxFilasPorActualizacion = 500;

    /// <summary>
    /// ACTUALIZAR POR CLAVE (ADR-0197): ubica cada fila por su columna clave y escribe solo las columnas recibidas.
    /// No duplica; las claves inexistentes se reportan (o se crean con crear_si_falta). Todo en un SaveChanges.
    /// </summary>
    private async Task<AgentToolResult> UpdateRowsByKeyAsync(JsonElement args, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tenantId) { return Err("Sin tenant activo."); }
        var keyCol = Str(args, "columna_clave")?.Trim();
        if (string.IsNullOrEmpty(keyCol)) { return Err("Falta la columna clave (columna_clave)."); }
        if (GetProp(args, "filas") is not { } filasEl || filasEl.ValueKind != JsonValueKind.Array || filasEl.GetArrayLength() == 0)
        {
            return Err("No hay filas: envia una lista de objetos columna->valor que incluyan la columna clave.");
        }
        if (filasEl.GetArrayLength() > MaxFilasPorActualizacion)
        {
            return Err($"Maximo {MaxFilasPorActualizacion} filas por llamada; divide el lote.");
        }

        var (table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (table is null) { return Err(error!); }

        var colByName = await ContenedorFilasPorClave.ColumnasAsync(_db, table.Value.Id, ct);
        if (!colByName.ContainsKey(keyCol))
        {
            return Err($"La tabla '{table.Value.Name}' no tiene la columna '{keyCol}'. Columnas: {string.Join(", ", colByName.Keys)}.");
        }

        var filas = filasEl.EnumerateArray()
            .Where(f => f.ValueKind == JsonValueKind.Object)
            .Select(f => (IReadOnlyDictionary<string, string?>)ContenedorFilasPorClave.ToFila(f))
            .ToList();
        var crear = GetProp(args, "crear_si_falta") is { ValueKind: JsonValueKind.True };

        var r = await ContenedorFilasPorClave.AplicarAsync(_db, tenantId, table.Value.Id, keyCol, colByName, filas, crear, ct);
        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            ok = true,
            tabla = table.Value.Name,
            actualizadas = r.Actualizadas,
            creadas = r.Creadas,
            celdas = r.Celdas,
            sin_clave = r.SinClave,
            no_encontradas = r.NoEncontradas.Take(50).ToList(),
            total_no_encontradas = r.NoEncontradas.Count
        });
    }

    /// <summary>
    /// Resuelve la TABLA destino por nombre (tenant-scoped). Sin 'tabla': 'contenedor' es el nombre de la tabla (como
    /// el resto de herramientas del agente); si se repite en varios contenedores pide la tabla. Con 'tabla':
    /// 'contenedor' es el nombre del contenedor (modelo) y 'tabla' la tabla dentro de el.
    /// </summary>
    private async Task<((Guid Id, string Name)? Table, string? Error)> ResolveTableAsync(string? contenedor, string? tabla, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contenedor)) { return (null, "Falta el nombre del contenedor (contenedor)."); }
        var c = contenedor.Trim().ToLower();
        if (!string.IsNullOrWhiteSpace(tabla))
        {
            var t = tabla.Trim().ToLower();
            var hits = await _db.DataContainers.AsNoTracking()
                .Where(x => x.Name.ToLower() == t && x.ModelId != null
                    && _db.DataModels.Any(m => m.Id == x.ModelId && m.Name.ToLower() == c))
                .Select(x => new { x.Id, x.Name })
                .ToListAsync(ct);
            return hits.Count switch
            {
                1 => ((hits[0].Id, hits[0].Name), null),
                0 => (null, $"No existe la tabla '{tabla}' en el contenedor '{contenedor}'."),
                _ => (null, $"Hay varias tablas '{tabla}' en '{contenedor}'.")
            };
        }

        var byName = await _db.DataContainers.AsNoTracking()
            .Where(x => x.Name.ToLower() == c)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        if (byName.Count == 1) { return ((byName[0].Id, byName[0].Name), null); }
        if (byName.Count > 1) { return (null, $"Hay {byName.Count} tablas llamadas '{contenedor}': envia el nombre del contenedor en 'contenedor' y la tabla en 'tabla'."); }
        var nombres = await _db.DataContainers.AsNoTracking().OrderBy(x => x.Name).Select(x => x.Name).Take(40).ToListAsync(ct);
        return (null, $"No existe un contenedor de datos llamado '{contenedor}'. Disponibles: {string.Join(", ", nombres)}.");
    }

    // Zona del tenant (America/Bogota = UTC-5) para la fecha de actualizacion del dato.
    private static readonly TimeSpan TenantOffset = TimeSpan.FromHours(-5);

    // Columnas por las que se filtra el texto (si existen en el contenedor; si no, se filtra por todas).
    private static readonly HashSet<string> SearchColumnNames =
        new(new[] { "nombre", "referencia", "marca", "categoria" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// LECTURA: busca filas de <paramref name="containerName"/> (por nombre, tenant-scoped, AsNoTracking). Filtra
    /// por <paramref name="texto"/> (substring case-insensitive) en las columnas nombre/referencia/marca/categoria
    /// si existen, si no en todos los valores. Ordena por fecha de actualizacion (UpdatedAt ?? CreatedAt) desc y
    /// aplica <paramref name="limite"/> (default 20, tope 50). Devuelve cada fila como columna->valor + la fecha.
    /// </summary>
    private async Task<AgentToolResult> QueryRowsAsync(string? containerName, string? texto, int? limite, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerName)) { return Err("Falta el nombre del contenedor (contenedor)."); }
        var name = containerName.Trim();
        var container = await _db.DataContainers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower(), ct);
        if (container is null)
        {
            var nombres = await _db.DataContainers.AsNoTracking().OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(ct);
            return Err($"No existe un contenedor de datos llamado '{name}'. Contenedores disponibles: {string.Join(", ", nombres)}.");
        }

        // Columnas escalares (id -> nombre) para reconstruir cada fila.
        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == container.Id
                && c.Type != DataContainerColumnType.Submodel && c.Type != DataContainerColumnType.RelationMany)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var colName = columns.ToDictionary(c => c.Id, c => c.Name);
        var searchColIds = columns.Where(c => SearchColumnNames.Contains(c.Name.Trim())).Select(c => c.Id).ToHashSet();

        var rows = await _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == container.Id)
            .Select(r => new { r.Id, r.CreatedAt, r.UpdatedAt })
            .ToListAsync(ct);
        if (rows.Count == 0) { return Ok(new { ok = true, contenedor = container.Name, total = 0, resultados = Array.Empty<object>() }); }

        var rowIds = rows.Select(r => r.Id).ToList();
        var cells = await _db.DataContainerCells.AsNoTracking()
            .Where(c => rowIds.Contains(c.RowId))
            .Select(c => new { c.RowId, c.ColumnId, c.Value })
            .ToListAsync(ct);
        var cellsByRow = cells.GroupBy(c => c.RowId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.ColumnId, x.Value)).ToList());

        var q = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().ToLowerInvariant();
        var limit = Math.Clamp(limite ?? 20, 1, 50);

        // Filtro + orden por fecha (UpdatedAt ?? CreatedAt) desc, en memoria (contenedor pequeno).
        var matched = rows
            .Select(r => new
            {
                Fecha = r.UpdatedAt ?? r.CreatedAt,
                Cells = cellsByRow.TryGetValue(r.Id, out var list) ? list : new List<(Guid ColumnId, string? Value)>()
            })
            .Where(x =>
            {
                if (q is null) { return true; }
                var toSearch = searchColIds.Count > 0
                    ? x.Cells.Where(c => searchColIds.Contains(c.ColumnId))
                    : x.Cells;
                return toSearch.Any(c => c.Value is not null && c.Value.ToLowerInvariant().Contains(q));
            })
            .ToList();

        var resultados = matched
            .OrderByDescending(x => x.Fecha)
            .Take(limit)
            .Select(x =>
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (columnId, value) in x.Cells)
                {
                    if (colName.TryGetValue(columnId, out var cn) && !dict.ContainsKey(cn)) { dict[cn] = value; }
                }
                dict["fecha_actualizacion"] = x.Fecha.ToOffset(TenantOffset).ToString("yyyy-MM-dd");
                return dict;
            })
            .ToList();

        return Ok(new { ok = true, contenedor = container.Name, total = matched.Count, resultados });
    }

    // El contenedor guarda TODO como texto (EAV): numeros/booleanos se serializan a su texto tal cual.
    private static string? CellValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => v.GetRawText()
    };

    private static JsonElement? GetProp(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) ? v : (JsonElement?)null;

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // Lee un entero de un argumento (tolera number o string numerico). Null si falta o no es numero.
    private static int? Int(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) { return null; }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) { return n; }
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) { return s; }
        return null;
    }

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);
}
