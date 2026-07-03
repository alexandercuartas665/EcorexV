using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

/// <summary>Vista de la marca de la plataforma para pintar el login y otras superficies publicas.</summary>
public sealed record PlatformBrandingDto(
    string PlatformName,
    string? Tagline,
    string? LoginLogoUrl,
    string? LoginHeadline,
    string? LoginSubtext)
{
    /// <summary>Valores por defecto cuando aun no se ha configurado la marca.</summary>
    public static PlatformBrandingDto Default => new(
        "ECOREX.tareas",
        "Sistema de Tareas",
        null,
        "Organiza tu operacion",
        "Gestiona tareas, proyectos, flujos BPMN, formularios y reglas configurables sin codigo, todo en un solo lugar.");
}

public sealed record SaveBrandingRequest(
    string PlatformName,
    string? Tagline,
    string? LoginLogoUrl,
    string? LoginHeadline,
    string? LoginSubtext);

public interface IPlatformBrandingService
{
    Task<PlatformBrandingDto> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SaveBrandingRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marca de la plataforma: tabla de una sola fila. El Super Admin la edita; el login la lee.
/// Si no existe fila, se devuelven valores por defecto sin tocar la BD.
/// </summary>
public sealed class PlatformBrandingService : IPlatformBrandingService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public PlatformBrandingService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PlatformBrandingDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.PlatformBrandings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (row is null) { return PlatformBrandingDto.Default; }
        return new PlatformBrandingDto(row.PlatformName, row.Tagline, row.LoginLogoUrl, row.LoginHeadline, row.LoginSubtext);
    }

    public async Task SaveAsync(SaveBrandingRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(request.PlatformName) ? "ECOREX.tareas" : request.PlatformName.Trim();

        var row = await _db.PlatformBrandings.FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new PlatformBranding();
            _db.PlatformBrandings.Add(row);
        }

        row.PlatformName = name;
        row.Tagline = request.Tagline?.Trim();
        row.LoginLogoUrl = string.IsNullOrWhiteSpace(request.LoginLogoUrl) ? null : request.LoginLogoUrl.Trim();
        row.LoginHeadline = request.LoginHeadline?.Trim();
        row.LoginSubtext = request.LoginSubtext?.Trim();

        _audit.Write(actorUserId, "platform.branding.save", nameof(PlatformBranding), row.Id,
            previousValue: null,
            newValue: new { row.PlatformName, HasLogo = row.LoginLogoUrl is not null });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
