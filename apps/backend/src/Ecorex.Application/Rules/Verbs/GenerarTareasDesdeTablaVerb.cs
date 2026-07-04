using System.Text.Json;
using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo GENERAR_TAREAS_DESDE_TABLA (port del legacy): crea TaskItems via ITaskItemService
/// desde las filas de un campo tabla del FormData (arreglo JSON) o desde filas fijas en
/// params ("rows"). Cada fila aporta el titulo (clave configurable, default "title") y
/// opcionalmente "description". RecordsAffected = tareas creadas.
/// </summary>
public sealed class GenerarTareasDesdeTablaVerb : IRuleVerb
{
    public const string VerbName = "GENERAR_TAREAS_DESDE_TABLA";

    private readonly ITaskItemService _tasks;

    public GenerarTareasDesdeTablaVerb(ITaskItemService tasks)
    {
        _tasks = tasks;
    }

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Generar tareas desde tabla",
        "Crea una tarea del nucleo por cada fila de un campo tabla del formulario (o de filas fijas en params).",
        [
            new RuleVerbParamDescriptor("activityTypeId", "Tipo de actividad (Id)", RuleParamType.Text, Required: true,
                "Guid del ActivityType con el que se crean las tareas."),
            new RuleVerbParamDescriptor("tableField", "Campo tabla", RuleParamType.FieldCode, Required: false,
                "FieldCode cuyo valor es un arreglo JSON de filas. Si falta, se usan las filas de 'rows'."),
            new RuleVerbParamDescriptor("titleKey", "Clave del titulo", RuleParamType.Text, Required: false,
                "Propiedad de cada fila que aporta el titulo (default: title)."),
            new RuleVerbParamDescriptor("titlePrefix", "Prefijo del titulo", RuleParamType.Text, Required: false,
                "Texto antepuesto al titulo de cada tarea creada."),
            new RuleVerbParamDescriptor("rows", "Filas fijas (JSON)", RuleParamType.Json, Required: false,
                "Arreglo JSON de filas usado cuando no hay campo tabla. Ej: [{\"title\":\"Preparar entrega\"}]"),
            new RuleVerbParamDescriptor("autoComplete", "Completar paso", RuleParamType.Boolean, Required: false,
                "Si es true y la regla es autonoma de un nodo, el paso del flujo se completa solo.")
        ]);

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var activityTypeRaw = context.GetStringParam("activityTypeId");
        if (!Guid.TryParse(activityTypeRaw, out var activityTypeId))
        {
            return RuleVerbResult.Fail("Parametro 'activityTypeId' obligatorio (Guid de un ActivityType).");
        }

        var rows = ResolveRows(context, out var rowsError);
        if (rowsError is not null)
        {
            return RuleVerbResult.Fail(rowsError);
        }
        if (rows.Count == 0)
        {
            return RuleVerbResult.Ok("Sin filas: no se crearon tareas.", 0,
                autoCompleteStep: context.GetBoolParam("autoComplete"));
        }

        var titleKey = context.GetStringParam("titleKey") ?? "title";
        var titlePrefix = context.GetStringParam("titlePrefix") ?? "";
        var created = 0;
        foreach (var row in rows)
        {
            var (title, description) = ReadRow(row, titleKey);
            if (string.IsNullOrWhiteSpace(title))
            {
                return RuleVerbResult.Fail(
                    $"Una fila no tiene titulo (clave '{titleKey}'). Se crearon {created} tarea(s) antes del error.");
            }
            var fullTitle = (titlePrefix + title).Trim();
            fullTitle = fullTitle.Length > 200 ? fullTitle[..200] : fullTitle;
            var result = await _tasks.CreateAsync(
                new CreateTaskItemRequest(fullTitle, activityTypeId, description),
                context.ActorUserId ?? Guid.Empty, context.ActorName, cancellationToken);
            if (!result.IsOk)
            {
                return new RuleVerbResult(false,
                    $"No se pudo crear la tarea '{fullTitle}': {result.Error}", created);
            }
            created++;
        }

        return RuleVerbResult.Ok($"{created} tarea(s) creada(s).", created,
            autoCompleteStep: context.GetBoolParam("autoComplete"));
    }

    /// <summary>Filas desde el campo tabla del FormData o, en su defecto, desde params.rows.</summary>
    private static List<JsonElement> ResolveRows(RuleContext context, out string? error)
    {
        error = null;
        var tableField = context.GetStringParam("tableField");
        if (!string.IsNullOrWhiteSpace(tableField))
        {
            var raw = context.FormData.GetValueOrDefault(tableField!);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    error = $"El campo tabla '{tableField}' no contiene un arreglo JSON.";
                    return [];
                }
                return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
            }
            catch (JsonException)
            {
                error = $"El campo tabla '{tableField}' no contiene JSON valido.";
                return [];
            }
        }

        if (context.Params.TryGetValue("rows", out var rowsElement))
        {
            if (rowsElement.ValueKind != JsonValueKind.Array)
            {
                error = "El parametro 'rows' debe ser un arreglo JSON.";
                return [];
            }
            return rowsElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }

        error = "Configura 'tableField' (campo tabla del formulario) o 'rows' (filas fijas).";
        return [];
    }

    private static (string? Title, string? Description) ReadRow(JsonElement row, string titleKey)
    {
        if (row.ValueKind == JsonValueKind.String)
        {
            return (row.GetString(), null);
        }
        if (row.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }
        string? title = null;
        string? description = null;
        if (row.TryGetProperty(titleKey, out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
        {
            title = titleElement.GetString();
        }
        if (row.TryGetProperty("description", out var descElement) && descElement.ValueKind == JsonValueKind.String)
        {
            description = descElement.GetString();
        }
        return (title, description);
    }
}
