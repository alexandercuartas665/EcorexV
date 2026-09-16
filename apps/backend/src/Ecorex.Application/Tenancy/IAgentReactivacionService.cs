namespace Ecorex.Application.Tenancy;

/// <summary>
/// Secuencia de REACTIVACION del agente: revive contactos dormidos (dejaron de responder sin cerrar). El
/// tenant configura N pasos por agente (offset de horas + texto y/o plantilla). Un worker de fondo corre
/// <see cref="RunTenantAsync"/> tenant por tenant: busca conversaciones dormidas, elige el siguiente paso
/// pendiente cuyo offset ya se cumplio y lo envia respetando la ventana de 24h de Meta (texto libre dentro,
/// plantilla HSM fuera; si no hay plantilla fuera de 24h, omite el paso). Es determinista y server-side.
/// </summary>
public interface IAgentReactivacionService
{
    /// <summary>Configuracion de reactivacion del agente (nunca null; vacia si no hay).</summary>
    Task<AgentReactivacionConfig> GetConfigAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>Guarda la configuracion de reactivacion del agente. Devuelve false si el agente no existe.</summary>
    Task<bool> SaveConfigAsync(Guid agentId, AgentReactivacionConfig config, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corre una pasada de reactivacion para el tenant AMBIENTE (el worker fija el TenantId en el scope, asi
    /// el filtro global acota las consultas). Envia como maximo UN paso por conversacion en esta corrida y es
    /// idempotente (no reenvia un paso ya enviado). Devuelve cuantos mensajes de reactivacion se enviaron.
    /// </summary>
    Task<int> RunTenantAsync(CancellationToken cancellationToken = default);
}
