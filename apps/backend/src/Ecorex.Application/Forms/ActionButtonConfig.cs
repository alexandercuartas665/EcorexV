using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>
/// Config del control "Boton de accion" (FormControlType.ActionButton), guardada en FormQuestion.OptionsJson
/// (bajo "button") y editable 100% en el FormDesigner (sin SQL ni regla de catalogo, a diferencia del Button
/// legacy). La accion se elige en el disenador. Acciones soportadas:
///  - "print-lienzo" -> abre la vista FormPrintLienzo del lienzo elegido (TargetField = field_code de un
///    LienzoBloques; vacio -> el primer LienzoBloques del formulario) en una pestana nueva.
///  - "print-form"   -> abre la vista FormPrint (diagnostico/registro completo) en una pestana nueva; no
///    usa TargetField.
/// Tolerante al parsear.
/// </summary>
public sealed record ActionButtonConfig(
    string Action,
    string? TargetField,
    string Label)
{
    public const string ActionPrintLienzo = "print-lienzo";
    public const string ActionPrintForm = "print-form";
    public const string DefaultLabel = "Imprimir lienzo";
    public const string DefaultFormLabel = "Imprimir formulario completo";

    public static ActionButtonConfig Default =>
        new(ActionPrintLienzo, null, DefaultLabel);

    /// <summary>Lee la config desde el OptionsJson del control (objeto bajo "button"). Tolerante.</summary>
    public static ActionButtonConfig Parse(string? optionsJson)
    {
        var d = Default;
        if (string.IsNullOrWhiteSpace(optionsJson)) { return d; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { return d; }
            var o = r.TryGetProperty("button", out var bEl) && bEl.ValueKind == JsonValueKind.Object ? bEl : r;

            var action = NullIfBlank(StrOr(o, "action", null)) ?? d.Action;
            var label = NullIfBlank(StrOr(o, "label", null)) ?? d.Label;

            return new ActionButtonConfig(
                Action: action,
                TargetField: NullIfBlank(StrOr(o, "targetField", null)),
                Label: label);
        }
        catch (JsonException) { return d; }
    }

    /// <summary>Serializa a OptionsJson (objeto bajo "button"), lo que escribe el disenador.</summary>
    public static string Serialize(ActionButtonConfig cfg)
        => JsonSerializer.Serialize(new
        {
            button = new
            {
                action = cfg.Action,
                targetField = cfg.TargetField,
                label = cfg.Label
            }
        });

    private static string? StrOr(JsonElement o, string name, string? fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback;
    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
