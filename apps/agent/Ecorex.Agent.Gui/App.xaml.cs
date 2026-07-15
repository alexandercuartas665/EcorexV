namespace Ecorex.Agent.Gui;

/// <summary>
/// Punto de entrada de la GUI colmena (Ola A). Por ahora solo abre la ventana principal; el tray
/// icon y el ciclo de vida residente se cablean en un paso posterior de esta misma ola.
/// Se cualifica la base (System.Windows.Application) porque UseWindowsForms -habilitado para el
/// NotifyIcon de la bandeja- tambien trae System.Windows.Forms.Application.
/// </summary>
public partial class App : System.Windows.Application
{
}
