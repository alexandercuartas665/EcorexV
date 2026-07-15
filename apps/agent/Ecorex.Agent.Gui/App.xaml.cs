using System;
using System.Windows;
using Ecorex.Agent.Gui.Services;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Gui;

/// <summary>
/// Punto de entrada de la GUI colmena. Soporta un arranque HEADLESS para configurar la identidad
/// sin abrir la ventana (util para despliegue/servicio y para pruebas del canal):
///   Ecorex.Agent.Gui --save-config &lt;clientId&gt; &lt;hubUrl&gt;
/// escribe la config cifrada (DPAPI) y sale. Sin argumentos, abre la colmena.
/// Se cualifica la base (System.Windows.Application) porque UseWindowsForms -habilitado para el
/// NotifyIcon de la bandeja- tambien trae System.Windows.Forms.Application.
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 3 && string.Equals(e.Args[0], "--save-config", StringComparison.OrdinalIgnoreCase))
        {
            var secret = e.Args.Length >= 4 ? e.Args[3].Trim() : string.Empty;
            new DpapiConfigStore().Save(new AgentConfig(e.Args[1].Trim(), e.Args[2].Trim(), secret));
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }
}
