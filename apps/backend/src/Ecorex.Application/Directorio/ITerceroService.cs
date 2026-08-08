namespace Ecorex.Application.Directorio;

/// <summary>
/// Gestion del Directorio General (modulo 000232): terceros (empresas / personas) con perfiles
/// de negocio, contactos embebidos y fichas dinamicas por perfil. Tenant-scoped (filtro global +
/// estampado en alta). El aislamiento cross-tenant lo garantiza el filtro global por reflexion;
/// aqui NUNCA se filtra a mano por TenantId.
/// </summary>
public interface ITerceroService
{
    /// <summary>
    /// Lista empresas + personas individuales (oculta las personas asignadas a una empresa, que
    /// cuentan como contactos). Aplica los tabs (tipo/naturaleza), la busqueda y el estado.
    /// </summary>
    Task<IReadOnlyList<TerceroListItemDto>> ListAsync(
        TerceroListFilter filter, CancellationToken cancellationToken = default);

    Task<TerceroDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TerceroResult<TerceroDetailDto>> CreateAsync(
        SaveTerceroRequest request, CancellationToken cancellationToken = default);

    Task<TerceroResult<TerceroDetailDto>> UpdateAsync(
        Guid id, SaveTerceroRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alta CONJUNTA de una empresa y su contacto (persona), vinculados. Los dos se insertan en un
    /// solo SaveChanges, asi que o quedan ambos con su relacion o no queda ninguno. Devuelve el
    /// detalle de la EMPRESA (registro cabecera).
    /// </summary>
    Task<TerceroResult<TerceroDetailDto>> CreateEmpresaConContactoAsync(
        SaveTerceroRequest empresa, SaveTerceroRequest contacto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca un tercero por el "criterio de busqueda" del formulario Publico: identificador (IDE),
    /// telefono o correo, en ese orden de prioridad. Devuelve el Id del primer registro que coincide
    /// (null si ninguno) para que el editor lo cargue. Prefiere la PERSONA sobre la empresa cuando
    /// el mismo dato aparece en ambas, porque el telefono/correo son del contacto.
    /// </summary>
    Task<Guid?> FindIdByCriterioAsync(string criterio, CancellationToken cancellationToken = default);

    /// <summary>Baja logica (soft-delete): pone el tercero en estado Inactivo.</summary>
    Task<TerceroResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Asigna una persona individual como contacto (persona) de una empresa.</summary>
    Task<TerceroResult<bool>> AssignToEmpresaAsync(
        Guid personaId, Guid empresaId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la persona a la lista principal como cliente individual (EmpresaId = null).</summary>
    Task<TerceroResult<bool>> UnassignFromEmpresaAsync(
        Guid personaId, CancellationToken cancellationToken = default);

    // ---- Contactos embebidos ----

    Task<IReadOnlyList<TerceroContactoDto>> ListContactosAsync(
        Guid terceroId, CancellationToken cancellationToken = default);

    Task<TerceroResult<TerceroContactoDto>> AddContactoAsync(
        Guid terceroId, SaveContactoRequest request, CancellationToken cancellationToken = default);

    Task<TerceroResult<TerceroContactoDto>> UpdateContactoAsync(
        Guid contactoId, SaveContactoRequest request, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> DeleteContactoAsync(
        Guid contactoId, CancellationToken cancellationToken = default);

    // ---- Notas / gestiones "Contacto cliente" (timeline) ----

    Task<IReadOnlyList<TerceroNotaDto>> ListNotasAsync(
        Guid terceroId, CancellationToken cancellationToken = default);

    Task<TerceroResult<TerceroNotaDto>> AddNotaAsync(
        Guid terceroId, SaveNotaRequest request, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> DeleteNotaAsync(
        Guid notaId, CancellationToken cancellationToken = default);

    Task<TerceroKpisDto> GetKpisAsync(CancellationToken cancellationToken = default);
}
