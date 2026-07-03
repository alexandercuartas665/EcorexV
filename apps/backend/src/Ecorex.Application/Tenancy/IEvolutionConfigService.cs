namespace Ecorex.Application.Tenancy;

/// <summary>
/// Configuracion de Evolution API del tenant activo (modulo 1.3). El token nunca se
/// devuelve completo: las respuestas exponen solo una version enmascarada.
/// </summary>
public interface IEvolutionConfigService
{
    Task<EvolutionConfigDto?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea o actualiza la config del tenant activo. Devuelve null si no hay tenant activo.</summary>
    Task<EvolutionConfigDto?> UpsertAsync(UpsertEvolutionConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
