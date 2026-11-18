namespace Ecorex.Application.Requerimientos;

/// <summary>Desglose fiscal de una linea de compra (Ola A): subtotal, descuento, base gravable, IVA, flete y
/// total. Fuente UNICA de verdad para el total de una oferta/linea: la usan el comparador, la canasta, la
/// planificacion y la orden de compra para que los numeros coincidan en todas las vistas.</summary>
public readonly record struct LineaFiscal(
    decimal Subtotal,
    decimal DescuentoMonto,
    decimal BaseGravable,
    decimal IvaPct,
    decimal IvaMonto,
    decimal Flete,
    decimal Total)
{
    /// <summary>Total sin IVA (base gravable + flete): util para comparaciones "antes de impuestos".</summary>
    public decimal TotalSinIva => BaseGravable + Flete;
}

/// <summary>Reglas fiscales del modulo de compras (IVA + descuento). Puro, sin estado ni IO.</summary>
public static class ComprasFiscal
{
    /// <summary>IVA general de Colombia por defecto cuando ni la linea ni el item lo definen.</summary>
    public const decimal IvaDefault = 19m;

    /// <summary>IVA a aplicar: el de la linea/oferta si viene; si no el del item (Inventario); si no el default (19%).</summary>
    public static decimal ResolverIva(decimal? ivaLinea, decimal? ivaItem)
        => ivaLinea ?? ivaItem ?? IvaDefault;

    /// <summary>Calcula el desglose de una linea. El descuento por VALOR gana sobre el porcentaje; nunca deja la
    /// base negativa. El flete se suma al final (no se grava). Los montos derivados se redondean a 2 decimales.</summary>
    public static LineaFiscal Calcular(
        decimal precioUnitario,
        decimal cantidad,
        decimal ivaPct,
        decimal? descuentoPct = null,
        decimal? descuentoValor = null,
        decimal fleteMonto = 0m,
        bool fleteEsPorUnidad = false)
    {
        var subtotal = precioUnitario * cantidad;
        decimal desc =
            descuentoValor is > 0m ? descuentoValor.Value :
            descuentoPct is > 0m ? Math.Round(subtotal * (descuentoPct.Value / 100m), 2) :
            0m;
        if (desc > subtotal) { desc = subtotal; }

        var baseGravable = subtotal - desc;
        if (ivaPct < 0m) { ivaPct = 0m; }
        var ivaMonto = Math.Round(baseGravable * (ivaPct / 100m), 2);

        var flete = fleteMonto <= 0m ? 0m : (fleteEsPorUnidad ? fleteMonto * cantidad : fleteMonto);

        return new LineaFiscal(subtotal, desc, baseGravable, ivaPct, ivaMonto, flete, baseGravable + ivaMonto + flete);
    }
}
