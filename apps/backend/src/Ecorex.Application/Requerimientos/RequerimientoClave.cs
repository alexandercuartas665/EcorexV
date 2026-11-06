namespace Ecorex.Application.Requerimientos;

/// <summary>
/// Clave tecnica de PRODUCTO: FUENTE UNICA para empatar necesidades, lineas de RFQ, cotizaciones, el comparador
/// y la precedencia de proceso de cotizacion (Ola D). "i:{itemId}" cuando hay item del catalogo; si no, el
/// texto de la descripcion normalizado como "d:{...}". Debe usarse en TODO el modulo para que las claves calcen.
/// </summary>
public static class RequerimientoClave
{
    public static string Of(Guid? itemId, string? descripcion)
        => itemId is Guid id
            ? "i:" + id
            : "d:" + string.Join(' ', (descripcion ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
