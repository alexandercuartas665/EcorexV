using System;
using System.Runtime.InteropServices;
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
            // Blanco de la auto-elevacion de la colmena (Verb=runas) y del MSI reconfigurable (ADR-0063,
            // install desatendido y reinstall/modify): escribe la boveda DIRECTO (machine-scope) y sale,
            // sin pasar por el pipe. La regla del secreto (vacio = conservar el actual) vive en
            // AgentIdentity.Merge, probada aparte: asi re-fijar solo ClientId/URL no borra la credencial.
            var store = new DpapiConfigStore();
            var secretArg = e.Args.Length >= 4 ? e.Args[3] : null;
            store.Save(AgentIdentity.Merge(e.Args[1], e.Args[2], secretArg, store.Load()));
            Shutdown(0);
            return;
        }

        // DIAGNOSTICO: imprime la identidad ACTIVA del vault (ClientId + hub + si hay secreto), sin
        // revelar el secreto. Sirve para verificar de un vistazo que la reconfiguracion por MSI/UAC
        // quedo aplicada. La GUI es WinExe (sin consola): se engancha a la consola del proceso padre
        // para que la salida sea visible al lanzarlo desde una terminal; si no hay, muestra un dialogo.
        if (e.Args.Length >= 1 && (string.Equals(e.Args[0], "--show-identity", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(e.Args[0], "--whoami", StringComparison.OrdinalIgnoreCase)))
        {
            var cfg = new DpapiConfigStore().Load();
            var report = string.Join(Environment.NewLine,
                "ECOREX Agente Colmena - identidad activa (boveda machine-scope):",
                "  ClientId : " + (string.IsNullOrEmpty(cfg.ClientId) ? "(sin configurar)" : cfg.ClientId),
                "  Hub      : " + (string.IsNullOrEmpty(cfg.HubUrl) ? "(sin configurar)" : cfg.HubUrl),
                "  Secreto  : " + (cfg.HasSecret ? "si" : "no"),
                "  Completa : " + (cfg.IsComplete ? "si" : "no"));
            ReportToConsoleOrDialog(report);
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

        // Guardado COMBINADO de una capacidad (allow-list + consentimiento) en UNA sola invocacion, para
        // persistir el toggle + la lista del flyout con un UNICO prompt de UAC. La boveda la posee el
        // Servicio (ADR-0039): el GUI no elevado no puede escribirla, por eso el flyout relanza esto
        // elevado. --save-caps <browser|files> <0|1> [entries-coma-separadas]
        if (e.Args.Length >= 3 && string.Equals(e.Args[0], "--save-caps", StringComparison.OrdinalIgnoreCase))
        {
            var on = e.Args[2] is "1" or "true";
            var entries = e.Args.Length >= 4
                ? e.Args[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>();
            var consent = new CapabilityConsent();
            if (string.Equals(e.Args[1], "browser", StringComparison.OrdinalIgnoreCase))
            {
                new BrowserAllowList().Save(entries);
                consent.SetBrowser(on);
            }
            else if (string.Equals(e.Args[1], "files", StringComparison.OrdinalIgnoreCase))
            {
                new FileAllowList().Save(entries);
                consent.SetFiles(on);
            }
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

    // ---- Diagnostico: salida por consola del proceso padre (o dialogo si no la hay) ----

    /// <summary>
    /// Escribe <paramref name="text"/> en la consola del proceso padre (cmd/PowerShell) enganchandose a
    /// ella, ya que una app WinExe no tiene consola propia. Si no hay consola padre (doble clic), cae a
    /// un MessageBox para que igual sea visible. Nunca lanza: el diagnostico no debe fallar el proceso.
    /// </summary>
    private static void ReportToConsoleOrDialog(string text)
    {
        try
        {
            if (AttachConsole(AttachParentProcess))
            {
                try
                {
                    Console.Out.WriteLine();
                    Console.Out.WriteLine(text);
                    Console.Out.Flush();
                }
                finally { FreeConsole(); }
            }
            else
            {
                System.Windows.MessageBox.Show(text, "ECOREX Agente - identidad");
            }
        }
        catch
        {
            // best-effort: un diagnostico no puede tumbar el proceso.
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    // ---- Instancia unica (per-sesion). Los campos se conservan para que el GC no los recoja: si el
    // Mutex se colecta, se libera y la instancia unica deja de valer. ----
    private const string InstanceMutexName = @"Local\EcorexAgentColmenaGui";
    private const string ShowEventName = @"Local\EcorexAgentColmenaGui.Show";
    private Mutex? _singleInstance;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showRegistration;
}
