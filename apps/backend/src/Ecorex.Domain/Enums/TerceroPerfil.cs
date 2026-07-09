namespace Ecorex.Domain.Enums;

/// <summary>
/// Perfiles de negocio que un tercero puede acumular simultaneamente (multi-valor).
/// Cada perfil activo despliega su ficha en el formulario. Es [Flags] para poder
/// combinar (ej. Cliente | Proveedor).
/// </summary>
[System.Flags]
public enum TerceroPerfil
{
    Ninguno = 0,
    Cliente = 1,
    Sospechoso = 2,
    Proveedor = 4,
    Empleado = 8
}
