using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo ASIGNAR_CONSECUTIVO: emite el siguiente valor de un consecutivo por tenant via
/// ISequenceService (CAS atomico, ADR-0013) y lo pone en un campo del formulario
/// (SetFieldValue). Si la ejecucion viene de un nodo de flujo con tarea asociada, deja
/// constancia en la actividad de la tarea. Con autoComplete=true la regla autonoma pide
/// completar el paso (AutoCompleteStep).
/// </summary>
public sealed class AsignarConsecutivoVerb : IRuleVerb
{
    public const string VerbName = "ASIGNAR_CONSECUTIVO";

    private readonly ISequenceService _sequences;
    private readonly IApplicationDbContext _db;

    public AsignarConsecutivoVerb(ISequenceService sequences, IApplicationDbContext db)
    {
        _sequences = sequences;
        _db = db;
    }

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Asignar consecutivo",
        "Emite el siguiente numero de un consecutivo por tenant y lo asigna a un campo del formulario.",
        [
            new RuleVerbParamDescriptor("sequenceCode", "Codigo del consecutivo", RuleParamType.Text, Required: true,
                "Codigo de la secuencia por tenant (max 10 caracteres, ej. RUL)."),
            new RuleVerbParamDescriptor("prefix", "Prefijo", RuleParamType.Text, Required: false,
                "Prefijo del numero emitido (ej. C -> C00042)."),
            new RuleVerbParamDescriptor("padding", "Relleno", RuleParamType.Number, Required: false,
                "Cantidad de digitos con ceros a la izquierda (default 5)."),
            new RuleVerbParamDescriptor("targetField", "Campo destino", RuleParamType.FieldCode, Required: false,
                "FieldCode del campo del formulario que recibe el consecutivo."),
            new RuleVerbParamDescriptor("autoComplete", "Completar paso", RuleParamType.Boolean, Required: false,
                "Si es true y la regla es autonoma de un nodo, el paso del flujo se completa solo.")
        ]);

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var sequenceCode = context.GetStringParam("sequenceCode")?.Trim();
        if (string.IsNullOrWhiteSpace(sequenceCode) || sequenceCode.Length > 10)
        {
            return RuleVerbResult.Fail("Parametro 'sequenceCode' obligatorio (maximo 10 caracteres).");
        }
        var prefix = context.GetStringParam("prefix") ?? "";
        var padding = Math.Clamp(context.GetIntParam("padding", 5), 1, 12);
        var targetField = context.GetStringParam("targetField")?.Trim();

        await _sequences.EnsureSequenceAsync(sequenceCode, cancellationToken);
        var number = await _sequences.NextAsync(sequenceCode, prefix, padding, cancellationToken);

        var actions = new List<RuleAction>();
        if (!string.IsNullOrWhiteSpace(targetField))
        {
            context.FormData[targetField!] = number;
            actions.Add(RuleAction.SetValue(targetField!, number));
        }

        // Trazabilidad en la tarea del caso (si la hay): el consecutivo emitido queda visible.
        if (context.TaskItemId is Guid taskItemId)
        {
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = context.TenantId,
                TaskItemId = taskItemId,
                Type = TaskActivityType.Action,
                ActorUserId = context.ActorUserId,
                ActorName = context.ActorName,
                Text = $"la regla asigno el consecutivo {number}"
            });
        }

        return RuleVerbResult.Ok($"Consecutivo asignado: {number}.", 1, actions,
            context.GetBoolParam("autoComplete"));
    }
}
