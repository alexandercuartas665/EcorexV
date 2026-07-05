using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

/// <summary>
/// Parser CSV del Cargador de contactos (000873): delimitadores autodetectados,
/// comillas RFC 4180 (escapes y saltos de linea internos), filas rotas reportadas
/// con numero de linea, lineas vacias ignoradas, BOM y mapeo automatico de columnas.
/// </summary>
public class CsvTableParserTests
{
    // ---------- Delimitadores ----------

    [Fact]
    public void Parse_CommaDelimited_ReadsHeadersAndRows()
    {
        var result = CsvTableParser.Parse("nombre,telefono,email\nJuan,300123,juan@x.com\nAna,301456,ana@x.com");

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "nombre", "telefono", "email" }, result.Table.Headers);
        Assert.Equal(2, result.Table.Rows.Count);
        Assert.Equal(new[] { "Juan", "300123", "juan@x.com" }, result.Table.Rows[0].Fields);
    }

    [Fact]
    public void Parse_SemicolonDelimited_IsAutodetected()
    {
        var result = CsvTableParser.Parse("nombre;telefono;email\nJuan;300123;juan@x.com");

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Table.Headers.Count);
        Assert.Equal("juan@x.com", result.Table.Rows[0].Fields[2]);
    }

    [Fact]
    public void Parse_TabDelimited_IsAutodetected()
    {
        var result = CsvTableParser.Parse("nombre\ttelefono\nJuan\t300123");

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "nombre", "telefono" }, result.Table.Headers);
        Assert.Equal(new[] { "Juan", "300123" }, result.Table.Rows[0].Fields);
    }

    [Fact]
    public void Parse_DelimiterInsideQuotes_DoesNotConfuseAutodetection()
    {
        // La primera linea tiene mas comas DENTRO de comillas que puntos y coma reales fuera.
        var result = CsvTableParser.Parse("\"nombre, apellido y algo, mas\";telefono\nJuan;300123");

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "nombre, apellido y algo, mas", "telefono" }, result.Table.Headers);
    }

    // ---------- Comillas ----------

    [Fact]
    public void Parse_QuotedFieldWithDelimiter_KeepsFieldTogether()
    {
        var result = CsvTableParser.Parse("nombre,empresa\n\"Perez, Juan\",ACME");

        Assert.Empty(result.Errors);
        Assert.Equal("Perez, Juan", result.Table.Rows[0].Fields[0]);
        Assert.Equal("ACME", result.Table.Rows[0].Fields[1]);
    }

    [Fact]
    public void Parse_EscapedQuotes_AreUnescaped()
    {
        var result = CsvTableParser.Parse("nombre,alias\nJuan,\"El \"\"Tigre\"\" Perez\"");

        Assert.Empty(result.Errors);
        Assert.Equal("El \"Tigre\" Perez", result.Table.Rows[0].Fields[1]);
    }

    [Fact]
    public void Parse_NewlineInsideQuotes_StaysInSameField()
    {
        var result = CsvTableParser.Parse("nombre,nota\nJuan,\"linea 1\nlinea 2\"\nAna,simple");

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Table.Rows.Count);
        Assert.Equal("linea 1\nlinea 2", result.Table.Rows[0].Fields[1]);
        // La fila siguiente conserva su numero de linea fisico (el campo citado ocupo 2 lineas).
        Assert.Equal(4, result.Table.Rows[1].LineNumber);
    }

    // ---------- Filas rotas ----------

    [Fact]
    public void Parse_RowWithWrongColumnCount_IsReportedAndSkipped()
    {
        var result = CsvTableParser.Parse("nombre,telefono,email\nJuan,300123\nAna,301,ana@x.com,EXTRA\nLuz,302,luz@x.com");

        Assert.Single(result.Table.Rows); // solo Luz
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(2, result.Errors[0].LineNumber);
        Assert.Contains("2 columnas", result.Errors[0].Message);
        Assert.Equal(3, result.Errors[1].LineNumber);
        Assert.Contains("4 columnas", result.Errors[1].Message);
    }

    [Fact]
    public void Parse_EmptyAndWhitespaceContent_ReportsEmptyFile()
    {
        Assert.Contains(CsvTableParser.Parse("").Errors, e => e.Message.Contains("vacio"));
        Assert.Contains(CsvTableParser.Parse("   \n  ").Errors, e => e.Message.Contains("vacio"));
        Assert.Contains(CsvTableParser.Parse(null).Errors, e => e.Message.Contains("vacio"));
    }

    [Fact]
    public void Parse_BlankLines_AreIgnored()
    {
        var result = CsvTableParser.Parse("nombre,tel\n\nJuan,300\n\n\nAna,301\n");

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Table.Rows.Count);
    }

    // ---------- Detalles de formato ----------

    [Fact]
    public void Parse_CrLfAndBom_AreTolerated()
    {
        var result = CsvTableParser.Parse("\uFEFFnombre,tel\r\nJuan,300\r\nAna,301");

        Assert.Empty(result.Errors);
        Assert.Equal("nombre", result.Table.Headers[0]);
        Assert.Equal(2, result.Table.Rows.Count);
        Assert.Equal(3, result.Table.Rows[1].LineNumber);
    }

    [Fact]
    public void Parse_EmptyHeader_GetsPositionalName()
    {
        var result = CsvTableParser.Parse("nombre,,tel\nJuan,x,300");

        Assert.Equal("columna_2", result.Table.Headers[1]);
    }

    [Fact]
    public void Parse_LastRowWithoutTrailingNewline_IsIncluded()
    {
        var result = CsvTableParser.Parse("nombre,tel\nJuan,300");

        Assert.Single(result.Table.Rows);
    }

    // ---------- AutoMap ----------

    [Fact]
    public void AutoMap_RecognizesSpanishSynonyms()
    {
        var mapping = ContactColumnMapping.AutoMap(
            ["Nombre Completo", "Correo", "Celular", "Empresa", "Destino", "Valor Estimado"]);

        Assert.Equal(0, mapping.Name);
        Assert.Equal(1, mapping.Email);
        Assert.Equal(2, mapping.Phone);
        Assert.Equal(3, mapping.Company);
        Assert.Equal(4, mapping.Destination);
        Assert.Equal(5, mapping.EstimatedValue);
    }

    [Fact]
    public void AutoMap_UnknownHeaders_MapToNull()
    {
        var mapping = ContactColumnMapping.AutoMap(["foo", "bar"]);

        Assert.Null(mapping.Name);
        Assert.Null(mapping.Phone);
        Assert.Null(mapping.Email);
    }
}
