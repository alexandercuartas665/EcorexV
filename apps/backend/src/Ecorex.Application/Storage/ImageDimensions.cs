namespace Ecorex.Application.Storage;

/// <summary>
/// Lee el ancho/alto de una imagen PNG, GIF o JPEG desde su cabecera (sin librerias de imagen). Lo usa la
/// galeria de plantillas (ADR-0177) para que el agente ponga width/height correctos en el correo.
/// Devuelve null si el formato no se reconoce o la cabecera esta truncada.
/// </summary>
public static class ImageDimensions
{
    public static (int Width, int Height)? TryRead(ReadOnlySpan<byte> data)
    {
        // PNG: firma de 8 bytes + chunk IHDR (ancho y alto big-endian en los bytes 16..23).
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
        {
            var w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            var h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return Valid(w, h);
        }

        // GIF: "GIF87a"/"GIF89a" + ancho y alto little-endian (bytes 6..9).
        if (data.Length >= 10 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
        {
            return Valid(data[6] | (data[7] << 8), data[8] | (data[9] << 8));
        }

        // JPEG: SOI (FF D8) y se recorren los segmentos hasta un SOFn (C0..CF salvo C4/C8/CC).
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var i = 2;
            while (i + 4 <= data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                var marker = data[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
                if (marker == 0xD9 || marker == 0xDA) { return null; }
                var len = (data[i + 2] << 8) | data[i + 3];
                if (len < 2) { return null; }
                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                {
                    if (i + 9 > data.Length) { return null; }
                    var h = (data[i + 5] << 8) | data[i + 6];
                    var w = (data[i + 7] << 8) | data[i + 8];
                    return Valid(w, h);
                }
                i += 2 + len;
            }
        }
        return null;
    }

    private static (int Width, int Height)? Valid(int w, int h)
        => w is > 0 and <= 20000 && h is > 0 and <= 20000 ? (w, h) : null;
}
