using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo DEVOLVER_CONCILIADOR: el paso "hermano" de REINICIAR_FLUJO_TAREA en el boton "Rechazar" del formulario
/// de autorizacion de compras DIAN. Cuando el usuario asignado NO sabe a quien redirigir y deja VACIO el campo de
/// redireccionamiento, en vez de bloquear, DEVUELVE el documento al conciliador: con confirmacion del usuario,
/// cancela la tarea y su flujo, desasigna el renglon del conciliador (vuelve al pool) y lo marca RECHAZADO,
/// dejando el evento en el historial de asignaciones.
///
/// Patron de dos verbos en el mismo boton (en SortOrder):
///   [0] REINICIAR_FLUJO_TAREA { assigneeFieldCode, onEmptyField:"skip" }  -> si el campo TIENE usuario, reinicia.
///   [1] DEVOLVER_CONCILIADOR  { emptyFieldCode, confirmMessage }          -> si el campo esta VACIO, devuelve.
///
/// Parametros:
///   emptyFieldCode : (obligatorio) codigo del campo de redireccionamiento que debe estar VACIO para devolver
///                    (el MISMO que assigneeFieldCode de REINICIAR, p. ej. "fecha_compra").
///   confirmMessage : (opcional) texto del modal de confirmacion antes de devolver.
///   logMessage     : (opcional) nota adicional para el historial/bitacora.
///
/// Mecanismo de confirmacion: si el campo esta vacio y el usuario aun NO confirmo
/// (<see cref="RuleContext.Confirmed"/> = false), el verbo devuelve <see cref="RuleAction.Confirm"/> SIN efectos;
/// el HOST (DynamicFormRenderer) muestra el modal y, si acepta, RE-EJECUTA las reglas del boton con Confirmed=true.
/// </summary>
public sealed class DevolverConciliadorVerb : IRuleVerb
{
    public const string VerbName = "DEVOLVER_CONCILIADOR";

    public string Name => VerbName;

    private readonly IFormResponseService _forms;
    private readonly IAutorizacionComprasConfigService _cfg;
    private readonly IConciliacionAsignacionHistorialService _hist;
    private readonly IApplicationDbContext _db;
    private readonly IWorkflowDesignService _design;
    private readonly ITaskItemService _tasks;

