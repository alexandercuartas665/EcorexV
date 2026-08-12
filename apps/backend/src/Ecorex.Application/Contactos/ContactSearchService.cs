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
        => await _db.ContactSearchDefinitions.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => Map(x))
            .ToListAsync(cancellationToken);

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
        entity.Schedule = request.Schedule;
        // Detalle del programador solo tiene sentido si no es Manual; se limpia cuando vuelve a Manual.
        entity.RunTime = request.Schedule == ContactSearchSchedule.Manual ? null : NormalizeTime(request.RunTime);
        entity.DayOfWeek = request.Schedule == ContactSearchSchedule.Semanal
            ? (request.DayOfWeek is >= 0 and <= 6 ? request.DayOfWeek : 1)
            : null;
        entity.DayOfMonth = request.Schedule == ContactSearchSchedule.Mensual
            ? (request.DayOfMonth is >= 1 and <= 31 ? request.DayOfMonth : 1)
            : null;
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

    private static ContactSearchDto Map(ContactSearchDefinition x) => new(
        x.Id, x.Name, x.SourceType, x.Query, x.SubQuery, x.Country, x.Region, x.City,
        x.ExtractionPrompt, x.ClientId, x.ClassifierAiAgentId, x.MaxContacts, x.Schedule,
        x.RunTime, x.DayOfWeek, x.DayOfMonth, x.LastRunAt, x.IsActive);
}
