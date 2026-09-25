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
            new RuleVerbParamDescriptor("gridMapping", "Mapeo de columnas de grilla", RuleParamType.Json, Required: false,
                "Opcional. Para grillas (tablas) cuyos IDs de columna difieren entre origen y destino: JSON "
                + "{ grilla: { colOrigen: colDestino } }. Cada fila del destino queda SOLO con las columnas "
                + "mapeadas; las no mapeadas se omiten. Sin entrada para la grilla se copia tal cual. Ej.: "
                + "{ \"items\": { \"producto\": \"descripcion\", \"cantidad\": \"cant\" } }."),
            new RuleVerbParamDescriptor("gridDerive", "Auto-marcado de columnas de grilla", RuleParamType.Json, Required: false,
                "Opcional. AUTO-MARCA columnas de una grilla por fila segun una condicion sobre OTRA columna de la "
                + "MISMA fila (corre DESPUES de copiar; no toca el resto de la fila). JSON { grilla: [ { target, "
                + "from, when, set } ] }. Por cada regla: si 'when' se cumple sobre la columna 'from', pone 'set' en "
                + "'target'; si no, 'target' queda vacio. Operadores 'when': '>N' (numerico mayor que N, ej. '>0'), "
                + "'=<valor>' (igualdad exacta trim + case-insensitive, ej. '=SI') y 'notempty' (no vacio y distinto "
                + "de 0/false). Ej.: "
                + "{ \"items\": [ { \"target\": \"ciz\", \"from\": \"cortes\", \"when\": \">0\", \"set\": \"X\" } ] }."),
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
        var gridMapping = ParseGridMapping(context, "gridMapping");
        var gridDerive = ParseGridDerive(context, "gridDerive");

        var result = await _forms.CreateDerivedFormAsync(
            sourceId, targetDef.Id, mapping, defaults, context.ExecutedByTenantUserId, gridMapping, gridDerive, cancellationToken);
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

    /// <summary>Lee 'gridMapping' anidado { grilla: { colOrigen: colDestino } }. Acepta objeto JSON o cadena
    /// con JSON. Cada grilla reusa ReadMap para su mapa de columnas.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ParseGridMapping(RuleContext context, string paramName)
    {
        if (!context.Params.TryGetValue(paramName, out var el)) { return null; }
        if (el.ValueKind == JsonValueKind.Object) { return ReadGridMap(el); }
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) { return null; }
            try
            {
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.ValueKind == JsonValueKind.Object ? ReadGridMap(doc.RootElement) : null;
            }
            catch (JsonException) { return null; }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ReadGridMap(JsonElement obj)
    {
        var outer = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var grid in obj.EnumerateObject())
        {
            if (grid.Value.ValueKind != JsonValueKind.Object) { continue; }
            var inner = ReadMap(grid.Value);
            if (inner is not null) { outer[grid.Name] = inner; }
        }
        return outer.Count > 0 ? outer : null;
    }

    /// <summary>Lee 'gridDerive' { grilla: [ { target, from, when, set } ] }. Acepta objeto JSON o cadena con JSON.
    /// Cada grilla trae una LISTA de reglas de auto-marcado por columna.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<GridDeriveRule>>? ParseGridDerive(RuleContext context, string paramName)
    {
        if (!context.Params.TryGetValue(paramName, out var el)) { return null; }
        if (el.ValueKind == JsonValueKind.Object) { return ReadGridDerive(el); }
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) { return null; }
            try
            {
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.ValueKind == JsonValueKind.Object ? ReadGridDerive(doc.RootElement) : null;
            }
            catch (JsonException) { return null; }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<GridDeriveRule>>? ReadGridDerive(JsonElement obj)
    {
        var outer = new Dictionary<string, IReadOnlyList<GridDeriveRule>>(StringComparer.Ordinal);
        foreach (var grid in obj.EnumerateObject())
        {
            if (grid.Value.ValueKind != JsonValueKind.Array) { continue; }
            var rules = new List<GridDeriveRule>();
            foreach (var r in grid.Value.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) { continue; }
                var target = Str(r, "target");
                var from = Str(r, "from");
                var when = Str(r, "when");
                if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(when)) { continue; }
                var set = Str(r, "set");
                rules.Add(new GridDeriveRule(target!.Trim(), from!.Trim(), when!.Trim(),
                    string.IsNullOrWhiteSpace(set) ? "X" : set!.Trim()));
            }
            if (rules.Count > 0) { outer[grid.Name] = rules; }
        }
        return outer.Count > 0 ? outer : null;
    }

    private static string? Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