    public DevolverConciliadorVerb(IFormResponseService forms, IAutorizacionComprasConfigService cfg,
        IConciliacionAsignacionHistorialService hist, IApplicationDbContext db,
        IWorkflowDesignService design, ITaskItemService tasks)
    {
        _forms = forms; _cfg = cfg; _hist = hist; _db = db; _design = design; _tasks = tasks;
    }

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Devolver al conciliador (rechazar)",
        "Cuando el campo de redireccionamiento esta VACIO, pide confirmacion y DEVUELVE el documento al "
        + "conciliador: cancela la tarea y su flujo, desasigna el renglon (vuelve al pool) y lo marca rechazado. "
        + "Se usa junto a REINICIAR_FLUJO_TAREA (onEmptyField=skip) en el boton 'Rechazar'.",
        new[]
        {
            new RuleVerbParamDescriptor("emptyFieldCode", "Campo de redireccion (debe estar vacio)",
                RuleParamType.Text, Required: true,
                "Codigo del campo de redireccionamiento; solo devuelve si esta vacio (el mismo de REINICIAR, "
                + "p. ej. fecha_compra)."),
            new RuleVerbParamDescriptor("confirmMessage", "Mensaje de confirmacion (opcional)",
                RuleParamType.Text, Required: false,
                "Texto del modal de confirmacion antes de devolver el documento al conciliador."),
            new RuleVerbParamDescriptor("logMessage", "Nota del rechazo (opcional)",
                RuleParamType.Text, Required: false,
                "Nota adicional para el historial de asignaciones / bitacora."),
        });

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        if (context.FormResponseId is not Guid respId)
        {
            return RuleVerbResult.Fail("Este boton corre desde el formulario de una tarea.");
        }

        var fieldCode = context.GetStringParam("emptyFieldCode");
        if (string.IsNullOrWhiteSpace(fieldCode))
        {
            return RuleVerbResult.Fail("Falta configurar el codigo del campo de redireccion (emptyFieldCode).");
        }

        // Solo actua si el campo de redireccion esta VACIO; con valor, lo maneja REINICIAR_FLUJO_TAREA.
        context.FormData.TryGetValue(fieldCode.Trim(), out var redir);
        if (!string.IsNullOrWhiteSpace(redir))
        {
            return RuleVerbResult.Ok("Hay usuario de redireccion: no se devuelve.", recordsAffected: 0);
        }

        // Campo vacio: pedir confirmacion antes de la accion destructiva (una sola vez).
        if (!context.Confirmed)
        {
            var msg = context.GetStringParam("confirmMessage");
            return RuleVerbResult.Ok(null, recordsAffected: 0, actions: new[]
            {
                RuleAction.Confirm(string.IsNullOrWhiteSpace(msg)
                    ? "Deseas regresar el documento al conciliador? Se quitara la asignacion y la tarea se cancelara."
                    : msg!),
            });
        }

        // ---- Confirmado: ejecutar la devolucion ----
        var resp = await _forms.GetAsync(respId, cancellationToken);
        var reference = resp?.Reference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return RuleVerbResult.Fail("El formulario no esta ligado a una tarea (sin referencia).");
        }

        var renglonId = await _cfg.ResolveRenglonByReferenceAsync(reference!, cancellationToken);
        if (renglonId is not Guid rid)
        {
            return RuleVerbResult.Fail($"No se encontro la factura del conciliador para la referencia {reference}.");
        }

        var taskNumber = await _cfg.GetSolicitadaAsync(rid, cancellationToken);

        // 1) Quien tenia el documento (para el historial): columna de entidad o, si no, el mapa JSON.
        var renglon = await _db.ConciliacionDianRenglones.FirstOrDefaultAsync(x => x.Id == rid, cancellationToken);
        var prevAssignee = renglon?.AssignedTenantUserId ?? await _cfg.GetAsignadoAsync(rid, cancellationToken);

        // 2) Desasignar la columna de entidad + marcar RECHAZADO; registrar el evento en el historial.
        if (renglon is not null)
        {
            renglon.AssignedTenantUserId = null;
            renglon.Rechazado = true;
        }
        await _hist.RegistrarAsync(rid, prevAssignee ?? context.ExecutedByTenantUserId, taskNumber,
            ConciliacionAsignacionAccion.Rechazado, cancellationToken);

        // 3) Desasignar el mapa JSON y quitar el vinculo renglon->tarea (re-direccionar creara una tarea nueva).
        //    SetAsignadoAsync guarda el contexto compartido, persistiendo tambien (2).
        await _cfg.SetAsignadoAsync(rid, null, cancellationToken);
        await _cfg.ClearSolicitadaAsync(rid, cancellationToken);

        // 4) Cancelar el flujo y la tarea de autorizacion (si se resuelven desde el numero de tarea).
        Guid? taskId = context.TaskItemId;
        if (taskId is null && !string.IsNullOrWhiteSpace(taskNumber))
        {
            taskId = await _cfg.ResolveTaskIdByNumberAsync(taskNumber!, cancellationToken);
        }
        if (taskId is Guid tid)
        {
            var instanceId = context.WorkflowInstanceId ?? await _db.WorkflowInstances.AsNoTracking()
                .Where(i => i.TaskItemId == tid && i.Status == WorkflowInstanceStatus.Running)
                .OrderByDescending(i => i.StartedAt)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (instanceId is Guid iid)
            {
                await _design.CancelInstanceAsync(iid, cancellationToken);
            }
            await _tasks.ArchiveAsync(tid, context.ActorUserId ?? Guid.Empty, context.ActorName, cancellationToken);
        }

        // Misma senal que COMPLETAR_PASO_FLUJO: el host refresca el flujo/detalle de la tarea.
        return RuleVerbResult.Ok("Documento devuelto al conciliador.", recordsAffected: 1,
            actions: new[] { RuleAction.StepCompleted() });
    }
}
