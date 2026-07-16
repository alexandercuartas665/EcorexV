using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Ecorex.Agent.Gui.Mvvm;
using Ecorex.Agent.Gui.Services;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Gui.ViewModels;

/// <summary>
/// ViewModel raiz de la colmena. Mantiene las celdas (Config ancla + Gateway/Archivos/Navegador +
/// workers efimeros), la configuracion local (DPAPI) y el estado de conexion. En la Ola A los datos
/// los alimenta un MOCK; en la Ola B, el cliente SignalR real -sin cambiar este VM ni la GUI-.
/// </summary>
public sealed class HiveViewModel : ObservableObject
{
    private readonly IAgentConfigStore _store;
    private readonly IHiveConnection _hive;
    private readonly MockHiveConnection? _mock;
    private readonly BrowserAllowList _browserAllow = new();
    private readonly FileAllowList _fileAllow = new();
    private readonly CapabilityConsent _consent = new();
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current.Dispatcher;

    private bool _isCapConfigOpen;
    private SubAgentKind _capKind;
    private string _capTitle = "";
    private string _capHint = "";
    private bool _capEnabled;
    private string _capAllowText = "";

    private string _clientId = "";
    private string _hubUrl = "";
    private string _secret = "";
    private ConnectionState _connection = ConnectionState.Offline;
    private bool _isConfigOpen;
    private bool _busy;
    private bool _demoRunning;

