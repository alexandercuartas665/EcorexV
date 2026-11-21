using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>Resultado de actualizar filas de una tabla por su columna clave (ADR-0197).</summary>
/// <param name="Actualizadas">Filas existentes a las que se les escribio al menos una celda.</param>
/// <param name="Creadas">Filas nuevas (solo con crear_si_falta).</param>
/// <param name="NoEncontradas">Claves que no existen en la tabla (y no se crearon).</param>
/// <param name="SinClave">Filas recibidas sin valor en la columna clave (se ignoran).</param>
/// <param name="Celdas">Celdas escritas (creadas o modificadas).</param>
public sealed record ActualizacionPorClaveResultado(int Actualizadas, int Creadas, IReadOnlyList<string> NoEncontradas, int SinClave, int Celdas);

/// <summary>
/// ACTUALIZAR POR CLAVE en una tabla de un contenedor de datos (EAV, ADR-0197). Ubica cada fila existente por el
/// valor de su columna clave y escribe SOLO las celdas de las columnas recibidas (crea la celda si no existia, no
/// toca las demas). Nunca duplica: si la clave no existe la fila se reporta como no encontrada, salvo que se pida
/// crearla. Tenant-scoped por el filtro global (las celdas y filas de otro tenant no existen aqui). No guarda:
/// el llamador hace SaveChanges (una sola transaccion implicita con el resto de su trabajo).
/// </summary>
public static class ContenedorFilasPorClave
{
    /// <summary>Tope de claves por consulta IN (los motores limitan los parametros; SQL Server ~2100).</summary>
    private const int ChunkSize = 500;

    /// <param name="colByName">Columnas escalares de la tabla (nombre -> id), sin distinguir mayusculas.</param>
    /// <param name="filas">Cada fila: columna -> valor (texto). Las claves que no sean columnas se ignoran.</param>
    public static async Task<ActualizacionPorClaveResultado> AplicarAsync(
        IApplicationDbContext db, Guid tenantId, Guid tableId, string keyColumnName,
        IReadOnlyDictionary<string, Guid> colByName, IReadOnlyList<IReadOnlyDictionary<string, string?>> filas,
        bool crearSiFalta, CancellationToken ct)
    {
        if (!colByName.TryGetValue(keyColumnName.Trim(), out var keyColId))
        {
            throw new InvalidOperationException($"La tabla no tiene la columna clave '{keyColumnName}'.");
        }

        // 1) Clave de cada fila recibida.
        var porClave = new List<(string Key, IReadOnlyDictionary<string, string?> Fila)>();
        var sinClave = 0;
        foreach (var f in filas)
        {
            var key = KeyOf(f, keyColumnName);
            if (key is null) { sinClave++; continue; }
            porClave.Add((key, f));
        }

        // 2) Filas existentes por clave (puede haber duplicados historicos: se actualizan todas).
        var keys = porClave.Select(p => p.Key).Distinct(StringComparer.Ordinal).ToList();
        var rowsByKey = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var chunk in keys.Chunk(ChunkSize))
        {
            var hits = await db.DataContainerCells.AsNoTracking()
                .Where(c => c.ColumnId == keyColId && c.Value != null && chunk.Contains(c.Value))
                .Join(db.DataContainerRows.Where(r => r.ContainerId == tableId && r.ParentRowId == null),
                    c => c.RowId, r => r.Id, (c, r) => new { c.Value, r.Id })
                .ToListAsync(ct);
            foreach (var h in hits)
            {
                if (!rowsByKey.TryGetValue(h.Value!, out var list)) { rowsByKey[h.Value!] = list = new List<Guid>(); }
                if (!list.Contains(h.Id)) { list.Add(h.Id); }
            }
        }

