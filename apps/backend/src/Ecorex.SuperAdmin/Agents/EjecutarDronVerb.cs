using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Rules;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Verbo EJECUTAR_DRON (ADR-0181): un BOTON del formulario de una tarea lanza un flujo del dron (modulo 000730)
/// pasandole los datos de la tarea, p.ej. "Pasar oferta al portal": el comercial llena la oferta en la grilla de la
/// tarea y el dron la escribe en el portal del cliente. El flujo recibe dos variables de corrida (no se guardan):
///   {{TAREA}} = numero de la tarea, y {{FORM}} = los campos del formulario como objeto JSON, con las grillas ya como
///   arreglos (en un script: <c>var f = JSON.parse('{{FORM}}'); f.items[0].vlr_uni</c>; o un bucle sobre FORM.items).
///
/// Parametros: flowId (obligatorio, el flujo a correr), gridField (grilla que debe estar completa: requeridos,
/// opciones y reglas por fila), requireFields (campos de cabecera con valor). Valida ANTES de lanzar (el borrador no
/// se valida al guardar). La corrida va en SEGUNDO PLANO (tarda minutos): el boton responde de una y el resultado
/// queda como comentario en la tarea, con la ultima captura del navegador adjunta. Un flujo no corre dos veces a la
/// vez (candado del runtime, ADR-0171). El verbo no decide si el portal "envia": eso lo define el flujo (el de
/// pdpartner GUARDA sin enviar; el envio final lo hace una persona).
/// </summary>
public sealed class EjecutarDronVerb : IRuleVerb
{
    public const string VerbName = "EJECUTAR_DRON";
    public const string VarTarea = "TAREA";
    public const string VarForm = "FORM";

    public string Name => VerbName;

    private readonly IApplicationDbContext _db;
    private readonly IBrowserRunService _runner;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EjecutarDronVerb> _log;

