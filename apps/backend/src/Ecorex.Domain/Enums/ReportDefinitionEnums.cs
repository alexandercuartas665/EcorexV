namespace Ecorex.Domain.Enums;

/// <summary>Tipo de reporte guardado (Motor de Reportes y BI, ADR-0051).</summary>
public enum ReportDefinitionKind
{
    /// <summary>Dashboard interactivo renderizado con ECharts (Ola 3/4).</summary>
    Dashboard = 0,

    /// <summary>Documento imprimible (RDL, editor/visor Bold, Ola 2).</summary>
    Printable
}

/// <summary>Estado de una definicion de reporte. El proyecto archiva, no borra (sin soft-delete).</summary>
public enum ReportDefinitionStatus
{
    Active = 0,
    Archived
}
