namespace CubotTravels.Application.Tenancy;

/// <summary>Metricas comerciales del tenant activo (modulo 2.6). Tenant-scoped, solo lectura.</summary>
public interface IDashboardService
{
    Task<TenantDashboardDto> GetAsync(CancellationToken cancellationToken = default);
}
