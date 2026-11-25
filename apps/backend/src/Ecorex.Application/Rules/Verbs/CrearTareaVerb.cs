using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo CREAR_TAREA: crea UNA tarea del nucleo clasificada por CONCEPTO (subcategoria) a partir de los datos del
/// formulario. Con SubcategoriaId y sin BoardId, CreateTaskItemRequest deriva el tablero (y su primera columna) del
/// concepto, asi la tarea cae en el mismo tablero/categoria que las demas de ese concepto. Pensado para formularios
/// publicos (/f/{token}, p. ej. registro de leads): el titulo y la descripcion se arman con tokens {campo} del
/// FormData, el asignado sale de params (en un form anonimo no hay actor) y 'marcaCreadaField' evita duplicados.
/// </summary>
public sealed partial class CrearTareaVerb : IRuleVerb
{
    public const string VerbName = "CREAR_TAREA";

    private readonly ITaskItemService _tasks;

    public CrearTareaVerb(ITaskItemService tasks)
    {
        _tasks = tasks;
    }

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Crear tarea",
        "Crea una tarea del nucleo clasificada por concepto (subcategoria) con los datos del formulario.",
        [
            new RuleVerbParamDescriptor("subcategoriaId", "Concepto / subcategoria (Id)", RuleParamType.Text, Required: true,
                "Guid de la subcategoria. La tarea toma el tablero del concepto (primera columna)."),
            new RuleVerbParamDescriptor("activityTypeId", "Tipo de actividad (Id)", RuleParamType.Text, Required: false,
                "Guid de un ActivityType. Respaldo cuando no hay subcategoria."),
            new RuleVerbParamDescriptor("titleTemplate", "Plantilla del titulo", RuleParamType.Text, Required: false,
                "Texto con tokens {campo} del formulario. Ej: Lead - {nombre} - {empresa}"),
            new RuleVerbParamDescriptor("titleField", "Campo del titulo", RuleParamType.FieldCode, Required: false,
                "Campo cuyo valor es el titulo (si no hay plantilla)."),
            new RuleVerbParamDescriptor("descriptionTemplate", "Plantilla de la descripcion", RuleParamType.Text, Required: false,
                "Texto con tokens {campo} del formulario."),
            new RuleVerbParamDescriptor("assigneeUserId", "Asignar a (TenantUser Id)", RuleParamType.Text, Required: false,
                "Guid del TenantUser asignado. Necesario en formularios anonimos, donde no hay usuario actor."),
            new RuleVerbParamDescriptor("requesterNameField", "Campo nombre del solicitante", RuleParamType.FieldCode, Required: false,
                "Campo que aporta el nombre del solicitante de la tarea."),
            new RuleVerbParamDescriptor("requesterEmailField", "Campo correo del solicitante", RuleParamType.FieldCode, Required: false,
                "Campo que aporta el correo del solicitante."),
            new RuleVerbParamDescriptor("requesterPhoneField", "Campo telefono del solicitante", RuleParamType.FieldCode, Required: false,
                "Campo que aporta el telefono del solicitante."),
            new RuleVerbParamDescriptor("marcaCreadaField", "Campo marca de tarea creada", RuleParamType.FieldCode, Required: false,
                "Anti-duplicado: si el campo ya tiene valor no se crea otra tarea; tras crear se escribe ahi el numero."),
            new RuleVerbParamDescriptor("autoComplete", "Completar paso", RuleParamType.Boolean, Required: false,
                "Si es true y la regla es autonoma de un nodo, el paso del flujo se completa solo.")
        ]);

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var autoComplete = context.GetBoolParam("autoComplete");
        var marcaField = Blank(context.GetStringParam("marcaCreadaField"));
        if (marcaField is not null && !string.IsNullOrWhiteSpace(context.FormData.GetValueOrDefault(marcaField)))
        {
            return RuleVerbResult.Ok(
                $"La tarea ya fue creada ({context.FormData[marcaField]}): no se crea otra.", 0, autoCompleteStep: autoComplete);
        }

        Guid? subId = Guid.TryParse(context.GetStringParam("subcategoriaId"), out var s) ? s : null;
        Guid? actType = Guid.TryParse(context.GetStringParam("activityTypeId"), out var a) ? a : null;
        if (subId is null && actType is null)
        {
            return RuleVerbResult.Fail("Configura 'subcategoriaId' (o 'activityTypeId') con un Guid valido.");
        }

        var title = Blank(Render(context.GetStringParam("titleTemplate"), context.FormData));
        if (title is null && Blank(context.GetStringParam("titleField")) is string titleField)
        {
            title = Blank(context.FormData.GetValueOrDefault(titleField));
        }
        if (title is null)
        {
            return RuleVerbResult.Fail("El titulo de la tarea quedo vacio: configura 'titleTemplate' o 'titleField'.");
        }
        title = SurrogateText.SafeTruncate(title, 200)!;

        var description = Blank(Render(context.GetStringParam("descriptionTemplate"), context.FormData));
        Guid? assignee = Guid.TryParse(context.GetStringParam("assigneeUserId"), out var asg) ? asg : null;

        var result = await _tasks.CreateAsync(
            new CreateTaskItemRequest(title, actType, description,
                AssigneeTenantUserId: assignee,
                RequesterName: FieldValue(context, "requesterNameField"),
                RequesterEmail: FieldValue(context, "requesterEmailField"),
                RequesterPhone: FieldValue(context, "requesterPhoneField"),
                SubcategoriaId: subId,
                SourceTaskId: context.TaskItemId),
            context.ActorUserId ?? Guid.Empty, context.ActorName, cancellationToken);
        if (!result.IsOk || result.Value is null)
        {
            return RuleVerbResult.Fail($"No se pudo crear la tarea '{title}': {result.Error}");
        }

        var number = result.Value.Item.Number;
        IReadOnlyList<RuleAction>? actions = null;
        if (marcaField is not null)
        {
            // Mismo efecto que PASAR_CAMPOS: el motor propaga el valor al FormData y el renderer lo pinta/guarda.
            context.FormData[marcaField] = number;
            actions = [RuleAction.SetValue(marcaField, number)];
        }
        return RuleVerbResult.Ok($"Tarea {number} creada.", 1, actions, autoComplete);
    }

    /// <summary>Sustituye cada {campo} por su valor del FormData (vacio si no existe). Null si no hay plantilla.</summary>
    internal static string? Render(string? template, IReadOnlyDictionary<string, string?> data)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }
        var text = TokenRegex().Replace(template, m => data.GetValueOrDefault(m.Groups[1].Value.Trim())?.Trim() ?? string.Empty);
        // Plantillas tipo "Lead - {nombre} - {empresa}" con un token vacio no deben dejar separadores colgando.
        return SpacesRegex().Replace(text, " ").Trim().Trim('-', ' ').Trim();
    }

    private static string? FieldValue(RuleContext context, string param)
        => Blank(context.GetStringParam(param)) is string field ? Blank(context.FormData.GetValueOrDefault(field)) : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex SpacesRegex();
}
