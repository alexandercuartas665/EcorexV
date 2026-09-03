using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Helpers PUROS de la fuente reportable de RESPUESTAS DE FORMULARIO (ADR-0068): esquema de clave
/// "form:{code}" (estable entre entornos) y mapeo de tipo de control -> tipo reportable. El control
/// Number del formulario admite decimales (montos), por eso mapea a Decimal (agregable y sin perder
/// precision al convertir). La lectura del jsonb y la agregacion se prueban de extremo a extremo en dev.
/// </summary>
public class FormResponseReportReaderTests
{
    [Theory]
    [InlineData("form:COT", true)]
    [InlineData("form:", true)]
    [InlineData("FORM:cot", true)]
    [InlineData("container:abc", false)]
    [InlineData("native:taskitem", false)]
    public void Handles_OnlyFormKeys(string key, bool expected)
    {
        Assert.Equal(expected, FormResponseReportReader.Handles(key));
    }

    [Fact]
    public void KeyForCode_And_ParseCode_RoundTrip()
    {
        Assert.Equal("form:COT", FormResponseReportReader.KeyForCode("COT"));
        Assert.Equal("COT", FormResponseReportReader.ParseCode("form:COT"));
        Assert.Equal("COT", FormResponseReportReader.ParseCode("form: COT ")); // trim
        Assert.Null(FormResponseReportReader.ParseCode("container:x"));
    }

    [Theory]
    [InlineData(FormControlType.Number, ReportFieldType.Decimal)]   // montos: Decimal, no entero
    [InlineData(FormControlType.Date, ReportFieldType.Date)]
    [InlineData(FormControlType.DateTime, ReportFieldType.Date)]
    [InlineData(FormControlType.Time, ReportFieldType.Date)]
    [InlineData(FormControlType.Toggle, ReportFieldType.Boolean)]
    [InlineData(FormControlType.Text, ReportFieldType.Text)]
    [InlineData(FormControlType.TextArea, ReportFieldType.Text)]
    [InlineData(FormControlType.Select, ReportFieldType.Text)]
    public void MapType_MapsControlToReportType(FormControlType control, ReportFieldType expected)
    {
        Assert.Equal(expected, FormResponseReportReader.MapType(control));
    }
}
