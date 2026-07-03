using System.Globalization;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Services;

public sealed record PublicSalonDto(Guid TenantId, string SalonName);
public sealed record PublicAdvisorDto(Guid Id, string Name, bool HasPhoto, ResourceKind Kind, IReadOnlyList<string> Services);
public sealed record PublicBookingInput(Guid ResourceId, DateOnly Date, string Time, IReadOnlyList<Guid> ServiceIds,
    string ClientName, string ClientPhone, HairLength? HairLength);
public sealed record PublicBookingResult(bool Ok, string? Error);

/// <summary>
/// Orquesta la reserva PUBLICA (link /r/{token}) sin login. Resuelve el tenant por su token opaco y
/// reusa el motor de agenda existente bajo AmbientTenantContext.Begin(tenant), de modo que el aislamiento
/// por tenant y el ANTI-OVERBOOKING aplican igual que en la consola. La cita queda Online + Programada.
/// </summary>
public interface IPublicBookingService
{
    Task<PublicSalonDto?> ResolveAsync(string token, CancellationToken ct = default);
    Task<IReadOnlyList<PublicAdvisorDto>> GetAdvisorsAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceOptionDto>> GetServicesAsync(Guid tenantId, Guid resourceId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAvailableStartsAsync(Guid tenantId, Guid resourceId, IReadOnlyList<Guid> serviceIds, HairLength? hairLength, DateOnly date, CancellationToken ct = default);
    Task<HairClassificationResultDto> ClassifyAsync(Guid tenantId, string base64, string mime, CancellationToken ct = default);
    Task<PublicBookingResult> BookAsync(Guid tenantId, PublicBookingInput input, CancellationToken ct = default);
    /// <summary>Mapea el NOMBRE de la medida detectada (categoria del salon) al enum de largo de las tarifas.</summary>
    HairLength? MapHairLength(string? name);
}

public sealed class PublicBookingService : IPublicBookingService
{
    private readonly IApplicationDbContext _db;
    private readonly IResourceService _resources;
    private readonly IAgendaService _agenda;
    private readonly IHairClassifierService _classifier;

    public PublicBookingService(IApplicationDbContext db, IResourceService resources, IAgendaService agenda, IHairClassifierService classifier)
    {
        _db = db; _resources = resources; _agenda = agenda; _classifier = classifier;
    }

    public async Task<PublicSalonDto?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) { return null; }
        // Sin contexto de tenant aun: el token ES la frontera. Se ignora el query filter para resolverlo.
        var t = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicBookingToken == token && x.OnlineBookingEnabled, ct);
        return t is null ? null : new PublicSalonDto(t.Id, t.Name);
    }

    public async Task<IReadOnlyList<PublicAdvisorDto>> GetAdvisorsAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var _ = AmbientTenantContext.Begin(tenantId);
        var list = await _resources.ListAsync(ct);
        return list.Where(r => r.IsActive)
            .Select(r => new PublicAdvisorDto(r.Id, r.Name, r.HasPhoto, r.Kind, r.Services.Select(s => s.ServiceName).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<ServiceOptionDto>> GetServicesAsync(Guid tenantId, Guid resourceId, CancellationToken ct = default)
    {
        using var _ = AmbientTenantContext.Begin(tenantId);
        return await _agenda.GetServiceOptionsAsync(resourceId, ct);
    }

    public async Task<IReadOnlyList<string>> GetAvailableStartsAsync(Guid tenantId, Guid resourceId, IReadOnlyList<Guid> serviceIds, HairLength? hairLength, DateOnly date, CancellationToken ct = default)
    {
        using var _ = AmbientTenantContext.Begin(tenantId);
        var starts = await _agenda.GetAvailableStartsAsync(resourceId, date, serviceIds, hairLength, ct);
        return starts.Select(t => t.ToString("HH\\:mm")).ToList();
    }

    public async Task<HairClassificationResultDto> ClassifyAsync(Guid tenantId, string base64, string mime, CancellationToken ct = default)
    {
        using var _ = AmbientTenantContext.Begin(tenantId);
        return await _classifier.ClassifyAsync(base64, mime, null, Guid.Empty, ct);
    }

    public async Task<PublicBookingResult> BookAsync(Guid tenantId, PublicBookingInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.ClientName)) { return new(false, "Escribe tu nombre."); }
        var digits = new string((input.ClientPhone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 7) { return new(false, "Escribe un telefono valido (con codigo de pais)."); }
        if (input.ServiceIds is null || input.ServiceIds.Count == 0) { return new(false, "Elige al menos un servicio."); }
        if (!TimeOnly.TryParseExact(input.Time, "HH\\:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            && !TimeOnly.TryParse(input.Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
        {
            return new(false, "La hora elegida no es valida.");
        }

        using var _ = AmbientTenantContext.Begin(tenantId);
        var req = new BookingRequest(
            AppointmentId: null, ResourceId: input.ResourceId, Date: input.Date, StartTime: start,
            ClientName: input.ClientName.Trim(), ClientPhone: input.ClientPhone?.Trim(), ClientId: null,
            ServiceIds: input.ServiceIds, Status: AppointmentStatus.Scheduled, Punctuality: Punctuality.Unknown,
            Notes: "Reserva online", ChainSteps: Array.Empty<BookingChainStep>(), Chat: Array.Empty<BookingChatLine>(),
            RescheduledFromId: null, FieldValues: null, HairLength: input.HairLength, Channel: BookingChannel.Online);
        var res = await _agenda.SaveBookingAsync(req, Guid.Empty, ct);
        return new PublicBookingResult(res.Success, res.Error);
    }

    public HairLength? MapHairLength(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return null; }
        var n = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        var k = sb.ToString();
        return k switch
        {
            "corto" => HairLength.Corto,
            "medio" or "mediano" => HairLength.Medio,
            "largo" => HairLength.Largo,
            "muy largo" or "muylargo" or "extra largo" or "extralargo" => HairLength.MuyLargo,
            _ => Enum.TryParse<HairLength>(name, true, out var e) ? e : null
        };
    }
}
