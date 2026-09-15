using System.Globalization;
using System.Text;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Homologacion RUT -> Directorio publico (Capa 8, regla 2.2). Logica PURA (sin BD): mapea las casillas
/// de la seccion tributaria (RUT) a los campos de la seccion publica y deduce la naturaleza desde la
/// casilla 24 (tipo de contribuyente). La usan tanto el servicio (al guardar, modo Fiscal = sobrescribe)
/// como el modal (previsualizacion en vivo / homologacion suave con "Usar los del RUT").
/// </summary>
public static class HomologacionRut
{
    /// <summary>Resultado del mapeo: la naturaleza deducida (o null), los campos publicos que aporta el RUT
    /// (clave publica -> valor) y etiquetas legibles de que se homologa (para notificar al usuario).</summary>
    public sealed record Resultado(
        TerceroTipo? Tipo,
        IReadOnlyDictionary<string, string> Publico,
        IReadOnlyList<string> Etiquetas);

    /// <summary>Mapea los valores de la ficha (RUT) a los campos publicos. No muta la ficha.</summary>
    public static Resultado Mapear(IReadOnlyDictionary<string, Dictionary<string, string>> valores)
    {
        string? V(string k)
        {
            foreach (var sec in valores.Values)
            {
                if (sec.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) { return v.Trim(); }
            }
            return null;
        }

        var tipoContrib = Norm(V("tipo_contribuyente"));
        var razon = V("razon_social");
        var nombreComercial = V("nombre_comercial");
        var sigla = V("sigla");
        var nombrePersona = Unir(V("primer_nombre"), V("otros_nombres"), V("primer_apellido"), V("segundo_apellido"));
        var nit = V("nit");
        var numeroId = V("numero_identificacion");
        var correoRut = V("correo_rut");
        var ciudadRut = V("ciudad_rut");
        var paisRut = V("pais_rut");
        var tel = V("telefono_1") ?? V("telefono_2");

        // Naturaleza desde la casilla 24; respaldo por lo que se haya diligenciado.
        TerceroTipo? tipo = tipoContrib.Contains("natural") ? TerceroTipo.Persona
            : tipoContrib.Contains("juridic") ? TerceroTipo.Empresa
            : null;
        if (tipo is null)
        {
            if (!string.IsNullOrWhiteSpace(razon) || !string.IsNullOrWhiteSpace(nombreComercial)) { tipo = TerceroTipo.Empresa; }
            else if (!string.IsNullOrWhiteSpace(nombrePersona)) { tipo = TerceroTipo.Persona; }
        }

        var publico = new Dictionary<string, string>(StringComparer.Ordinal);
        var etiquetas = new List<string>();

        if (tipo == TerceroTipo.Empresa)
        {
            var nombre = razon ?? nombreComercial ?? sigla;
            if (!string.IsNullOrWhiteSpace(nombre)) { publico["nombre_empresa"] = nombre!; etiquetas.Add("Nombre empresa <- Razon social del RUT"); }
            if (!string.IsNullOrWhiteSpace(tel)) { publico["telefono_empresa"] = tel!; }
        }
        else if (tipo == TerceroTipo.Persona)
        {
            if (!string.IsNullOrWhiteSpace(nombrePersona)) { publico["contacto"] = nombrePersona!; etiquetas.Add("Contacto <- Nombres y apellidos del RUT"); }
            if (!string.IsNullOrWhiteSpace(tel)) { publico["telefono_contacto"] = tel!; }
        }

        // IDE = NIT sin digito de verificacion (O3-2). Persona natural: la cedula (casilla 26) o el NIT.
        var ideRaw = tipo == TerceroTipo.Persona ? (numeroId ?? nit) : (nit ?? numeroId);
        var ide = NitAIde(ideRaw);
        if (!string.IsNullOrWhiteSpace(ide)) { publico["ide"] = ide!; etiquetas.Add("IDE <- NIT sin digito de verificacion"); }
        if (!string.IsNullOrWhiteSpace(correoRut)) { publico["correo"] = correoRut!; etiquetas.Add("Correo <- Correo del RUT"); }
        if (!string.IsNullOrWhiteSpace(ciudadRut)) { publico["ciudad"] = ciudadRut!; etiquetas.Add("Ciudad <- Ciudad del RUT"); }
        if (!string.IsNullOrWhiteSpace(paisRut)) { publico["pais"] = paisRut!; }

        return new Resultado(tipo, publico, etiquetas);
    }

    /// <summary>Homologacion FUERTE (modo Fiscal): mapea y SOBRESCRIBE la seccion destino con los datos del
    /// RUT. Devuelve el resultado (para notificar). Muta <paramref name="valores"/>.</summary>
    public static Resultado Aplicar(Dictionary<string, Dictionary<string, string>> valores, string seccionDestino)
    {
        var r = Mapear(valores);
        if (r.Publico.Count > 0)
        {
            if (!valores.TryGetValue(seccionDestino, out var dest))
            {
                dest = valores[seccionDestino] = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            foreach (var kv in r.Publico) { dest[kv.Key] = kv.Value; }
        }
        return r;
    }

    /// <summary>Quita el digito de verificacion de un NIT: "900123456-7" -> "900123456". Idempotente.</summary>
    public static string? NitAIde(string? nit)
    {
        if (string.IsNullOrWhiteSpace(nit)) { return null; }
        var s = nit.Trim();
        var dash = s.IndexOf('-');
        if (dash > 0) { s = s[..dash]; }
        return s.Trim();
    }

    private static string Unir(params string?[] partes)
        => string.Join(" ", partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    // Minusculas + sin acentos, para comparar el tipo de contribuyente de forma robusta.
    private static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) { return string.Empty; }
        var formD = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) { sb.Append(ch); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
