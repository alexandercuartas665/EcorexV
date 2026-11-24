using System.Globalization;
using System.Text.Json;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// FILAS de un contenedor por el MCP (ADR-0206): cargas chicas y pruebas sin pasar por Excel, BD o API.
/// agregar_filas valida CADA valor contra el tipo de su columna (numero, decimal, fecha, booleano) y las columnas
/// obligatorias; si una fila tiene un error no se guarda NADA (todo o nada, un solo SaveChanges). Con 'columna_clave'
/// hace upsert (actualiza la fila con esa clave o la crea) reusando <see cref="ContenedorFilasPorClave"/>. ver_filas es
/// una lectura acotada (filtros por texto, tope 100) para verificar. Tenant-scoped por el filtro global; permisos del
/// modulo "contenedor-datos" (Ver para leer, Editar para cargar).
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const int MaxFilasPorLlamada = 100;
    private const int MaxFilasLectura = 100;

    private static readonly AiToolSpec[] ContainerRowSpecs =
    {
        new("agregar_filas",
            "Inserta POCAS filas (maximo 100 por llamada) en una tabla de un contenedor: para pruebas y cargas chicas " +
            "(para volumen usa un conector + programacion de importacion). 'filas' = lista de objetos columna->valor " +
            "con los NOMBRES de columna (ver_contenedor_datos). Cada valor se valida contra el tipo de la columna " +
            "(numero entero, decimal con punto, fecha AAAA-MM-DD, booleano true/false) y las columnas obligatorias; " +
            "una columna desconocida o un valor invalido rechaza TODO el lote sin guardar nada. Con 'columna_clave' " +
            "hace upsert: actualiza la fila que tenga esa clave (solo las columnas enviadas) o la crea si no existe.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"id o nombre del contenedor"},"tabla":{"type":"string","description":"id o nombre de la tabla (opcional si el contenedor tiene una sola)"},"filas":{"type":"array","items":{"type":"object"},"minItems":1,"maxItems":100},"columna_clave":{"type":"string","description":"opcional: columna para upsert"}},"required":["contenedor","filas"],"additionalProperties":false}"""),
        new("ver_filas",
            "Lee filas de una tabla de un contenedor para verificar una carga: las mas recientes primero, con " +
            "'filtros' opcionales (columna + texto que contiene, sin distinguir mayusculas) y 'limite' (20 por " +
            "defecto, maximo 100). Devuelve el total que cumple los filtros y las filas como columna->valor.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"},"filtros":{"type":"array","items":{"type":"object","properties":{"columna":{"type":"string"},"valor":{"type":"string"}},"required":["columna","valor"]}},"limite":{"type":"integer","minimum":1,"maximum":100}},"required":["contenedor"],"additionalProperties":false}"""),
    };

    /// <summary>Tabla destino: 'tabla' (id o nombre) o, si el contenedor tiene una sola tabla, esa.</summary>
    private async Task<(ModelTableDto? table, string? error)> ResolveRowTableAsync(JsonElement args, CancellationToken ct)
    {
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return (null, error); }
        var tableKey = Str(args, "tabla");
        if (tableKey is null)
        {
            return detail!.Tables.Count == 1
                ? (detail.Tables[0], null)
                : (null, $"El contenedor '{detail.Name}' tiene {detail.Tables.Count} tablas: indica 'tabla' ({string.Join(", ", detail.Tables.Select(t => t.Name))}).");
        }
        var table = FindTable(detail!, tableKey);
        return table is null ? (null, $"No encontre la tabla '{tableKey}' en el contenedor '{detail!.Name}'.") : (table, null);
    }

    private async Task<McpToolResult> AddRowsAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (!args.TryGetProperty("filas", out var filasEl) || filasEl.ValueKind != JsonValueKind.Array || filasEl.GetArrayLength() == 0)
        {
            return Err("Indica 'filas': una lista de objetos columna->valor.");
        }
        if (filasEl.GetArrayLength() > MaxFilasPorLlamada)
        {
            return Err($"Maximo {MaxFilasPorLlamada} filas por llamada. Para volumen usa un conector y una programacion de importacion.");
        }

        var (table, tError) = await ResolveRowTableAsync(args, ct);
        if (table is null) { return Err(tError!); }
        if (table.DerivedFromContainerId is not null)
        {
            return Err($"La tabla '{table.Name}' es DERIVADA (se calcula desde '{table.DerivedFromName}'): no admite filas a mano.");
        }

        var columns = table.Columns
            .Where(c => c.Type is not (DataContainerColumnType.Submodel or DataContainerColumnType.RelationMany))
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var keyCol = Str(args, "columna_clave")?.Trim();
        if (keyCol is not null && !columns.ContainsKey(keyCol))
        {
            return Err($"La tabla '{table.Name}' no tiene la columna clave '{keyCol}'. Columnas: {string.Join(", ", columns.Keys)}.");
        }

        // Validacion de TODO el lote antes de escribir (todo o nada).
        var errores = new List<string>();
        var filas = new List<Dictionary<string, string?>>();
        var n = 0;
        foreach (var item in filasEl.EnumerateArray())
        {
            n++;
            if (item.ValueKind != JsonValueKind.Object) { errores.Add($"Fila {n}: no es un objeto columna->valor."); continue; }
            var fila = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in item.EnumerateObject())
            {
                if (!columns.TryGetValue(p.Name.Trim(), out var col))
                {
                    errores.Add($"Fila {n}: la columna '{p.Name}' no existe en '{table.Name}'.");
                    continue;
                }
                var (value, valueError) = NormalizeCell(ContenedorFilasPorClave.ToCellValue(p.Value), col.Type);
                if (valueError is not null) { errores.Add($"Fila {n}, columna '{col.Name}': {valueError}"); continue; }
                fila[col.Name.Trim()] = value;
            }
            // Obligatorias: se exigen al CREAR (sin clave) o en el upsert si la fila podria ser nueva (se valida igual).
            foreach (var req in columns.Values.Where(c => c.IsRequired))
            {
                if (!fila.TryGetValue(req.Name.Trim(), out var v) || string.IsNullOrWhiteSpace(v))
                {
                    if (keyCol is null) { errores.Add($"Fila {n}: falta la columna obligatoria '{req.Name}'."); }
                }
            }
            if (keyCol is not null && (!fila.TryGetValue(keyCol, out var kv) || string.IsNullOrWhiteSpace(kv)))
            {
                errores.Add($"Fila {n}: falta el valor de la columna clave '{keyCol}'.");
            }
            filas.Add(fila);
        }
        if (errores.Count > 0)
        {
            return Err($"No se guardo ninguna fila ({errores.Count} errores): " + string.Join(" | ", errores.Take(20))
                + (errores.Count > 20 ? " | ..." : string.Empty));
        }

        if (keyCol is not null)
        {
            var colIds = columns.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase);
            var r = await ContenedorFilasPorClave.AplicarAsync(_db, actor.TenantId, table.Id, keyCol, colIds,
                filas.Cast<IReadOnlyDictionary<string, string?>>().ToList(), crearSiFalta: true, ct);
            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true, tabla = table.Name, creadas = r.Creadas, actualizadas = r.Actualizadas, celdas = r.Celdas });
        }

        var celdas = 0;
        foreach (var fila in filas)
        {
            var row = new DataContainerRow { TenantId = actor.TenantId, ContainerId = table.Id };
            _db.DataContainerRows.Add(row);
            foreach (var (name, value) in fila)
            {
                _db.DataContainerCells.Add(new DataContainerCell { TenantId = actor.TenantId, RowId = row.Id, ColumnId = columns[name].Id, Value = value });
                celdas++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, tabla = table.Name, creadas = filas.Count, celdas, siguiente = "Verifica con ver_filas." });
    }

    /// <summary>Valor de celda normalizado al formato del EAV (cultura invariante) o el motivo por el que no es valido.</summary>
    internal static (string? value, string? error) NormalizeCell(string? raw, DataContainerColumnType type)
    {
        if (raw is null || raw.Trim().Length == 0) { return (null, null); }
        var s = raw.Trim();
        switch (type)
        {
            case DataContainerColumnType.Number:
                return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? (l.ToString(CultureInfo.InvariantCulture), null)
                    : (null, $"'{s}' no es un numero entero.");
            case DataContainerColumnType.Decimal:
                return decimal.TryParse(s, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d)
                    ? (d.ToString(CultureInfo.InvariantCulture), null)
                    : (null, $"'{s}' no es un decimal (usa punto: 1234.50, sin separador de miles).");
            case DataContainerColumnType.Date:
                if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    return (day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null);
                }
                return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto) && s.Contains('T')
                    ? (dto.ToString("o", CultureInfo.InvariantCulture), null)
                    : (null, $"'{s}' no es una fecha (usa AAAA-MM-DD o ISO 8601 con hora).");
            case DataContainerColumnType.Boolean:
                return s.ToLowerInvariant() switch
                {
                    "true" or "1" or "si" or "yes" => ("true", null),
                    "false" or "0" or "no" => ("false", null),
                    _ => (null, $"'{s}' no es booleano (true/false).")
                };
            default:
                return (raw, null);
        }
    }

    private async Task<McpToolResult> ViewRowsAsync(JsonElement args, CancellationToken ct)
    {
        var (table, tError) = await ResolveRowTableAsync(args, ct);
        if (table is null) { return Err(tError!); }
        var columns = table.Columns
            .Where(c => c.Type is not (DataContainerColumnType.Submodel or DataContainerColumnType.RelationMany))
            .OrderBy(c => c.SortOrder)
            .ToList();
        var byName = columns.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        IQueryable<DataContainerRow> q = _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == table.Id && r.ParentRowId == null);
        if (args.TryGetProperty("filtros", out var fs) && fs.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fs.EnumerateArray())
            {
                var filterCol = Str(f, "columna")?.Trim();
                var val = Str(f, "valor");
                if (filterCol is null || val is null) { return Err("Cada filtro necesita 'columna' y 'valor'."); }
                if (!byName.TryGetValue(filterCol, out var colId))
                {
                    return Err($"La tabla '{table.Name}' no tiene la columna '{filterCol}'. Columnas: {string.Join(", ", byName.Keys)}.");
                }
                var needle = val.Trim().ToLower();
                q = q.Where(r => r.Cells.Any(c => c.ColumnId == colId && c.Value != null && c.Value.ToLower().Contains(needle)));
            }
        }

        var total = await q.CountAsync(ct);
        var limit = Math.Clamp(Int(args, "limite") ?? 20, 1, MaxFilasLectura);
        var rows = await q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(limit)
            .Select(r => new { r.Id, r.CreatedAt, Cells = r.Cells.Select(c => new { c.ColumnId, c.Value }).ToList() })
            .ToListAsync(ct);

        var colName = columns.ToDictionary(c => c.Id, c => c.Name);
        return Ok(new
        {
            tabla = table.Name,
            total,
            mostradas = rows.Count,
            columnas = columns.Select(c => c.Name),
            filas = rows.Select(r =>
            {
                var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["_fila_id"] = r.Id.ToString() };
                foreach (var c in r.Cells)
                {
                    if (colName.TryGetValue(c.ColumnId, out var name) && !d.ContainsKey(name)) { d[name] = c.Value; }
                }
                return d;
            })
        });
    }
}
