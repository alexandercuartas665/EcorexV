using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo COMPLETAR_PASO_FLUJO: cierra el PASO ACTUAL del flujo de la tarea a la que pertenece el formulario,
/// disparado por un BOTON del formulario. Sirve para dos casos segun el tipo del paso vigente:
///   - Paso Task (p. ej. "Validacion Factura"): lo completa, igual que cerrarlo desde el menu del nodo
///     ("Redireccionar"). Combinado con un nodo siguiente cuyo encargado sale de un campo del form
///     (WorkflowAssigneeSource.FormField, ver FlowEditor), el paso siguiente cae en el usuario elegido.
///   - Compuerta ATENDIDA (ExclusiveGateway, p. ej. "Autorizar Compra"): ELIGE la ruta hacia el nodo indicado
///     en targetNodeBpmnId ("Aprobar" -> Contabilizar Factura). Una compuerta solo queda como paso vigente
///     cuando espera decision humana, asi que resolverla ES "cerrar" ese paso.
///
/// Parametros (opcionales):
///   requireFields   : arreglo JSON de codigos de campo que DEBEN tener valor antes de cerrar (p. ej.
///                     ["text_ca893"] para exigir el "Aprobador del gasto"). Si falta alguno, NO cierra y avisa.
///                     Tambien acepta un string suelto o lista separada por comas.
///   requireMessage  : mensaje a mostrar si falta un campo requerido (si no, uno generico).
///   targetNodeBpmnId: BpmnElementId del nodo destino a seguir cuando el paso actual es una compuerta (p. ej.
///                     "Event_0kh7q6v"). OBLIGATORIO si el paso vigente es compuerta; si se pasa con un paso
///                     Task se rechaza (es un boton mal dirigido: evita cerrar el paso equivocado).
///
/// Resuelve la tarea por la referencia del formulario (numero de tarea), toma su instancia Running y su UNICO
/// paso actual Pending (Task o compuerta) y lo cierra/resuelve con IWorkflowEngine (CompleteStepAsync para un
/// Task, ChooseGatewayRouteAsync para una compuerta) como el usuario que pulso el boton. Si hay 0 o mas de 1
/// paso actual, no adivina: pide cerrarlo desde el flujo. No tiene estado propio.
/// </summary>
public sealed class CompletarPasoFlujoVerb : IRuleVerb
{
    public const string VerbName = "COMPLETAR_PASO_FLUJO";

    public string Name => VerbName;

    private readonly IFormResponseService _forms;
    private readonly IApplicationDbContext _db;
    private readonly IWorkflowEngine _engine;

