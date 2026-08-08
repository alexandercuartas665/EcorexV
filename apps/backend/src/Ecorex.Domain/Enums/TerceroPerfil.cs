namespace Ecorex.Domain.Enums;

/// <summary>
/// Perfiles de negocio que un tercero puede acumular simultaneamente (multi-valor). Cada perfil
/// corresponde a un DIRECTORIO del modulo 000232 (la fila TIPO) y a la ficha de datos que ese
/// directorio edita. Es [Flags] para poder combinar (ej. Cartera | Proveedor): un mismo tercero
/// aparece en varios directorios.
///
/// El directorio "Publico" no tiene bit propio: es la vista de TODOS los terceros y edita los datos
/// basicos, que viven en columnas de la tabla y no en una ficha.
/// </summary>
[System.Flags]
public enum TerceroPerfil
{
    Ninguno = 0,
    /// <summary>Legado del Cargador de contactos (000740): marca al prospecto ganado. Ya NO es un
    /// directorio del modulo 000232; se conserva porque el Gestor y el KPI de clientes lo usan.</summary>
    Cliente = 1,
    // El bit 2 fue "Sospechoso" y se retiro: el embudo del Cargador (000740) lo modela con su propia
    // columna de bolsa, no con un perfil del tercero. No reutilizar el 2 para otra cosa.
    Proveedor = 4,
    /// <summary>Directorio "Laboral".</summary>
    Empleado = 8,
    Fiscal = 16,
    Comercial = 32,
    Cartera = 64
}
