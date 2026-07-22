namespace Ecorex.Application.Workflows;

/// <summary>
/// Arma el contexto que recibira un agente de IA para atender un paso (ola 1). NO ejecuta al
/// agente ni consume cupo: solo lee y estructura. La ola 2 tomara este DTO, lo serializara a
/// texto y lo mandara por IAiInferenceService.
///
/// Todo lo que lee pasa por el filtro global de tenant: un paso de otro tenant simplemente no
/// existe para este servicio (NotFound), que es como se cumple el aislamiento cross-tenant.
/// </summary>
public interface IWorkflowAgentContextBuilder
{
    /// <summary>
    /// Contexto del paso indicado. El paso debe ser VIGENTE (IsCurrent): armar contexto de un
    /// paso ya cerrado seria pagar tokens por una decision que ya se tomo.
    /// </summary>
    Task<WorkflowResult<WorkflowAgentContextDto>> BuildAsync(
        Guid stepId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Topes del contexto. PORQUE: cada elemento incluido son tokens facturados al tenant, que
/// tiene cupo por plan (AiUsageLog), y un prompt gigante ademas degrada la calidad de la
/// respuesta (lo relevante se diluye). Los valores buscan cubrir el caso real del vault
/// (flujo 00001: 8 pasos) con margen de sobra, sin que un caso patologico (loops con
/// CycleIndex alto, formularios de cientos de campos) reviente la ventana de contexto.
///
/// El presupuesto aproximado del peor caso es del orden de 10-12k tokens, que cabe con
/// holgura en cualquier modelo actual y deja espacio para el prompt de sistema del agente.
/// Cuando algo se recorta se marca el flag Truncated correspondiente: el agente debe SABER
/// que esta viendo una ventana, no inventar sobre lo que no ve.
/// </summary>
public static class WorkflowAgentContextLimits
{
    /// <summary>
    /// Pasos del historial (los MAS RECIENTES). 50 = el mismo tope de iteraciones que usa el
    /// motor para declarar Stuck: mas alla de eso el caso esta en loop y repetirlo no informa.
    /// </summary>
    public const int MaxHistorySteps = 50;

    /// <summary>
    /// Formularios ya enviados en pasos anteriores. 20 cubre flujos largos; los envios mas
    /// recientes son los que pesan en la decision del paso actual.
    /// </summary>
    public const int MaxPriorForms = 20;

    /// <summary>Campos por formulario (definicion del nodo actual y respuestas previas).</summary>
    public const int MaxFieldsPerForm = 60;

    /// <summary>
    /// Caracteres por valor capturado. Un campo de texto largo (una descripcion pegada por el
    /// usuario) puede traer miles de caracteres; 500 conserva el sentido y acota el gasto.
    /// </summary>
    public const int MaxValueChars = 500;

    /// <summary>Caracteres de textos libres largos (detalle de la tarea, comentarios de aprobacion).</summary>
    public const int MaxTextChars = 2000;
}
