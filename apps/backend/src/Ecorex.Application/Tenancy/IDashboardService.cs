namespace Ecorex.Application.Tenancy;

/// <summary>Metricas comerciales del tenant activo (modulo 2.6). Tenant-scoped, solo lectura.</summary>
public interface IDashboardService
{
    Task<TenantDashboardDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Reportes comerciales del modulo de Metricas (embudo, asesores, ventas, motivos, destinos, WhatsApp).</summary>
    Task<TenantReportsDto> GetReportsAsync(CancellationToken cancellationToken = default);
}
