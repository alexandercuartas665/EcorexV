namespace Ecorex.Domain.Enums;

/// <summary>
/// Modo de identidad de un formulario transaccional (Formularios avanzados, ola F3; doc 01 D3).
/// Define como el registro produce su numero/clave de negocio. Se persiste como string.
/// </summary>
public enum FormIdentityMode
{
    /// <summary>Sin identidad de negocio (formulario "solido" / captura suelta). Default.</summary>
    None = 0,

    /// <summary>Clave natural: el numero sale de un campo del propio formulario; se valida unicidad por tenant.</summary>
    NaturalKey,

    /// <summary>Consecutivo del sistema: al confirmar se consume el siguiente numero de una TenantSequence.</summary>
    Sequence
}
