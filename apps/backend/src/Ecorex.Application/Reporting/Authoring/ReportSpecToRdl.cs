using System.Xml.Linq;

namespace Ecorex.Application.Reporting.Authoring;

/// <summary>
/// Convierte un <see cref="ReportSpec"/> + el shape de su resultado (<see cref="ReportDataSet"/>) en
/// un documento RDL (RDL 2016, el formato de Bold Reports/SSRS): un imprimible con una tabla (Tablix)
/// de una columna por campo del resultado. Es el camino T1/D6 del ADR-0051: la IA o el usuario generan
/// el MISMO artefacto RDL que luego abre el editor/visor Bold.
///
/// El binding de datos apunta a un data source JSON logico ("EcorexTenantSafe") que el visor Bold
/// resuelve contra el endpoint tenant-safe (/api/reporting/query); NUNCA una cadena de conexion a BD.
/// El RDL generado es RDL 2016 estandar: su ESTRUCTURA se valida en pruebas; el render pixel-perfect
/// se afina cuando el editor/visor Bold este embebido (requiere la clave de licencia Community).
/// </summary>
public static class ReportSpecToRdl
{
    private static readonly XNamespace R = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";
    private static readonly XNamespace Rd = "http://schemas.microsoft.com/SQLServer/reporting/reportdesigner";

    /// <summary>Nombre logico del data source JSON que el visor Bold enlaza al endpoint tenant-safe.</summary>
    public const string DataSourceName = "EcorexTenantSafe";

