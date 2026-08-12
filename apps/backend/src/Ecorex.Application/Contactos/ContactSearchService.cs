using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Contactos;

/// <summary>
/// CRUD de <see cref="ContactSearchDefinition"/>. Aislamiento por el filtro global de tenant (nunca se
/// filtra a mano por TenantId); el alta estampa el TenantId del contexto.
/// </summary>
public sealed class ContactSearchService : IContactSearchService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ContactSearchService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ContactSearchDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Se materializa antes de Map: este deserializa SchedulesJson (no traducible a SQL).
        var rows = await _db.ContactSearchDefinitions.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ContactSearchDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var x = await _db.ContactSearchDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return x is null ? null : Map(x);
    }

    public async Task<string?> SaveAsync(SaveContactSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return "No hay tenant activo."; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 200) { return "El nombre es obligatorio (maximo 200)."; }
        var prompt = (request.ExtractionPrompt ?? string.Empty).Trim();
        if (prompt.Length == 0) { return "El prompt de extraccion es obligatorio."; }

        // Nombre unico por tenant (defensa: hay indice unico).
        var dupe = await _db.ContactSearchDefinitions
            .AnyAsync(d => d.Name == name && (request.Id == null || d.Id != request.Id), cancellationToken);
        if (dupe) { return $"Ya existe una busqueda llamada '{name}'."; }

        ContactSearchDefinition entity;
        if (request.Id is Guid id)
        {
            var existing = await _db.ContactSearchDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (existing is null) { return "La busqueda no existe."; }
            entity = existing;
        }
        else
        {
            entity = new ContactSearchDefinition { TenantId = tenantId };
            _db.ContactSearchDefinitions.Add(entity);
        }

        entity.Name = name;
        entity.SourceType = request.SourceType;
        entity.Query = Trim(request.Query);
        entity.SubQuery = Trim(request.SubQuery);
        entity.Country = Trim(request.Country);
        entity.Region = Trim(request.Region);
        entity.City = Trim(request.City);
        entity.ExtractionPrompt = prompt;
        entity.ClientId = Trim(request.ClientId);
        entity.ClassifierAiAgentId = request.ClassifierAiAgentId;
        entity.MaxContacts = request.MaxContacts < 0 ? 0 : request.MaxContacts > 500 ? 500 : request.MaxContacts;

        // Varias programaciones: normaliza (descarta Manual, acota hora/dia) y guarda como JSON. La primera
        // se espeja en las columnas LEGACY por compatibilidad. Lista vacia = Manual (solo bajo demanda).
        var slots = NormalizeSlots(request.Schedules);
        entity.SchedulesJson = slots.Count == 0 ? null : JsonSerializer.Serialize(slots, JsonOpts);
        var first = slots.Count > 0 ? slots[0] : null;
        entity.Schedule = first?.Frequency ?? ContactSearchSchedule.Manual;
        entity.RunTime = first?.RunTime;
        entity.DayOfWeek = first?.DayOfWeek;
        entity.DayOfMonth = first?.DayOfMonth;
        entity.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var x = await _db.ContactSearchDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (x is null) { return false; }
        _db.ContactSearchDefinitions.Remove(x);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Acepta "HH:mm" (o "H:mm"); devuelve normalizado a 2 digitos, o null si no es una hora valida.
    private static string? NormalizeTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        return TimeOnly.TryParse(s.Trim(), out var t) ? t.ToString("HH:mm") : null;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Descarta Manual (Manual = sin programacion), acota hora/dia por frecuencia.
    private static List<ContactSearchScheduleSlot> NormalizeSlots(IEnumerable<ContactSearchScheduleSlot>? slots)
    {
        var list = new List<ContactSearchScheduleSlot>();
        if (slots is null) { return list; }
        foreach (var s in slots)
        {
            if (s.Frequency == ContactSearchSchedule.Manual) { continue; }
            var dow = s.Frequency == ContactSearchSchedule.Semanal
                ? (s.DayOfWeek is >= 0 and <= 6 ? s.DayOfWeek : 1) : (int?)null;
            var dom = s.Frequency == ContactSearchSchedule.Mensual
                ? (s.DayOfMonth is >= 1 and <= 31 ? s.DayOfMonth : 1) : (int?)null;
            list.Add(new ContactSearchScheduleSlot(s.Frequency, NormalizeTime(s.RunTime), dow, dom));
        }
        return list;
    }

    // Fuente de verdad = SchedulesJson; si esta vacio pero hay una programacion LEGACY, se sintetiza una.
    private static IReadOnlyList<ContactSearchScheduleSlot> ReadSlots(ContactSearchDefinition x)
    {
        if (!string.IsNullOrWhiteSpace(x.SchedulesJson))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<ContactSearchScheduleSlot>>(x.SchedulesJson, JsonOpts);
                if (list is not null) { return list; }
            }
            catch (JsonException) { /* cae al fallback legacy */ }
        }
        return x.Schedule != ContactSearchSchedule.Manual
            ? new[] { new ContactSearchScheduleSlot(x.Schedule, x.RunTime, x.DayOfWeek, x.DayOfMonth) }
            : Array.Empty<ContactSearchScheduleSlot>();
    }

    private static ContactSearchDto Map(ContactSearchDefinition x) => new(
        x.Id, x.Name, x.SourceType, x.Query, x.SubQuery, x.Country, x.Region, x.City,
        x.ExtractionPrompt, x.ClientId, x.ClassifierAiAgentId, x.MaxContacts,
        ReadSlots(x), x.LastRunAt, x.IsActive);
}
