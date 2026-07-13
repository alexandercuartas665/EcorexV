namespace Ecorex.Domain.Enums;

/// <summary>
/// Valor por defecto DINAMICO de un campo (Formularios avanzados, ola F6, doc 01 D8): se resuelve al
/// abrir el formulario para llenar (no un literal como DefaultValue). Se persiste como string.
/// </summary>
public enum FormDefaultDynamic
{
    /// <summary>Sin default dinamico (usa DefaultValue literal si lo hay). Default.</summary>
    None = 0,

    /// <summary>Fecha de hoy (para campos Date).</summary>
    Today,

    /// <summary>Usuario actual (id del TenantUser que llena).</summary>
    CurrentUser,

    /// <summary>Entidad/sede del contexto (reservado; sin resolucion aun).</summary>
    CurrentEntidad
}
