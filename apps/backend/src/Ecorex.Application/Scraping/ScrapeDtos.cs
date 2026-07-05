using Ecorex.Domain.Enums;

namespace Ecorex.Application.Scraping;

/// <summary>Estados del resultado tipado del modulo de extraccion (patron TaskCoreResult).</summary>
public enum ScrapeOpStatus
{
    Ok = 0,
    /// <summary>La fuente no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos (validacion de negocio, URL bloqueada al guardar, etc.).</summary>
    Invalid
}

public sealed record ScrapeOpResult<T>(ScrapeOpStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == ScrapeOpStatus.Ok;

    public static ScrapeOpResult<T> Ok(T value) => new(ScrapeOpStatus.Ok, value, null);
    public static ScrapeOpResult<T> NotFound(string? error = null) => new(ScrapeOpStatus.NotFound, default, error ?? "Fuente no encontrada.");
    public static ScrapeOpResult<T> Invalid(string error) => new(ScrapeOpStatus.Invalid, default, error);
}

/// <summary>Fuente de extraccion con las metricas del hero del proto (KPIs).</summary>
public sealed record ScrapeSourceDto(
    Guid Id,
    string Name,
    string Url,
    string? Selector,
    ScrapeSourceKind Kind,
    ScrapeSourceStatus Status,
    DateTimeOffset? LastRunAt,
    string? LastResultSummary,
    int RunCount,
    int SuccessCount30d,
    int RunCount30d,
    long TotalItems,
    DateTimeOffset CreatedAt);

/// <summary>Corrida del historial (dot verde/rojo del proto). ResultJson solo en la mas reciente pedida.</summary>
public sealed record ScrapeRunDto(
    Guid Id,
    Guid SourceId,
    ScrapeRunStatus Status,
    int ItemCount,
    int DurationMs,
    string? ErrorMessage,
    string? ResultJson,
    DateTimeOffset CreatedAt);

/// <summary>Alta/edicion de fuente (Id null = crear).</summary>
public sealed record SaveScrapeSourceRequest(
    Guid? Id,
    string Name,
    string Url,
    ScrapeSourceKind Kind,
    string? Selector,
    ScrapeSourceStatus Status = ScrapeSourceStatus.Active);
