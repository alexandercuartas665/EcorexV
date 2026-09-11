using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Lectura LIGERA de un paquete de flujo (ADR-0097 B3): cuenta los formularios vinculados a los nodos sin
/// materializar el grafo, para que el asistente de "Traer" pregunte si migrarlos. Usa las mismas opciones
/// de serializacion que <see cref="FlowPackageService"/> (enums como texto).
/// </summary>
public static class FlowPackageInspector
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Devuelve si el paquete trae formularios en los nodos y cuantos. Si el JSON no se puede
    /// leer, devuelve (false, 0) y que el import decida.</summary>
    public static (bool HasNodeForms, int Count) CountNodeForms(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return (false, 0); }
        try
        {
            var pkg = JsonSerializer.Deserialize<FlowPackage>(json, Json);
            if (pkg is null) { return (false, 0); }
            var n = pkg.Nodes.Sum(node => node.Forms?.Count ?? 0);
            return (n > 0, n);
        }
        catch (JsonException) { return (false, 0); }
    }
}