        // 3) Celdas actuales (tracked) de esas filas para las columnas que se van a escribir.
        var colIds = porClave.SelectMany(p => p.Fila.Keys)
            .Select(k => colByName.TryGetValue(k.Trim(), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty && id != keyColId)
            .Distinct().ToList();
        var allRowIds = rowsByKey.Values.SelectMany(v => v).Distinct().ToList();
        var cellIndex = new Dictionary<(Guid Row, Guid Col), DataContainerCell>();
        if (colIds.Count > 0)
        {
            foreach (var chunk in allRowIds.Chunk(ChunkSize))
            {
                var existing = await db.DataContainerCells
                    .Where(c => chunk.Contains(c.RowId) && colIds.Contains(c.ColumnId))
                    .ToListAsync(ct);
                foreach (var c in existing) { cellIndex[(c.RowId, c.ColumnId)] = c; }
            }
        }

        // 4) Escribir.
        var actualizadas = new HashSet<Guid>();
        var creadas = 0;
        var celdas = 0;
        var noEncontradas = new List<string>();
        foreach (var (key, fila) in porClave)
        {
            if (rowsByKey.TryGetValue(key, out var rowIds))
            {
                foreach (var rowId in rowIds)
                {
                    var wrote = false;
                    foreach (var (col, value) in fila)
                    {
                        if (!colByName.TryGetValue(col.Trim(), out var colId) || colId == keyColId) { continue; }
                        if (cellIndex.TryGetValue((rowId, colId), out var cell))
                        {
                            if (!string.Equals(cell.Value, value, StringComparison.Ordinal)) { cell.Value = value; }
                        }
                        else
                        {
                            cell = new DataContainerCell { TenantId = tenantId, RowId = rowId, ColumnId = colId, Value = value };
                            db.DataContainerCells.Add(cell);
                            cellIndex[(rowId, colId)] = cell;
                        }
                        celdas++;
                        wrote = true;
                    }
                    if (wrote) { actualizadas.Add(rowId); }
                }
                continue;
            }

            if (!crearSiFalta) { noEncontradas.Add(key); continue; }

            // Fila nueva con la clave + las columnas recibidas. Se registra para que una segunda fila con la misma
            // clave en el mismo lote actualice esta y no cree otra.
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = tableId };
            db.DataContainerRows.Add(row);
            db.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = keyColId, Value = key });
            celdas++;
            foreach (var (col, value) in fila)
            {
                if (!colByName.TryGetValue(col.Trim(), out var colId) || colId == keyColId) { continue; }
                if (cellIndex.TryGetValue((row.Id, colId), out var dup)) { dup.Value = value; continue; }
                var cell = new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = colId, Value = value };
                db.DataContainerCells.Add(cell);
                cellIndex[(row.Id, colId)] = cell;
                celdas++;
            }
            rowsByKey[key] = new List<Guid> { row.Id };
            creadas++;
        }

        return new ActualizacionPorClaveResultado(actualizadas.Count, creadas, noEncontradas, sinClave, celdas);
    }

    /// <summary>Columnas escalares de una tabla (nombre -> id), sin Submodel ni RelationMany (no tienen celda simple).</summary>
    public static async Task<Dictionary<string, Guid>> ColumnasAsync(IApplicationDbContext db, Guid tableId, CancellationToken ct)
    {
        var columns = await db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == tableId
                && c.Type != DataContainerColumnType.Submodel && c.Type != DataContainerColumnType.RelationMany)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in columns)
        {
            var key = c.Name.Trim();
            if (!map.ContainsKey(key)) { map[key] = c.Id; }
        }
        return map;
    }

    /// <summary>El contenedor guarda TODO como texto (EAV): numeros/booleanos se serializan a su texto tal cual.</summary>
    public static string? ToCellValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => v.GetRawText()
    };

    /// <summary>Objeto JSON -> fila columna->texto.</summary>
    public static Dictionary<string, string?> ToFila(JsonElement obj)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in obj.EnumerateObject()) { d[p.Name.Trim()] = ToCellValue(p.Value); }
        return d;
    }

    private static string? KeyOf(IReadOnlyDictionary<string, string?> fila, string keyColumnName)
    {
        foreach (var (k, v) in fila)
        {
            if (string.Equals(k.Trim(), keyColumnName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var t = v?.Trim();
                return string.IsNullOrEmpty(t) ? null : t;
            }
        }
        return null;
    }
}
