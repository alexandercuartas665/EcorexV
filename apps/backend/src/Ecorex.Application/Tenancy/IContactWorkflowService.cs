namespace Ecorex.Application.Tenancy;

/// <summary>Estado del resultado de <see cref="IContactWorkflowService"/> (sin excepciones a la UI).</summary>
public enum ContactWorkflowStatus
{
    Ok = 0,
    /// <summary>El filtro (o el workflow) no existe, o pertenece a otro tenant (filtro global lo oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos o regla de negocio violada.</summary>
    Invalid
}

/// <summary>Resultado tipado del servicio del disenador de acciones (mismo patron que TerceroResult).</summary>
public sealed record ContactWorkflowResult<T>(ContactWorkflowStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == ContactWorkflowStatus.Ok;

    public static ContactWorkflowResult<T> Ok(T value) => new(ContactWorkflowStatus.Ok, value, null);
    public static ContactWorkflowResult<T> NotFound(string? error = null) => new(ContactWorkflowStatus.NotFound, default, error ?? "No encontrado.");
    public static ContactWorkflowResult<T> Invalid(string error) => new(ContactWorkflowStatus.Invalid, default, error);
}

/// <summary>
/// Disenador de acciones por filtro de contactos (ADR-0056, Fase 1). Persiste, atado 1:1 a un
/// <c>TerceroFiltro</c>, la LISTA de pasos + sus ventanas de horario. TENANT-SCOPED (aislamiento por
/// el filtro global del DbContext; nunca se filtra a mano por TenantId). El MOTOR DE EJECUCION que
/// dispara los pasos sobre el segmento del filtro es Fase 2 (aqui SOLO se guarda la configuracion).
/// </summary>
public interface IContactWorkflowService
{
    /// <summary>Devuelve el workflow del filtro (o null si aun no tiene uno).</summary>
    Task<ContactWorkflowDto?> GetByFiltroAsync(Guid filtroId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert del workflow del filtro: crea o actualiza la cabecera y REEMPLAZA por completo los
    /// pasos y ventanas (soft-nada; los pasos removidos se borran fisicamente porque son detalle
    /// del propio workflow, no agregados). Todo en una transaccion (un SaveChanges). Auditado.
    /// </summary>
    Task<ContactWorkflowResult<ContactWorkflowDto>> SaveAsync(
        Guid filtroId, SaveContactWorkflowRequest request, CancellationToken cancellationToken = default);
}