    public static string ToRdl(ReportSpec spec, ReportDataSet ds)
    {
        var columns = ds.Columns;
        var title = string.IsNullOrWhiteSpace(spec.Title) ? "Reporte" : spec.Title.Trim();

        var report = new XElement(R + "Report",
            new XAttribute(XNamespace.Xmlns + "rd", Rd.NamespaceName),
            BuildDataSources(spec.SourceKey),
            BuildDataSet(columns),
            new XElement(R + "ReportSections",
                new XElement(R + "ReportSection",
                    new XElement(R + "Body",
                        new XElement(R + "ReportItems",
                            BuildTitleTextbox(title),
                            BuildTablix(columns)),
                        new XElement(R + "Height", "2in")),
                    new XElement(R + "Width", "6.5in"),
                    new XElement(R + "Page",
                        new XElement(R + "PageHeight", "11in"),
                        new XElement(R + "PageWidth", "8.5in"),
                        new XElement(R + "Margin",
                            new XElement(R + "Left", "1in"),
                            new XElement(R + "Right", "1in"),
                            new XElement(R + "Top", "1in"),
                            new XElement(R + "Bottom", "1in"))))));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), report);
        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    private static XElement BuildDataSources(string sourceKey) =>
        new(R + "DataSources",
            new XElement(R + "DataSource",
                new XAttribute("Name", DataSourceName),
                new XElement(R + "ConnectionProperties",
                    new XElement(R + "DataProvider", "JSON"),
                    // El sourceKey viaja como ConnectString logico; el visor Bold lo mapea al endpoint
                    // /api/reporting/query (autenticado por cookie), NUNCA una cadena de conexion a BD.
                    new XElement(R + "ConnectString", "reporting/query?source=" + sourceKey)),
                new XElement(Rd + "SecurityType", "None")));

    private static XElement BuildDataSet(IReadOnlyList<ReportColumn> columns)
    {
        var fields = new XElement(R + "Fields");
        foreach (var c in columns)
        {
            fields.Add(new XElement(R + "Field",
                new XAttribute("Name", c.Key),
                new XElement(R + "DataField", c.Key),
                new XElement(Rd + "TypeName", ClrTypeName(c.Type))));
        }

        return new XElement(R + "DataSets",
            new XElement(R + "DataSet",
                new XAttribute("Name", "Data"),
                new XElement(R + "Query",
                    new XElement(R + "DataSourceName", DataSourceName),
                    new XElement(R + "CommandText", "")),
                fields));
    }

    private static XElement BuildTitleTextbox(string title) =>
        new(R + "Textbox",
            new XAttribute("Name", "ReportTitle"),
            new XElement(R + "CanGrow", "true"),
            new XElement(R + "Paragraphs",
                new XElement(R + "Paragraph",
                    new XElement(R + "TextRuns",
                        new XElement(R + "TextRun",
                            new XElement(R + "Value", title),
                            new XElement(R + "Style",
                                new XElement(R + "FontSize", "14pt"),
                                new XElement(R + "FontWeight", "Bold")))))),
            new XElement(R + "Top", "0in"),
            new XElement(R + "Left", "0in"),
            new XElement(R + "Height", "0.35in"),
            new XElement(R + "Width", "6.5in"));

    private static XElement BuildTablix(IReadOnlyList<ReportColumn> columns)
    {
        var colWidth = columns.Count > 0 ? (6.5 / columns.Count) : 6.5;

        var tablixColumns = new XElement(R + "TablixColumns");
        foreach (var _ in columns)
        {
            tablixColumns.Add(new XElement(R + "TablixColumn",
                new XElement(R + "Width", colWidth.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "in")));
        }

        // Fila de encabezados (labels estaticos).
        var headerCells = new XElement(R + "TablixCells");
        foreach (var c in columns)
        {
            headerCells.Add(Cell($"h_{c.Key}", c.DisplayName, bold: true));
        }

        // Fila de detalle (=Fields!Key.Value).
        var detailCells = new XElement(R + "TablixCells");
        foreach (var c in columns)
        {
            detailCells.Add(Cell($"d_{c.Key}", $"=Fields!{c.Key}.Value", bold: false));
        }

        var tablixRows = new XElement(R + "TablixRows",
            new XElement(R + "TablixRow",
                new XElement(R + "Height", "0.28in"),
                headerCells),
            new XElement(R + "TablixRow",
                new XElement(R + "Height", "0.25in"),
                detailCells));

        var columnMembers = new XElement(R + "TablixMembers");
        foreach (var _ in columns)
        {
            columnMembers.Add(new XElement(R + "TablixMember"));
        }

        return new XElement(R + "Tablix",
            new XAttribute("Name", "Tablix1"),
            new XElement(R + "TablixBody", tablixColumns, tablixRows),
            new XElement(R + "TablixColumnHierarchy", columnMembers),
            new XElement(R + "TablixRowHierarchy",
                new XElement(R + "TablixMembers",
                    // Encabezado: miembro estatico.
                    new XElement(R + "TablixMember"),
                    // Detalle: grupo de detalle sobre el DataSet.
                    new XElement(R + "TablixMember",
                        new XElement(R + "Group", new XAttribute("Name", "Detalle")),
                        new XElement(R + "TablixMembers", new XElement(R + "TablixMember"))))),
            new XElement(R + "DataSetName", "Data"),
            new XElement(R + "Top", "0.5in"),
            new XElement(R + "Left", "0in"),
            new XElement(R + "Height", "0.53in"),
            new XElement(R + "Width", "6.5in"));
    }

    private static XElement Cell(string name, string value, bool bold)
    {
        var style = new XElement(R + "Style",
            new XElement(R + "Border", new XElement(R + "Style", "Solid"), new XElement(R + "Width", "0.5pt")));
        if (bold)
        {
            style.Add(new XElement(R + "FontWeight", "Bold"));
        }

        return new XElement(R + "TablixCell",
            new XElement(R + "CellContents",
                new XElement(R + "Textbox",
                    new XAttribute("Name", name),
                    new XElement(R + "CanGrow", "true"),
                    new XElement(R + "Paragraphs",
                        new XElement(R + "Paragraph",
                            new XElement(R + "TextRuns",
                                new XElement(R + "TextRun",
                                    new XElement(R + "Value", value))))),
                    style)));
    }

    private static string ClrTypeName(ReportFieldType type) => type switch
    {
        ReportFieldType.Number => "System.Int64",
        ReportFieldType.Decimal => "System.Decimal",
        ReportFieldType.Date => "System.DateTime",
        ReportFieldType.Boolean => "System.Boolean",
        _ => "System.String"
    };
}
