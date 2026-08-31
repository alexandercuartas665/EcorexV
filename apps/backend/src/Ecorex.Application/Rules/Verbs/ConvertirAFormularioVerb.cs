using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo CONVERTIR_A_FORMULARIO (ADR-0078): declara la intencion de TRANSFORMAR el registro actual en un
/// registro NUEVO de OTRO formulario (por su codigo), copiando los datos mapeables y ABRIENDO el nuevo
/// registro para completar lo pendiente. Es el gemelo "transformador" de IMPRIMIR_PLANTILLA: el boton que
/// dispara esta regla, en vez de imprimir, crea+abre. Ejemplo AGROMETALICAS: Cotizacion (COT) -> Orden de
/// Trabajo (FT-C-008).
///
/// El verbo NO abre nada (no tiene render): crea el registro (via IFormResponseService.CreateDerivedFormAsync,
/// que hereda el anclaje a la tarea del origen) y devuelve una accion OpenForm que el DynamicFormRenderer
/// detecta al hacer clic y reenvia al HOST (OnOpenFormRequested) para abrirlo (modal en la tarea, etc.).
/// </summary>
public sealed class ConvertirAFormularioVerb : IRuleVerb
{
    public const string VerbName = "CONVERTIR_A_FORMULARIO";

    public string Name => VerbName;

    private readonly IApplicationDbContext _db;
    private readonly IFormResponseService _forms;

    public ConvertirAFormularioVerb(IApplicationDbContext db, IFormResponseService forms)
    {
        _db = db;
        _forms = forms;
    }

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Convertir a otro formulario",
        "Crea un registro NUEVO de otro formulario (por su codigo) copiando los datos mapeables del registro "
        + "actual y lo abre para completar. Si el registro se trabaja dentro de una tarea, el nuevo queda "
        + "anclado a ESA misma tarea (cae en su pestana Formularios).",
        [
            new RuleVerbParamDescriptor("targetCode", "Formulario destino (codigo)", RuleParamType.Text, Required: true,
                "CODIGO EXACTO del formulario destino (form_definitions.code), p.ej. FT-C-008."),
            new RuleVerbParamDescriptor("mapping", "Mapeo de campos", RuleParamType.Json, Required: false,
                "Opcional. JSON { campoOrigen: campoDestino } solo para los campos que cambian de nombre. Los "
                + "campos con el MISMO codigo en ambos formularios se copian automaticamente."),
            new RuleVerbParamDescriptor("defaults", "Valores por defecto / transformacion", RuleParamType.Json, Required: false,
                "Opcional. JSON { campoDestino: valor } que RELLENA campos del destino que NO vienen del origen "
                + "(solo si quedan vacios). El valor puede ser una constante o un token de contexto: "
                + "'@usuario.nombre' (nombre del usuario que convierte), '@usuario.email', '@fecha.hoy', "
                + "'@fecha.hora'. Ej: { \"vendedor\": \"@usuario.nombre\" }."),
            new RuleVerbParamDescriptor("openMode", "Como abrir", RuleParamType.Text, Required: false,
                "Opcional. 'modal' (por defecto): el host abre el registro creado en un modal.")
        ]);

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var targetCode = context.GetStringParam("targetCode")?.Trim();
        if (string.IsNullOrWhiteSpace(targetCode))
        {
            return RuleVerbResult.Fail("Parametro 'targetCode' obligatorio (codigo del formulario destino).");
        }
        if (context.FormResponseId is not Guid sourceId)
        {
            return RuleVerbResult.Fail("No hay registro origen para convertir.");
        }

        // Resolver la definicion destino por codigo (activa, no archivada). El filtro global de tenant aplica.
        var targetDef = await _db.FormDefinitions
            .Where(d => d.Code == targetCode && d.Status == FormStatus.Active && !d.IsArchived)
            .Select(d => new { d.Id, d.Title })
            .FirstOrDefaultAsync(cancellationToken);
        if (targetDef is null)
        {
            return RuleVerbResult.Fail($"No existe un formulario activo con codigo '{targetCode}'.");
        }

        var mapping = ParseMapping(context, "mapping");
        var defaults = ParseMapping(context, "defaults");

        var result = await _forms.CreateDerivedFormAsync(
            sourceId, targetDef.Id, mapping, defaults, context.ExecutedByTenantUserId, cancellationToken);
        if (!result.IsOk)
        {
            return RuleVerbResult.Fail(result.Error ?? "No se pudo crear el formulario destino.");
        }
        var newId = result.Value;

        return RuleVerbResult.Ok(
            $"Se creo el registro de '{targetDef.Title}'. Abrelo para completar lo pendiente.",
            recordsAffected: 1,
            actions: [RuleAction.OpenForm(newId.ToString())]);
    }

    /// <summary>Lee un parametro JSON de mapa { clave: valor }. Acepta un objeto JSON o una cadena con JSON.
    /// Se reusa para 'mapping' (origen->destino) y 'defaults' (destino->valor/token).</summary>
    private static IReadOnlyDictionary<string, string>? ParseMapping(RuleContext context, string paramName)
    {
        if (!context.Params.TryGetValue(paramName, out var el)) { return null; }
        if (el.ValueKind == JsonValueKind.Object) { return ReadMap(el); }
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) { return null; }
            try
            {
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.ValueKind == JsonValueKind.Object ? ReadMap(doc.RootElement) : null;
            }
            catch (JsonException) { return null; }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string>? ReadMap(JsonElement obj)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                var v = p.Value.GetString();
                if (!string.IsNullOrWhiteSpace(v)) { map[p.Name] = v!.Trim(); }
            }
        }
        return map.Count > 0 ? map : null;
    }
}
