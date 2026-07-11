namespace Ecorex.Domain.Enums;

/// <summary>
/// Naturaleza de una entidad de la cuenta (Configuracion de la entidad, 000615). Define que
/// campos aplican en el modal: una <see cref="Sede"/> es una ubicacion fisica con identidad
/// legal, mientras un <see cref="Area"/> es una unidad organizativa interna sin datos legales.
/// </summary>
public enum EntidadKind
{
    /// <summary>Sucursal / agencia / sede: ubicacion fisica con identidad legal (NIT, direccion, logo).</summary>
    Sede,
    /// <summary>Area: unidad organizativa interna de la cuenta (sin identidad legal).</summary>
    Area
}
