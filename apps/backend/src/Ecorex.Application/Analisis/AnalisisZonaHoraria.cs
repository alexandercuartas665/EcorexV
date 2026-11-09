using Ecorex.Application.Common;
using Ecorex.Application.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Analisis;

/// <summary>
/// Zona horaria del tenant para los filtros de fecha de la hoja (regla 9: zona del tenant + UTC). Un dia
/// "1 de noviembre" del usuario empieza a las 00:00 de SU zona, no a las 00:00 UTC.
/// </summary>
public interface IAnalisisZonaHoraria
{
    Task<TimeZoneInfo> ObtenerAsync(CancellationToken ct = default);
}

/// <summary>Lee Tenant.TimeZoneId una vez por scope (circuito) en un contexto efimero (ADR-0157).</summary>
public sealed class AnalisisZonaHoraria : IAnalisisZonaHoraria
{
    private readonly ITenantContext _tenant;
    private readonly IApplicationDbContextFactory _factory;
    private TimeZoneInfo? _zona;

    public AnalisisZonaHoraria(ITenantContext tenant, IApplicationDbContextFactory factory)
    {
        _tenant = tenant;
        _factory = factory;
    }

    public async Task<TimeZoneInfo> ObtenerAsync(CancellationToken ct = default)
    {
        if (_zona is not null)
        {
            return _zona;
        }

        string? tzId = null;
        if (_tenant.TenantId is Guid tenantId)
        {
            await using var db = _factory.Create();
            tzId = await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId).Select(t => t.TimeZoneId)
                .FirstOrDefaultAsync(ct);
        }

        _zona = ScheduledJobRecurrence.ResolveTimeZone(tzId);
        return _zona;
    }
}
