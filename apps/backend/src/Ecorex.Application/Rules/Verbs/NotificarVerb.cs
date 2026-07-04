using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo NOTIFICAR: registra la INTENCION de notificacion como actividad de la tarea del
/// caso (o solo en el resultado si no hay tarea). El envio real de correo queda como TODO
/// de integracion (IEmailSender ya existe en Common); esta ola solo deja la trazabilidad,
/// ADR-0016.
/// </summary>
public sealed class NotificarVerb : IRuleVerb
{
    public const string VerbName = "NOTIFICAR";

    private readonly IApplicationDbContext _db;

    public NotificarVerb(IApplicationDbContext db)
    {
        _db = db;
    }

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Notificar",
        "Registra la intencion de notificacion en la actividad de la tarea del caso (el envio de correo real es una integracion futura).",
        [
            new RuleVerbParamDescriptor("message", "Mensaje", RuleParamType.Text, Required: true,
                "Texto de la notificacion."),
            new RuleVerbParamDescriptor("recipient", "Destinatario", RuleParamType.Text, Required: false,
                "Correo o nombre del destinatario (informativo)."),
            new RuleVerbParamDescriptor("autoComplete", "Completar paso", RuleParamType.Boolean, Required: false,
                "Si es true y la regla es autonoma de un nodo, el paso del flujo se completa solo.")
        ]);

    public Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var message = context.GetStringParam("message")?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult(RuleVerbResult.Fail("Parametro 'message' obligatorio."));
        }
        var recipient = context.GetStringParam("recipient")?.Trim();
        var text = string.IsNullOrWhiteSpace(recipient)
            ? $"notificacion (pendiente de envio): {message}"
            : $"notificacion (pendiente de envio) para {recipient}: {message}";

        // TODO(integracion): enviar el correo real via IEmailSender cuando el modulo de
        // notificaciones defina plantillas y destinatarios; esta ola solo deja constancia.
        var recorded = 0;
        if (context.TaskItemId is Guid taskItemId)
        {
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = context.TenantId,
                TaskItemId = taskItemId,
                Type = TaskActivityType.Action,
                ActorUserId = context.ActorUserId,
                ActorName = context.ActorName,
                Text = text.Length > 4000 ? text[..4000] : text
            });
            recorded = 1;
        }

        return Task.FromResult(RuleVerbResult.Ok(
            recorded == 1 ? "Notificacion registrada en la actividad de la tarea." : $"Notificacion registrada: {message}",
            recorded, autoCompleteStep: context.GetBoolParam("autoComplete")));
    }
}
