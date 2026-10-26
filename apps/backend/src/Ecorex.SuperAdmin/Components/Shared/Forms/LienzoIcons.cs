namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>
/// Set corto de iconos (SVG inline, trazo con currentColor -> respeta tema claro/oscuro) para el control
/// LienzoBloques (ADR-0133, HAND-OFF iconos por bloque). Compartido por el renderer (DynamicFormRenderer) y el
/// disenador (FormDesigner). El bloque guarda el NOMBRE del icono; aqui se resuelve a su markup. Nombre vacio o
/// desconocido -> sin icono (null), el bloque se ve solo con su titulo (comportamiento previo).
/// </summary>
public static class LienzoIcons
{
    /// <summary>Nombre -> contenido interno del &lt;svg&gt; (paths/figuras). El &lt;svg&gt; lo arma quien renderiza.</summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["handshake"] = "<path d=\"m11 17 2 2a1 1 0 1 0 3-3\"/><path d=\"m14 14 2.5 2.5a1 1 0 1 0 3-3l-3.88-3.88a3 3 0 0 0-4.24 0l-.88.88a1 1 0 1 1-3-3l2.81-2.81a5.79 5.79 0 0 1 7.06-.87l.47.28a2 2 0 0 0 1.42.25L21 4\"/><path d=\"m21 3 1 11h-2\"/><path d=\"M3 3 2 14l6.5 6.5a1 1 0 1 0 3-3\"/><path d=\"M3 4h8\"/>",
        ["activity"] = "<path d=\"M22 12h-4l-3 9L9 3l-3 9H2\"/>",
        ["box"] = "<path d=\"M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z\"/><path d=\"m3.3 7 8.7 5 8.7-5\"/><path d=\"M12 22V12\"/>",
        ["star"] = "<polygon points=\"12 2 15 9 22 9.5 17 14.5 18.5 21.5 12 17.5 5.5 21.5 7 14.5 2 9.5 9 9\"/>",
        ["heart"] = "<path d=\"M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.6l-1-1a5.5 5.5 0 0 0-7.8 7.8l1 1L12 21l7.8-7.6 1-1a5.5 5.5 0 0 0 0-7.8z\"/>",
        ["truck"] = "<path d=\"M14 18V6a1 1 0 0 0-1-1H2a1 1 0 0 0-1 1v11a1 1 0 0 0 1 1h1\"/><path d=\"M14 9h4l3 3v5a1 1 0 0 1-1 1h-1\"/><circle cx=\"6\" cy=\"18\" r=\"2\"/><circle cx=\"18\" cy=\"18\" r=\"2\"/>",
        ["users"] = "<path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2\"/><circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M22 21v-2a4 4 0 0 0-3-3.87\"/><path d=\"M16 3.13a4 4 0 0 1 0 7.75\"/>",
        ["trending-up"] = "<polyline points=\"22 7 13.5 15.5 8.5 10.5 2 17\"/><polyline points=\"16 7 22 7 22 13\"/>",
        ["trending-down"] = "<polyline points=\"22 17 13.5 8.5 8.5 13.5 2 7\"/><polyline points=\"16 17 22 17 22 11\"/>",
        ["target"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><circle cx=\"12\" cy=\"12\" r=\"5\"/><circle cx=\"12\" cy=\"12\" r=\"1\"/>",
        ["gift"] = "<rect x=\"3\" y=\"8\" width=\"18\" height=\"4\" rx=\"1\"/><path d=\"M12 8v13\"/><path d=\"M19 12v7a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-7\"/><path d=\"M7.5 8a2.5 2.5 0 0 1 0-5C11 3 12 8 12 8s1-5 4.5-5a2.5 2.5 0 0 1 0 5\"/>",
        ["wallet"] = "<path d=\"M19 7V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-2\"/><path d=\"M21 12h-6a2 2 0 0 0 0 4h6v-4z\"/>",
        ["coins"] = "<circle cx=\"8\" cy=\"8\" r=\"6\"/><path d=\"M18.09 10.37A6 6 0 1 1 10.34 18\"/><path d=\"M7 6h1v4\"/>",
        ["share"] = "<circle cx=\"18\" cy=\"5\" r=\"3\"/><circle cx=\"6\" cy=\"12\" r=\"3\"/><circle cx=\"18\" cy=\"19\" r=\"3\"/><path d=\"m8.6 13.5 6.8 4M15.4 6.5 8.6 10.5\"/>",
        ["checklist"] = "<path d=\"M9 6h11M9 12h11M9 18h11\"/><path d=\"M4 6l1 1 2-2M4 12l1 1 2-2M4 18l1 1 2-2\"/>",
    };

    /// <summary>Orden de la lista corta para el selector del disenador.</summary>
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "handshake", "activity", "box", "star", "heart", "truck", "users",
        "trending-up", "trending-down", "target", "gift", "wallet", "coins", "share", "checklist"
    };

    public static bool Has(string? name) => !string.IsNullOrWhiteSpace(name) && Map.ContainsKey(name!.Trim());

    /// <summary>Markup interno del icono (paths), o null si el nombre es vacio/desconocido.</summary>
    public static string? Inner(string? name)
        => !string.IsNullOrWhiteSpace(name) && Map.TryGetValue(name!.Trim(), out var svg) ? svg : null;
}
