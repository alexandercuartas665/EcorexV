using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Ecorex.Application.Forms;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo DIRECCIONAR_CONCILIADOR: lo dispara el boton "Direccionar compra" del formulario de la tarea creada
/// desde el conciliador DIAN. Por cada mapeo configurado en "Configuracion de tarea" (campo del formulario ->
/// columna del conciliador: "oc" | "numsol" | "dyn:{fieldKey}") toma el valor ACTUAL del campo del formulario
/// y lo ESCRIBE en el renglon del conciliador del que nacio la tarea. Los mapeos viven en la config compartida
/// (IAutorizacionComprasConfigService), asi que cambiarlos no toca la regla. No tiene params.
/// </summary>
public sealed class DireccionarConciliadorVerb : IRuleVerb
{
    public const string VerbName = "DIRECCIONAR_CONCILIADOR";

    public string Name => VerbName;

    private readonly IFormResponseService _forms;
    private readonly IAutorizacionComprasConfigService _cfg;
    private readonly IConciliacionDianService _conc;
    private readonly IConciliacionFieldService _fields;

    public DireccionarConciliadorVerb(IFormResponseService forms, IAutorizacionComprasConfigService cfg,
        IConciliacionDianService conc, IConciliacionFieldService fields)
    {
        _forms = forms; _cfg = cfg; _conc = conc; _fields = fields;
    }

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Direccionar compra (conciliador)",
        "Devuelve valores del formulario de la tarea al renglon del conciliador del que nacio, segun los mapeos "
        + "configurados en 'Configuracion de tarea' (campo del formulario -> columna del conciliador). Sin parametros.",
        Array.Empty<RuleVerbParamDescriptor>());

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        if (context.FormResponseId is not Guid respId)
        {
            return RuleVerbResult.Fail("Este boton corre desde el formulario de una tarea (sin formulario no hay nada que direccionar).");
        }

        var resp = await _forms.GetAsync(respId, cancellationToken);
        var reference = resp?.Reference;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return RuleVerbResult.Fail("El formulario no esta ligado a una tarea (sin referencia).");
        }

        var renglonId = await _cfg.ResolveRenglonByTaskNumberAsync(reference!, cancellationToken);
        if (renglonId is not Guid rid)
        {
            return RuleVerbResult.Fail($"No se encontro la factura del conciliador para la tarea {reference}.");
        }

        // Mapeos POR FORMULARIO: cada salida configurada tiene su propio Direccionar; se resuelve por el
        // formulario de esta respuesta (cae a la config legacy compartida si no hay salida que coincida).
        var cfg = await _cfg.GetDireccionarByFormAsync(resp!.DefinitionId, cancellationToken);
        if (!cfg.Enabled || cfg.Mappings.Count == 0)
        {
            return RuleVerbResult.Fail("El 'Direccionar compra' no esta configurado en el conciliador.");
        }

        IReadOnlyList<ConciliacionFieldDefDto>? dynDefs = null;
        var applied = 0;
        foreach (var m in cfg.Mappings)
        {
            var value = context.FormData.TryGetValue(m.FormField, out var v) ? (v ?? string.Empty) : string.Empty;
            var target = m.Target;
            if (string.Equals(target, "oc", StringComparison.OrdinalIgnoreCase))
            {
                await _conc.UpdateOrdenCompraAsync(rid, value, cancellationToken); applied++;
            }
            else if (string.Equals(target, "numsol", StringComparison.OrdinalIgnoreCase))
            {
                await _conc.UpdateNumFacturaSoldarcoAsync(rid, value, cancellationToken); applied++;
            }
            else if (target.StartsWith("dyn:", StringComparison.Ordinal))
            {
                var key = target[4..];
                dynDefs ??= await _fields.ListDefsAsync(cancellationToken);
                var def = dynDefs.FirstOrDefault(d => string.Equals(d.FieldKey, key, StringComparison.OrdinalIgnoreCase));
                if (def is not null) { await _fields.SetValueAsync(rid, def.Id, value, cancellationToken); applied++; }
            }
        }

        return applied == 0
            ? RuleVerbResult.Fail("Ningun mapeo aplico (revisa los campos del formulario y las columnas configuradas).")
            : RuleVerbResult.Ok($"Direccionado {applied} dato(s) al conciliador.", recordsAffected: applied);
    }
}