    public CompletarPasoFlujoVerb(IFormResponseService forms, IApplicationDbContext db, IWorkflowEngine engine)
    {
        _forms = forms; _db = db; _engine = engine;
    }

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Completar paso del flujo",
        "Cierra el paso actual del flujo de la tarea desde un boton del formulario (como cerrarlo en el menu del "
        + "nodo). Opcional: exigir que ciertos campos tengan valor antes de cerrar (p. ej. el aprobador).",
        new[]
        {
            new RuleVerbParamDescriptor("requireFields", "Campos obligatorios antes de cerrar",
                RuleParamType.Json, Required: false,
                "Arreglo JSON de codigos de campo que deben tener valor, p. ej. [\"text_ca893\"]. Si falta alguno, no cierra el paso."),
            new RuleVerbParamDescriptor("requireMessage", "Mensaje si falta un campo",
                RuleParamType.Text, Required: false,
                "Texto a mostrar cuando falta un campo obligatorio (si se deja vacio, se usa uno generico)."),
            new RuleVerbParamDescriptor("targetNodeBpmnId", "Ruta destino (si el paso es compuerta)",
                RuleParamType.Text, Required: false,
                "BpmnElementId del nodo destino a seguir cuando el paso actual es una compuerta atendida, p. ej. "
                + "Event_0kh7q6v (Contabilizar Factura). Obligatorio si el paso vigente es una compuerta."),
        });

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        if (context.FormResponseId is not Guid respId)
        {
            return RuleVerbResult.Fail("Este boton corre desde el formulario de una tarea (sin formulario no hay paso que cerrar).");
        }

        // (1) Validar campos obligatorios ANTES de cerrar (lee los valores ACTUALES del formulario).
        var required = ReadRequireFields(context);
        var missing = required
            .Where(code => !context.FormData.TryGetValue(code, out var v) || string.IsNullOrWhiteSpace(v))
            .ToList();
        if (missing.Count > 0)
        {
            var msg = context.GetStringParam("requireMessage");
            return RuleVerbResult.Fail(string.IsNullOrWhiteSpace(msg)
                ? "Completa los campos obligatorios antes de cerrar el paso."
                : msg!);
        }

        // (2) Resolver la tarea por la referencia del formulario (numero de tarea).
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
            return RuleVerbResult.Fail($"No se encontro la tarea {reference} para cerrar su paso.");
        }

        // (3) Instancia Running de la tarea y su paso Task actual (unico) Pending.
        var instanceId = context.WorkflowInstanceId ?? await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.TaskItemId == tid && i.Status == WorkflowInstanceStatus.Running)
            .OrderByDescending(i => i.StartedAt)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (instanceId is not Guid iid)
        {
            return RuleVerbResult.Fail("La tarea no tiene un flujo activo.");
        }

        var steps = await _engine.GetCurrentStepsAsync(iid, cancellationToken);
        var candidates = steps
            .Where(s => s.Status == WorkflowStepStatus.Pending
                && (s.NodeType == WorkflowNodeType.Task || s.NodeType == WorkflowNodeType.ExclusiveGateway))
            .ToList();
        if (candidates.Count == 0)
        {
            return RuleVerbResult.Fail("No hay un paso por cerrar en el flujo (puede que ya haya avanzado).");
        }
        if (candidates.Count > 1)
        {
            return RuleVerbResult.Fail("Hay varios pasos actuales; cierra el paso desde el flujo de la tarea.");
        }

        var step = candidates[0];
        var targetBpmn = context.GetStringParam("targetNodeBpmnId");

        WorkflowResult<WorkflowInstanceDto> result;
        if (step.NodeType == WorkflowNodeType.ExclusiveGateway)
        {
            // (4a) Compuerta ATENDIDA: hay que ELEGIR la ruta por nodo destino (ADR-0072). El destino sale del
            // parametro (self-serve); ChooseGatewayRouteAsync revalida que sea una salida directa de la compuerta.
            if (string.IsNullOrWhiteSpace(targetBpmn))
            {
                return RuleVerbResult.Fail("El paso actual es una compuerta: configura la ruta destino (targetNodeBpmnId) para resolverla.");
            }
            var defId = await _db.WorkflowInstances.AsNoTracking()
                .Where(i => i.Id == iid).Select(i => i.DefinitionId)
                .FirstOrDefaultAsync(cancellationToken);
            var targetId = await _db.WorkflowNodes.AsNoTracking()
                .Where(n => n.DefinitionId == defId && n.BpmnElementId == targetBpmn!.Trim())
                .Select(n => (Guid?)n.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (targetId is not Guid tgt)
            {
                return RuleVerbResult.Fail($"No se encontro el nodo destino '{targetBpmn}' en el flujo.");
            }
            result = await _engine.ChooseGatewayRouteAsync(
                iid, step.Id, tgt, context.ExecutedByTenantUserId, cancellationToken: cancellationToken);
        }
        else
        {
            // (4b) Paso Task: la ruta destino solo aplica a compuertas; si la pasaron aqui, el boton apunta mal.
            if (!string.IsNullOrWhiteSpace(targetBpmn))
            {
                return RuleVerbResult.Fail("La ruta destino solo aplica a una compuerta; el paso actual no es una compuerta.");
            }
            result = await _engine.CompleteStepAsync(
                iid, step.Id, context.ExecutedByTenantUserId, cancellationToken: cancellationToken);
        }

        if (!result.IsOk)
        {
            return RuleVerbResult.Fail(result.Error ?? "No se pudo cerrar el paso del flujo.");
        }

        return RuleVerbResult.Ok(
            "Paso cerrado; el flujo avanzo.", recordsAffected: 1,
            actions: new[] { RuleAction.StepCompleted() });
    }

    /// <summary>Lee el parametro requireFields: arreglo JSON de strings, o un string suelto / lista por comas.</summary>
    private static IReadOnlyList<string> ReadRequireFields(RuleContext context)
    {
        if (!context.Params.TryGetValue("requireFields", out var el))
        {
            return Array.Empty<string>();
        }
        var codes = new List<string>();
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) { codes.Add(s!.Trim()); }
                }
            }
        }
        else if (el.ValueKind == JsonValueKind.String)
        {
            var raw = el.GetString() ?? string.Empty;
            codes.AddRange(raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return codes;
    }
}
