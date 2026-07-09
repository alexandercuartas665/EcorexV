namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado del tercero en el directorio. Activo (verde), Prospecto (ambar) e Inactivo
/// (gris). El prototipo muestra Activo/Prospecto; Inactivo cubre el soft-delete/baja.
/// </summary>
public enum TerceroEstado
{
    Activo,
    Prospecto,
    Inactivo
}
