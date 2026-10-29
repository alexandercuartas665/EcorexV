using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>Un destino del asistente AiCompose: o un campo simple (Field), o un bloque de un lienzo (Lienzo+Block).</summary>
public sealed record AiComposeTarget(string? Field, string? Lienzo, string? Block)
{
    public bool IsLienzo => !string.IsNullOrWhiteSpace(Lienzo) && !string.IsNullOrWhiteSpace(Block);
    public bool IsField => !string.IsNullOrWhiteSpace(Field) && !IsLienzo;
}

/// <summary>
/// Config del control "Asistente IA (llenar campos)" (FormControlType.AiCompose), guardada en
/// FormQuestion.OptionsJson (bajo "ai") y editable en el FormDesigner (sin SQL). Primo de AiIntakeConfig: en vez
/// de subir imagen/PDF, entrega al agente las RESPUESTAS ya capturadas del formulario (sources, o todas) y este
/// PROPONE valores para los targets (campos simples y/o bloques de un LienzoBloques). El humano confirma antes de
/// escribir (confirm). Ver IFormAiComposeService / FormScopedToolset.set_campo / set_bloque.
/// </summary>
public sealed record AiComposeConfig(
    Guid? AgentId,
    string? Prompt,
    IReadOnlyList<string> Sources,
    IReadOnlyList<AiComposeTarget> Targets,
    string ButtonLabel,
    bool Confirm,
    string? Model)
{
    public const string DefaultButtonLabel = "Llenar con IA";

    public static AiComposeConfig Default =>
        new(null, null, Array.Empty<string>(), Array.Empty<AiComposeTarget>(), DefaultButtonLabel, true, null);

    /// <summary>Lee la config desde el OptionsJson del control (objeto bajo "ai"). Tolerante.</summary>
    public static AiComposeConfig Parse(string? optionsJson)
    {
        var d = Default;
        if (string.IsNullOrWhiteSpace(optionsJson)) { return d; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { return d; }
            var o = r.TryGetProperty("ai", out var aiEl) && aiEl.ValueKind == JsonValueKind.Object ? aiEl : r;

            Guid? agentId = null;
            if (o.TryGetProperty("agentId", out var ag) && ag.ValueKind == JsonValueKind.String
                && Guid.TryParse(ag.GetString(), out var gid)) { agentId = gid; }

            var buttonLabel = NullIfBlank(StrOr(o, "buttonLabel", null)) ?? d.ButtonLabel;

            return new AiComposeConfig(
                AgentId: agentId,
                Prompt: NullIfBlank(StrOr(o, "prompt", null)),
                Sources: ReadStrings(o, "sources"),
                Targets: ReadTargets(o),
                ButtonLabel: buttonLabel,
                Confirm: BoolOr(o, "confirm", d.Confirm),
                Model: NullIfBlank(StrOr(o, "model", null)));
        }
        catch (JsonException) { return d; }
    }

    /// <summary>Serializa a OptionsJson (objeto bajo "ai"), lo que escribe el disenador.</summary>
    public static string Serialize(AiComposeConfig cfg)
        => JsonSerializer.Serialize(new
        {
            ai = new
            {
                agentId = cfg.AgentId?.ToString(),
                prompt = cfg.Prompt,
                sources = cfg.Sources,
                targets = cfg.Targets.Select(t => t.IsLienzo
                    ? (object)new { lienzo = t.Lienzo, block = t.Block }
                    : new { field = t.Field }),
                buttonLabel = cfg.ButtonLabel,
                confirm = cfg.Confirm,
                model = cfg.Model
            }
        });

    private static IReadOnlyList<AiComposeTarget> ReadTargets(JsonElement o)
    {
        if (!o.TryGetProperty("targets", out var ts) || ts.ValueKind != JsonValueKind.Array) { return Array.Empty<AiComposeTarget>(); }
        var list = new List<AiComposeTarget>();
        foreach (var t in ts.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) { continue; }
            var lienzo = NullIfBlank(StrOr(t, "lienzo", null));
            var block = NullIfBlank(StrOr(t, "block", null));
            if (lienzo is not null && block is not null) { list.Add(new AiComposeTarget(null, lienzo, block)); continue; }
            var field = NullIfBlank(StrOr(t, "field", null));
            if (field is not null) { list.Add(new AiComposeTarget(field, null, null)); }
        }
        return list;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) { return Array.Empty<string>(); }
        var list = new List<string>();
        foreach (var el in v.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString())) { list.Add(el.GetString()!.Trim()); }
        }
        return list;
    }

    private static string? StrOr(JsonElement o, string name, string? fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : fallback;
    private static bool BoolOr(JsonElement o, string name, bool fallback)
        => o.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;
    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
