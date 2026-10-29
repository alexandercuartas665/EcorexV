namespace Ecorex.Domain.Enums;

/// <summary>De donde nacio una oferta/cotizacion de proveedor del comparador de compras.</summary>
public enum RequerimientoCotizacionFuente
{
    /// <summary>Capturada a mano por el comprador.</summary>
    Manual = 0,
    /// <summary>Derivada del ultimo precio del historico (hijos ya registrados) para ese producto+proveedor.</summary>
    Historico = 1,
    /// <summary>Derivada de la matriz de proveedores del producto (sin precio hasta que haya historico).</summary>
    Matriz = 2
}
