namespace Ecorex.Application.Workflows;

/// <summary>
/// Por que el primer paso de un concepto-proceso no se pudo resolver (o se resolvio bien).
/// Lo consume el arranque de una actividad (wizard y form-first) para preseleccionar el encargado
/// que DICTA EL FLUJO, y para avisar cuando la configuracion no da para arrancar un proceso.
/// Ver el capitulo "Tareas de proceso - Arranque y encargado del flujo" (Ola A1) del vault.
/// </summary>
public enum FirstStepStatus
{
    /// <summary>Se resolvio el primer nodo Task, su cargo y al menos un candidato.</summary>
    Ok = 0,

    /// <summary>La subcategoria no existe o no tiene flujo vinculado: es una actividad simple.</summary>
    SinFlujo,

    /// <summary>Tiene flujo, pero esta en borrador o archivado: la actividad naceria SIN proceso (D3).</summary>
    FlujoNoPublicado,

    /// <summary>El flujo esta publicado pero no se alcanza ningun nodo Task desde el startEvent.</summary>
    SinNodoTask,

    /// <summary>El primer nodo Task existe pero no tiene cargo (WorkflowNodePolicy) asignado.</summary>
    SinCargo,

    /// <summary>El cargo del primer nodo no tiene ocupantes: el paso naceria huerfano.</summary>
    SinCandidatos,
}

/// <summary>Cargo (OrgUnit) asociado al nodo por su <c>WorkflowNodePolicy</c>.</summary>
public sealed record FirstStepCargoDto(Guid OrgUnitId, string Nombre);

/// <summary>
/// Resultado de resolver "quien atiende el primer paso" de un concepto-proceso, SIN ejecutar nada.
/// </summary>
/// <param name="Status">Ver <see cref="FirstStepStatus"/>. Solo <c>Ok</c> garantiza candidatos.</param>
/// <param name="WorkflowDefinitionId">Flujo del concepto, si lo tiene.</param>
/// <param name="NodeId">Primer nodo Task alcanzable desde el startEvent.</param>
/// <param name="NodeName">Nombre del nodo (para la etiqueta "Paso 1 - Cotizar").</param>
/// <param name="Cargos">Cargos del nodo, en su orden (<c>SortOrder</c>).</param>
/// <param name="CandidateUserIds">TenantUserIds que ocupan esos cargos. Son los UNICOS asignables (D2).</param>
public sealed record FirstStepDto(
    FirstStepStatus Status,
    Guid? WorkflowDefinitionId,
    Guid? NodeId,
    string? NodeName,
    IReadOnlyList<FirstStepCargoDto> Cargos,
    IReadOnlyList<Guid> CandidateUserIds)
{
    /// <summary>El concepto arranca un proceso (aunque su config este incompleta).</summary>
    public bool EsProceso => Status != FirstStepStatus.SinFlujo;

    /// <summary>Hay exactamente un candidato: el wizard lo preselecciona sin preguntar (Ola A2).</summary>
    public bool TieneCandidatoUnico => Status == FirstStepStatus.Ok && CandidateUserIds.Count == 1;

    /// <summary>Nombre del primer cargo, para la etiqueta del combo. Vacio si no hay.</summary>
    public string? CargoPrincipal => Cargos.Count > 0 ? Cargos[0].Nombre : null;
}

/// <summary>
/// Resuelve, para una subcategoria de concepto con flujo, QUIEN debe atender el primer paso,
/// caminando el grafo BPMN desde el startEvent SIN ejecutar el motor ni persistir nada.
/// </summary>
public interface IWorkflowStartService
{
    /// <summary>
    /// Camina el flujo publicado de la subcategoria desde el startEvent (auto-resolviendo gateways
    /// igual que el motor) hasta el primer nodo Task, y devuelve su cargo y sus candidatos.
    /// Nunca lanza por configuracion incompleta: lo reporta en <see cref="FirstStepDto.Status"/>.
    /// </summary>
    Task<FirstStepDto> ResolveFirstStepAsync(Guid subcategoriaId, CancellationToken cancellationToken = default);
}
