using Ecorex.Domain.Enums;

namespace Ecorex.Application.Contactos;

/// <summary>Una programacion (slot) del programador estilo agente SQL: frecuencia + hora + dia. Manual no
/// se guarda como slot (Manual = lista vacia).</summary>
public sealed record ContactSearchScheduleSlot(
    ContactSearchSchedule Frequency, string? RunTime, int? DayOfWeek, int? DayOfMonth);

/// <summary>Una busqueda de contactos configurada (Cargador de contactos 000873).</summary>
public sealed record ContactSearchDto(
    Guid Id, string Name, ContactSearchSource SourceType, string? Query, string? SubQuery,
    string? Country, string? Region, string? City, string ExtractionPrompt,
    string? ClientId, Guid? ClassifierAiAgentId, int MaxContacts,
    IReadOnlyList<ContactSearchScheduleSlot> Schedules, DateTimeOffset? LastRunAt, bool IsActive);

/// <summary>Alta/edicion de una busqueda configurada.</summary>
public sealed record SaveContactSearchRequest(
    Guid? Id, string Name, ContactSearchSource SourceType, string? Query, string? SubQuery,
    string? Country, string? Region, string? City, string ExtractionPrompt,
    string? ClientId, Guid? ClassifierAiAgentId, int MaxContacts,
    IReadOnlyList<ContactSearchScheduleSlot> Schedules, bool IsActive);

/// <summary>
/// CRUD de las busquedas de contactos configuradas por tenant. La EJECUCION (disparar el agente
/// Colmena + clasificar -> ProspectoScrapeado) vive aparte (orquestador); aqui solo se gestiona la
/// configuracion reutilizable. Todo tenant-scoped por el filtro global.
/// </summary>
public interface IContactSearchService
{
    Task<IReadOnlyList<ContactSearchDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ContactSearchDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Crea (Id null) o actualiza. Devuelve un mensaje de error, o null si OK.</summary>
    Task<string?> SaveAsync(SaveContactSearchRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
