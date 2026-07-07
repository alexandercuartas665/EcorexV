namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado funcional de un nodo del menu (metadata; en la Ola 1 no altera el render).
/// Ready = apunta a una pagina real; InDevelopment = stub (rutas modulo/...); Disabled = oculto
/// aunque IsVisible sea true (reservado para la Ola 2).
/// </summary>
public enum MenuNodeState
{
    Ready = 0,
    InDevelopment,
    Disabled
}
