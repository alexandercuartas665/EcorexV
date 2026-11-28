using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace Ecorex.Application.Reporting;

/// <summary>Donde y con que datos se dibuja un grafico en la hoja del reporte (filas 1-based, ancla 0-based).</summary>
internal sealed record ReportExcelChartPlacement(
    ReportExcelChartKind Kind,
    string Title,
    int AnchorColumn,
    int AnchorRow,
    int WidthPx,
    int HeightPx,
    int HeaderRow,
    int FirstRow,
    int LastRow,
    int SeriesColumns,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> SeriesNames,
    IReadOnlyList<IReadOnlyList<double?>> Values);

/// <summary>
/// Graficos NATIVOS de Excel (DrawingML) ligados a las celdas de la hoja del reporte. ClosedXML no sabe
/// dibujar graficos, asi que se agregan con el SDK OpenXML sobre el libro ya guardado. Cada serie apunta a
/// su rango ('Reporte'!$B$5:$B$12) y lleva cache de valores para que se vea sin recalcular. Paleta = la de
/// los graficos del tablero (EChartBuilders.Palette).
/// </summary>
internal static class ReportExcelCharts
{
    private const long EmuPerPx = 9525;
    private const int DataLabelsMaxPoints = 15;

    private static readonly string[] Palette =
        { "4F46E5", "0EA5E9", "10B981", "F59E0B", "EF4444", "8B5CF6", "EC4899", "14B8A6" };

    private const string AxisColor = "8A8A94";
    private const string GridColor = "E5E7EB";

    public static byte[] AddCharts(byte[] xlsx, string sheetName, IReadOnlyList<ReportExcelChartPlacement> charts)
    {
        using var ms = new MemoryStream();
        ms.Write(xlsx, 0, xlsx.Length);
        ms.Position = 0;

        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var wbPart = doc.WorkbookPart ?? throw new InvalidOperationException("Libro sin WorkbookPart.");
            var sheet = wbPart.Workbook.Descendants<S.Sheet>().FirstOrDefault(s => s.Name?.Value == sheetName)
                ?? throw new InvalidOperationException($"No existe la hoja '{sheetName}'.");
            var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!.Value!);

            var drawingsPart = wsPart.DrawingsPart ?? wsPart.AddNewPart<DrawingsPart>();
            drawingsPart.WorksheetDrawing ??= new Xdr.WorksheetDrawing();
            var drawing = drawingsPart.WorksheetDrawing;
            drawing.AddNamespaceDeclaration("xdr", "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing");
            drawing.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");

            if (!wsPart.Worksheet.Elements<S.Drawing>().Any())
            {
                InsertDrawingElement(wsPart.Worksheet, new S.Drawing { Id = wsPart.GetIdOfPart(drawingsPart) });
            }

            var shapeId = (uint)drawing.Elements<Xdr.OneCellAnchor>().Count() + 2;
            foreach (var ch in charts)
            {
                var chartPart = drawingsPart.AddNewPart<ChartPart>();
                chartPart.ChartSpace = BuildChartSpace(ch, sheetName);
                chartPart.ChartSpace.Save();
                drawing.Append(BuildAnchor(ch, drawingsPart.GetIdOfPart(chartPart), shapeId++));
            }

