namespace Ecorex.Domain.Enums;

/// <summary>
/// Presentacion de un campo con origen de datos (Formularios avanzados, ola F1, doc 01 seccion D4
/// y doc 02 seccion 2). Define como el renderer ofrece el catalogo al llenar. Se persiste como
/// string (HaveConversion) para portabilidad DAL dual.
/// </summary>
public enum FormFieldPresentation
{
    /// <summary>Typeahead server-side paginado: el usuario escribe y se filtran resultados (default).</summary>
    Autocomplete = 0,

    /// <summary>Lista desplegable con el catalogo (acotado por filtro/tenant).</summary>
    Dropdown,

    /// <summary>Buscador en modal para catalogos grandes.</summary>
    Modal
}
