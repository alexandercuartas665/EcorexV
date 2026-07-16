namespace Ecorex.Contracts.Agent;

/// <summary>
/// Capacidad de un sub-agente de la colmena (doc 06 s3.2). Extensible: capacidades nuevas =
/// valores nuevos, sin tocar el orquestador. <see cref="Configuration"/> es la celda ancla
/// (siempre llena) desde la que se configura el ClientId.
/// </summary>
public enum SubAgentKind
{
    /// <summary>Celda ancla de configuracion/monitoreo (siempre llena). No es un sub-agente ejecutable.</summary>
    Configuration,

    /// <summary>Gateway de datos: consulta solo-lectura contra BD/API de la LAN (docs 01-05).</summary>
    Gateway,

    /// <summary>Archivos/Directorios: lee/escribe archivos del equipo segun tareas del servidor.</summary>
    Files,

    /// <summary>Navegador web: abre paginas, ejecuta JS inyectado y expone herramientas MCP.</summary>
    Browser,
}

/// <summary>
/// Estado visual de una celda del panal (una capacidad o un worker efimero). Gobierna el
/// relleno/animacion del hexagono en la GUI.
/// </summary>
public enum HiveCellState
{
    /// <summary>Apagado: capacidad disponible pero sin actividad. Hexagono vacio (solo contorno).</summary>
    Idle,

    /// <summary>Encendido: la capacidad esta activa/lista. Hexagono lleno con glow suave.</summary>
    Active,

    /// <summary>Atendiendo una peticion: hexagono lleno con pulso.</summary>
    Working,

    /// <summary>La ultima operacion fallo: hexagono en estado de error.</summary>
    Error,
}

/// <summary>
/// Estado de la conexion del orquestador con el servidor (la app web). En la Ola A es solo
/// visual (lo alterna el stub "Probar conexion"); en la Ola B lo alimenta el cliente SignalR.
/// </summary>
public enum ConnectionState
{
    Offline,
    Connecting,
    Online,
}

/// <summary>
/// Configuracion local del agente (doc 06 s3.4): ata el equipo on-prem a un cliente/tenant por
/// su ClientId. Se persiste cifrada con DPAPI (por-usuario Windows); NUNCA en el repo.
/// </summary>
public sealed record AgentConfig(string ClientId, string HubUrl, string Secret = "")
{
    public static readonly AgentConfig Empty = new(string.Empty, string.Empty, string.Empty);

    public bool IsComplete => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(HubUrl);

    /// <summary>Hay secreto para el handshake autenticado (opcion A). Sin el, se conecta anonimo.</summary>
    public bool HasSecret => !string.IsNullOrWhiteSpace(Secret);
}

/// <summary>
/// Almacen local de la configuracion del agente. La implementacion de la Ola A usa DPAPI; el
/// orquestador de la Ola B reusa la misma interfaz. Deja el punto de extension listo.
/// </summary>
public interface IAgentConfigStore
{
    /// <summary>Carga la configuracion persistida, o <see cref="AgentConfig.Empty"/> si no hay.</summary>
    AgentConfig Load();

    /// <summary>Persiste la configuracion (cifrada).</summary>
    void Save(AgentConfig config);

    /// <summary>Borra la configuracion persistida.</summary>
    void Clear();
}

/// <summary>
/// Sub-agente Navegador visto por quien lo NECESITA (el canal y el servidor MCP), sin saber con que
/// esta hecho ni en que hilo vive (ADR-0039). La implementacion WebView2 es un control de UI y
/// marshala al Dispatcher POR DENTRO; una implementacion headless (Playwright) o una que delegue por
/// IPC a la colmena interactiva encajan igual. Este seam es lo que permite que el canal y el MCP no
/// dependan de WPF y puedan vivir en un Worker Service sin escritorio.
/// </summary>
public interface IBrowserSubAgent
{
    /// <summary>Ejecuta la secuencia de acciones tipadas. Seguro de llamar desde cualquier hilo.</summary>
    Task<BrowserResultMsg> ExecuteAsync(BrowserRequestMsg request);

    /// <summary>El host (dominio) esta en la allow-list local. Lo usa el MCP para responder rapido.</summary>
    bool IsAllowed(string? host);

    /// <summary>
    /// El dueno de la boveda EMPUJA la politica vigente (ADR-0039). Existe porque el navegador vive
    /// en la colmena, que corre sin elevar y NO puede leer la boveda: si se dejara que la consultara
    /// sola, fallaria cerrado siempre. Quien la lee es el servicio, y la manda por el pipe.
    /// </summary>
    void ApplyPolicy(BrowserPolicy policy);
}

/// <summary>
/// Permiso vigente del Navegador: si el operador lo habilito y a que dominios puede ir. Viaja del
/// servicio (dueno de la boveda) a la colmena (dueno del escritorio). Fail-closed por defecto.
/// </summary>
public sealed record BrowserPolicy(bool Enabled, IReadOnlyList<string> Domains)
{
    /// <summary>Nada permitido: el estado inicial, hasta que el servicio diga otra cosa.</summary>
    public static readonly BrowserPolicy Denied = new(false, Array.Empty<string>());

    /// <summary>Coincidencia por sufijo de host ("example.com" permite "www.example.com").</summary>
    public bool IsAllowed(string? host)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(host)) { return false; }
        host = host.ToLowerInvariant();
        foreach (var d in Domains)
        {
            var domain = d.Trim().ToLowerInvariant();
            if (domain.Length == 0) { continue; }
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)) { return true; }
        }
        return false;
    }
}

/// <summary>
/// Fuente de eventos de la colmena que la GUI observa. En la Ola A la implementa un MOCK (modo
/// demo); en la Ola B la implementara el cliente SignalR real, sin cambiar la GUI ni el
/// ViewModel. Es el punto de sutura entre "lo que se ve" (esta ola) y "los datos reales".
/// </summary>
public interface IHiveConnection
{
    /// <summary>Estado de conexion con el servidor.</summary>
    ConnectionState State { get; }

    /// <summary>Cambia el estado de conexion (Ola A: lo dispara el stub "Probar conexion").</summary>
    event Action<ConnectionState>? ConnectionChanged;

    /// <summary>
    /// Una peticion entrante para una capacidad: el orquestador abriria un worker efimero. En la
    /// GUI hace que aparezca una celda nueva (crecimiento del panal) que pulsa y luego se retira.
    /// </summary>
    event Action<HiveRequest>? RequestStarted;

    /// <summary>Una peticion termino (ok o error): la celda del worker se apaga/retira.</summary>
    event Action<HiveRequestResult>? RequestFinished;

    /// <summary>Intento de conexion (Ola A: stub que alterna Online/Offline).</summary>
    Task<bool> TestConnectionAsync(AgentConfig config, CancellationToken cancellationToken = default);
}

/// <summary>Peticion entrante para una capacidad (correlationId = request/response del canal push).</summary>
public sealed record HiveRequest(string CorrelationId, SubAgentKind Kind, string? Detail = null);

/// <summary>Resultado de una peticion atendida.</summary>
public sealed record HiveRequestResult(string CorrelationId, bool Ok, string? Detail = null);
