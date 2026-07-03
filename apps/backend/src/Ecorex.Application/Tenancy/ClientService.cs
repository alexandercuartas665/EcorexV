using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record ClientListItemDto(Guid Id, string FullName, string Phone, int VisitCount, int NoShowCount, int? PunctualityRate);
public sealed record ClientHistoryItemDto(Guid AppointmentId, DateOnly Date, TimeOnly StartTime, string ResourceName,
    string ServicesText, AppointmentStatus Status, Punctuality Punctuality, decimal EstimatedValue);
public sealed record ClientDetailDto(Guid Id, string FullName, string Phone, string? Email, int VisitCount, int NoShowCount,
    int? PunctualityRate, decimal TotalSpent, string? PreferredResourceName, string? TopServiceName, string? Notes,
    IReadOnlyList<ClientHistoryItemDto> History, IReadOnlyDictionary<string, string?>? FieldValues = null,
    IReadOnlyList<Guid>? BusinessUnitIds = null);

/// <summary>Ficha del cliente (Modulo 2.6): lista con puntualidad e historial con marcado de puntualidad por visita.</summary>
public interface IClientService
{
    Task<IReadOnlyList<ClientListItemDto>> ListAsync(string? search, CancellationToken cancellationToken = default);
    Task<ClientDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Guarda los valores de los campos configurables del cliente (ficha).</summary>
    Task<bool> SaveFieldValuesAsync(Guid clientId, IReadOnlyDictionary<string, string?> values, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Guarda los canales / unidades de negocio del cliente (multi-canal).</summary>
    Task<bool> SaveBusinessUnitsAsync(Guid clientId, IReadOnlyList<Guid> businessUnitIds, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Busca un cliente por telefono (o nombre) y lo crea si no existe. Devuelve su Id. Usado por el puente desde el pipeline.</summary>
    Task<Guid?> EnsureByPhoneAsync(string name, string? phone, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class ClientService : IClientService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ClientService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid?> EnsureByPhoneAsync(string name, string? phone, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var clean = (name ?? string.Empty).Trim();
        var ph = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        if (ph is not null)
        {
            var byPhone = await _db.Clients.FirstOrDefaultAsync(c => c.Phone == ph, cancellationToken);
            if (byPhone is not null) { return byPhone.Id; }
        }
        if (clean.Length > 0)
        {
            var byName = await _db.Clients.FirstOrDefaultAsync(c => c.FullName == clean, cancellationToken);
            if (byName is not null) { return byName.Id; }
        }
        if (clean.Length == 0 && ph is null) { return null; }

        var created = new Client { TenantId = tenantId, FullName = clean.Length == 0 ? (ph ?? "Cliente") : clean, Phone = ph ?? string.Empty };
        _db.Clients.Add(created);
        await _db.SaveChangesAsync(cancellationToken);
        return created.Id;
    }

    // Tasa de puntualidad (derivada): OnTime / (OnTime + Late). Null si no hay datos.
    private static int? Rate(int onTime, int late) => (onTime + late) == 0 ? null : (int)Math.Round(onTime * 100.0 / (onTime + late));

    public async Task<IReadOnlyList<ClientListItemDto>> ListAsync(string? search, CancellationToken cancellationToken = default)
    {
        var q = (search ?? string.Empty).Trim();
        var query = _db.Clients.AsNoTracking().AsQueryable();
        if (q.Length > 0)
        {
            var ql = q.ToLowerInvariant();
            query = query.Where(c => c.FullName.ToLower().Contains(ql) || c.Phone.Contains(q));
        }
        var clients = await query.OrderBy(c => c.FullName).ToListAsync(cancellationToken);
        return clients.Select(c => new ClientListItemDto(c.Id, c.FullName, c.Phone, c.VisitCount, c.NoShowCount, Rate(c.OnTimeCount, c.LateCount))).ToList();
    }

    public async Task<ClientDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null) { return null; }

        var appts = await _db.Appointments.AsNoTracking()
            .Where(a => a.ClientId == id)
            .OrderByDescending(a => a.AppointmentDate).ThenByDescending(a => a.StartTime)
            .ToListAsync(cancellationToken);
        var apptIds = appts.Select(a => a.Id).ToList();

        var items = apptIds.Count == 0
            ? new List<AppointmentServiceItem>()
            : await _db.AppointmentServiceItems.AsNoTracking().Where(i => apptIds.Contains(i.AppointmentId)).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var serviceNames = await _db.Services.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var resourceNames = await _db.Resources.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var servicesText = items.GroupBy(i => i.AppointmentId)
            .ToDictionary(g => g.Key, g => string.Join(" + ", g.Select(i => serviceNames.TryGetValue(i.ServiceId, out var n) ? n : "")));

        // Servicio top (mas frecuente en el historial).
        string? topService = items.GroupBy(i => i.ServiceId)
            .OrderByDescending(g => g.Count())
            .Select(g => serviceNames.TryGetValue(g.Key, out var n) ? n : null)
            .FirstOrDefault();

        // Asesor preferido: el configurado, o el mas frecuente en citas completadas.
        string? preferred = null;
        if (c.PreferredResourceId is Guid pref && resourceNames.TryGetValue(pref, out var pn)) { preferred = pn; }
        else
        {
            var topRes = appts.Where(a => a.Status == AppointmentStatus.Completed)
                .GroupBy(a => a.ResourceId).OrderByDescending(g => g.Count()).Select(g => (Guid?)g.Key).FirstOrDefault();
            if (topRes is Guid tr && resourceNames.TryGetValue(tr, out var trn)) { preferred = trn; }
        }

        var totalSpent = appts.Where(a => a.Status == AppointmentStatus.Completed).Sum(a => a.EstimatedValue ?? 0m);

        var history = appts.Select(a => new ClientHistoryItemDto(
            a.Id, a.AppointmentDate, a.StartTime,
            resourceNames.TryGetValue(a.ResourceId, out var rn) ? rn : "?",
            servicesText.TryGetValue(a.Id, out var st) ? st : "",
            a.Status, a.Punctuality, a.EstimatedValue ?? 0m)).ToList();

        return new ClientDetailDto(c.Id, c.FullName, c.Phone, c.Email, c.VisitCount, c.NoShowCount,
            Rate(c.OnTimeCount, c.LateCount), totalSpent, preferred, topService, null, history,
            SalonFieldJson.Parse(c.FieldValuesJson), ParseUnitIds(c.BusinessUnitIdsJson));
    }

    // Serializacion de la lista de canales (unidades de negocio) del cliente.
    private static IReadOnlyList<Guid> ParseUnitIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<Guid>(); }
        try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>(); }
        catch { return Array.Empty<Guid>(); }
    }

    public async Task<bool> SaveFieldValuesAsync(Guid clientId, IReadOnlyDictionary<string, string?> values, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        if (client is null) { return false; }
        client.FieldValuesJson = SalonFieldJson.Serialize(values);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SaveBusinessUnitsAsync(Guid clientId, IReadOnlyList<Guid> businessUnitIds, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        if (client is null) { return false; }
        var ids = (businessUnitIds ?? Array.Empty<Guid>()).Distinct().ToList();
        client.BusinessUnitIdsJson = ids.Count == 0 ? null : JsonSerializer.Serialize(ids);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
