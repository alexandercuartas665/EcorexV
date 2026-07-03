namespace Ecorex.Domain.Enums;

/// <summary>
/// Que se abre al hacer click en la tarjeta de un lead segun su unidad de negocio. Extensible:
/// cada unidad puede mapear a un modal/comportamiento distinto.
/// </summary>
public enum BusinessUnitModalKind
{
    /// <summary>Modal estandar del lead (datos + campos + chat).</summary>
    Generic,
    /// <summary>Abre el modulo del salon para gestionar al cliente (ficha, historial, agendar).</summary>
    ImageAdvisory
}
