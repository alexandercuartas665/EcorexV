using System.Globalization;
using System.Text;

namespace Ecorex.Application.Forms;

/// <summary>
/// Genera codigos de barras LINEALES autocontenidos (SVG) para la impresion de plantillas (p.ej. el
/// consecutivo de la Orden de Trabajo). SERVER-SIDE, sin librerias externas ni JS. Simbologia Code39
/// (charset A-Z 0-9 espacio - . $ / + %; envuelve con '*' de start/stop; sin digito de control). El SVG
/// es barras negras sobre blanco + el valor legible debajo; escalable y escaneable. Se emite SIN escapar
/// (igual que el Canvas), Chromium lo rinde en el PDF.
/// </summary>
public static class Barcode
{
    // Patron de 9 elementos por caracter (B S B S B S B S B): 'n' = angosto, 'w' = ancho.
    private static readonly Dictionary<char, string> Code39 = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn",
        ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw", ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw",
        ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn", ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn",
        ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn", ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww",
        ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww", ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn",
        ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn", ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn",
        ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw", ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw",
        ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn", ['*'] = "nwnnwnwnn",
    };

    /// <summary>Deja solo caracteres validos de Code39 (mayusculas). Los no soportados se descartan.</summary>
    public static string Sanitize(string? data)
    {
        if (string.IsNullOrEmpty(data)) { return string.Empty; }
        var sb = new StringBuilder(data.Length);
        foreach (var ch in data.ToUpperInvariant())
        {
            if (ch != '*' && Code39.ContainsKey(ch)) { sb.Append(ch); }
        }
        return sb.ToString();
    }

    /// <summary>
    /// SVG Code39 del valor. <paramref name="height"/> es la altura de despliegue en px (clamp 16..200);
    /// el ancho es proporcional (escala por viewBox) y no desborda (max-width:100%). Cadena vacia si el
    /// valor queda sin caracteres codificables.
    /// </summary>
    public static string Code39Svg(string? data, int height = 44)
    {
        var payload = Sanitize(data);
        if (payload.Length == 0) { return string.Empty; }
        height = Math.Clamp(height, 16, 200);

        const int narrow = 2, wide = 6, quiet = 20, barH = 60, textH = 16, gap = narrow;
        var full = "*" + payload + "*"; // start/stop

        // Emite las barras (elementos en indice par) y avanza x por cada elemento; espacio entre chars.
        var bars = new StringBuilder();
        var x = quiet;
        for (var c = 0; c < full.Length; c++)
        {
            var pat = Code39[full[c]];
            for (var i = 0; i < pat.Length; i++)
            {
                var w = pat[i] == 'w' ? wide : narrow;
                if (i % 2 == 0) // barra (negro)
                {
                    bars.Append("<rect x=\"").Append(x.ToString(CultureInfo.InvariantCulture))
                        .Append("\" y=\"0\" width=\"").Append(w.ToString(CultureInfo.InvariantCulture))
                        .Append("\" height=\"").Append(barH.ToString(CultureInfo.InvariantCulture))
                        .Append("\" fill=\"#000\"/>");
                }
                x += w;
            }
            x += gap; // separacion entre caracteres
        }
        var totalW = x - gap + quiet;
        var vbH = barH + textH;

        // Valor legible centrado debajo de las barras (ayuda humana; no afecta el escaneo).
        var label = Esc(payload);
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
           .Append(totalW.ToString(CultureInfo.InvariantCulture)).Append(' ')
           .Append(vbH.ToString(CultureInfo.InvariantCulture))
           .Append("\" role=\"img\" aria-label=\"Codigo de barras ").Append(label)
           .Append("\" style=\"height:").Append(height.ToString(CultureInfo.InvariantCulture))
           .Append("px;width:auto;max-width:100%\" shape-rendering=\"crispEdges\" preserveAspectRatio=\"xMidYMid meet\">");
        svg.Append("<rect x=\"0\" y=\"0\" width=\"").Append(totalW.ToString(CultureInfo.InvariantCulture))
           .Append("\" height=\"").Append(vbH.ToString(CultureInfo.InvariantCulture)).Append("\" fill=\"#fff\"/>");
        svg.Append(bars);
        svg.Append("<text x=\"").Append((totalW / 2).ToString(CultureInfo.InvariantCulture))
           .Append("\" y=\"").Append((barH + textH - 3).ToString(CultureInfo.InvariantCulture))
           .Append("\" text-anchor=\"middle\" font-family=\"monospace\" font-size=\"")
           .Append((textH - 3).ToString(CultureInfo.InvariantCulture)).Append("\" fill=\"#000\">")
           .Append(label).Append("</text>");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
