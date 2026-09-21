namespace Ecorex.Application.Tenancy;

/// <summary>Un dia no operativo del tenant (festivo / dia sin operacion) que el modo "habil" de los plazos
/// de flujo salta (Fase 2 - plazos de flujo, ADR-0106).</summary>
public sealed record TenantOperatingDayDto(Guid Id, DateOnly Date, string? Reason);

/// <summary>
/// CRUD del CALENDARIO OPERATIVO del tenant: los dias no operativos (festivos / sin operacion) que se marcan en
/// la configuracion de la entidad. Tenant-scoped por el filtro global. Los usa el modo "habil" del calculo de
/// plazos (StepDeadlineCalculator) ademas de sabados y domingos.
/// </summary>
public interface ITenantOperatingCalendarService
{
    /// <summary>Dias no operativos, ordenados por fecha. Si <paramref name="year"/> viene, filtra a ese anio.</summary>
    Task<IReadOnlyList<TenantOperatingDayDto>> ListAsync(int? year = null, CancellationToken cancellationToken = default);

    /// <summary>Marca un dia como no operativo. Idempotente por fecha (si ya existe, actualiza el motivo).</summary>
    Task<TenantOperatingDayDto> AddAsync(DateOnly date, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Quita un dia no operativo por su id. Devuelve false si no existe (en el tenant).</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
