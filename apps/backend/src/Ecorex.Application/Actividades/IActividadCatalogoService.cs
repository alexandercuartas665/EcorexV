using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Actividades;

/// <summary>
/// Catalogo de Conceptos de actividades del tenant activo (modulo 000270): jerarquia de dos
/// niveles Categoria -> Subcategoria (concepto). Aislamiento por tenant via filtro global
/// (nunca se filtra a mano por TenantId); el alta estampa el TenantId del contexto. La baja
/// es logica (IsArchived en categorias; eliminar borra la subcategoria y sus hijas en cascada).
/// Reusa <see cref="TaskCoreResult{T}"/> como resultado tipado del nucleo.
/// </summary>
public interface IActividadCatalogoService
{
    // ---- Categorias (nivel 1) ----

    Task<IReadOnlyList<ActividadCategoriaDto>> ListCategoriasAsync(
        bool includeArchived = true, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<ActividadCategoriaDto>> CreateCategoriaAsync(
        SaveCategoriaRequest request, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<ActividadCategoriaDto>> UpdateCategoriaAsync(
        Guid categoriaId, SaveCategoriaRequest request, CancellationToken cancellationToken = default);

    /// <summary>Archiva o restaura la categoria (soft). Invalid si ya esta en ese estado.</summary>
    Task<TaskCoreResult<ActividadCategoriaDto>> SetCategoriaArchivedAsync(
        Guid categoriaId, bool archived, CancellationToken cancellationToken = default);

    // ---- Subcategorias / conceptos (nivel 2) ----

    Task<IReadOnlyList<ActividadSubcategoriaDto>> ListSubcategoriasAsync(
        Guid? categoriaId = null, bool includeArchived = true, CancellationToken cancellationToken = default);

    Task<ActividadSubcategoriaDto?> GetSubcategoriaAsync(
        Guid subcategoriaId, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<ActividadSubcategoriaDto>> CreateSubcategoriaAsync(
        SaveSubcategoriaRequest request, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<ActividadSubcategoriaDto>> UpdateSubcategoriaAsync(
        Guid subcategoriaId, SaveSubcategoriaRequest request, CancellationToken cancellationToken = default);

    /// <summary>Elimina la subcategoria y sus relaciones hijas (cargos/terceros) en transaccion.</summary>
    Task<TaskCoreResult<bool>> DeleteSubcategoriaAsync(
        Guid subcategoriaId, CancellationToken cancellationToken = default);

    /// <summary>Archiva o restaura la subcategoria (soft). Invalid si ya esta en ese estado.</summary>
    Task<TaskCoreResult<ActividadSubcategoriaDto>> SetSubcategoriaArchivedAsync(
        Guid subcategoriaId, bool archived, CancellationToken cancellationToken = default);

    // ---- Combos + KPIs ----

    Task<ActividadComboOptionsDto> GetComboOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids de usuarios candidatos a "Encargado" de una tarea del concepto: derivados de los cargos
    /// del concepto (funcionarios que ocupan el cargo + miembros + responsable de la unidad).
    /// Devuelve vacio si el concepto no tiene cargos (el llamador cae a todos los usuarios).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListEncargadoUserIdsAsync(
        IReadOnlyList<Guid> cargoIds, CancellationToken cancellationToken = default);

    Task<ActividadKpisDto> GetKpisAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Siembra el catalogo demo (CAT-01..CAT-04 con 1-3 subcategorias) del tenant activo si aun
    /// no tiene ninguna categoria. Idempotente. Estampa el TenantId del contexto.
    /// </summary>
    Task EnsureConceptosDemoAsync(CancellationToken cancellationToken = default);
}
