namespace Ecorex.Application.Agents;

// ---- Cliente/agente colmena: identidad on-prem (ClientId + secreto cifrado) que conecta al hub ----
// Modulo propio (ADR-0045): antes vivia dentro de "Contenedor de datos" y su CRUD estaba duplicado en
// Contenedores y Extraccion. Se promueve a recurso transversal: se registra UNA vez aqui y los demas
// modulos (Contenedores, Extraccion, ...) solo SELECCIONAN un cliente existente.

public sealed record AgentClientDto(
    Guid Id,
    string Name,
    string? Description,
    string ClientId,
    bool HasSecret,
    bool IsActive);

/// <summary>Resultado de crear/rotar un cliente: incluye el secreto EN CLARO una sola vez (no se
/// vuelve a mostrar; el resto del tiempo solo se guarda cifrado).</summary>
public sealed record AgentClientSecretDto(Guid Id, string ClientId, string ClientSecret);

public sealed record SaveAgentClientRequest(
    Guid? Id,
    string Name,
    string? Description,
    bool IsActive = true);

/// <summary>
/// Duenio del ciclo de vida de los clientes/agentes colmena de un tenant (tabla <c>data_clients</c>).
/// Genera identidad (ClientId + secreto), permite rotar el secreto, revocar (deshabilitar sin borrar,
/// para no perder historia/bitacora) y borrar. Tenant-scoped por el filtro global.
/// </summary>
public interface IAgentClientService
{
    Task<IReadOnlyList<AgentClientDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Crea o edita un cliente. Si es nuevo, genera ClientId + secreto y lo devuelve UNA vez.</summary>
    Task<(AgentClientDto Client, AgentClientSecretDto? Secret)> SaveAsync(
        SaveAgentClientRequest req, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Rota el secreto de un cliente y devuelve el nuevo en claro UNA vez.</summary>
    Task<AgentClientSecretDto?> RotateSecretAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Revoca (deshabilita) el cliente SIN borrarlo: deja de estar activo pero se conserva la
    /// fila para que su historia/bitacora sigan siendo validas. Devuelve false si no existe.</summary>
    Task<bool> RevokeAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Borra el cliente por completo. Devuelve false si no existe.</summary>
    Task<bool> DeleteAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default);
}
