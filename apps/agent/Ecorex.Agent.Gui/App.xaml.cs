using System;
using System.Threading;
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
            // Blanco de la auto-elevacion de la colmena (Verb=runas) y del MSI (install desatendido):
            // escribe la boveda DIRECTO (machine-scope) y sale, sin pasar por el pipe.
            var store = new DpapiConfigStore();
            var secretArg = e.Args.Length >= 4 ? e.Args[3].Trim() : string.Empty;
            // Secreto vacio = "no lo cambies": se conserva el que ya hay (mismo criterio que la ruta por
            // pipe). Asi cambiar solo el ClientId/URL no borra la credencial. Con un secreto nuevo, se usa.
            var secret = string.IsNullOrEmpty(secretArg) ? store.Load().Secret : secretArg;
            store.Save(new AgentConfig(e.Args[1].Trim(), e.Args[2].Trim(), secret));
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

        // INSTANCIA UNICA: si ya hay una colmena corriendo, no abrir una segunda (evita la trampa de
        // "Ejecutar como administrador" abriendo una ventana confusa mientras el usuario sigue en el
        // icono de bandeja de la instancia vieja). Se le hace una senal para que se muestre y se sale.
        // OJO: esto va DESPUES de los modos headless (--save-config, etc.), que ya salieron arriba; el
        // relanzamiento elevado de --save-config NO llega aqui, asi que el mutex no lo bloquea.
        _singleInstance = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { /* best-effort: la otra instancia quiza no expone el evento */ }
            Shutdown(0);
            return;
        }

        // Arranque en la BANDEJA (autostart del instalador, llave Run de Windows): la colmena nace
        // oculta como icono de bandeja, sin robar foco en el logon. El usuario la abre con doble clic.
        // Se muestra minimizada y sin barra de tareas y acto seguido se oculta, evitando el parpadeo
        // de una ventana que aparece y desaparece.
        var startInTray = e.Args.Length >= 1 && string.Equals(e.Args[0], "--tray", StringComparison.OrdinalIgnoreCase);

        var window = new MainWindow();

        // Escucha la senal de "mostrar" que enviaria una segunda instancia (traer al frente).
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => Dispatcher.Invoke(() => window.ShowFromTrayPublic()),
            null, Timeout.Infinite, executeOnlyOnce: false);

        if (startInTray)
        {
            window.WindowState = WindowState.Minimized;
            window.ShowInTaskbar = false;
            window.Show();
            window.Hide();
        }
        else
        {
            window.Show();
        }
    }

    // ---- Instancia unica (per-sesion). Los campos se conservan para que el GC no los recoja: si el
    // Mutex se colecta, se libera y la instancia unica deja de valer. ----
    private const string InstanceMutexName = @"Local\EcorexAgentColmenaGui";
    private const string ShowEventName = @"Local\EcorexAgentColmenaGui.Show";
    private Mutex? _singleInstance;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showRegistration;
}