            drawing.Save();
            wsPart.Worksheet.Save();
        }

        return ms.ToArray();
    }

    /// <summary>El elemento drawing va en una posicion fija del esquema de la hoja: antes de legacyDrawing,
    /// tableParts, extLst, etc. Si no hay ninguno de esos, al final.</summary>
    private static void InsertDrawingElement(S.Worksheet ws, S.Drawing drawing)
    {
        var after = ws.ChildElements.FirstOrDefault(e =>
            e is S.LegacyDrawing or S.LegacyDrawingHeaderFooter or S.DrawingHeaderFooter or S.Picture
              or S.OleObjects or S.Controls or S.WebPublishItems or S.TableParts or S.WorksheetExtensionList);
        if (after is null)
        {
            ws.Append(drawing);
        }
        else
        {
            ws.InsertBefore(drawing, after);
        }
    }

    private static Xdr.OneCellAnchor BuildAnchor(ReportExcelChartPlacement ch, string relId, uint shapeId)
    {
        return new Xdr.OneCellAnchor(
            new Xdr.FromMarker(
                new Xdr.ColumnId(ch.AnchorColumn.ToString(CultureInfo.InvariantCulture)),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(ch.AnchorRow.ToString(CultureInfo.InvariantCulture)),
                new Xdr.RowOffset("0")),
            new Xdr.Extent { Cx = ch.WidthPx * EmuPerPx, Cy = ch.HeightPx * EmuPerPx },
            new Xdr.GraphicFrame(
                new Xdr.NonVisualGraphicFrameProperties(
                    new Xdr.NonVisualDrawingProperties { Id = shapeId, Name = $"Grafico {shapeId - 1}" },
                    new Xdr.NonVisualGraphicFrameDrawingProperties()),
                new Xdr.Transform(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 0, Cy = 0 }),
                new A.Graphic(new A.GraphicData(new C.ChartReference { Id = relId })
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart",
                }))
            { Macro = "" },
            new Xdr.ClientData());
    }

    private static C.ChartSpace BuildChartSpace(ReportExcelChartPlacement ch, string sheetName)
    {
        var plotArea = new C.PlotArea(new C.Layout());
        var showLegend = false;

        switch (ch.Kind)
        {
            case ReportExcelChartKind.Doughnut:
                plotArea.Append(Doughnut(ch, sheetName));
                showLegend = true;
                break;
            case ReportExcelChartKind.Line:
                plotArea.Append(LineChart(ch, sheetName, 0, ch.SeriesColumns, 1u, 2u, smooth: true));
                plotArea.Append(CategoryAxis(1u, 2u, C.AxisPositionValues.Bottom, reversed: false, deleted: false));
                plotArea.Append(ValueAxis(2u, 1u, C.AxisPositionValues.Left, gridlines: true, crossesMax: false, max100: false));
                showLegend = ch.SeriesColumns > 1;
                break;
            case ReportExcelChartKind.Pareto:
                plotArea.Append(BarChart(ch, sheetName, 0, 1, horizontal: false, 1u, 2u));
                plotArea.Append(LineChart(ch, sheetName, 1, 1, 3u, 4u, smooth: false));
                plotArea.Append(CategoryAxis(1u, 2u, C.AxisPositionValues.Bottom, reversed: false, deleted: false));
                plotArea.Append(ValueAxis(2u, 1u, C.AxisPositionValues.Left, gridlines: true, crossesMax: false, max100: false));
                plotArea.Append(CategoryAxis(3u, 4u, C.AxisPositionValues.Bottom, reversed: false, deleted: true));
                plotArea.Append(ValueAxis(4u, 3u, C.AxisPositionValues.Right, gridlines: false, crossesMax: true, max100: true));
                showLegend = true;
                break;
            case ReportExcelChartKind.Bar:
                plotArea.Append(BarChart(ch, sheetName, 0, ch.SeriesColumns, horizontal: true, 1u, 2u));
                // Categorias de arriba hacia abajo (como en el tablero): eje invertido y valores abajo.
                plotArea.Append(CategoryAxis(1u, 2u, C.AxisPositionValues.Left, reversed: true, deleted: false));
                plotArea.Append(ValueAxis(2u, 1u, C.AxisPositionValues.Bottom, gridlines: true, crossesMax: true, max100: false));
                showLegend = ch.SeriesColumns > 1;
                break;
            default:
                plotArea.Append(BarChart(ch, sheetName, 0, ch.SeriesColumns, horizontal: false, 1u, 2u));
                plotArea.Append(CategoryAxis(1u, 2u, C.AxisPositionValues.Bottom, reversed: false, deleted: false));
                plotArea.Append(ValueAxis(2u, 1u, C.AxisPositionValues.Left, gridlines: true, crossesMax: false, max100: false));
                showLegend = ch.SeriesColumns > 1;
                break;
        }

        var chart = new C.Chart();
        if (!string.IsNullOrWhiteSpace(ch.Title))
        {
            chart.Append(Title(ch.Title.Trim()));
        }
        chart.Append(new C.AutoTitleDeleted { Val = string.IsNullOrWhiteSpace(ch.Title) });
        chart.Append(plotArea);
        if (showLegend)
        {
            chart.Append(new C.Legend(
                new C.LegendPosition { Val = C.LegendPositionValues.Bottom },
                new C.Overlay { Val = false },
                TextProps(900, AxisColor)));
        }
        chart.Append(new C.PlotVisibleOnly { Val = true });
        chart.Append(new C.DisplayBlanksAs { Val = C.DisplayBlanksAsValues.Gap });

        var space = new C.ChartSpace(
            new C.EditingLanguage { Val = "es-CO" },
            new C.RoundedCorners { Val = false },
            chart,
            new C.ChartShapeProperties(new A.NoFill(), new A.Outline(new A.NoFill())));
        space.AddNamespaceDeclaration("c", "http://schemas.openxmlformats.org/drawingml/2006/chart");
        space.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        space.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        return space;
    }

    private static C.BarChart BarChart(ReportExcelChartPlacement ch, string sheet, int firstSeries, int count, bool horizontal, uint catAx, uint valAx)
    {
        var bar = new C.BarChart(
            new C.BarDirection { Val = horizontal ? C.BarDirectionValues.Bar : C.BarDirectionValues.Column },
            new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
            new C.VaryColors { Val = false });

        for (var s = firstSeries; s < firstSeries + count; s++)
        {
            var ser = new C.BarChartSeries(
                new C.Index { Val = (uint)s },
                new C.Order { Val = (uint)s },
                SeriesText(ch, sheet, s),
                Fill(Palette[s % Palette.Length]),
                new C.InvertIfNegative { Val = false });
            if (ch.Categories.Count <= DataLabelsMaxPoints)
            {
                ser.Append(DataLabels(showVal: true, showPercent: false));
            }
            ser.Append(CategoryData(ch, sheet));
            ser.Append(ValuesData(ch, sheet, s));
            bar.Append(ser);
        }

        bar.Append(new C.GapWidth { Val = 60 });
        bar.Append(new C.AxisId { Val = catAx });
        bar.Append(new C.AxisId { Val = valAx });
        return bar;
    }

    private static C.LineChart LineChart(ReportExcelChartPlacement ch, string sheet, int firstSeries, int count, uint catAx, uint valAx, bool smooth)
    {
        var line = new C.LineChart(
            new C.Grouping { Val = C.GroupingValues.Standard },
            new C.VaryColors { Val = false });

        for (var s = firstSeries; s < firstSeries + count; s++)
        {
            // Pareto: el acumulado va en el color de acento (ambar) para separarlo de las barras.
            var color = ch.Kind == ReportExcelChartKind.Pareto ? Palette[3] : Palette[s % Palette.Length];
            var ser = new C.LineChartSeries(
                new C.Index { Val = (uint)s },
                new C.Order { Val = (uint)s },
                SeriesText(ch, sheet, s),
                new C.ChartShapeProperties(new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = color }))
                {
                    Width = 28575,
                    CapType = A.LineCapValues.Round,
                }),
                new C.Marker(
                    new C.Symbol { Val = C.MarkerStyleValues.Circle },
                    new C.Size { Val = 5 },
                    Fill(color)),
                CategoryData(ch, sheet),
                ValuesData(ch, sheet, s),
                new C.Smooth { Val = smooth });
            line.Append(ser);
        }

        line.Append(new C.ShowMarker { Val = true });
        line.Append(new C.AxisId { Val = catAx });
        line.Append(new C.AxisId { Val = valAx });
        return line;
    }

    private static C.DoughnutChart Doughnut(ReportExcelChartPlacement ch, string sheet)
    {
        var ser = new C.PieChartSeries(
            new C.Index { Val = 0u },
            new C.Order { Val = 0u },
            SeriesText(ch, sheet, 0));
        for (var p = 0; p < ch.Categories.Count; p++)
        {
            ser.Append(new C.DataPoint(
                new C.Index { Val = (uint)p },
                new C.Bubble3D { Val = false },
                new C.ChartShapeProperties(
                    new A.SolidFill(new A.RgbColorModelHex { Val = Palette[p % Palette.Length] }),
                    new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = "FFFFFF" })) { Width = 12700 })));
        }
        ser.Append(DataLabels(showVal: false, showPercent: true));
        ser.Append(CategoryData(ch, sheet));
        ser.Append(ValuesData(ch, sheet, 0));

        return new C.DoughnutChart(
            new C.VaryColors { Val = true },
            ser,
            new C.FirstSliceAngle { Val = 0 },
            new C.HoleSize { Val = 55 });
    }

    private static C.CategoryAxis CategoryAxis(uint id, uint cross, C.AxisPositionValues pos, bool reversed, bool deleted)
    {
        return new C.CategoryAxis(
            new C.AxisId { Val = id },
            new C.Scaling(new C.Orientation { Val = reversed ? C.OrientationValues.MaxMin : C.OrientationValues.MinMax }),
            new C.Delete { Val = deleted },
            new C.AxisPosition { Val = pos },
            new C.NumberingFormat { FormatCode = "General", SourceLinked = true },
            new C.MajorTickMark { Val = C.TickMarkValues.None },
            new C.MinorTickMark { Val = C.TickMarkValues.None },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.ChartShapeProperties(new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = GridColor }))),
            TextProps(900, AxisColor),
            new C.CrossingAxis { Val = cross },
            new C.Crosses { Val = C.CrossesValues.AutoZero },
            new C.AutoLabeled { Val = true },
            new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
            new C.LabelOffset { Val = 100 },
            new C.NoMultiLevelLabels { Val = false });
    }

    private static C.ValueAxis ValueAxis(uint id, uint cross, C.AxisPositionValues pos, bool gridlines, bool crossesMax, bool max100)
    {
        var scaling = new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax });
        if (max100)
        {
            scaling.Append(new C.MaxAxisValue { Val = 100 });
            scaling.Append(new C.MinAxisValue { Val = 0 });
        }

        var ax = new C.ValueAxis(
            new C.AxisId { Val = id },
            scaling,
            new C.Delete { Val = false },
            new C.AxisPosition { Val = pos });
        if (gridlines)
        {
            ax.Append(new C.MajorGridlines(new C.ChartShapeProperties(
                new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = GridColor })) { Width = 6350 })));
        }
        ax.Append(new C.NumberingFormat { FormatCode = max100 ? "0\"%\"" : "#,##0", SourceLinked = false });
        ax.Append(new C.MajorTickMark { Val = C.TickMarkValues.None });
        ax.Append(new C.MinorTickMark { Val = C.TickMarkValues.None });
        ax.Append(new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo });
        ax.Append(new C.ChartShapeProperties(new A.Outline(new A.NoFill())));
        ax.Append(TextProps(900, AxisColor));
        ax.Append(new C.CrossingAxis { Val = cross });
        ax.Append(new C.Crosses { Val = crossesMax ? C.CrossesValues.Maximum : C.CrossesValues.AutoZero });
        ax.Append(new C.CrossBetween { Val = C.CrossBetweenValues.Between });
        return ax;
    }

    private static C.DataLabels DataLabels(bool showVal, bool showPercent)
    {
        return new C.DataLabels(
            new C.NumberingFormat { FormatCode = showPercent ? "0%" : "#,##0", SourceLinked = false },
            new C.ChartShapeProperties(new A.NoFill(), new A.Outline(new A.NoFill())),
            TextProps(800, "3F3F4A"),
            new C.ShowLegendKey { Val = false },
            new C.ShowValue { Val = showVal },
            new C.ShowCategoryName { Val = false },
            new C.ShowSeriesName { Val = false },
            new C.ShowPercent { Val = showPercent },
            new C.ShowBubbleSize { Val = false });
    }

    private static C.Title Title(string text)
    {
        return new C.Title(
            new C.ChartText(new C.RichText(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties(new A.DefaultRunProperties { FontSize = 1200, Bold = true }),
                    new A.Run(
                        new A.RunProperties(new A.SolidFill(new A.RgbColorModelHex { Val = "1F1F2E" }))
                        {
                            Language = "es-CO",
                            FontSize = 1200,
                            Bold = true,
                        },
                        new A.Text(text))))),
            new C.Overlay { Val = false });
    }

    private static C.TextProperties TextProps(int size, string color)
    {
        return new C.TextProperties(
            new A.BodyProperties(),
            new A.ListStyle(),
            new A.Paragraph(
                new A.ParagraphProperties(new A.DefaultRunProperties(
                    new A.SolidFill(new A.RgbColorModelHex { Val = color }))
                {
                    FontSize = size,
                }),
                new A.EndParagraphRunProperties { Language = "es-CO" }));
    }

    private static C.ChartShapeProperties Fill(string color)
        => new(new A.SolidFill(new A.RgbColorModelHex { Val = color }));

    private static C.SeriesText SeriesText(ReportExcelChartPlacement ch, string sheet, int s)
    {
        var name = s < ch.SeriesNames.Count ? ch.SeriesNames[s] : "";
        return new C.SeriesText(new C.StringReference(
            new C.Formula(Ref(sheet, s + 1, ch.HeaderRow, ch.HeaderRow)),
            new C.StringCache(
                new C.PointCount { Val = 1u },
                new C.StringPoint(new C.NumericValue(name)) { Index = 0u })));
    }

    private static C.CategoryAxisData CategoryData(ReportExcelChartPlacement ch, string sheet)
    {
        var cache = new C.StringCache(new C.PointCount { Val = (uint)ch.Categories.Count });
        for (var i = 0; i < ch.Categories.Count; i++)
        {
            cache.Append(new C.StringPoint(new C.NumericValue(ch.Categories[i] ?? "")) { Index = (uint)i });
        }
        return new C.CategoryAxisData(new C.StringReference(
            new C.Formula(Ref(sheet, 0, ch.FirstRow, ch.LastRow)),
            cache));
    }

    private static C.Values ValuesData(ReportExcelChartPlacement ch, string sheet, int s)
    {
        var vals = s < ch.Values.Count ? ch.Values[s] : Array.Empty<double?>();
        var cache = new C.NumberingCache(
            new C.FormatCode("General"),
            new C.PointCount { Val = (uint)vals.Count });
        for (var i = 0; i < vals.Count; i++)
        {
            if (vals[i] is double v)
            {
                cache.Append(new C.NumericPoint(new C.NumericValue(v.ToString("R", CultureInfo.InvariantCulture))) { Index = (uint)i });
            }
        }
        return new C.Values(new C.NumberReference(
            new C.Formula(Ref(sheet, s + 1, ch.FirstRow, ch.LastRow)),
            cache));
    }

    /// <summary>'Hoja'!$B$5:$B$12 (col 0-based, filas 1-based).</summary>
    private static string Ref(string sheet, int col, int fromRow, int toRow)
    {
        var letter = ColumnLetter(col + 1);
        var quoted = "'" + sheet.Replace("'", "''") + "'";
        return fromRow == toRow
            ? $"{quoted}!${letter}${fromRow}"
            : $"{quoted}!${letter}${fromRow}:${letter}${toRow}";
    }

    private static string ColumnLetter(int col)
    {
        var s = "";
        while (col > 0)
        {
            var m = (col - 1) % 26;
            s = (char)('A' + m) + s;
            col = (col - 1) / 26;
        }
        return s;
    }
}
