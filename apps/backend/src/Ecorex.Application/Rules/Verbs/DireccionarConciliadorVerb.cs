using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Ecorex.Application.Forms;
using Ecorex.Application.Forms.Lookups;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo DIRECCIONAR_CONCILIADOR: lo dispara el boton "Direccionar compra" del formulario de la tarea creada
/// desde el conciliador DIAN. Por cada mapeo configurado en "Configuracion de tarea" (campo del formulario ->
/// columna del conciliador: "oc" | "numsol" | "asignado" | "dyn:{fieldKey}") toma el valor ACTUAL del campo del
/// formulario y lo ESCRIBE en el renglon del conciliador del que nacio la tarea. El destino "asignado" guarda el
/// usuario (id de TenantUser, p. ej. de un campo origen SystemUser como "Redireccionamiento") como responsable del
/// renglon, lo mismo que el selector "responsable" del panel. Los mapeos viven en la config compartida
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
    private readonly IFormDefinitionService _defs;
    private readonly IFormLookupService _lookups;

    public DireccionarConciliadorVerb(IFormResponseService forms, IAutorizacionComprasConfigService cfg,
        IConciliacionDianService conc, IConciliacionFieldService fields,
        IFormDefinitionService defs, IFormLookupService lookups)
    {
        _forms = forms; _cfg = cfg; _conc = conc; _fields = fields; _defs = defs; _lookups = lookups;
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

        // La referencia puede ser el numero de tarea (form dentro de la tarea) o el id del renglon directo
        // (form abierto en el panel del modulo de autorizacion): se resuelve en ambos casos.
        var renglonId = await _cfg.ResolveRenglonByReferenceAsync(reference!, cancellationToken);
        if (renglonId is not Guid rid)
        {
            return RuleVerbResult.Fail($"No se encontro la factura del conciliador para la referencia {reference}.");
        }

        // Mapeos POR FORMULARIO: cada salida configurada tiene su propio Direccionar; se resuelve por el
        // formulario de esta respuesta (cae a la config legacy compartida si no hay salida que coincida).
        var cfg = await _cfg.GetDireccionarByFormAsync(resp!.DefinitionId, cancellationToken);
        if (!cfg.Enabled || cfg.Mappings.Count == 0)
        {
            return RuleVerbResult.Fail("El 'Direccionar compra' no esta configurado en el conciliador.");
        }

        IReadOnlyList<ConciliacionFieldDefDto>? dynDefs = null;
        FormDefinitionDetailDto? def0 = null;
        var applied = 0;
        foreach (var m in cfg.Mappings)
        {
            var value = context.FormData.TryGetValue(m.FormField, out var v) ? (v ?? string.Empty) : string.Empty;
            var target = m.Target;
            // Un campo LISTA de Contenedor/Tercero/Item guarda el id de la fila elegida; al conciliador va el TEXTO
            // que el usuario ve (su campo a mostrar, p. ej. DOCUMENTO de ORDENES_COMPRA = "OMP0011528"). El destino
            // "asignado" se excluye: espera el id del usuario.
            if (!string.Equals(target, "asignado", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value, out _))
            {
                def0 ??= await _defs.GetAsync(resp!.DefinitionId, cancellationToken);
                var q = def0?.Questions.FirstOrDefault(x => string.Equals(x.FieldCode, m.FormField, StringComparison.Ordinal));
                if (q is not null) { value = await TextoDeListaAsync(q, value, cancellationToken) ?? value; }
            }
            if (string.Equals(target, "oc", StringComparison.OrdinalIgnoreCase))
            {
                await _conc.UpdateOrdenCompraAsync(rid, value, cancellationToken); applied++;
            }
            else if (string.Equals(target, "numsol", StringComparison.OrdinalIgnoreCase))
            {
                await _conc.UpdateNumFacturaSoldarcoAsync(rid, value, cancellationToken); applied++;
            }
            else if (string.Equals(target, "asignado", StringComparison.OrdinalIgnoreCase))
            {
                // El valor es el id del TenantUser (campo origen SystemUser). Solo asignamos si trae un GUID
                // valido; vacio = no tocar el responsable (no lo borramos por accidente al direccionar).
                if (Guid.TryParse(value, out var userId))
                {
                    await _cfg.SetAsignadoAsync(rid, userId, cancellationToken); applied++;
                }
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

    /// <summary>Texto visible de un valor de LISTA (Contenedor/Tercero/Item): su campo a mostrar o, si no hay, la
    /// etiqueta del item. Null si el campo no es lista o no se resuelve (se envia el valor tal cual).</summary>
    private async Task<string?> TextoDeListaAsync(FormQuestionDto q, string value, CancellationToken ct)
    {
        if (q.SourceKind is not (FormSourceKind.DataContainer or FormSourceKind.Tercero or FormSourceKind.Item)) { return null; }
        try
        {
            var fields = string.IsNullOrWhiteSpace(q.DisplayField) ? Array.Empty<string>() : new[] { q.DisplayField! };
            var item = await _lookups.ResolveAsync(q.SourceKind, q.SourceRef ?? string.Empty, value, fields, ct);
            if (item is null) { return null; }
            if (!string.IsNullOrWhiteSpace(q.DisplayField) && item.Fields.TryGetValue(q.DisplayField!, out var dv)
                && !string.IsNullOrWhiteSpace(dv))
            {
                return dv;
            }
            return string.IsNullOrWhiteSpace(item.Display) ? null : item.Display;
        }
        catch { return null; }
    }
}
