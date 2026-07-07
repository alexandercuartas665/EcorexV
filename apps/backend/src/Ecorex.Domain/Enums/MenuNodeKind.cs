namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de nodo del menu configurable del workspace del tenant (Ola 1 del menu por perfil).
/// QuickLink = enlace de primer nivel antes de "Modulos" (Inicio, Anuncios); Section = grupo
/// acordeon de primer nivel (data-acc); Subgroup = grupo anidado dentro de una seccion (ej.
/// "Comercial"); Item = enlace hoja (NavLink) dentro de una seccion o subgrupo.
/// </summary>
public enum MenuNodeKind
{
    QuickLink = 0,
    Section,
    Subgroup,
    Item
}
