namespace Ecorex.Application.Catalogos;

/// <summary>
/// Catalogo de paises para los campos geograficos del Directorio Modular (tipo Pais). Es una lista
/// en codigo (no tabla): el catalogo de ciudades/municipios (DANE) solo existe para Colombia, asi que
/// la cascada Departamento/Ciudad solo aplica cuando el pais es Colombia. Colombia va primero por ser
/// el caso por defecto; el resto en orden alfabetico. Se puede ampliar sin migracion.
/// </summary>
public static class GeoPaises
{
    /// <summary>Nombre del pais por defecto y unico con catalogo de ciudades (cascada depto/ciudad).</summary>
    public const string Colombia = "Colombia";

    /// <summary>Paises disponibles en el desplegable (Colombia primero, luego alfabetico).</summary>
    public static readonly IReadOnlyList<string> Todos = new[]
    {
        "Colombia",
        "Alemania", "Argentina", "Bolivia", "Brasil", "Canada", "Chile", "China", "Costa Rica",
        "Ecuador", "El Salvador", "Espana", "Estados Unidos", "Francia", "Guatemala", "Honduras",
        "Italia", "Mexico", "Nicaragua", "Panama", "Paraguay", "Peru", "Portugal", "Reino Unido",
        "Republica Dominicana", "Uruguay", "Venezuela", "Otro"
    };
}
