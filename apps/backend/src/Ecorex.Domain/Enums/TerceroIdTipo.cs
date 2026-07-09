namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de documento con el que se identifica un tercero. Unificado para empresa y
/// persona (el prototipo permite identificar por cualquiera). "Ninguno" registra el
/// tercero sin documento.
/// </summary>
public enum TerceroIdTipo
{
    Nit,
    Identificacion,
    Correo,
    Telefono,
    Ninguno
}
