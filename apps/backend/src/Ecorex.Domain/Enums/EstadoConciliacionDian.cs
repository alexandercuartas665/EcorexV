namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de un renglon de la Conciliacion DIAN de compras (modulo Automatizaciones).
/// Calca las 4 pestanas del molde legacy (soldarco_conciliacion_provdian): Por conciliar,
/// Conciliado, Buzon, Procesado. En el nucleo real solo se mueven Conciliar -> Conciliado;
/// Buzon/Procesado se conservan por fidelidad al molde (ramas heredadas del modulo original).
/// </summary>
public enum EstadoConciliacionDian
{
    /// <summary>Recien traido del bot DIAN, aun sin cruzar/aprobar.</summary>
    Conciliar = 0,

    /// <summary>Cruzado con el ERP y trabajado.</summary>
    Conciliado = 1,

    /// <summary>Buzon (rama heredada del molde).</summary>
    Buzon = 2,

    /// <summary>Procesado (rama heredada del molde).</summary>
    Procesado = 3
}
