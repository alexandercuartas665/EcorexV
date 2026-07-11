namespace Ecorex.Application.Organization;

/// <summary>
/// Organigrama del tenant (modulo Dependencias, legacy 000850, ADR-0017): CRUD de unidades
/// en arbol (con validacion de ciclos: una unidad no puede ser su propio ancestro),
/// miembros por unidad y KPIs de cabecera. Las unidades nunca se borran fisicamente
/// (se archivan). Todo tenant-scoped por el filtro global.
/// </summary>
public interface IOrgUnitService
{
    // ---- Arbol / consulta ----

    /// <summary>Arbol completo del organigrama (raices con hijos anidados, ordenados por SortOrder y nombre).</summary>
    Task<IReadOnlyList<OrgUnitNodeDto>> GetTreeAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>Lista plana (para combos de padre / administracion).</summary>
    Task<IReadOnlyList<OrgUnitDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<OrgUnitDto?> GetAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>KPIs de cabecera: total dependencias activas, usuarios asignados (miembros + responsables distintos) y areas.</summary>
    Task<OrgKpisDto> GetKpisAsync(CancellationToken cancellationToken = default);

    // ---- CRUD ----

    Task<OrgResult<OrgUnitDto>> CreateAsync(SaveOrgUnitRequest request, CancellationToken cancellationToken = default);

    /// <summary>Actualiza la unidad. Invalid si el nuevo padre crea un ciclo en el arbol.</summary>
    Task<OrgResult<OrgUnitDto>> UpdateAsync(Guid unitId, SaveOrgUnitRequest request, CancellationToken cancellationToken = default);

    /// <summary>Archiva/restaura la unidad (soft-delete; no hay DELETE fisico de unidades).</summary>
    Task<OrgResult<bool>> SetArchivedAsync(Guid unitId, bool archived, CancellationToken cancellationToken = default);

    // ---- Miembros ----

    Task<IReadOnlyList<OrgUnitMemberDto>> ListMembersAsync(Guid unitId, CancellationToken cancellationToken = default);
    Task<OrgResult<OrgUnitMemberDto>> AddMemberAsync(Guid unitId, Guid tenantUserId, string? role = null, CancellationToken cancellationToken = default);
    Task<OrgResult<bool>> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca/desmarca a un miembro como jefe/responsable de su unidad (a lo sumo uno por unidad).
    /// Al marcar, sincroniza <c>OrgUnit.ResponsibleTenantUserId</c> con el usuario del miembro; al
    /// desmarcar, lo limpia si apuntaba a ese usuario. Operacion multi-tabla en una transaccion.
    /// </summary>
    Task<OrgResult<bool>> SetMemberResponsibleAsync(Guid memberId, bool isResponsible, CancellationToken cancellationToken = default);
}
