namespace Ecorex.SuperAdmin.Agents;

/// <summary>Presencia de un agente on-prem conectado al hub (doc 03 s2).</summary>
public sealed record AgentPresence(
    string ClientId,
    Guid TenantId,
    string ConnectionId,
    string? Host,
    string? Version,
    DateTimeOffset ConnectedAt,
    DateTimeOffset LastSeen);

/// <summary>
/// Sabe que agentes (`clientId`) estan en linea, para responder "hay agente?" antes de empujar una
/// orden y para mostrar el estado en el web (doc 03 s2). v1 en memoria (una instancia); multi-instancia
/// con backplane Redis queda como backlog.
/// </summary>
public interface IAgentRegistry
{
    /// <summary>Registra una conexion como agente en linea.</summary>
    void MarkOnline(string clientId, Guid tenantId, string connectionId);

    /// <summary>Quita una conexion (desconexion).</summary>
    void MarkOffline(string connectionId);

    /// <summary>Actualiza metadatos reportados por AgentHello (host/version).</summary>
    void Hello(string connectionId, string? host, string? version);

    /// <summary>Marca actividad reciente (heartbeat / mensajes).</summary>
    void Touch(string connectionId);

    /// <summary>true si ese clientId tiene al menos una conexion activa.</summary>
    bool IsOnline(string clientId);

    /// <summary>Presencia de un clientId (la mas reciente), o null si offline.</summary>
    AgentPresence? Get(string clientId);

    /// <summary>Agentes en linea de un tenant (para el panel "Clientes remotos").</summary>
    IReadOnlyCollection<AgentPresence> ForTenant(Guid tenantId);
}
