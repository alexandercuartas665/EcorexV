namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de una plantilla de reporte de plataforma (ADR-0062). Superset de
/// <see cref="ReportDefinitionKind"/>: agrega Panel (dashboard multi-grafico que se resuelve por
/// SourceKey "panel:..."). Al activar la plantilla, Panel se mapea a Dashboard en la instancia
/// (el panel lo detecta la galeria por el prefijo del SourceKey, no por el Kind).
/// </summary>
public enum ReportTemplateKind
{
    Dashboard = 0,
    Printable,
    Panel
}

/// <summary>
/// Naturaleza de la fuente que exige una plantilla (ADR-0062), evaluada en la ACTIVACION:
/// Native (entidad curada, ej. tareas) es activable en cualquier tenant; Container exige que el
/// tenant tenga un contenedor cuyo nombre coincida con RequiredContainerName.
/// </summary>
public enum ReportTemplateSourceKind
{
    Native = 0,
    Container
}
