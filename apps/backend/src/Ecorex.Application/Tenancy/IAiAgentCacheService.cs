namespace Ecorex.Application.Tenancy;

/// <summary>
/// Gestion de "Datos Cache" del agente (capa 3): definicion de campos que el agente debe capturar
/// durante la conversacion y los valores percibidos por sesion. La sesion se identifica por SessionId:
/// para pruebas internas del modulo /agentes se usa el AgentId como sesion; cuando un telefono se enlace
/// al sistema se usara el ConversationId como sesion.
/// </summary>
public interface IAiAgentCacheService
{
    // Definicion de campos del agente.
    Task<IReadOnlyList<AiAgentCacheFieldDto>> ListFieldsAsync(Guid agentId, CancellationToken cancellationToken = default);
    Task<AiAgentCacheFieldDto?> CreateFieldAsync(CreateAgentCacheFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<AiAgentCacheFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateAgentCacheFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca todos los campos cache del agente como actualizables o sticky de una sola vez.
    /// Devuelve cuantos quedaron afectados.
    /// </summary>
    Task<int> BulkSetFieldsUpdatableAsync(Guid agentId, bool isUpdatable, Guid actorUserId, CancellationToken cancellationToken = default);

    // Valores capturados en una sesion (datos percibidos).
    Task<IReadOnlyList<AiAgentCacheValueDto>> GetValuesAsync(Guid agentId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<AiAgentCacheValueDto?> SetValueAsync(SetAgentCacheValueRequest request, CancellationToken cancellationToken = default);
    Task<int> ClearValuesAsync(Guid agentId, Guid sessionId, Guid actorUserId, CancellationToken cancellationToken = default);
}
