namespace Ecorex.Application.Common;

/// <summary>
/// Normalizacion de telefonos a MSISDN (digitos, sin "+", sin separadores) para WhatsApp/Meta y para
/// la LLAVE de conversacion por contacto. El envio saliente (WhatsAppConnectorService) y la ingesta
/// entrante (ChatIngestService) DEBEN compartir esta regla: si difieren, el mismo contacto se parte en
/// dos hilos (p.ej. "573166578604" vs "3166578604") y un soporte de pago cae en el hilo equivocado.
/// Regla CO (ADR-0125 v2): quita todo lo que no sea digito; si es un MOVIL colombiano de 10 digitos
/// (empieza por 3) SIN codigo de pais, le antepone "57" (default del despliegue actual, tenants CO).
/// NO adivina digitos faltantes ni asume pais para otras longitudes: un numero ya en E.164 (57 + movil)
/// o de otro pais/longitud se deja igual. Si en el futuro hay multi-pais, el "57" deberia venir de un
/// codigo de pais por tenant.
/// </summary>
public static class PhoneNumber
{
    /// <summary>Forma canonica (MSISDN) del numero. Cadena vacia si no hay digitos.</summary>
    public static string ToMsisdn(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 10 && digits[0] == '3') { return "57" + digits; }
        return digits;
    }

    /// <summary>
    /// Formas equivalentes de un numero para BUSCAR una conversacion existente sin fabricar una nueva:
    /// la canonica (MSISDN) y, cuando aplica, la forma nacional (sin el "57"). Asi un entrante en cualquiera
    /// de las dos variantes aterriza en el MISMO hilo ya existente (no se fusionan los hilos viejos: solo se
    /// enruta el trafico nuevo de forma deterministica a uno de ellos). El trimmeo crudo tambien se incluye
    /// para empatar hilos viejos guardados con formato (espacios/guiones) que no pasaron por esta regla.
    /// </summary>
    public static IReadOnlyList<string> LookupForms(string? phone)
    {
        var raw = (phone ?? "").Trim();
        var canonical = ToMsisdn(phone);
        var forms = new List<string>();
        void Add(string s) { if (!string.IsNullOrEmpty(s) && !forms.Contains(s)) { forms.Add(s); } }
        Add(canonical);
        // Forma nacional CO: si la canonica es "57" + movil de 10 (empieza por 3), tambien el numero sin el "57".
        if (canonical.Length == 12 && canonical.StartsWith("57") && canonical[2] == '3')
        {
            Add(canonical[2..]);
        }
        Add(raw);
        return forms;
    }
}
