using System.Globalization;
using System.Xml.Linq;

namespace Ecorex.Application.Reporting.Authoring;

/// <summary>
/// Genera un RDL RICO y MULTI-PAGINA (estilo "cuaderno" de Power BI) de las actividades del sistema:
/// Pagina 1 = portada + KPIs + tabla MATRIZ (Estado x Prioridad, con totales); Pagina 2 = GRAFICO de
/// columnas por estado; Pagina 3 = detalle (Tablix). Todo aggrega desde un unico dataset de DETALLE
/// (Number/Title/Status/Priority/CreatedAt/DueDate) que el controller inyecta ya filtrado por tenant
/// (ProcessingMode.Local): el motor de reportes nunca abre una conexion a BD. Exporta a PDF con el
/// visor Bold. RDL 2016 estandar.
/// </summary>
public static class RichActivityReportRdl
{
    private static readonly XNamespace R = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";
    private static readonly XNamespace Rd = "http://schemas.microsoft.com/SQLServer/reporting/reportdesigner";

    // Nombre del data source/dataset embebido; el ReportDataSource inyectado debe llamarse igual.
    public const string DataName = "EcorexTenantSafe";

    private static readonly (string Key, string Type)[] Fields =
    {
        ("Number", "System.String"),
        ("Title", "System.String"),
        ("Status", "System.String"),
        ("Priority", "System.String"),
        ("CreatedAt", "System.DateTime"),
        ("DueDate", "System.DateTime")
    };

    private const string Indigo = "#4F46E5";
    private const string IndigoDark = "#3730A3";
    private const string Grey = "#6B7280";
    private const string Border = "#D1D5DB";

