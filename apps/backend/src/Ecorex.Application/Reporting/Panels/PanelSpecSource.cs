namespace Ecorex.Application.Reporting.Panels;

/// <summary>
/// Marca de fuente de un panel GENERICO por spec (ADR-0066). Un <c>ReportDefinition</c> cuyo SourceKey
/// sea <see cref="SourceKey"/> guarda un <see cref="PanelSpec"/> en SpecJson y lo pinta el unico
/// componente SpecPanelRenderer (no un componente a medida). Convive con los paneles a medida
/// "panel:ocs" / "panel:system-activities" como fallback (la galeria despacha por SourceKey).
/// </summary>
public static class PanelSpecSource
{
    public const string SourceKey = "panel:spec";

    public static bool Is(string? sourceKey) =>
        string.Equals(sourceKey, SourceKey, StringComparison.OrdinalIgnoreCase);
}
