namespace Ecorex.Application.Common;

/// <summary>
/// Utilidades de higiene de texto Unicode para lo que se persiste. PostgreSQL (y SQL Server) guardan
/// texto en UTF-8/UTF-16 VALIDO: un "surrogate suelto" (media mitad de un par suplente UTF-16, p.ej. la
/// mitad alta U+D83D de un emoji (U+D83D U+DE00) sin su mitad baja) NO se puede codificar y Npgsql lanza
/// System.Text.EncoderFallbackException al escribir ("Unable to translate Unicode character \D83D ...").
///
/// Un surrogate suelto aparece cuando algo PARTE un par suplente: un truncado por longitud que corta un
/// emoji a la mitad (Substring/[..n] "por chars"), o una cadena mal decodificada que llega de una fuente
/// externa (webhook de WhatsApp/Evolution, respuesta del proveedor de IA, pegado del usuario). Como el
/// texto se ENVIA al cliente antes de persistirse y el guardado falla despues, el fallo ademas dispara
/// reintentos que REENVIAN el mensaje: por eso el saneo va en el punto de escritura (ver el interceptor
/// SurrogateSanitizingInterceptor en Infrastructure), y aqui vive la logica reusable y testeable.
/// </summary>
public static class SurrogateText
{
    /// <summary>Caracter de reemplazo Unicode estandar (U+FFFD) para un surrogate que no se puede representar.</summary>
    public const char Replacement = (char)0xFFFD;

    /// <summary>
    /// Devuelve la cadena sin surrogates sueltos: cada mitad de par suplente sin su pareja valida se
    /// reemplaza por U+FFFD. Los pares suplentes BIEN FORMADOS (emojis, etc.) se conservan intactos.
    /// Camino rapido: si no hay ningun surrogate suelto, devuelve la MISMA referencia (sin asignar memoria),
    /// lo que permite al llamador detectar "no cambio nada" con ReferenceEquals.
    /// </summary>
    public static string? StripLoneSurrogates(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Camino rapido: si NO hay ningun surrogate SUELTO (los pares bien formados, p.ej. emojis, cuentan
        // como validos), se devuelve la MISMA referencia sin asignar memoria. Asi el interceptor no reescribe
        // los mensajes con emojis correctos (el caso normal).
        if (!HasLoneSurrogate(value))
        {
            return value;
        }

        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                // Valido solo si le sigue una mitad baja; en ese caso se copia el par completo.
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(value[i + 1]);
                    i++;
                }
                else
                {
                    sb.Append(Replacement);
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                // Mitad baja sin una mitad alta previa que la haya consumido: suelta.
                sb.Append(Replacement);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// True si la cadena tiene al menos un surrogate SUELTO (una mitad de par suplente sin su pareja). Los
    /// pares bien formados (emojis, etc.) NO cuentan. Util para decidir si hace falta sanear.
    /// </summary>
    public static bool HasLoneSurrogate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                // Valido solo si lo sigue una mitad baja; en ese caso se salta el par completo.
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }
                return true; // mitad alta sin mitad baja
            }
            if (char.IsLowSurrogate(c))
            {
                return true; // mitad baja sin una mitad alta previa que la consumiera
            }
        }
        return false;
    }

    /// <summary>
    /// Trunca a lo sumo a <paramref name="maxLength"/> UTF-16 code units SIN partir un par suplente: si el
    /// corte caeria entre la mitad alta y la baja de un emoji, retrocede un caracter para no dejar una mitad
    /// colgando. Asi un truncado por longitud nunca crea un surrogate suelto. Entrada null/corta se devuelve
    /// tal cual; <paramref name="maxLength"/> &lt;= 0 devuelve cadena vacia.
    /// </summary>
    public static string? SafeTruncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        var cut = maxLength;
        // Si el ultimo caracter conservado es una mitad ALTA, su pareja baja quedo fuera del corte:
        // retrocede uno para no dejar la mitad alta suelta.
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }
        return value.Substring(0, cut);
    }
}
