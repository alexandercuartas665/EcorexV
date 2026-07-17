using System;
using System.Windows;
using Ecorex.Agent.Core.Services;
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

        // Fuente local del Gateway (Ola C): guarda la cadena de conexion SQL Server cifrada con DPAPI.
        // La credencial se aporta en tiempo de ejecucion; NUNCA se versiona.
        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--save-source", StringComparison.OrdinalIgnoreCase))
        {
            new GatewaySourceStore().SaveSqlServer(e.Args[1].Trim());
            Shutdown(0);
            return;
        }

        // Allow-list de dominios del sub-agente Navegador (doc 06 s4). Coma-separada. DPAPI local.
        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--save-browser-allow", StringComparison.OrdinalIgnoreCase))
        {
            new BrowserAllowList().Save(e.Args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Shutdown(0);
            return;
        }

        // Allow-list de rutas raiz del sub-agente Archivos (doc 06 s4). Coma-separada. DPAPI local.
        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--save-file-allow", StringComparison.OrdinalIgnoreCase))
        {
            new FileAllowList().Save(e.Args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            Shutdown(0);
            return;
        }

        // Consentimiento local de una capacidad (doc 06 s4): --enable <browser|files> <0|1>. Lo mismo
        // que hace el toggle de la colmena; util para despliegue/servicio y pruebas.
        if (e.Args.Length >= 3 && string.Equals(e.Args[0], "--enable", StringComparison.OrdinalIgnoreCase))
        {
            var on = e.Args[2] is "1" or "true";
            var consent = new CapabilityConsent();
            if (string.Equals(e.Args[1], "browser", StringComparison.OrdinalIgnoreCase)) { consent.SetBrowser(on); }
            else if (string.Equals(e.Args[1], "files", StringComparison.OrdinalIgnoreCase)) { consent.SetFiles(on); }
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }
}