    public EjecutarDronVerb(IApplicationDbContext db, IBrowserRunService runner, IServiceScopeFactory scopes,
        ILogger<EjecutarDronVerb> log)
    {
        _db = db; _runner = runner; _scopes = scopes; _log = log;
    }

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Ejecutar flujo del dron",
        "Lanza un flujo de Extraccion de datos (dron) con los datos de la tarea: {{TAREA}} y {{FORM}} (campos y "
        + "grillas del formulario). Corre en segundo plano; el resultado y la captura quedan en la tarea.",
        new[]
        {
            new RuleVerbParamDescriptor("flowId", "Flujo del dron (id)", RuleParamType.Text, Required: true,
                "Id del flujo de Extraccion de datos que se ejecuta (ej. 'Responder oferta - pdpartner')."),
            new RuleVerbParamDescriptor("gridField", "Grilla que debe estar completa", RuleParamType.FieldCode, Required: false,
                "Grilla a validar antes de lanzar: obligatorios, opciones y reglas por fila (requiredWhen, maxCol...)."),
            new RuleVerbParamDescriptor("requireFields", "Campos obligatorios", RuleParamType.FieldCodeList, Required: false,
                "Campos de cabecera que deben tener valor antes de lanzar el flujo."),
            new RuleVerbParamDescriptor("confirmMessage", "Mensaje de confirmacion", RuleParamType.Text, Required: false,
                "Texto del modal que pide confirmar antes de lanzar (si se deja vacio, uno generico)."),
        });

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        if (context.FormResponseId is not Guid respId)
        {
            return RuleVerbResult.Fail("Este boton corre desde el formulario de una tarea.");
        }
        if (!Guid.TryParse(context.GetStringParam("flowId"), out var flowId))
        {
            return RuleVerbResult.Fail("El boton no tiene configurado el flujo del dron (flowId).");
        }

        var resp = await _db.FormResponses.AsNoTracking().Where(r => r.Id == respId)
            .Select(r => new { r.Reference, r.DefinitionId }).FirstOrDefaultAsync(cancellationToken);
        if (resp is null || string.IsNullOrWhiteSpace(resp.Reference))
        {
            return RuleVerbResult.Fail("El formulario no esta ligado a una tarea (sin referencia).");
        }
        var task = await _db.TaskItems.AsNoTracking().Where(t => t.Number == resp.Reference)
            .Select(t => new { t.Id, t.Number }).FirstOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return RuleVerbResult.Fail($"No se encontro la tarea {resp.Reference}.");
        }

        // (1) Validar lo que el flujo necesita (el borrador no se valida al guardar).
        foreach (var code in ReadCodes(context, "requireFields"))
        {
            if (!context.FormData.TryGetValue(code, out var v) || string.IsNullOrWhiteSpace(v))
            {
                var label = await _db.FormQuestions.AsNoTracking()
                    .Where(q => q.DefinitionId == resp.DefinitionId && q.FieldCode == code)
                    .Select(q => q.Label).FirstOrDefaultAsync(cancellationToken);
                return RuleVerbResult.Fail($"Completa '{label ?? code}' antes de continuar.");
            }
        }
        var grid = context.GetStringParam("gridField");
        if (!string.IsNullOrWhiteSpace(grid))
        {
            var opts = await _db.FormQuestions.AsNoTracking()
                .Where(q => q.DefinitionId == resp.DefinitionId && q.FieldCode == grid)
                .Select(q => q.OptionsJson).FirstOrDefaultAsync(cancellationToken);
            context.FormData.TryGetValue(grid!, out var gv);
            if (string.IsNullOrWhiteSpace(gv) || gv.Trim() == "[]")
            {
                return RuleVerbResult.Fail("La tabla de items esta vacia.");
            }
            var err = FormFieldValidator.ValidateGridValue(gv, opts);
            if (err is not null) { return RuleVerbResult.Fail(err); }
        }

        // (2) Flujo listo para correr.
        var flow = await _db.ScrapeFlows.AsNoTracking().Where(f => f.Id == flowId)
            .Select(f => new { f.Id, f.Name, f.ClientId }).FirstOrDefaultAsync(cancellationToken);
        if (flow is null) { return RuleVerbResult.Fail("El flujo del dron configurado no existe en esta empresa."); }
        if (flow.ClientId is null) { return RuleVerbResult.Fail($"El flujo '{flow.Name}' no tiene agente asignado."); }
        if (_runner.IsFlowRunning(flowId))
        {
            return RuleVerbResult.Fail($"El dron '{flow.Name}' ya esta corriendo. Intenta de nuevo en unos minutos.");
        }

        // (3) Confirmacion en el modal de la app (no el confirm nativo del navegador), ya con todo validado:
        // el host re-ejecuta el boton con Confirmed=true si el usuario acepta.
        if (!context.Confirmed)
        {
            var msg = context.GetStringParam("confirmMessage");
            return RuleVerbResult.Ok(null, recordsAffected: 0, actions: new[]
            {
                RuleAction.Confirm(string.IsNullOrWhiteSpace(msg)
                    ? $"El dron '{flow.Name}' va a pasar los datos de {task.Number} al portal. Continuar?"
                    : msg!),
            });
        }

        // (4) Lanzar en segundo plano: el boton no espera los minutos de navegador.
        var overrides = BuildOverrides(task.Number, context.FormData);
        var tenantId = context.TenantId;
        var actorId = context.ActorUserId ?? Guid.Empty;
        var actorName = context.ActorName;
        // Desde un boton el motor solo trae el TenantUser: el comentario debe quedar a nombre de quien lo pulso.
        if (context.ExecutedByTenantUserId is Guid tuId && (context.ActorUserId is null || actorName == "Sistema"))
        {
            var who = await _db.TenantUsers.AsNoTracking().Where(u => u.Id == tuId)
                .Select(u => new { u.PlatformUserId, u.Email }).FirstOrDefaultAsync(cancellationToken);
            if (who is not null)
            {
                var name = await _db.PlatformUsers.AsNoTracking().Where(p => p.Id == who.PlatformUserId)
                    .Select(p => p.DisplayName).FirstOrDefaultAsync(cancellationToken);
                actorId = context.ActorUserId ?? who.PlatformUserId;
                actorName = string.IsNullOrWhiteSpace(name) ? who.Email : name!;
            }
        }
        _ = Task.Run(() => RunAndReportAsync(tenantId, flowId, flow.Name, task.Id, task.Number, overrides, actorId, actorName));

        return RuleVerbResult.Ok($"El dron '{flow.Name}' se esta ejecutando con los datos de {task.Number}. "
            + "El resultado y la captura quedaran en los comentarios de la tarea.");
    }

    /// <summary>Variables de corrida: TAREA y FORM (objeto JSON; las grillas y valores JSON van anidados).</summary>
    public static Dictionary<string, string> BuildOverrides(string taskNumber, IReadOnlyDictionary<string, string?> formData)
    {
        var form = new JsonObject();
        foreach (var (code, value) in formData)
        {
            if (string.IsNullOrWhiteSpace(code)) { continue; }
            form[code] = ToNode(value);
        }
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VarTarea] = taskNumber,
            [VarForm] = form.ToJsonString(),
        };
    }

    private static JsonNode? ToNode(string? value)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t)) { return JsonValue.Create(value ?? string.Empty); }
        if ((t[0] == '[' && t[^1] == ']') || (t[0] == '{' && t[^1] == '}'))
        {
            try { return JsonNode.Parse(t); }
            catch (JsonException) { /* texto que solo parece JSON */ }
        }
        return JsonValue.Create(value);
    }

    private async Task RunAndReportAsync(Guid tenantId, Guid flowId, string flowName, Guid taskId, string taskNumber,
        Dictionary<string, string> overrides, Guid actorId, string actorName)
    {
        var progress = new StepCollector();
        string resumen;
        try
        {
            var r = await _runner.RunFlowStepwiseAsync(flowId, tenantId, 0, 1, progress, CancellationToken.None,
                overrides, ImportRunTrigger.Manual);
            resumen = Summarize(flowName, r, progress);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EJECUTAR_DRON] fallo el flujo {Flow} para la tarea {Task}", flowId, taskNumber);
            resumen = $"Dron '{flowName}': fallo inesperado ({ex.Message}). Revisa la bitacora del flujo.";
        }

        try
        {
            using var scope = _scopes.CreateScope();
            using (AmbientTenantContext.Begin(tenantId))
            {
                var tasks = scope.ServiceProvider.GetRequiredService<ITaskItemService>();
                await tasks.AddCommentAsync(taskId, resumen, actorId, actorName, CancellationToken.None);
                if (progress.LastScreenshot is { Length: > 0 } b64)
                {
                    var bytes = Convert.FromBase64String(b64);
                    var files = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
                    var fileName = $"dron-{taskNumber}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.png";
                    var stored = await files.SaveAsync(new FileStorageSaveRequest("dron", taskNumber, flowId.ToString(), fileName,
                        "image/png", bytes), CancellationToken.None);
                    await tasks.AddAttachmentAsync(new AddTaskAttachmentRequest(taskId, fileName, stored.Url, "image/png", bytes.Length),
                        actorId, actorName, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[EJECUTAR_DRON] no se pudo dejar el resultado en la tarea {Task}: {Resumen}", taskNumber, resumen);
        }
    }

    /// <summary>Texto del comentario: OK con el conteo de pasos, o los pasos que fallaron.</summary>
    internal static string Summarize(string flowName, BrowserRunResult r, StepCollector p)
    {
        if (r.Offline) { return $"Dron '{flowName}': el agente no esta en linea; no se ejecuto. Intenta cuando vuelva."; }
        if (!r.Dispatched || (!string.IsNullOrWhiteSpace(r.Error) && p.Total == 0))
        {
            return $"Dron '{flowName}': no se pudo ejecutar. {r.Error}".Trim();
        }
        if (p.Failed.Count > 0)
        {
            return $"Dron '{flowName}': {p.Failed.Count} de {p.Total} paso(s) con error: "
                + string.Join(" | ", p.Failed.Take(5)) + ". Revisa la captura y el portal.";
        }
        return $"Dron '{flowName}': {p.Total} paso(s) ejecutados sin error. Revisa la captura adjunta.";
    }

    private static IEnumerable<string> ReadCodes(RuleContext context, string name)
    {
        if (!context.Params.TryGetValue(name, out var el)) { return []; }
        if (el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0).ToList();
        }
        if (el.ValueKind == JsonValueKind.String)
        {
            return (el.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        return [];
    }

    /// <summary>Junta el progreso de la corrida de forma SINCRONA (no Progress&lt;T&gt;, que despacha a otro hilo).</summary>
    internal sealed class StepCollector : IProgress<FlowStepProgress>
    {
        private readonly object _gate = new();
        public int Total { get; private set; }
        public List<string> Failed { get; } = new();
        public string? LastScreenshot { get; private set; }

        public void Report(FlowStepProgress value)
        {
            if (!value.Done) { return; }
            lock (_gate)
            {
                Total++;
                if (!value.Ok) { Failed.Add($"{value.StepName}: {value.Detail}"); }
                if (!string.IsNullOrEmpty(value.ScreenshotBase64)) { LastScreenshot = value.ScreenshotBase64; }
            }
        }
    }
}
