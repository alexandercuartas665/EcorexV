using System.Text.RegularExpressions;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>Un lugar reescrito por el renombrado y cuantas filas/objetos cambiaron.</summary>
public sealed record RenameFieldCodeChange(string Place, int Count);

/// <summary>
/// Resultado del renombrado del nombre interno de un campo. Applied=false es la SIMULACION (no guarda nada).
/// Warnings = referencias FUERA del formulario (plantillas, flujos, reportes, configuraciones) que pueden
/// servir a varios formularios y por eso NO se reescriben solas: hay que revisarlas a mano.
/// </summary>
public sealed record RenameFieldCodeReport(
    string OldCode, string NewCode, bool Applied,
    IReadOnlyList<RenameFieldCodeChange> Changes, IReadOnlyList<string> Warnings);

public sealed partial class FormDefinitionService
{
    /// <summary>Codigos que el conciliador DIAN lee POR NOMBRE en el codigo C# (no en datos).</summary>
    private static readonly HashSet<string> CodigosLeidosPorNombre = new(StringComparer.Ordinal)
    {
        "proveedor", "nit", "rut", "fecha_compra", "num_factura", "nit_proveedor", "btn_direccionar",
    };

    public async Task<FormResult<RenameFieldCodeReport>> RenameFieldCodeAsync(
        Guid questionId, string newCode, bool apply, CancellationToken cancellationToken = default)
    {
        var target = await _db.FormQuestions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);
        if (target is null)
        {
            return FormResult<RenameFieldCodeReport>.NotFound("Pregunta no encontrada.");
        }
        newCode = (newCode ?? string.Empty).Trim();
        var error = await ValidateNewFieldCodeAsync(target.DefinitionId, target.Id, target.FieldCode, newCode, cancellationToken);
        if (error is not null)
        {
            return FormResult<RenameFieldCodeReport>.Invalid(error);
        }
        var report = await RenameFieldCodeCoreAsync(target.DefinitionId, target.Id, target.FieldCode, newCode, apply, cancellationToken);
        if (apply)
        {
            // Un solo SaveChanges = una sola transaccion: o se renombra todo (campo, respuestas, reglas,
            // config) o nada.
            await TouchRevisionAsync(target.DefinitionId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return FormResult<RenameFieldCodeReport>.Ok(report);
    }

    private async Task<string?> ValidateNewFieldCodeAsync(Guid definitionId, Guid questionId, string oldCode, string newCode,
        CancellationToken cancellationToken)
    {
        if (newCode.Length is 0 or > 60)
        {
            return "El nombre interno es obligatorio (maximo 60 caracteres).";
        }
        if (!FieldCodeRegex().IsMatch(newCode))
        {
            return "El nombre interno debe iniciar con letra y usar solo letras, numeros, guion y guion bajo.";
        }
        if (string.Equals(oldCode, newCode, StringComparison.Ordinal))
        {
            return "El nombre interno nuevo es igual al actual.";
        }
        var duplicated = await _db.FormQuestions.AnyAsync(
            q => q.DefinitionId == definitionId && q.FieldCode == newCode && q.Id != questionId, cancellationToken);
        return duplicated ? $"Ya existe un campo con el nombre interno '{newCode}' en este formulario." : null;
    }

    /// <summary>
    /// Reescribe (apply) o cuenta (simulacion) todas las referencias al codigo DENTRO del formulario y junta
    /// las advertencias de afuera. En simulacion carga sin seguimiento: nada queda pendiente en el contexto.
    /// En apply deja las entidades modificadas en el contexto; el llamador hace el SaveChanges.
    /// </summary>
    private async Task<RenameFieldCodeReport> RenameFieldCodeCoreAsync(Guid definitionId, Guid questionId,
        string oldCode, string newCode, bool apply, CancellationToken ct)
    {
        IQueryable<T> Load<T>(IQueryable<T> q) where T : class => apply ? q : q.AsNoTracking();
        var changes = new Dictionary<string, int>(StringComparer.Ordinal);
        void Hit(string place, bool changed)
        {
            if (changed) { changes[place] = changes.GetValueOrDefault(place) + 1; }
        }
        // Asigna y reporta si cambio (las funciones devuelven la misma cadena cuando no hay cambio).
        static bool Set(string? before, string? after, Action<string?> assign)
        {
            if (string.Equals(before, after, StringComparison.Ordinal)) { return false; }
            assign(after);
            return true;
        }

        // 1) Campos del formulario.
        var questions = await Load(_db.FormQuestions.Where(q => q.DefinitionId == definitionId)).ToListAsync(ct);
        foreach (var q in questions)
        {
            if (q.Id == questionId) { q.FieldCode = newCode; Hit("Campo renombrado", true); }
            Hit("Calculos de otros campos", Set(q.CalcExpression, FormFieldCodeRename.FieldTokens(q.CalcExpression, oldCode, newCode), v => q.CalcExpression = v));
            Hit("Condiciones \"Mostrar solo si\"", Set(q.VisibleWhenJson, FormFieldCodeRename.FieldProps(q.VisibleWhenJson, oldCode, newCode), v => q.VisibleWhenJson = v));
            Hit("Autollenado desde listas", Set(q.AutofillMapJson, FormFieldCodeRename.AutofillValues(q.AutofillMapJson, oldCode, newCode), v => q.AutofillMapJson = v));
            Hit("Filtros en cascada de listas", Set(q.FilterJson, FormFieldCodeRename.FilterCascade(q.FilterJson, oldCode, newCode), v => q.FilterJson = v));
            Hit("Grillas, lienzos, IA y botones", Set(q.OptionsJson, FormFieldCodeRename.Options(q.OptionsJson, oldCode, newCode), v => q.OptionsJson = v));
        }

        // 2) Secciones.
        var containers = await Load(_db.FormContainers.Where(c => c.DefinitionId == definitionId)).ToListAsync(ct);
        foreach (var c in containers)
        {
            Hit("Condiciones de secciones", Set(c.VisibleWhenJson, FormFieldCodeRename.FieldProps(c.VisibleWhenJson, oldCode, newCode), v => c.VisibleWhenJson = v));
        }

        // 3) Propiedades del formulario.
        var def = await Load(_db.FormDefinitions.Where(d => d.Id == definitionId)).FirstAsync(ct);
        var defChanged = false;
        defChanged |= Set(def.IdentitySourceFieldCode, def.IdentitySourceFieldCode == oldCode ? newCode : def.IdentitySourceFieldCode, v => def.IdentitySourceFieldCode = v);
        defChanged |= Set(def.AutoCodeTargetFieldCode, def.AutoCodeTargetFieldCode == oldCode ? newCode : def.AutoCodeTargetFieldCode, v => def.AutoCodeTargetFieldCode = v);
        defChanged |= Set(def.UniqueKeyFieldsJson, FormFieldCodeRename.CodeArray(def.UniqueKeyFieldsJson, oldCode, newCode), v => def.UniqueKeyFieldsJson = v);
        defChanged |= Set(def.ListColumnsJson, FormFieldCodeRename.CodeArray(def.ListColumnsJson, oldCode, newCode), v => def.ListColumnsJson = v);
        defChanged |= Set(def.FilterFieldsJson, FormFieldCodeRename.CodeArray(def.FilterFieldsJson, oldCode, newCode), v => def.FilterFieldsJson = v);
        defChanged |= Set(def.StatusLadderJson, FormFieldCodeRename.FieldProps(def.StatusLadderJson, oldCode, newCode), v => def.StatusLadderJson = v);
        defChanged |= Set(def.KpisJson, FormFieldCodeRename.FieldProps(def.KpisJson, oldCode, newCode), v => def.KpisJson = v);
        defChanged |= Set(def.CloseRuleJson, FormFieldCodeRename.FieldProps(def.CloseRuleJson, oldCode, newCode), v => def.CloseRuleJson = v);
        defChanged |= Set(def.CustomCss, FormFieldCodeRename.Css(def.CustomCss, oldCode, newCode), v => def.CustomCss = v);
        Hit("Propiedades del formulario (consecutivo, columnas, filtros, estados, KPIs, CSS)", defChanged);

        // 4) Reglas: el override del vinculo siempre es de este formulario; la regla base solo si NADIE mas la usa.
        var questionIds = questions.Select(q => q.Id).ToList();
        var fieldLinks = await Load(_db.FormFieldRules.Where(l => questionIds.Contains(l.FormQuestionId))).ToListAsync(ct);
        var submitLinks = await Load(_db.FormSubmitRules.Where(l => l.DefinitionId == definitionId)).ToListAsync(ct);
        foreach (var l in fieldLinks)
        {
            Hit("Parametros de reglas del formulario", Set(l.ParamsOverrideJson, FormFieldCodeRename.RuleParams(l.ParamsOverrideJson, oldCode, newCode), v => l.ParamsOverrideJson = v));
        }
        foreach (var l in submitLinks)
        {
            Hit("Parametros de reglas del formulario", Set(l.ParamsOverrideJson, FormFieldCodeRename.RuleParams(l.ParamsOverrideJson, oldCode, newCode), v => l.ParamsOverrideJson = v));
        }
        var warnings = new List<string>();
        var ruleIds = fieldLinks.Select(l => l.RuleId).Concat(submitLinks.Select(l => l.RuleId)).Distinct().ToList();
        if (ruleIds.Count > 0)
        {
            var rules = await Load(_db.Rules.Where(r => ruleIds.Contains(r.Id))).ToListAsync(ct);
            var allFieldLinks = await _db.FormFieldRules.AsNoTracking().Where(l => ruleIds.Contains(l.RuleId))
                .Select(l => new { l.RuleId, l.FormQuestionId }).ToListAsync(ct);
            var allSubmitLinks = await _db.FormSubmitRules.AsNoTracking().Where(l => ruleIds.Contains(l.RuleId))
                .Select(l => new { l.RuleId, l.DefinitionId }).ToListAsync(ct);
            var nodeLinks = await _db.WorkflowNodeRules.AsNoTracking().Where(l => ruleIds.Contains(l.RuleId))
                .Select(l => l.RuleId).ToListAsync(ct);
            var questionSet = questionIds.ToHashSet();
            foreach (var rule in rules)
            {
                var rewritten = FormFieldCodeRename.RuleParams(rule.ParamsJson, oldCode, newCode);
                if (string.Equals(rewritten, rule.ParamsJson, StringComparison.Ordinal)) { continue; }
                var shared = allFieldLinks.Any(l => l.RuleId == rule.Id && !questionSet.Contains(l.FormQuestionId))
                    || allSubmitLinks.Any(l => l.RuleId == rule.Id && l.DefinitionId != definitionId)
                    || nodeLinks.Contains(rule.Id);
                if (shared)
                {
                    warnings.Add($"La regla \"{rule.Name}\" usa '{oldCode}' en sus parametros y tambien la usan otros formularios o flujos: no se cambio; revisala.");
                }
                else
                {
                    rule.ParamsJson = rewritten;
                    Hit("Parametros de reglas del formulario", true);
                }
            }
        }

        // 5) Respuestas guardadas: la clave del documento (si no, el valor se pierde al siguiente guardado).
        var responses = await Load(_db.FormResponses.Where(r => r.DefinitionId == definitionId)).ToListAsync(ct);
        foreach (var r in responses)
        {
            Hit("Respuestas guardadas", Set(r.Data, FormFieldCodeRename.DataKey(r.Data, oldCode, newCode), v => r.Data = v ?? "{}"));
        }

        // 6) Vinculos de subformulario/gestiones y firmas pendientes de respuestas de este formulario.
        var links = await Load(_db.FormRecordLinks.Where(l => l.ParentFieldCode == oldCode
            && _db.FormResponses.Any(r => r.Id == l.ParentResponseId && r.DefinitionId == definitionId))).ToListAsync(ct);
        foreach (var l in links) { l.ParentFieldCode = newCode; Hit("Registros hijos vinculados", true); }
        var signatures = await Load(_db.FormSignatureTokens.Where(t => t.FieldCode == oldCode
            && _db.FormResponses.Any(r => r.Id == t.ResponseId && r.DefinitionId == definitionId))).ToListAsync(ct);
        foreach (var t in signatures) { t.FieldCode = newCode; Hit("Enlaces de firma", true); }

        // 7) Columnas de vista lista de tableros "form:{defId}:{codigo}" (inequivocas: llevan el id del form).
        var boardKey = $"form:{definitionId}:";
        var boards = await Load(_db.TaskBoards.Where(b => b.ListViewConfigJson != null)).ToListAsync(ct);
        foreach (var b in boards)
        {
            var cfg = b.ListViewConfigJson!;
            if (!cfg.Contains(boardKey + oldCode, StringComparison.OrdinalIgnoreCase)) { continue; }
            var updated = Regex.Replace(cfg, Regex.Escape(boardKey + oldCode) + "(?=\")", boardKey + newCode,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Hit("Columnas de tableros (vista lista)", Set(cfg, updated, v => b.ListViewConfigJson = v));
        }

        warnings.AddRange(await ScanExternalReferencesAsync(def.Code, definitionId, oldCode, ct));
        if (CodigosLeidosPorNombre.Contains(oldCode))
        {
            warnings.Add($"'{oldCode}' es un nombre que el conciliador DIAN lee por codigo: si este formulario es de conciliacion, renombrarlo lo desconecta.");
        }

        return new RenameFieldCodeReport(oldCode, newCode, apply,
            changes.Select(kv => new RenameFieldCodeChange(kv.Key, kv.Value)).ToList(), warnings);
    }

    /// <summary>
    /// Referencias FUERA del formulario que pueden servir a varios formularios a la vez (el token no dice de
    /// cual es): se reportan para revision manual, no se reescriben. Solo lectura.
    /// </summary>
    private async Task<List<string>> ScanExternalReferencesAsync(string formCode, Guid definitionId, string oldCode, CancellationToken ct)
    {
        var esc = Regex.Escape(oldCode);
        var formToken = new Regex(@"form\." + esc + @"(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant);
        var campoToken = new Regex(@"campo\." + esc + @"(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant);
        var quoted = "\"" + oldCode + "\"";
        var w = new List<string>();

        foreach (var t in await _db.QuoteTemplates.AsNoTracking().Select(t => new { t.Name, t.HtmlContent }).ToListAsync(ct))
        {
            if (campoToken.IsMatch(t.HtmlContent ?? "")) { w.Add($"Plantilla de impresion \"{t.Name}\" usa {{{{campo.{oldCode}}}}}."); }
        }
        foreach (var n in await _db.WorkflowNodes.AsNoTracking()
                     .Where(n => n.AssigneeFormFieldCode == oldCode || n.NotifyJson != null)
                     .Select(n => new { n.Name, n.AssigneeFormFieldCode, n.NotifyJson }).ToListAsync(ct))
        {
            if (n.AssigneeFormFieldCode == oldCode) { w.Add($"Paso de flujo \"{n.Name}\" asigna segun el campo '{oldCode}'."); }
            if (formToken.IsMatch(n.NotifyJson ?? "")) { w.Add($"Aviso del paso de flujo \"{n.Name}\" usa {{form.{oldCode}}}."); }
        }
        foreach (var a in await _db.WorkflowNodeAgents.AsNoTracking().Where(a => a.WhatsAppTemplateVarsJson != null)
                     .Select(a => a.WhatsAppTemplateVarsJson).ToListAsync(ct))
        {
            if (formToken.IsMatch(a ?? "")) { w.Add($"Una plantilla de WhatsApp de un agente en un flujo usa {{form.{oldCode}}}."); break; }
        }
        foreach (var a in await _db.AiAgents.AsNoTracking().Where(a => a.CierreJson != null)
                     .Select(a => new { a.Name, a.CierreJson }).ToListAsync(ct))
        {
            if (formToken.IsMatch(a.CierreJson ?? "")) { w.Add($"Agente de IA \"{a.Name}\" usa {{form.{oldCode}}} en sus avisos."); }
        }
        foreach (var d in await _db.DocumentTemplates.AsNoTracking().Select(d => new { d.Name, d.HtmlContent }).ToListAsync(ct))
        {
            if (formToken.IsMatch(d.HtmlContent ?? "")) { w.Add($"Plantilla de documento \"{d.Name}\" usa {{form.{oldCode}}}."); }
        }
        foreach (var g in await _db.DocumentTemplateGroups.AsNoTracking().Where(g => g.HeaderHtml != null)
                     .Select(g => new { g.Name, g.HeaderHtml }).ToListAsync(ct))
        {
            if (formToken.IsMatch(g.HeaderHtml ?? "")) { w.Add($"Membrete \"{g.Name}\" usa {{form.{oldCode}}}."); }
        }
        foreach (var s in await _db.ScrapeSteps.AsNoTracking().Where(s => s.MappingJson != null)
                     .Select(s => new { s.Name, s.MappingJson }).ToListAsync(ct))
        {
            if (formToken.IsMatch(s.MappingJson ?? "") || (s.MappingJson ?? "").Contains(quoted, StringComparison.Ordinal))
            {
                w.Add($"Paso de dron \"{s.Name}\" podria usar el campo '{oldCode}'.");
            }
        }
        var formSource = "form:" + formCode;
        foreach (var r in await _db.ReportDefinitions.AsNoTracking().Select(r => new { r.Name, r.SpecJson }).ToListAsync(ct))
        {
            var spec = r.SpecJson ?? "";
            if (spec.Contains(formSource, StringComparison.OrdinalIgnoreCase) && spec.Contains(quoted, StringComparison.Ordinal))
            {
                w.Add($"Reporte \"{r.Name}\" usa el campo '{oldCode}' del formulario.");
            }
        }
        foreach (var c in await _db.TenantConfigurations.AsNoTracking()
                     .Where(c => c.ConfigKey == "conciliacion-dian.direccionar" || c.ConfigKey == "conciliacion-dian.salidas"
                         || c.ConfigKey == "requerimientos.rfq")
                     .Select(c => new { c.ConfigKey, c.ConfigValue }).ToListAsync(ct))
        {
            if ((c.ConfigValue ?? "").Contains(quoted, StringComparison.Ordinal))
            {
                w.Add($"La configuracion \"{c.ConfigKey}\" menciona el campo '{oldCode}'.");
            }
        }
        // CONVERTIR_A_FORMULARIO de OTROS formularios que apuntan a este (sus mapeos usan codigos de aqui).
        var defIdText = definitionId.ToString();
        foreach (var r in await _db.Rules.AsNoTracking().Where(r => r.VerbName == "CONVERTIR_A_FORMULARIO" && r.ParamsJson != null)
                     .Select(r => new { r.Name, r.ParamsJson }).ToListAsync(ct))
        {
            var p = r.ParamsJson ?? "";
            if ((p.Contains("\"" + formCode + "\"", StringComparison.OrdinalIgnoreCase) || p.Contains(defIdText, StringComparison.OrdinalIgnoreCase))
                && p.Contains(quoted, StringComparison.Ordinal))
            {
                w.Add($"La regla de conversion \"{r.Name}\" (de otro formulario hacia este) mapea el campo '{oldCode}'.");
            }
        }
        return w.Distinct().Take(40).ToList();
    }
}
