using System.Text.Json;

namespace Ecorex.Application.Inventory;

/// <summary>Par etiqueta/valor ad-hoc creado dentro de la ficha del item ("Datos tienda").</summary>
public sealed record DatoTiendaDto(string Etiqueta, string Valor);

/// <summary>
/// Serializa/parsea los "Datos tienda" (Item.DatosTiendaJson) entre la lista de pares y el
/// documento JSON almacenado (jsonb en PG, nvarchar(max) en SQL Server). Ignora filas sin
/// etiqueta y es tolerante a JSON invalido (devuelve lista vacia).
/// </summary>
public static class DatosTiendaJson
{
    public static string? Serialize(IEnumerable<DatoTiendaDto>? datos)
    {
        var list = (datos ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.Etiqueta))
            .Select(d => new DatoTiendaDto(d.Etiqueta.Trim(), (d.Valor ?? string.Empty).Trim()))
            .ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    public static IReadOnlyList<DatoTiendaDto> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<DatoTiendaDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
