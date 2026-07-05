using System.Text;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Parser CSV puro y testeable del Cargador de contactos (sin IO, sin dependencias).
/// Reglas: autodeteccion de delimitador (coma, punto y coma o tabulador) sobre la linea
/// de encabezados; campos entrecomillados RFC 4180 (comilla doble escapada como "" y
/// saltos de linea permitidos dentro de comillas); CRLF/LF indistintos; BOM tolerado;
/// lineas totalmente vacias se ignoran; una fila con un numero de campos distinto al
/// del encabezado se reporta como fila rota (no se adivina el contenido).
/// </summary>
public static class CsvTableParser
{
    private static readonly char[] CandidateDelimiters = [',', ';', '\t'];

    public static CsvParseResult Parse(string? content, char? delimiter = null)
    {
        var errors = new List<CsvRowError>();
        if (string.IsNullOrWhiteSpace(content))
        {
            errors.Add(new CsvRowError(1, "El archivo esta vacio."));
            return new CsvParseResult(new CsvTable([], []), errors);
        }

        // BOM UTF-8 (U+FEFF): el InputFile de Blazor entrega el texto tal cual viene del archivo.
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        var sep = delimiter ?? DetectDelimiter(content);
        var records = SplitRecords(content, sep);
        if (records.Count == 0)
        {
            errors.Add(new CsvRowError(1, "El archivo no tiene encabezados."));
            return new CsvParseResult(new CsvTable([], []), errors);
        }

        var headers = records[0].Fields
            .Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"columna_{i + 1}" : h.Trim())
            .ToList();

        var rows = new List<CsvRow>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Count == headers.Count)
            {
                rows.Add(new CsvRow(record.LineNumber, record.Fields));
                continue;
            }

            errors.Add(new CsvRowError(
                record.LineNumber,
                $"Fila rota: tiene {record.Fields.Count} columnas y el encabezado define {headers.Count}."));
        }

        return new CsvParseResult(new CsvTable(headers, rows), errors);
    }

    /// <summary>
    /// Elige el delimitador que mas campos produce en la primera linea logica,
    /// contando solo separadores fuera de comillas. Empate o cero -> coma.
    /// </summary>
    private static char DetectDelimiter(string content)
    {
        var firstLine = ReadFirstLogicalLine(content);
        var best = ',';
        var bestCount = 0;
        foreach (var candidate in CandidateDelimiters)
        {
            var count = CountOutsideQuotes(firstLine, candidate);
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    private static string ReadFirstLogicalLine(string content)
    {
        var inQuotes = false;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                return content[..i];
            }
        }

        return content;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }

    private sealed record RawRecord(int LineNumber, List<string> Fields);

    /// <summary>
    /// Maquina de estados caracter a caracter: separa registros y campos respetando
    /// comillas. Devuelve solo registros con contenido (lineas vacias fuera).
    /// </summary>
    private static List<RawRecord> SplitRecords(string content, char delimiter)
    {
        var records = new List<RawRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasQuotedField = false;
        var line = 1;
        var recordStartLine = 1;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            // Un registro "vacio" es el producido por una linea en blanco: un solo campo sin
            // texto y sin comillas. No cuenta como fila (ni buena ni rota).
            var isEmpty = fields.Count == 1 && fields[0].Length == 0 && !hasQuotedField;
            if (!isEmpty)
            {
                records.Add(new RawRecord(recordStartLine, [.. fields]));
            }

            fields.Clear();
            hasQuotedField = false;
        }

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"'); // comilla escapada ""
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                hasQuotedField = true;
            }
            else if (c == delimiter)
            {
                EndField();
            }
            else if (c == '\r')
            {
                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
                line++;
                recordStartLine = line;
            }
            else if (c == '\n')
            {
                EndRecord();
                line++;
                recordStartLine = line;
            }
            else
            {
                field.Append(c);
            }
        }

        // Ultimo registro sin salto de linea final.
        if (field.Length > 0 || fields.Count > 0 || hasQuotedField)
        {
            EndRecord();
        }

        return records;
    }
}