    /// <summary>El spec (consulta tabular tenant-safe) + el RDL rico. Se guardan juntos en la ReportDefinition.</summary>
    public static (ReportSpec Spec, string Rdl) Build()
    {
        var spec = new ReportSpec
        {
            Title = "Reporte de Actividades del Sistema",
            SourceKey = "native:taskitem",
            Chart = ReportChartKind.Table,
            Fields = new List<string> { "Number", "Title", "Status", "Priority", "CreatedAt", "DueDate" }
        };

        var body = new XElement(R + "ReportItems",
            // ---- Pagina 1: portada + KPIs + matriz ----
            Textbox("Titulo", "=\"Reporte de Actividades del Sistema\"", "0in", "0in", "6.9in", "0.4in",
                fontSize: "18pt", bold: true, color: "#111827"),
            Textbox("Subtitulo", "=\"Generado \" & Format(Now(), \"yyyy-MM-dd HH:mm\") & \"  -  Total: \" & Count(Fields!Number.Value, \"" + DataName + "\") & \" actividades\"",
                "0in", "0.42in", "6.9in", "0.25in", fontSize: "9pt", color: Grey),
            Kpi("k1", "Total", "=Count(Fields!Number.Value, \"" + DataName + "\")", "0in", "#64748B"),
            Kpi("k2", "Abiertas", CountIf("Fields!Status.Value=\"Pending\" OrElse Fields!Status.Value=\"Active\" OrElse Fields!Status.Value=\"InProgress\""), "1.17in", "#0EA5E9"),
            Kpi("k3", "En progreso", CountIf("Fields!Status.Value=\"InProgress\""), "2.34in", "#6366F1"),
            Kpi("k4", "Cerradas", CountIf("Fields!Status.Value=\"Done\" OrElse Fields!Status.Value=\"Closed\""), "3.51in", "#10B981"),
            Kpi("k5", "Suspendidas", CountIf("Fields!Status.Value=\"Suspended\""), "4.68in", "#F59E0B"),
            Kpi("k6", "Vencidas", CountIf("Not IsNothing(Fields!DueDate.Value) AndAlso Fields!DueDate.Value < Now() AndAlso Fields!Status.Value<>\"Done\" AndAlso Fields!Status.Value<>\"Closed\""), "5.85in", "#EF4444"),
            Textbox("MatrizTitulo", "=\"Tabla matriz - Estado x Prioridad\"", "0in", "2.05in", "6.9in", "0.28in", fontSize: "12pt", bold: true, color: IndigoDark),
            BuildMatrix("2.4in"),

            // ---- Pagina 2: grafico ----
            Textbox("GraficoTitulo", "=\"Actividades por estado\"", "0in", "5.4in", "6.9in", "0.3in",
                fontSize: "14pt", bold: true, color: IndigoDark, pageBreakStart: true),
            BuildColumnChart("5.75in"),

            // ---- Pagina 3: detalle ----
            Textbox("DetalleTitulo", "=\"Detalle de actividades\"", "0in", "9.6in", "6.9in", "0.3in",
                fontSize: "14pt", bold: true, color: IndigoDark, pageBreakStart: true),
            BuildDetailTablix("9.95in"));

        var report = new XElement(R + "Report",
            new XAttribute(XNamespace.Xmlns + "rd", Rd.NamespaceName),
            new XElement(Rd + "ReportUnitType", "Inch"),
            BuildDataSource(),
            BuildDataSet(),
            new XElement(R + "ReportSections",
                new XElement(R + "ReportSection",
                    new XElement(R + "Body",
                        body,
                        new XElement(R + "Height", "13in")),
                    new XElement(R + "Width", "6.9in"),
                    new XElement(R + "Page",
                        new XElement(R + "PageHeight", "11in"),
                        new XElement(R + "PageWidth", "8.5in"),
                        new XElement(R + "Margin",
                            new XElement(R + "Left", "0.8in"),
                            new XElement(R + "Right", "0.8in"),
                            new XElement(R + "Top", "0.6in"),
                            new XElement(R + "Bottom", "0.6in"))))));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), report);
        return (spec, doc.Declaration + Environment.NewLine + doc.ToString());
    }

    private static string CountIf(string condition) =>
        "=Sum(iif(" + condition + ", 1, 0), \"" + DataName + "\")";

    // ---- Data source / dataset ----

    private static XElement BuildDataSource() =>
        new(R + "DataSources",
            new XElement(R + "DataSource",
                new XAttribute("Name", DataName),
                new XElement(R + "ConnectionProperties",
                    new XElement(R + "DataProvider", "System.Data.DataSet"),
                    new XElement(R + "ConnectString", "provided-at-runtime"))));

    private static XElement BuildDataSet()
    {
        var fields = new XElement(R + "Fields");
        foreach (var (key, type) in Fields)
        {
            fields.Add(new XElement(R + "Field",
                new XAttribute("Name", key),
                new XElement(R + "DataField", key),
                new XElement(Rd + "TypeName", type)));
        }

        return new XElement(R + "DataSets",
            new XElement(R + "DataSet",
                new XAttribute("Name", DataName),
                new XElement(R + "Query",
                    new XElement(R + "DataSourceName", DataName),
                    new XElement(R + "CommandText", "")),
                fields));
    }

    // ---- Textbox generico ----

    private static XElement Textbox(string name, string value, string left, string top, string width, string height,
        string fontSize = "9pt", bool bold = false, string color = "#374151", string align = "Left",
        string? backgroundColor = null, string? borderColor = null, bool pageBreakStart = false)
    {
        var runStyle = new XElement(R + "Style",
            new XElement(R + "FontSize", fontSize),
            new XElement(R + "Color", color));
        if (bold) { runStyle.Add(new XElement(R + "FontWeight", "Bold")); }

        var boxStyle = new XElement(R + "Style",
            new XElement(R + "PaddingLeft", "3pt"),
            new XElement(R + "PaddingRight", "3pt"),
            new XElement(R + "PaddingTop", "2pt"),
            new XElement(R + "PaddingBottom", "2pt"),
            new XElement(R + "VerticalAlign", "Middle"));
        if (backgroundColor is not null) { boxStyle.Add(new XElement(R + "BackgroundColor", backgroundColor)); }
        if (borderColor is not null)
        {
            boxStyle.Add(new XElement(R + "Border", new XElement(R + "Color", borderColor), new XElement(R + "Style", "Solid"), new XElement(R + "Width", "0.75pt")));
        }

        var tb = new XElement(R + "Textbox",
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
            new XElement(R + "Top", top),
            new XElement(R + "Left", left),
            new XElement(R + "Height", height),
            new XElement(R + "Width", width),
            boxStyle);

        if (pageBreakStart)
        {
            tb.AddFirst(new XElement(R + "PageBreak", new XElement(R + "BreakLocation", "Start")));
        }

        return tb;
    }

    // ---- KPI (tarjeta: valor grande + etiqueta) ----

    private static XElement Kpi(string name, string label, string valueExpr, string left, string accent)
    {
        // El valor lleva fondo del color de acento (banda superior) para diferenciar cada KPI.
        var value = Textbox(name + "_v", "=\"\"", "0in", "0in", "1.05in", "0.06in", "6pt", bold: false, color: accent, align: "Center", backgroundColor: accent, borderColor: Border);
        var num = Textbox(name + "_n", valueExpr, "0in", "0.06in", "1.05in", "0.46in", "22pt", bold: true, color: "#111827", align: "Center", borderColor: Border);
        var lab = Textbox(name + "_l", "=\"" + label + "\"", "0in", "0.52in", "1.05in", "0.22in", "7.5pt", bold: false, color: Grey, align: "Center", borderColor: Border);
        // Rectangle contenedor con POSICION Y TAMANIO reales; los hijos van en coordenadas relativas.
        return new XElement(R + "Rectangle",
            new XAttribute("Name", name + "_box"),
            new XElement(R + "ReportItems", value, num, lab),
            new XElement(R + "KeepTogether", "true"),
            new XElement(R + "Top", "0.72in"),
            new XElement(R + "Left", left),
            new XElement(R + "Height", "0.74in"),
            new XElement(R + "Width", "1.05in"),
            new XElement(R + "Style"));
    }

    // ---- Matriz Estado x Prioridad ----

    private static XElement BuildMatrix(string top)
    {
        XElement HeaderCell(string text) => Textbox("m_h_" + text.GetHashCode(), "=\"" + text + "\"", "0in", "0in", "1in", "0.28in",
            fontSize: "9pt", bold: true, color: "#FFFFFF", align: "Center", backgroundColor: Indigo, borderColor: Border);
        XElement Corner() => Textbox("m_corner", "=\"Estado \\ Prioridad\"", "0in", "0in", "1.6in", "0.28in",
            fontSize: "9pt", bold: true, color: "#FFFFFF", align: "Left", backgroundColor: IndigoDark, borderColor: Border);
        XElement RowHead(string expr) => Textbox("m_rh", expr, "0in", "0in", "1.6in", "0.24in",
            fontSize: "9pt", bold: true, color: "#374151", align: "Left", backgroundColor: "#F9FAFB", borderColor: Border);
        XElement DataCell(string expr, bool total = false) => Textbox("m_d", expr, "0in", "0in", "1in", "0.24in",
            fontSize: "9pt", bold: total, color: total ? IndigoDark : "#111827", align: "Center",
            backgroundColor: total ? "#EEF2FF" : null, borderColor: Border);

        var count = "=Count(Fields!Number.Value)";

        var body = new XElement(R + "TablixBody",
            new XElement(R + "TablixColumns",
                new XElement(R + "TablixColumn", new XElement(R + "Width", "1.6in")),
                new XElement(R + "TablixColumn", new XElement(R + "Width", "1in")),
                new XElement(R + "TablixColumn", new XElement(R + "Width", "1in"))),
            new XElement(R + "TablixRows",
                // Fila de encabezado
                new XElement(R + "TablixRow",
                    new XElement(R + "Height", "0.28in"),
                    new XElement(R + "TablixCells",
                        Cell(Corner()),
                        Cell(HeaderCell("Prioridad").ReplaceValue("=Fields!Priority.Value")),
                        Cell(HeaderCell("Total")))),
                // Fila de datos (grupo Estado)
                new XElement(R + "TablixRow",
                    new XElement(R + "Height", "0.24in"),
                    new XElement(R + "TablixCells",
                        Cell(RowHead("=Fields!Status.Value")),
                        Cell(DataCell(count)),
                        Cell(DataCell(count, total: true)))),
                // Fila de totales
                new XElement(R + "TablixRow",
                    new XElement(R + "Height", "0.24in"),
                    new XElement(R + "TablixCells",
                        Cell(RowHead("=\"Total\"")),
                        Cell(DataCell(count, total: true)),
                        Cell(DataCell(count, total: true))))));

        var colHierarchy = new XElement(R + "TablixColumnHierarchy",
            new XElement(R + "TablixMembers",
                new XElement(R + "TablixMember"), // columna de encabezado de fila (estatica)
                new XElement(R + "TablixMember",
                    new XElement(R + "Group", new XAttribute("Name", "PriorityGroup"),
                        new XElement(R + "GroupExpressions", new XElement(R + "GroupExpression", "=Fields!Priority.Value"))),
                    new XElement(R + "SortExpressions", new XElement(R + "SortExpression", new XElement(R + "Value", "=Fields!Priority.Value")))),
                new XElement(R + "TablixMember"))); // columna Total (estatica)

        var rowHierarchy = new XElement(R + "TablixRowHierarchy",
            new XElement(R + "TablixMembers",
                new XElement(R + "TablixMember"), // fila de encabezado (estatica)
                new XElement(R + "TablixMember",
                    new XElement(R + "Group", new XAttribute("Name", "StatusGroup"),
                        new XElement(R + "GroupExpressions", new XElement(R + "GroupExpression", "=Fields!Status.Value"))),
                    new XElement(R + "SortExpressions", new XElement(R + "SortExpression", new XElement(R + "Value", "=Fields!Status.Value")))),
                new XElement(R + "TablixMember"))); // fila Total (estatica)

        return new XElement(R + "Tablix",
            new XAttribute("Name", "Matriz"),
            body, colHierarchy, rowHierarchy,
            new XElement(R + "DataSetName", DataName),
            new XElement(R + "Top", top),
            new XElement(R + "Left", "0in"),
            new XElement(R + "Height", "0.76in"),
            new XElement(R + "Width", "3.6in"));
    }

    private static XElement Cell(XElement content) =>
        new(R + "TablixCell", new XElement(R + "CellContents", content));

    // ---- Grafico de columnas por estado ----

    private static XElement BuildColumnChart(string top) =>
        new(R + "Chart",
            new XAttribute("Name", "GraficoEstado"),
            new XElement(R + "ChartCategoryHierarchy",
                new XElement(R + "ChartMembers",
                    new XElement(R + "ChartMember",
                        new XElement(R + "Group", new XAttribute("Name", "chartCat"),
                            new XElement(R + "GroupExpressions", new XElement(R + "GroupExpression", "=Fields!Status.Value"))),
                        new XElement(R + "SortExpressions", new XElement(R + "SortExpression", new XElement(R + "Value", "=Fields!Status.Value"))),
                        new XElement(R + "Label", "=Fields!Status.Value")))),
            new XElement(R + "ChartSeriesHierarchy",
                new XElement(R + "ChartMembers", new XElement(R + "ChartMember"))),
            new XElement(R + "ChartData",
                new XElement(R + "ChartSeriesCollection",
                    new XElement(R + "ChartSeries", new XAttribute("Name", "Actividades"),
                        new XElement(R + "ChartDataPoints",
                            new XElement(R + "ChartDataPoint",
                                new XElement(R + "ChartDataPointValues",
                                    new XElement(R + "Y", "=Count(Fields!Number.Value)")),
                                new XElement(R + "ChartDataLabel",
                                    new XElement(R + "Visible", "true"),
                                    new XElement(R + "Style")),
                                new XElement(R + "Style", new XElement(R + "Color", Indigo)))),
                        new XElement(R + "Type", "Column"),
                        new XElement(R + "Style")))),
            new XElement(R + "ChartAreas",
                new XElement(R + "ChartArea", new XAttribute("Name", "Default"),
                    new XElement(R + "CategoryAxes",
                        new XElement(R + "CategoryAxis", new XAttribute("Name", "Primary"),
                            new XElement(R + "Style"),
                            new XElement(R + "Visible", "true"))),
                    new XElement(R + "ValueAxes",
                        new XElement(R + "ValueAxis", new XAttribute("Name", "Primary"),
                            new XElement(R + "Style"),
                            new XElement(R + "Visible", "true"))),
                    new XElement(R + "Style"))),
            new XElement(R + "ChartLegends",
                new XElement(R + "ChartLegend", new XAttribute("Name", "Default"),
                    new XElement(R + "Style"),
                    new XElement(R + "Hidden", "true"))),
            new XElement(R + "Palette", "BrightPastel"),
            new XElement(R + "Style"),
            new XElement(R + "DataSetName", DataName),
            new XElement(R + "Top", top),
            new XElement(R + "Left", "0in"),
            new XElement(R + "Height", "3.4in"),
            new XElement(R + "Width", "6.5in"));

    // ---- Tablix de detalle ----

    private static XElement BuildDetailTablix(string top)
    {
        var cols = new (string Header, string Value, string Width, string Align)[]
        {
            ("Numero", "=Fields!Number.Value", "0.9in", "Left"),
            ("Titulo", "=Fields!Title.Value", "3.0in", "Left"),
            ("Estado", "=Fields!Status.Value", "1.0in", "Left"),
            ("Prioridad", "=Fields!Priority.Value", "0.8in", "Left"),
            ("Creada", "=Fields!CreatedAt.Value", "0.9in", "Left")
        };

        var tablixColumns = new XElement(R + "TablixColumns");
        var headerCells = new XElement(R + "TablixCells");
        var detailCells = new XElement(R + "TablixCells");
        foreach (var c in cols)
        {
            tablixColumns.Add(new XElement(R + "TablixColumn", new XElement(R + "Width", c.Width)));
            headerCells.Add(Cell(Textbox("d_h_" + c.Header, "=\"" + c.Header + "\"", "0in", "0in", c.Width, "0.26in",
                fontSize: "9pt", bold: true, color: "#FFFFFF", align: "Center", backgroundColor: Indigo, borderColor: Border)));
            var fmt = c.Header == "Creada" ? "yyyy-MM-dd" : (string?)null;
            var cell = Textbox("d_d_" + c.Header, c.Value, "0in", "0in", c.Width, "0.22in",
                fontSize: "8.5pt", color: "#374151", align: c.Align, borderColor: Border);
            if (fmt is not null)
            {
                cell.Elements(R + "Paragraphs").Descendants(R + "TextRun").First().Element(R + "Style")!
                    .Add(new XElement(R + "Format", fmt));
            }

            detailCells.Add(Cell(cell));
        }

        var colMembers = new XElement(R + "TablixMembers");
        foreach (var _ in cols) { colMembers.Add(new XElement(R + "TablixMember")); }

        return new XElement(R + "Tablix",
            new XAttribute("Name", "Detalle"),
            new XElement(R + "TablixBody", tablixColumns,
                new XElement(R + "TablixRows",
                    new XElement(R + "TablixRow", new XElement(R + "Height", "0.26in"), headerCells),
                    new XElement(R + "TablixRow", new XElement(R + "Height", "0.22in"), detailCells))),
            new XElement(R + "TablixColumnHierarchy", colMembers),
            new XElement(R + "TablixRowHierarchy",
                new XElement(R + "TablixMembers",
                    new XElement(R + "TablixMember", new XElement(R + "KeepWithGroup", "After"), new XElement(R + "RepeatOnNewPage", "true")),
                    new XElement(R + "TablixMember",
                        new XElement(R + "Group", new XAttribute("Name", "DetalleGrupo")),
                        new XElement(R + "SortExpressions", new XElement(R + "SortExpression", new XElement(R + "Value", "=Fields!CreatedAt.Value"), new XElement(R + "Direction", "Descending")))))),
            new XElement(R + "DataSetName", DataName),
            new XElement(R + "Top", top),
            new XElement(R + "Left", "0in"),
            new XElement(R + "Height", "0.48in"),
            new XElement(R + "Width", "6.6in"));
    }

    // Helper: reemplaza el <Value> del TextRun de un textbox (para reusar la plantilla del header).
    private static XElement ReplaceValue(this XElement textbox, string newValue)
    {
        var v = textbox.Descendants(R + "Value").FirstOrDefault();
        if (v is not null) { v.Value = newValue; }
        return textbox;
    }
}
