namespace Ecorex.Domain.Enums;

/// <summary>
/// Origen de datos de un campo de formulario (Formularios avanzados, ola F1, doc 01 seccion D4).
/// Gobierna de donde salen las opciones/valores de una pregunta: fijas o desde una tabla de
/// datos con dominio del tenant. Se persiste como string (HaveConversion) para que agregar
/// valores al final sea seguro entre motores (PG / SQL Server).
/// </summary>
public enum FormSourceKind
{
    /// <summary>Opciones fijas definidas en OptionsJson (comportamiento actual, default).</summary>
    Options = 0,

    /// <summary>Contenedor de datos generico del tenant (DataContainerService).</summary>
    DataContainer,

    /// <summary>Directorio de terceros (TerceroService + campos dinamicos TerceroFieldService).</summary>
    Tercero,

    /// <summary>Inventario de items (ItemService + campos dinamicos ItemFieldService).</summary>
    Item
}
