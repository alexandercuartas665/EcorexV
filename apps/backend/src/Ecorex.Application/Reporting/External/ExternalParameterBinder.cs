using System.Globalization;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Reporting.External;

/// <summary>
/// Enlaza los parametros DECLARADOS de un ExternalDataSet a valores tipados, listos para un comando
/// parametrizado (cero concatenacion). Es una pieza PURA (sin BD) y por eso el corazon testable de la
/// seguridad del conector:
/// - Parametros <see cref="ExternalDataParameterBinding.Context"/> toman su valor del CONTEXTO de
///   confianza (tenant/usuario/sucursal), NUNCA de entrada libre.
/// - Parametros <see cref="ExternalDataParameterBinding.Input"/> toman su valor de la entrada del
///   reporte (o su default), pero SIEMPRE convertido al tipo declarado y enlazado como parametro
///   tipado: un valor como "1; DROP TABLE" viaja como el string literal de un parametro, no como SQL.
/// </summary>
public static class ExternalParameterBinder
{
    /// <summary>Claves de contexto reservadas que el binder resuelve por si mismo del
    /// <see cref="ExternalRunContext"/> (ademas de las de <see cref="ExternalRunContext.Extra"/>).</summary>
    public const string ContextTenantId = "tenantid";
    public const string ContextUserId = "userid";

    /// <summary>
    /// Construye la lista ordenada de parametros enlazados. <paramref name="inputs"/> son valores de
    /// entrada tipados-como-texto por nombre de parametro (los provee el runtime del reporte). Si falta
    /// uno, se usa el <see cref="ExternalDataSetParameter.DefaultValue"/>. Los parametros Context IGNORAN
    /// <paramref name="inputs"/> por completo: su valor solo puede venir del contexto de confianza.
    /// <paramref name="reportRowLimit"/>: cuando NO es null (contexto de REPORTE), un parametro
    /// <see cref="ExternalDataParameterBinding.RowLimit"/> se enlaza a ESE tope (el MaxRows del sistema) en
    /// vez de a su DefaultValue de autoria, para que el default no cape la salida del panel. En la consola
    /// (reportRowLimit null) un RowLimit se comporta como Input (valor tecleado o su DefaultValue).
    /// </summary>
    public static IReadOnlyList<ExternalBoundParameter> Bind(
        IEnumerable<ExternalDataSetParameter> declared,
        ExternalRunContext context,
        IReadOnlyDictionary<string, string?>? inputs = null,
        int? reportRowLimit = null)
    {
        var result = new List<ExternalBoundParameter>();

        foreach (var p in declared)
        {
            string? raw;
            if (p.Binding == ExternalDataParameterBinding.Context)
            {
                // Alcance: SOLO del contexto de confianza. Entrada libre jamas alcanza estos parametros.
                raw = ResolveContext(p.ContextKey, context);
            }
            else if (p.Binding == ExternalDataParameterBinding.RowLimit && reportRowLimit is int cap)
            {
                // En reporte: forzar el tope duro del sistema; el default de autoria NO capa la salida.
                raw = cap.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                // Entrada del reporte/consola (o default). Se convierte y enlaza como parametro tipado.
                raw = inputs is not null && inputs.TryGetValue(p.Name, out var v) && v is not null
                    ? v
                    : p.DefaultValue;
            }

            result.Add(new ExternalBoundParameter(p.Name, p.Type, ConvertTyped(raw, p.Type)));
        }

        return result;
    }

    private static string? ResolveContext(string? contextKey, ExternalRunContext context)
    {
        if (string.IsNullOrWhiteSpace(contextKey))
        {
            return null;
        }

        var key = contextKey.Trim().ToLowerInvariant();
        if (key == ContextTenantId)
        {
            return context.TenantId.ToString();
        }

        if (key == ContextUserId)
        {
            return context.UserId?.ToString();
        }

        if (context.Extra is not null && context.Extra.TryGetValue(key, out var extra))
        {
            return extra;
        }

        // Clave de contexto no resuelta: valor nulo (parametro tipado NULL), nunca un texto arbitrario.
        return null;
    }

    /// <summary>Convierte el texto al tipo logico declarado. Un valor malformado se enlaza como NULL:
    /// nunca se inyecta texto crudo en el comando.</summary>
    public static object? ConvertTyped(string? raw, ExternalDataParameterType type)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();
        return type switch
        {
            ExternalDataParameterType.Int =>
                long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : (object?)null,
            ExternalDataParameterType.Decimal =>
                decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : (object?)null,
            ExternalDataParameterType.Boolean =>
                bool.TryParse(s, out var b) ? b : (s == "1" ? true : s == "0" ? false : (object?)null),
            ExternalDataParameterType.Date =>
                DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : (object?)null,
            ExternalDataParameterType.Guid =>
                Guid.TryParse(s, out var g) ? g : (object?)null,
            _ => s // String: el valor viaja como parametro tipado string, nunca concatenado.
        };
    }
}
