using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo REINICIAR_FLUJO_TAREA: REINICIA el flujo de la tarea a la que pertenece el formulario y lo reasigna al
/// usuario elegido en un campo del formulario (p. ej. "Redireccionamiento" en "Validacion Factura" del
/// conciliador DIAN). Caso: quien revisa detecta que el ciclo arranco mal, elige el usuario correcto y pulsa
/// "Rechazar"; el flujo vuelve al inicio (ciclo nuevo) con el paso humano y la tarea asignados a ese usuario, y
/// queda constancia en la bitacora de la tarea.
///
/// Parametros:
///   assigneeFieldCode : (obligatorio) codigo del campo-usuario del formulario con el nuevo responsable (id o
///                       correo), p. ej. "fecha_compra" (el campo "Redireccionamiento").
///   restartNodeBpmnId : (opcional) BpmnElementId del nodo donde reiniciar; vacio = el inicio del flujo.
///   requireMessage    : (opcional) mensaje si el campo del usuario esta vacio (si no, uno generico).
///   logMessage        : (opcional) nota adicional que se agrega a la bitacora del reinicio.
///
/// Resuelve la tarea por la referencia del formulario (numero de tarea), toma su instancia Running y delega en
/// IWorkflowEngine.RestartInstanceAsync como el usuario que pulso el boton. No tiene estado propio.
/// </summary>
public sealed class ReiniciarFlujoTareaVerb : IRuleVerb
{
    public const string VerbName = "REINICIAR_FLUJO_TAREA";

    public string Name => VerbName;

    private readonly IFormResponseService _forms;
    private readonly IApplicationDbContext _db;
    private readonly IWorkflowEngine _engine;

    public ReiniciarFlujoTareaVerb(IFormResponseService forms, IApplicationDbContext db, IWorkflowEngine engine)
    {
        _forms = forms; _db = db; _engine = engine;
    }

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Reiniciar el flujo de la tarea",
        "Reinicia el flujo de la tarea desde un boton del formulario y lo reasigna al usuario elegido en un "
        + "campo (p. ej. 'Redireccionamiento'). Util cuando el ciclo arranco mal y hay que volver a empezar con "
        + "el responsable correcto; deja constancia en la bitacora.",
        new[]
        {
            new RuleVerbParamDescriptor("assigneeFieldCode", "Campo del usuario de redireccion",
                RuleParamType.Text, Required: true,
                "Codigo del campo-usuario del formulario con el nuevo responsable, p. ej. fecha_compra."),
            new RuleVerbParamDescriptor("restartNodeBpmnId", "Nodo donde reiniciar (opcional)",
                RuleParamType.Text, Required: false,
                "BpmnElementId del nodo donde reiniciar el flujo. Vacio = el inicio del flujo."),
            new RuleVerbParamDescriptor("requireMessage", "Mensaje si falta el usuario",
                RuleParamType.Text, Required: false,
                "Texto a mostrar cuando el campo del usuario de redireccion esta vacio (si no, uno generico)."),
            new RuleVerbParamDescriptor("logMessage", "Nota del reinicio (opcional)",
                RuleParamType.Text, Required: false,
                "Nota adicional que se agrega a la bitacora de la tarea al reiniciar."),
        });

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        if (context.FormResponseId is not Guid respId)
        {
            return RuleVerbResult.Fail("Este boton corre desde el formulario de una tarea (sin formulario no hay flujo que reiniciar).");
        }

        var fieldCode = context.GetStringParam("assigneeFieldCode");
        if (string.IsNullOrWhiteSpace(fieldCode))
        {
            return RuleVerbResult.Fail("Falta configurar el codigo del campo del usuario de redireccion (assigneeFieldCode).");
        }

        // Usuario de redireccion desde los valores ACTUALES del formulario.
        context.FormData.TryGetValue(fieldCode.Trim(), out var assigneeValue);
        if (string.IsNullOrWhiteSpace(assigneeValue))
        {
            var msg = context.GetStringParam("requireMessage");
            return RuleVerbResult.Fail(string.IsNullOrWhiteSpace(msg)
                ? "Elige el usuario de redireccionamiento antes de reiniciar."
                : msg!);
        }

        // Tarea por la referencia del formulario (numero de tarea).
        var resp = await _forms.GetAsync(respId, cancellationToken);
        var reference = resp?.Reference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return RuleVerbResult.Fail("El formulario no esta ligado a una tarea (sin referencia).");
        }

        var taskId = context.TaskItemId ?? await _db.TaskItems.AsNoTracking()
            .Where(t => t.Number == reference)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (taskId is not Guid tid)
        {
            return RuleVerbResult.Fail($"No se encontro la tarea {reference} para reiniciar su flujo.");
        }

        // Instancia Running de la tarea.
        var instanceId = context.WorkflowInstanceId ?? await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.TaskItemId == tid && i.Status == WorkflowInstanceStatus.Running)
            .OrderByDescending(i => i.StartedAt)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (instanceId is not Guid iid)
        {
            return RuleVerbResult.Fail("La tarea no tiene un flujo activo.");
        }

        var restartNode = context.GetStringParam("restartNodeBpmnId");
        var note = context.GetStringParam("logMessage");

        var result = await _engine.RestartInstanceAsync(
            iid, restartNode, assigneeValue!.Trim(), context.ExecutedByTenantUserId, note, cancellationToken);
        if (!result.IsOk)
        {
            return RuleVerbResult.Fail(result.Error ?? "No se pudo reiniciar el flujo.");
        }

        // Misma senal que COMPLETAR_PASO_FLUJO: el host refresca el flujo/detalle sin cerrar el formulario.
        return RuleVerbResult.Ok(
            "Flujo reiniciado y reasignado.", recordsAffected: 1,
            actions: new[] { RuleAction.StepCompleted() });
    }
}
