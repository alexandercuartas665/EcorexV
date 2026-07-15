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
