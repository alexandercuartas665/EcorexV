using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Ingesta de las filas extraidas por un flujo (deterministas Extract y paso de IA), compartida por el
/// runtime determinista y el orquestador de IA. Convierte el resultado de un Eval / la salida del
/// agente en filas y las mete en la tabla destino reusando el nucleo <see cref="IRowIngestService"/>.
/// </summary>
public static class ScrapeRowIngest
{
    /// <summary>Convierte el Value de un Eval (que WebView2 serializa a JSON) en filas campo->valor. El
    /// script Extract debe evaluar a un arreglo de objetos planos; si por error llega como cadena JSON
    /// (doble codificada), se desanida una vez.</summary>
    public static List<IReadOnlyDictionary<string, string?>> ParseRows(string? value)
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        if (string.IsNullOrWhiteSpace(value)) { return rows; }

        JsonElement root;
        try { using var doc = JsonDocument.Parse(value); root = doc.RootElement.Clone(); }
        catch { return rows; }

        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (string.IsNullOrWhiteSpace(inner)) { return rows; }
            try { using var doc2 = JsonDocument.Parse(inner); root = doc2.RootElement.Clone(); }
            catch { return rows; }
        }

        if (root.ValueKind == JsonValueKind.Object) { rows.Add(ToRow(root)); }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in root.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object) { rows.Add(ToRow(el)); }
            }
        }
        return rows;
    }

    /// <summary>Ingiere filas (ya materializadas) en la tabla destino, modo Append (cada corrida agrega).
    /// Devuelve (insertadas, actualizadas, borradas). Resuelve el mapeo campo->columna: si hay
    /// <paramref name="mappingJson"/> (campo->nombreColumna) lo invierte; si no, identidad por nombre.</summary>
    public static async Task<(int Inserted, int Updated, int Deleted)> IngestAsync(
        IRowIngestService ingest, IApplicationDbContext db, Guid containerId, Guid tenantId,
        string? mappingJson, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct)
    {
        if (rows.Count == 0) { return (0, 0, 0); }
        var mapping = await BuildMappingAsync(db, containerId, mappingJson, ct);
        if (mapping.Count == 0)
        {
            throw new InvalidOperationException(
                "El mapeo no apunta a ninguna columna escalar de la tabla destino.");
        }
        var session = ingest.CreateSession(containerId, tenantId, mapping, ApiImportMode.Append, null);
        await session.PrepareAsync(ct);
        await session.IngestChunkAsync(rows, ct);
        return (session.Inserted, session.Updated, session.Deleted);
    }

    /// <summary>columnId -> campo-del-resultado, que consume IRowIngestService.</summary>
    public static async Task<Dictionary<Guid, string>> BuildMappingAsync(
        IApplicationDbContext db, Guid containerId, string? mappingJson, CancellationToken ct)
    {
        var columns = await db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == containerId && (
                c.Type == DataContainerColumnType.Text || c.Type == DataContainerColumnType.Number ||
                c.Type == DataContainerColumnType.Decimal || c.Type == DataContainerColumnType.Date ||
                c.Type == DataContainerColumnType.Boolean))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var mapping = new Dictionary<Guid, string>();
        if (columns.Count == 0) { return mapping; }

        if (string.IsNullOrWhiteSpace(mappingJson))
        {
            foreach (var c in columns) { mapping[c.Id] = c.Name; }
            return mapping;
        }

        var byName = columns.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? pairs = null;
        try { pairs = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson); } catch { /* invalido */ }
        if (pairs is null) { return mapping; }
        foreach (var (field, colName) in pairs)
        {
            if (!string.IsNullOrWhiteSpace(colName) && byName.TryGetValue(colName, out var colId))
            {
                mapping[colId] = field;
            }
        }
        return mapping;
    }

    private static IReadOnlyDictionary<string, string?> ToRow(JsonElement obj)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.EnumerateObject())
        {
            row[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Null => null,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.GetRawText()
            };
        }
        return row;
    }
}
