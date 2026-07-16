using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Ecorex.Agent.Gui.Services;
using Ecorex.Agent.Gui.ViewModels;
using Ecorex.Contracts.Agent;
using Forms = System.Windows.Forms;

namespace Ecorex.Agent.Gui;

/// <summary>
/// Ventana principal de la colmena (Ola A): sin borde, translucida y arrastrable por la barra
/// superior, con minimizar/cerrar propios y un icono de bandeja (tray). El panal (ItemsControl +
/// HoneycombPanel + HexTile) y el flyout de configuracion viven en el XAML; el estado lo gobierna
/// el HiveViewModel con datos MOCK (Ola A). Cerrar oculta a la bandeja; "Salir" termina de verdad.
/// </summary>
public partial class MainWindow : Window
{
    private readonly HiveViewModel _vm;
    private Forms.NotifyIcon? _tray;
    private Services.AgentMcpServer? _mcp;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();

        // Seam IHiveConnection: por defecto el cliente SignalR REAL (Ola B); en modo captura/QA
        // (ECOREX_AGENT_CAPTURE) o forzado, el MOCK (Ola A) para demos deterministas. La ventana y
        // el ViewModel son identicos en ambos casos.
        var store = new DpapiConfigStore();
        var cfg = store.Load();
        var captureMode = Environment.GetEnvironmentVariable("ECOREX_AGENT_CAPTURE");
        var forceMock = string.Equals(Environment.GetEnvironmentVariable("ECOREX_AGENT_FORCE_MOCK"), "1", StringComparison.Ordinal);
        var useMock = !string.IsNullOrEmpty(captureMode) || forceMock;

        IHiveConnection hive;
        if (useMock)
        {
            hive = new MockHiveConnection();
        }
        else
        {
            // Sub-agente Navegador (WebView2) compartido por el hub y el servidor MCP local.
            var browser = new WebView2BrowserSubAgent();
            hive = new RealHiveConnection(cfg, browser);
            _mcp = new AgentMcpServer(browser);
            try { _mcp.Start(); }
            catch { _mcp = null; /* puerto ocupado / sin permiso: el MCP queda apagado */ }
        }
        _vm = new HiveViewModel(store, hive);
        DataContext = _vm;

        InitTray();
        ApplyCaptureHook();

        // En modo real, si ya hay config completa, conecta solo al arrancar.
        if (!useMock && cfg.IsComplete)
        {
            Loaded += async (_, _) => await _vm.AutoConnectAsync();
        }
    }

    /// <summary>
    /// Hook de captura/QA (inerte salvo que se defina ECOREX_AGENT_CAPTURE): "config" abre el panel
    /// al arrancar; "demo" corre la DEMO. Sirve para tomar evidencias deterministas sin inyectar
    /// teclas globales. No afecta el uso normal (la variable no existe en produccion).
    /// </summary>
    private void ApplyCaptureHook()
    {
        var mode = Environment.GetEnvironmentVariable("ECOREX_AGENT_CAPTURE");
        if (string.Equals(mode, "config", StringComparison.OrdinalIgnoreCase))
        {
            _vm.IsConfigOpen = true;
        }
        else if (string.Equals(mode, "demo", StringComparison.OrdinalIgnoreCase))
        {
            Loaded += async (_, _) => { await System.Threading.Tasks.Task.Delay(400); _vm.RunDemoCommand.Execute(null); };
        }
        else if (string.Equals(mode, "busy", StringComparison.OrdinalIgnoreCase))
        {
            // Frame estable "colmena atendiendo": siembra peticiones sin cerrarlas (para evidencia).
            Loaded += (_, _) => _vm.SeedBusyState();
        }
        else if (string.Equals(mode, "capbrowser", StringComparison.OrdinalIgnoreCase))
        {
            Loaded += (_, _) => _vm.OpenCapabilityConfig(Ecorex.Contracts.Agent.SubAgentKind.Browser);
        }
        else if (string.Equals(mode, "capfiles", StringComparison.OrdinalIgnoreCase))
        {
            Loaded += (_, _) => _vm.OpenCapabilityConfig(Ecorex.Contracts.Agent.SubAgentKind.Files);
        }
    }

    /// <summary>Arrastre de la ventana sin barra de titulo nativa (solo con boton izquierdo).</summary>
    private void OnDragBackground(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Clic sobre una celda: Configuracion abre su flyout; las capacidades sensibles (Navegador/
    /// Archivos) abren su flyout de consentimiento + allow-list. Los workers efimeros no abren nada.
    /// </summary>
    private void OnCellClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HiveCellViewModel cell) { return; }

        if (cell.IsConfig)
        {
            _vm.IsConfigOpen = !_vm.IsConfigOpen;
            e.Handled = true;
        }
        else if (!cell.IsEphemeral && cell.Kind is SubAgentKind.Browser or SubAgentKind.Files)
        {
            _vm.OpenCapabilityConfig(cell.Kind);
            e.Handled = true;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>El boton cerrar oculta a la bandeja (no termina el proceso).</summary>
    private void OnClose(object sender, RoutedEventArgs e) => HideToTray();

    // ---- Tray icon (System.Windows.Forms.NotifyIcon, sin NuGet) ----

    private void InitTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar", null, (_, _) => ShowFromTray());
        menu.Items.Add("Demo (Ctrl+D)", null, (_, _) => { ShowFromTray(); _vm.RunDemoCommand.Execute(null); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Text = "ECOREX - Agente Conector",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void HideToTray()
    {
        Hide();
        _tray?.ShowBalloonTip(1500, "ECOREX", "El agente sigue activo en la bandeja.", Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Cerrar por la X interna oculta a la bandeja; solo "Salir" cierra de verdad.
        if (!_reallyExit)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _mcp?.Stop();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        base.OnClosing(e);
    }
}
