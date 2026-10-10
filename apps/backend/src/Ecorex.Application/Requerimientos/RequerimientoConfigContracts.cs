using Ecorex.Domain.Enums;

namespace Ecorex.Application.Requerimientos;

/// <summary>Opcion generica para los selectores de la config (subcategorias, formularios...).</summary>
public sealed record RequerimientoOpcionDto(Guid Id, string Nombre);

/// <summary>Un origen de requerimientos configurado (con nombres resueltos para mostrar).</summary>
public sealed record RequerimientoOrigenDto(
    Guid Id,
    string Nombre,
    string Area,
    RequerimientoFuenteTipo TipoFuente,
    Guid? FuenteRefId,
    string? FuenteNombre,
    Guid? SubcategoriaCompraId,
    string? SubcategoriaNombre,
    bool Activo);

/// <summary>Alta/edicion de un origen. Id null = alta.</summary>
public sealed record SaveRequerimientoOrigenRequest(
    Guid? Id,
    string Nombre,
    string Area,
    RequerimientoFuenteTipo TipoFuente,
    Guid? FuenteRefId,
    Guid? SubcategoriaCompraId,
    bool Activo);

/// <summary>
/// Configuracion del modulo de Requerimientos (Capa 10, Ola 1): CRUD de ORIGENES (de donde viene cada
/// necesidad y que subcategoria/proceso del modulo de Actividades EJECUTA la compra de ese genero). La
/// matriz de proveedores (Ola 4) se agrega aparte. Tenant-scoped por el filtro global.
/// </summary>
public interface IRequerimientoConfigService
{
    Task<IReadOnlyList<RequerimientoOrigenDto>> ListOrigenesAsync(CancellationToken ct = default);
    Task<RequerimientoOrigenDto?> SaveOrigenAsync(SaveRequerimientoOrigenRequest req, CancellationToken ct = default);
    Task<bool> DeleteOrigenAsync(Guid id, CancellationToken ct = default);

    /// <summary>Subcategorias (conceptos 000270) para elegir el proceso de compra del origen.</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListSubcategoriasAsync(CancellationToken ct = default);
    /// <summary>Formularios publicados/activos para elegir la fuente (TipoFuente=Formulario).</summary>
    Task<IReadOnlyList<RequerimientoOpcionDto>> ListFormulariosAsync(CancellationToken ct = default);
}
