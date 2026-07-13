using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms;

/// <summary>
/// CRUD de definiciones de formulario dinamico con su arbol de contenedores y preguntas
/// (FASE 4, ola 2: port del constructor EAV legacy, ADR-0015). Valida FieldCode unico por
/// definicion, opciones obligatorias en Select/MultiCheck/Radio y pattern compilable.
/// Los cambios estructurales sobre una definicion Active incrementan Revision (snapshot
/// logico de version de negocio). Todo tenant-scoped por el filtro global.
/// </summary>
public interface IFormDefinitionService
{
    Task<IReadOnlyList<FormDefinitionListItemDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>Definicion completa con contenedores y preguntas ordenados. Null si no existe.</summary>
    Task<FormDefinitionDetailDto?> GetAsync(Guid definitionId, CancellationToken cancellationToken = default);

    Task<FormResult<FormDefinitionDetailDto>> CreateAsync(CreateFormDefinitionRequest request, CancellationToken cancellationToken = default);

    Task<FormResult<FormDefinitionDetailDto>> UpdateHeaderAsync(Guid definitionId, UpdateFormDefinitionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Configura la transaccionalidad del formulario (ola F3): IsTransactional + modo de identidad + campo clave.</summary>
    Task<FormResult<FormDefinitionDetailDto>> SetTransactionalAsync(Guid definitionId, SetFormTransactionalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Promueve/retira el formulario como modulo (ola F4): crea/borra el nodo de menu en el grupo elegido.</summary>
    Task<FormResult<FormDefinitionDetailDto>> SetModuleAsync(Guid definitionId, SetFormModuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Draft/Inactive -> Active, validando la estructura (preguntas, opciones, patterns).</summary>
    Task<FormResult<FormDefinitionDetailDto>> ActivateAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>Active -> Inactive: deja de aceptar respuestas nuevas.</summary>
    Task<FormResult<FormDefinitionDetailDto>> DeactivateAsync(Guid definitionId, CancellationToken cancellationToken = default);

    Task<FormResult<bool>> SetArchivedAsync(Guid definitionId, bool archived, CancellationToken cancellationToken = default);

    // ---- Contenedores ----

    Task<FormResult<FormContainerDto>> AddContainerAsync(Guid definitionId, SaveFormContainerRequest request, CancellationToken cancellationToken = default);

    Task<FormResult<FormContainerDto>> UpdateContainerAsync(Guid containerId, SaveFormContainerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Borra el contenedor; sus preguntas y sub-contenedores pasan al padre (o a la raiz).</summary>
    Task<FormResult<bool>> DeleteContainerAsync(Guid containerId, CancellationToken cancellationToken = default);

    /// <summary>Reordena el contenedor entre sus hermanos (paso a paso).</summary>
    Task<FormResult<bool>> MoveContainerAsync(Guid containerId, bool moveUp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve el contenedor a otro padre (o a la raiz con null) en la posicion index,
    /// renumerando ambos grupos de hermanos (drag and drop del constructor, ADR-0021).
    /// </summary>
    Task<FormResult<bool>> MoveContainerToAsync(Guid containerId, Guid? parentId, int index, CancellationToken cancellationToken = default);

    // ---- Preguntas ----

    Task<FormResult<FormQuestionDto>> AddQuestionAsync(Guid definitionId, SaveFormQuestionRequest request, CancellationToken cancellationToken = default);

    Task<FormResult<FormQuestionDto>> UpdateQuestionAsync(Guid questionId, SaveFormQuestionRequest request, CancellationToken cancellationToken = default);

    Task<FormResult<bool>> DeleteQuestionAsync(Guid questionId, CancellationToken cancellationToken = default);

    /// <summary>Reordena la pregunta dentro de su contenedor (paso a paso).</summary>
    Task<FormResult<bool>> MoveQuestionAsync(Guid questionId, bool moveUp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve la pregunta a otro contenedor (o a la raiz con null) en la posicion index,
    /// renumerando ambos grupos de hermanos (drag and drop del constructor, ADR-0021).
    /// </summary>
    Task<FormResult<bool>> MoveQuestionToAsync(Guid questionId, Guid? containerId, int index, CancellationToken cancellationToken = default);

    // ---- Vinculo nodo de flujo -> formulario (WorkflowNodeForm) ----

    /// <summary>Asigna (o quita, con null) el formulario exigido por un nodo de flujo.</summary>
    Task<FormResult<bool>> AssignToWorkflowNodeAsync(Guid workflowNodeId, Guid? definitionId, CancellationToken cancellationToken = default);

    /// <summary>Id de la definicion de formulario asignada al nodo, si la hay.</summary>
    Task<Guid?> GetWorkflowNodeFormAsync(Guid workflowNodeId, CancellationToken cancellationToken = default);
}
