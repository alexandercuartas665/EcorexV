using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>
/// Config del control "Asistente de requerimiento (IA, imagen/PDF -> tabla)" (FormControlType.AiIntake),
/// guardada en FormQuestion.OptionsJson y editable en el FormDesigner (sin SQL). El control referencia un
/// AGENTE de IA del tenant (proveedor/modelo/prompt base) y PROPONE filas para una grilla de ESTE formulario;
/// el humano confirma antes de escribir (confirm). Ver IFormAiIntakeService / FormScopedToolset.
/// </summary>
public sealed record AiIntakeConfig(
    Guid? AgentId,
    string? Prompt,
    string? TargetGrid,
    string? ColumnsHint,
    string Accept,
    int MaxMb,
    int MaxRows,
    string Mode,
    bool Confirm,
    string? Model)
{
    public static AiIntakeConfig Default => new(null, null, null, null, "image,pdf", 15, 100, "append", true, null);

    /// <summary>Lee la config desde el OptionsJson del control. Tolerante: valores faltantes/invalidos -> default.</summary>
    public static AiIntakeConfig Parse(string? optionsJson)
    {
        var d = Default;
        if (string.IsNullOrWhiteSpace(optionsJson)) { return d; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { return d; }
            // El objeto de config del AiIntake vive bajo "ai" (para no chocar con otros extras del OptionsJson).
            var o = r.TryGetProperty("ai", out var aiEl) && aiEl.ValueKind == JsonValueKind.Object ? aiEl : r;

            Guid? agentId = null;
            if (o.TryGetProperty("agentId", out var ag) && ag.ValueKind == JsonValueKind.String
                && Guid.TryParse(ag.GetString(), out var gid)) { agentId = gid; }

            var mode = StrOr(o, "mode", d.Mode).Trim().ToLowerInvariant();
            if (mode != "append" && mode != "replace") { mode = d.Mode; }

            return new AiIntakeConfig(
                AgentId: agentId,
                Prompt: NullIfBlank(StrOr(o, "prompt", null)),
                TargetGrid: NullIfBlank(StrOr(o, "target_grid", null)),
                ColumnsHint: NullIfBlank(StrOr(o, "columns_hint", null)),
                Accept: NullIfBlank(StrOr(o, "accept", null)) ?? d.Accept,
                MaxMb: Clamp(IntOr(o, "max_mb", d.MaxMb), 1, 50),
                MaxRows: Clamp(IntOr(o, "max_rows", d.MaxRows), 1, 500),
                Mode: mode,
                Confirm: BoolOr(o, "confirm", d.Confirm),
                Model: NullIfBlank(StrOr(o, "model", null)));
        }
        catch (JsonException) { return d; }
    }

    public bool AcceptsImages => Accept.Contains("image", StringComparison.OrdinalIgnoreCase);
    public bool AcceptsPdf => Accept.Contains("pdf", StringComparison.OrdinalIgnoreCase);

    private static string? StrOr(JsonElement o, string name, string? fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback;
    private static int IntOr(JsonElement o, string name, int fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    private static bool BoolOr(JsonElement o, string name, bool fallback)
        => o.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;
    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}
