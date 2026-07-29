using System.Xml.Linq;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Authoring;

namespace Ecorex.Application.Tests;

/// <summary>
/// Convertidor ReportSpec -> RDL (ADR-0051, camino T1/D6): el imprimible que abrira el editor/visor
/// Bold se genera desde el MISMO artefacto declarativo. Estas pruebas fijan la ESTRUCTURA del RDL 2016
/// (well-formed, namespace correcto, un Field por columna del resultado, un Tablix con enlace
/// =Fields!X.Value por columna, titulo). El render pixel-perfect se valida al embeber Bold (Ola 2).
/// </summary>
public class ReportRdlTests
{
    private static readonly XNamespace R = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";

    private static ReportDataSet SampleDataSet() => new(
        new[]
        {
            new ReportColumn("Status", "Estado", ReportFieldType.Text),
            new ReportColumn("Count", "Conteo", ReportFieldType.Number)
        },
        new IReadOnlyList<object?>[]
        {
            new object?[] { "Pending", 123L },
            new object?[] { "Done", 4L }
        });

    [Fact]
    public void ToRdl_ProducesWellFormedRdl2016_WithTitleAndDataSet()
    {
        var spec = new ReportSpec { Title = "Actividades por estado", SourceKey = "native:taskitem", Chart = ReportChartKind.Table };

        var rdl = ReportSpecToRdl.ToRdl(spec, SampleDataSet());

        // Well-formed + namespace RDL 2016.
        var doc = XDocument.Parse(rdl);
        Assert.Equal(R + "Report", doc.Root!.Name);

        // DataSet "Data" con un Field por columna del resultado.
        var fields = doc.Descendants(R + "Field").Select(f => f.Attribute("Name")!.Value).ToList();
        Assert.Contains("Status", fields);
        Assert.Contains("Count", fields);

        // Titulo presente.
        Assert.Contains("Actividades por estado", doc.Descendants(R + "Value").Select(v => v.Value));

        // Un Tablix con enlace de datos por columna (=Fields!X.Value).
        Assert.Single(doc.Descendants(R + "Tablix"));
        var values = doc.Descendants(R + "Value").Select(v => v.Value).ToList();
        Assert.Contains("=Fields!Status.Value", values);
        Assert.Contains("=Fields!Count.Value", values);
    }

    [Fact]
    public void ToRdl_BindsToLogicalJsonDataSource_NotAConnectionString()
    {
        var spec = new ReportSpec { Title = "x", SourceKey = "container:abc", Chart = ReportChartKind.Table };

        var rdl = ReportSpecToRdl.ToRdl(spec, SampleDataSet());
        var doc = XDocument.Parse(rdl);

        // Data source JSON logico, apuntando al endpoint tenant-safe; nunca una cadena de conexion a BD.
        Assert.Contains(ReportSpecToRdl.DataSourceName, doc.Descendants(R + "DataSource").Select(d => d.Attribute("Name")!.Value));
        var connect = doc.Descendants(R + "ConnectString").Select(c => c.Value).FirstOrDefault() ?? "";
        Assert.Equal("JSON", doc.Descendants(R + "DataProvider").First().Value);
        Assert.Contains("reporting/query", connect);
        Assert.DoesNotContain("Password", connect, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", connect, StringComparison.OrdinalIgnoreCase);
    }
}