    public HiveViewModel(IAgentConfigStore store, IHiveConnection hive)
    {
        _store = store;
        _hive = hive;
        _mock = hive as MockHiveConnection; // los comandos de DEMO solo aplican con el mock (Ola A)

        // Celdas fijas: Config es el ancla (siempre llena); las 3 capacidades nacen apagadas.
        ConfigCell = new HiveCellViewModel(SubAgentKind.Configuration, "Configuracion", "\u2699");
        Cells = new ObservableCollection<HiveCellViewModel>
        {
            ConfigCell,
            new(SubAgentKind.Gateway, "Gateway", "DB"),
            new(SubAgentKind.Files, "Archivos", "FS"),
            new(SubAgentKind.Browser, "Navegador", "WB"),
        };

        // Carga la config persistida (DPAPI).
        var cfg = _store.Load();
        _clientId = cfg.ClientId;
        _hubUrl = cfg.HubUrl;
        _secret = cfg.Secret;

        _hive.ConnectionChanged += OnConnectionChanged;
        _hive.RequestStarted += OnRequestStarted;
        _hive.RequestFinished += OnRequestFinished;

        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync(), () => !_busy);
        SaveConfigCommand = new RelayCommand(SaveConfig, () => !_busy);
        ToggleConfigCommand = new RelayCommand(() => IsConfigOpen = !IsConfigOpen);
        RunDemoCommand = new RelayCommand(async () => await RunDemoAsync(), () => _mock is not null && !_demoRunning);
        SaveCapabilityCommand = new RelayCommand(SaveCapability);
        CloseCapabilityCommand = new RelayCommand(() => IsCapConfigOpen = false);
    }

    public ObservableCollection<HiveCellViewModel> Cells { get; }

    public HiveCellViewModel ConfigCell { get; }

    public RelayCommand TestConnectionCommand { get; }
    public RelayCommand SaveConfigCommand { get; }
    public RelayCommand ToggleConfigCommand { get; }
    public RelayCommand RunDemoCommand { get; }
    public RelayCommand SaveCapabilityCommand { get; }
    public RelayCommand CloseCapabilityCommand { get; }

    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value);
    }

    public string HubUrl
    {
        get => _hubUrl;
        set => SetProperty(ref _hubUrl, value);
    }

    /// <summary>Secreto del cliente (opcion A). Se persiste cifrado con DPAPI; opcional (sin el, anonimo).</summary>
    public string Secret
    {
        get => _secret;
        set => SetProperty(ref _secret, value);
    }

    public bool IsConfigOpen
    {
        get => _isConfigOpen;
        set => SetProperty(ref _isConfigOpen, value);
    }

    // ---- Flyout de configuracion POR capacidad (Navegador / Archivos): consentimiento + allow-list ----

    public bool IsCapConfigOpen
    {
        get => _isCapConfigOpen;
        set => SetProperty(ref _isCapConfigOpen, value);
    }

    public string CapTitle
    {
        get => _capTitle;
        private set => SetProperty(ref _capTitle, value);
    }

    public string CapHint
    {
        get => _capHint;
        private set => SetProperty(ref _capHint, value);
    }

    /// <summary>Consentimiento local: la capacidad esta habilitada por el operador.</summary>
    public bool CapEnabled
    {
        get => _capEnabled;
        set => SetProperty(ref _capEnabled, value);
    }

    /// <summary>Allow-list editable (una entrada por linea).</summary>
    public string CapAllowText
    {
        get => _capAllowText;
        set => SetProperty(ref _capAllowText, value);
    }

    /// <summary>Abre el flyout de una capacidad sensible (Navegador/Archivos) con su estado actual.</summary>
    public void OpenCapabilityConfig(SubAgentKind kind)
    {
        _capKind = kind;
        IsConfigOpen = false;
        if (kind == SubAgentKind.Browser)
        {
            CapTitle = "Navegador";
            CapHint = "Dominios permitidos, uno por linea (ej. example.com). Vacio = todo bloqueado.";
            CapEnabled = _consent.IsBrowserEnabled();
            CapAllowText = string.Join(Environment.NewLine, _browserAllow.Load());
        }
        else if (kind == SubAgentKind.Files)
        {
            CapTitle = "Archivos";
            CapHint = "Rutas raiz permitidas, una por linea (ej. C:\\Datos). Vacio = todo bloqueado.";
            CapEnabled = _consent.IsFilesEnabled();
            CapAllowText = string.Join(Environment.NewLine, _fileAllow.Load());
        }
        else
        {
            return; // Config/Gateway no usan este flyout
        }
        IsCapConfigOpen = true;
    }

    private void SaveCapability()
    {
        var entries = CapAllowText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0);

        if (_capKind == SubAgentKind.Browser)
        {
            _browserAllow.Save(entries);
            _consent.SetBrowser(CapEnabled);
        }
        else if (_capKind == SubAgentKind.Files)
        {
            _fileAllow.Save(entries);
            _consent.SetFiles(CapEnabled);
        }
        IsCapConfigOpen = false;
    }

    public ConnectionState Connection
    {
        get => _connection;
        private set
        {
            if (SetProperty(ref _connection, value))
            {
                OnPropertyChanged(nameof(ConnectionLabel));
                OnPropertyChanged(nameof(IsOnline));
            }
        }
    }

    public bool IsOnline => _connection == ConnectionState.Online;

    public string ConnectionLabel => _connection switch
    {
        ConnectionState.Online => "En linea",
        ConnectionState.Connecting => "Conectando...",
        _ => "Offline",
    };

    // ---- Config ----

    private void SaveConfig()
    {
        _store.Save(new AgentConfig(ClientId.Trim(), HubUrl.Trim(), Secret.Trim()));
    }

    private AgentConfig CurrentConfig() => new(ClientId.Trim(), HubUrl.Trim(), Secret.Trim());

    /// <summary>Conexion automatica al arrancar (Ola B, cuando ya hay ClientId/URL guardados).</summary>
    public Task AutoConnectAsync() => TestConnectionAsync();

    private async Task TestConnectionAsync()
    {
        _busy = true;
        TestConnectionCommand.RaiseCanExecuteChanged();
        try
        {
            SaveConfig(); // persiste lo tecleado antes de probar
            await _hive.TestConnectionAsync(CurrentConfig());
        }
        finally
        {
            _busy = false;
            TestConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnConnectionChanged(ConnectionState state)
        => _dispatcher.Invoke(() => Connection = state);

    // ---- Crecimiento del panal por peticiones ----

    private void OnRequestStarted(HiveRequest req) => _dispatcher.Invoke(() =>
    {
        // La capacidad se enciende...
        var capability = Cells.FirstOrDefault(c => c.Kind == req.Kind && !c.IsEphemeral);
        if (capability is not null && capability.State == HiveCellState.Idle)
        {
            capability.State = HiveCellState.Active;
        }
        // ...y aparece un worker EFIMERO atendiendo (el panal crece).
        var worker = new HiveCellViewModel(req.Kind, WorkerLabel(req.Kind), GlyphFor(req.Kind), isEphemeral: true, correlationId: req.CorrelationId)
        {
            State = HiveCellState.Working,
            Detail = req.Detail,
        };
        Cells.Add(worker);
    });

    private void OnRequestFinished(HiveRequestResult res) => _dispatcher.Invoke(async () =>
    {
        var worker = Cells.FirstOrDefault(c => c.IsEphemeral && c.CorrelationId == res.CorrelationId);
        if (worker is null) { return; }
        worker.State = res.Ok ? HiveCellState.Active : HiveCellState.Error;

        // El worker se muestra un instante en su estado final y luego se retira (el panal decrece).
        await Task.Delay(res.Ok ? 700 : 1400);
        Cells.Remove(worker);

        // Si ya no quedan workers de esa capacidad, la capacidad vuelve a apagarse.
        if (!Cells.Any(c => c.IsEphemeral && c.Kind == worker.Kind))
        {
            var capability = Cells.FirstOrDefault(c => c.Kind == worker.Kind && !c.IsEphemeral);
            if (capability is not null && capability.State != HiveCellState.Error)
            {
                capability.State = HiveCellState.Idle;
            }
        }
    });

    /// <summary>
    /// Siembra un estado "colmena atendiendo" estable (para evidencia/QA): conexion en linea +
    /// varias peticiones en curso SIN cerrarlas (capacidades encendidas y workers efimeros trabajando).
    /// </summary>
    public void SeedBusyState()
    {
        if (_mock is null) { return; }
        _mock.DemoSetConnection(ConnectionState.Online);
        _mock.DemoStartRequest(SubAgentKind.Gateway, "SELECT ...");
        _mock.DemoStartRequest(SubAgentKind.Browser, "abrir portal");
        _mock.DemoStartRequest(SubAgentKind.Files, "leer PDF");
        _mock.DemoStartRequest(SubAgentKind.Gateway, "UPDATE ...");
    }

    // ---- DEMO (Ola A): guion que muestra encender/atender/apagar y el crecimiento del panal ----

    private async Task RunDemoAsync()
    {
        if (_mock is null || _demoRunning) { return; }
        _demoRunning = true;
        RunDemoCommand.RaiseCanExecuteChanged();
        try
        {
            if (_connection != ConnectionState.Online)
            {
                _mock.DemoSetConnection(ConnectionState.Connecting);
                await Task.Delay(500);
                _mock.DemoSetConnection(ConnectionState.Online);
                await Task.Delay(400);
            }

            // 1) Una consulta al Gateway: nace, atiende, termina ok.
            var g1 = _mock.DemoStartRequest(SubAgentKind.Gateway, "SELECT ...");
            await Task.Delay(1100);
            _mock.DemoFinishRequest(g1, ok: true);
            await Task.Delay(600);

            // 2) Rafaga: Navegador + Archivos en paralelo (el panal crece).
            var b1 = _mock.DemoStartRequest(SubAgentKind.Browser, "abrir portal");
            await Task.Delay(350);
            var f1 = _mock.DemoStartRequest(SubAgentKind.Files, "leer PDF");
            await Task.Delay(300);
            var g2 = _mock.DemoStartRequest(SubAgentKind.Gateway, "UPDATE ...");
            await Task.Delay(1000);
            _mock.DemoFinishRequest(b1, ok: true);
            await Task.Delay(500);
            _mock.DemoFinishRequest(f1, ok: true);
            await Task.Delay(500);

            // 3) Un fallo: termina en error (acento rojo) antes de retirarse.
            _mock.DemoFinishRequest(g2, ok: false, detail: "timeout");
            await Task.Delay(1600);
        }
        finally
        {
            _demoRunning = false;
            RunDemoCommand.RaiseCanExecuteChanged();
        }
    }

    private static string WorkerLabel(SubAgentKind kind) => kind switch
    {
        SubAgentKind.Gateway => "consulta",
        SubAgentKind.Files => "archivo",
        SubAgentKind.Browser => "pagina",
        _ => "tarea",
    };

    private static string GlyphFor(SubAgentKind kind) => kind switch
    {
        SubAgentKind.Gateway => "DB",
        SubAgentKind.Files => "FS",
        SubAgentKind.Browser => "WB",
        _ => "\u2699",
    };
}
