using System.Xml.Linq;

namespace Ecorex.Application.Reporting.Authoring;

/// <summary>
/// Convierte un <see cref="ReportSpec"/> + el shape de su resultado (<see cref="ReportDataSet"/>) en
/// un documento RDL (RDL 2016, el formato de Bold Reports/SSRS): un imprimible con una tabla (Tablix)
/// de una columna por campo del resultado. Es el camino T1/D6 del ADR-0051: la IA o el usuario generan
/// el MISMO artefacto RDL que luego abre el editor/visor Bold.
///
/// El data source es EMBEBIDO ("EcorexTenantSafe"); el visor Bold corre en ProcessingMode.Local y el
/// controller inyecta las filas YA FILTRADAS POR TENANT como ReportDataSource (Name == el nombre del
/// data source), de modo que Bold NUNCA abre una conexion a BD (la ConnectString se ignora en Local).
/// El RDL es RDL 2016 estandar; su estructura se valida en pruebas y el render se verifico en vivo.
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
                // Data source EMBEBIDO. El visor corre en ProcessingMode.Local y el controller inyecta
                // las filas ya filtradas por tenant como ReportDataSource (Name == este DataSource);
                // por eso la ConnectString se IGNORA (nunca se abre una conexion a BD). El sourceKey
                // queda como pista informativa.
                new XElement(R + "ConnectionProperties",
                    new XElement(R + "DataProvider", "System.Data.DataSet"),
                    new XElement(R + "ConnectString", "provided-at-runtime:" + sourceKey))));

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

        // El nombre del DataSet coincide con el del DataSource (y con el ReportDataSource inyectado):
        // asi Bold enlaza las filas en memoria al dataset en ProcessingMode.Local.
        return new XElement(R + "DataSets",
            new XElement(R + "DataSet",
                new XAttribute("Name", DataSourceName),
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

        // Fila de encabezados (labels estaticos, fondo indigo + texto blanco).
        var headerCells = new XElement(R + "TablixCells");
        foreach (var c in columns)
        {
            headerCells.Add(Cell($"h_{c.Key}", c.DisplayName, isHeader: true, c.Type));
        }

        // Fila de detalle (=Fields!Key.Value), alineada y formateada segun el tipo.
        var detailCells = new XElement(R + "TablixCells");
        foreach (var c in columns)
        {
            detailCells.Add(Cell($"d_{c.Key}", $"=Fields!{c.Key}.Value", isHeader: false, c.Type));
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
            new XElement(R + "DataSetName", DataSourceName),
            new XElement(R + "Top", "0.5in"),
            new XElement(R + "Left", "0in"),
            new XElement(R + "Height", "0.53in"),
            new XElement(R + "Width", "6.5in"));
    }

    private static XElement Cell(string name, string value, bool isHeader, ReportFieldType type)
    {
        var isNumeric = type is ReportFieldType.Number or ReportFieldType.Decimal;
        var align = isHeader ? "Center" : (isNumeric ? "Right" : "Left");

        // Estilo de la CELDA: borde gris, padding y (encabezado) fondo indigo.
        var boxStyle = new XElement(R + "Style",
            new XElement(R + "Border",
                new XElement(R + "Color", "#d1d5db"),
                new XElement(R + "Style", "Solid"),
                new XElement(R + "Width", "0.5pt")),
            new XElement(R + "PaddingLeft", "4pt"),
            new XElement(R + "PaddingRight", "4pt"),
            new XElement(R + "PaddingTop", "2pt"),
            new XElement(R + "PaddingBottom", "2pt"),
            new XElement(R + "VerticalAlign", "Middle"));
        if (isHeader)
        {
            boxStyle.Add(new XElement(R + "BackgroundColor", "#4f46e5"));
        }

        // Estilo del TEXTO: color, negrita (encabezado) y formato numerico/fecha.
        var runStyle = new XElement(R + "Style",
            new XElement(R + "FontSize", "9pt"),
            new XElement(R + "Color", isHeader ? "#ffffff" : "#374151"));
        if (isHeader)
        {
            runStyle.Add(new XElement(R + "FontWeight", "Bold"));
        }

        var format = type switch
        {
            ReportFieldType.Number => "N0",
            ReportFieldType.Decimal => "N2",
            ReportFieldType.Date => "yyyy-MM-dd",
            _ => null
        };
        if (!isHeader && format is not null)
        {
            runStyle.Add(new XElement(R + "Format", format));
        }

        return new XElement(R + "TablixCell",
            new XElement(R + "CellContents",
                new XElement(R + "Textbox",
                    new XAttribute("Name", name),
                    new XElement(R + "CanGrow", "true"),
                    new XElement(R + "KeepTogether", "true"),
                    new XElement(R + "Paragraphs",
                        new XElement(R + "Paragraph",
                            new XElement(R + "TextRuns",
                                new XElement(R + "TextRun",
                                    new XElement(R + "Value", value),
                                    runStyle)),
                            new XElement(R + "Style", new XElement(R + "TextAlign", align)))),
                    boxStyle)));
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
