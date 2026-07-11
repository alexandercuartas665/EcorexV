using Ecorex.Domain.Enums;

namespace Ecorex.Application.Entidades;

// ==== Configuracion de la entidad (000615): agencias/areas/sucursales del tenant ====
// Una cuenta (tenant = "Mi cuenta") administra VARIAS entidades, cada una con identidad legal,
// ubicacion, config y logo, mas campos dinamicos por tenant. Son la fuente del selector
// "Empresa/Area" al crear actividades. Tenant-scoped por el filtro global.

/// <summary>Fila del listado de entidades (tarjeta/lista).</summary>
public sealed record EntidadDto(
    Guid Id, string Codigo, string Nombre, string? NombreComercial, string? Ciudad,
    string? TipoEntidad, EntidadKind Kind, bool IsPrincipal, bool IsActive, bool IsArchived, bool HasLogo,
    DateTimeOffset UpdatedAt);

/// <summary>Detalle completo de una entidad, incluyendo los valores de sus campos dinamicos.</summary>
public sealed record EntidadDetailDto(
    Guid Id, string Codigo, EntidadKind Kind, string Nombre, string? NombreComercial, string? Sigla, string? TipoEntidad,
    string? TaxId, string? TaxIdDv, string? RepresentanteLegal, string? NaturalezaJuridica,
    string? Pais, string? Departamento, string? Ciudad, string? Direccion, string? Telefono,
    string? Email, string? Web, string? ZonaHoraria, string? Idioma, string? Observaciones,
    string? LogoBase64, bool IsPrincipal, bool IsActive, bool IsArchived,
    IReadOnlyDictionary<string, string?> FieldValues);

/// <summary>Alta/edicion de una entidad (Id null = nueva). FieldValues = dict FieldKey -&gt; valor.</summary>
public sealed record SaveEntidadRequest(
    Guid? Id, EntidadKind Kind, string Nombre, string? NombreComercial, string? Sigla, string? TipoEntidad,
    string? TaxId, string? TaxIdDv, string? RepresentanteLegal, string? NaturalezaJuridica,
    string? Pais, string? Departamento, string? Ciudad, string? Direccion, string? Telefono,
    string? Email, string? Web, string? ZonaHoraria, string? Idioma, string? Observaciones,
    string? LogoBase64, bool IsPrincipal, bool IsActive,
    IReadOnlyDictionary<string, string?>? FieldValues);

/// <summary>Definicion de un campo dinamico de la entidad (a nivel de tenant).</summary>
public sealed record EntidadFieldDefDto(
    Guid Id, string FieldKey, string Label, TerceroFieldType FieldType, string? Options,
    int Column, int SortOrder, string? Description, bool IsRequired, bool IsSystem);

public sealed record SaveEntidadFieldDefRequest(
    Guid? Id, string Label, TerceroFieldType FieldType, string? Options, int Column,
    bool IsRequired, string? Description);

/// <summary>Opcion para el selector "Empresa/Area" (usado por el modulo de Tareas).</summary>
public sealed record EntidadOptionDto(Guid Id, string Label);

/// <summary>
/// CRUD de la Configuracion de la entidad: entidades (agencias/areas/sucursales) con identidad,
/// ubicacion, config, logo y campos dinamicos; definiciones de campos dinamicos por tenant; y el
/// listado de opciones para el selector "Empresa/Area" de Tareas. Tenant-scoped por filtro global.
/// </summary>
public interface IEntidadService
{
    Task<IReadOnlyList<EntidadDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<EntidadDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<EntidadDetailDto?> SaveAsync(SaveEntidadRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> SetArchivedAsync(Guid id, bool archived, Guid actorUserId, CancellationToken ct = default);
    Task<bool> SetPrincipalAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    // Campos dinamicos (por tenant).
    Task<IReadOnlyList<EntidadFieldDefDto>> ListFieldDefsAsync(CancellationToken ct = default);
    Task<EntidadFieldDefDto?> SaveFieldDefAsync(SaveEntidadFieldDefRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteFieldDefAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    // Para el selector "Empresa/Area" del modulo de Tareas.
    Task<IReadOnlyList<EntidadOptionDto>> ListOptionsAsync(CancellationToken ct = default);

    // Siembra idempotente de una entidad demo para el tenant activo.
    Task EnsureDemoAsync(CancellationToken ct = default);
}
