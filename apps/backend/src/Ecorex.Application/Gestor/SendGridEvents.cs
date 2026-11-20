using System.Text.Json;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor;

/// <summary>Un evento del Event Webhook de SendGrid, ya parseado (ADR-0188).</summary>
/// <param name="Event">processed, deferred, delivered, open, click, bounce, dropped, spamreport, unsubscribe,
/// group_unsubscribe, group_resubscribe (en minusculas).</param>
/// <param name="Email">Destinatario normalizado (trim + minusculas).</param>
/// <param name="AtUtc">Instante del evento segun SendGrid.</param>
/// <param name="TenantId">Argumento ecx_tenant que pusimos al enviar (null si el correo no lo llevaba).</param>
/// <param name="RunId">Argumento ecx_run: la ejecucion del motor de acciones (null si no era del motor).</param>
/// <param name="MessageId">Prefijo de sg_message_id (= el "queued as" que dio el SMTP al aceptar).</param>
/// <param name="Reason">Motivo (bounce/dropped) o respuesta del servidor destino (deferred).</param>
/// <param name="BounceType">Para bounce: "bounce" (rebote) o "blocked" (bloqueo, temporal).</param>
public sealed record SendGridEvent(
    string Event,
    string? Email,
    DateTimeOffset AtUtc,
    Guid? TenantId,
    Guid? RunId,
    string? MessageId,
    string? Reason,
    string? BounceType);

/// <summary>Supresion (o reactivacion) de un correo que deriva de un evento.</summary>
/// <param name="Remove">true = el contacto se re-suscribio: quitar la baja de origen <see cref="SendGridEvents.OrigenProveedor"/>.</param>
public sealed record EmailSuppression(string Email, string Origen, string? Motivo, bool Remove = false);

/// <summary>
/// Logica PURA del Event Webhook de SendGrid (ADR-0188): parseo tolerante del lote, mapeo evento -> estado
/// de entrega con precedencia, y que eventos suprimen el correo para el motor de acciones. Sin BD: lo usa
/// <see cref="EmailDeliveryEventService"/> y se prueba aislado.
/// </summary>
public static class SendGridEvents
{
    /// <summary>Argumento que lleva el tenant en los eventos (unique_args de X-SMTPAPI).</summary>
    public const string TenantArg = "ecx_tenant";

    /// <summary>Argumento que lleva el id de la ejecucion del motor de acciones.</summary>
    public const string RunArg = "ecx_run";

    /// <summary>Origen de una baja por rebote definitivo o direccion invalida.</summary>
    public const string OrigenRebote = "rebote";

    /// <summary>Origen de una baja porque el contacto marco el correo como spam.</summary>
    public const string OrigenSpam = "spam";

    /// <summary>Origen de una baja hecha desde el enlace de baja del PROVEEDOR (no el nuestro).</summary>
    public const string OrigenProveedor = "proveedor";

    /// <summary>Tope de eventos por lote (SendGrid manda lotes de ~1000; mas es anomalo).</summary>
    public const int MaxEventsPerBatch = 5000;

    private const int DetailMax = 300;

    /// <summary>
    /// Parsea el cuerpo del webhook (arreglo JSON de eventos). Tolerante: ignora elementos sin "event" y
    /// campos con tipo inesperado; JSON invalido = lista vacia.
    /// </summary>
    public static IReadOnlyList<SendGridEvent> Parse(string? json)
    {
        var list = new List<SendGridEvent>();
        if (string.IsNullOrWhiteSpace(json)) { return list; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return list; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (list.Count >= MaxEventsPerBatch) { break; }
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var evt = Str(el, "event")?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(evt)) { continue; }

                var ts = Num(el, "timestamp");
                var at = ts is long secs && secs > 0 ? DateTimeOffset.FromUnixTimeSeconds(secs) : DateTimeOffset.UtcNow;
                var email = Str(el, "email");
                var reason = evt == "deferred" ? Str(el, "response") ?? Str(el, "reason") : Str(el, "reason") ?? Str(el, "response");

                list.Add(new SendGridEvent(
                    evt,
                    string.IsNullOrWhiteSpace(email) ? null : EmailUnsubscribeService.Normalize(email),
                    at,
                    Guid.TryParse(Str(el, TenantArg), out var t) ? t : null,
                    Guid.TryParse(Str(el, RunArg), out var r) ? r : null,
                    MessagePrefix(Str(el, "sg_message_id")),
                    string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                    Str(el, "type")?.Trim().ToLowerInvariant()));
            }
        }
        catch (JsonException)
        {
            // Cuerpo invalido: nada que aplicar.
        }
        return list;
    }

    /// <summary>
    /// sg_message_id = "{id del SMTP}.{filtro}.{n}..." ; el prefijo antes del primer punto es el id que el SMTP
    /// devolvio al aceptar ("queued as X"), que guardamos en ExternalRef de la ejecucion.
    /// </summary>
    public static string? MessagePrefix(string? sgMessageId)
    {
        if (string.IsNullOrWhiteSpace(sgMessageId)) { return null; }
        var v = sgMessageId.Trim();
        var dot = v.IndexOf('.');
        return dot > 0 ? v[..dot] : v;
    }

    /// <summary>Estado de entrega que representa el evento (None = evento que no cambia el estado).</summary>
    public static EmailDeliveryStatus StatusFor(SendGridEvent e) => e.Event switch
    {
        "processed" => EmailDeliveryStatus.Processed,
        "deferred" => EmailDeliveryStatus.Deferred,
        "delivered" => EmailDeliveryStatus.Delivered,
        "open" => EmailDeliveryStatus.Opened,
        "click" => EmailDeliveryStatus.Clicked,
        "unsubscribe" or "group_unsubscribe" => EmailDeliveryStatus.Unsubscribed,
        "spamreport" => EmailDeliveryStatus.SpamReport,
        "bounce" => EmailDeliveryStatus.Bounced,
        "dropped" => EmailDeliveryStatus.Dropped,
        _ => EmailDeliveryStatus.None
    };

    /// <summary>
    /// Aplica el evento a la ejecucion. El estado solo SUBE (precedencia del enum: los eventos llegan
    /// desordenados); la primera apertura y el primer clic se guardan aparte. Devuelve true si cambio algo.
    /// </summary>
    public static bool ApplyToRun(ContactWorkflowRun run, SendGridEvent e)
    {
        var target = StatusFor(e);
        if (target == EmailDeliveryStatus.None) { return false; }
        var changed = false;

        if (target is EmailDeliveryStatus.Opened or EmailDeliveryStatus.Clicked && run.OpenedAtUtc is null)
        {
            // Un clic implica apertura aunque el pixel de apertura este bloqueado.
            run.OpenedAtUtc = e.AtUtc;
            changed = true;
        }
        if (target == EmailDeliveryStatus.Clicked && run.ClickedAtUtc is null)
        {
            run.ClickedAtUtc = e.AtUtc;
            changed = true;
        }
        if ((int)target > (int)run.DeliveryStatus)
        {
            run.DeliveryStatus = target;
            run.DeliveryUpdatedAtUtc = e.AtUtc;
            run.DeliveryDetail = DetailFor(e);
            changed = true;
        }
        return changed;
    }

    /// <summary>Texto del detalle para la ejecucion: motivo del rebote/descarte/aplazamiento.</summary>
    public static string? DetailFor(SendGridEvent e)
    {
        string? text = e.Event switch
        {
            "bounce" when e.BounceType == "blocked" => "Bloqueado por el servidor destino" + Suffix(e.Reason),
            "bounce" => "Rebote" + Suffix(e.Reason),
            "dropped" => "No enviado por SendGrid" + Suffix(e.Reason),
            "deferred" => "Aplazado" + Suffix(e.Reason),
            _ => null
        };
        if (text is null) { return null; }
        return text.Length <= DetailMax ? text : text[..DetailMax];
    }

    private static string Suffix(string? reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : ": " + reason.Trim();

    /// <summary>
    /// Que hace el evento con la lista de bajas del motor: rebote definitivo, spam y baja del proveedor
    /// SUPRIMEN el correo; un "dropped" solo si SendGrid lo descarto por una supresion o direccion invalida;
    /// un bloqueo ("blocked") es temporal y NO suprime; group_resubscribe reactiva. Null = no toca la lista.
    /// </summary>
    public static EmailSuppression? SuppressionFor(SendGridEvent e)
    {
        if (string.IsNullOrWhiteSpace(e.Email)) { return null; }
        var reason = Trim500(e.Reason);
        switch (e.Event)
        {
            case "bounce" when e.BounceType != "blocked":
                return new EmailSuppression(e.Email, OrigenRebote, reason);
            case "spamreport":
                return new EmailSuppression(e.Email, OrigenSpam, "Marco el correo como spam");
            case "unsubscribe":
            case "group_unsubscribe":
                return new EmailSuppression(e.Email, OrigenProveedor, "Baja desde el enlace de SendGrid");
            case "group_resubscribe":
                return new EmailSuppression(e.Email, OrigenProveedor, null, Remove: true);
            case "dropped":
                var r = (e.Reason ?? string.Empty).Trim();
                // "Invalid" a secas = direccion invalida; "Invalid SMTPAPI header" es error NUESTRO, no del contacto.
                if (r.Contains("Bounced Address", StringComparison.OrdinalIgnoreCase)
                    || r.Equals("Invalid", StringComparison.OrdinalIgnoreCase)
                    || r.Contains("Invalid Address", StringComparison.OrdinalIgnoreCase))
                {
                    return new EmailSuppression(e.Email, OrigenRebote, reason);
                }
                if (r.Contains("Spam Reporting", StringComparison.OrdinalIgnoreCase))
                {
                    return new EmailSuppression(e.Email, OrigenSpam, reason);
                }
                if (r.Contains("Unsubscribed", StringComparison.OrdinalIgnoreCase))
                {
                    return new EmailSuppression(e.Email, OrigenProveedor, reason);
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>Texto para el motor cuando un correo esta suprimido, segun el origen de la baja.</summary>
    public static string SkipReasonFor(string? origen) => origen switch
    {
        OrigenRebote => "Correo suprimido: reboto (direccion invalida o inexistente).",
        OrigenSpam => "Correo suprimido: el contacto marco un correo como spam.",
        OrigenProveedor => "El contacto se dio de baja desde el enlace de SendGrid.",
        _ => "El contacto se dio de baja de los correos."
    };

    /// <summary>Etiqueta legible del origen de una baja (lista de bajas de Plantillas de correo).</summary>
    public static string OrigenLabel(string? origen) => origen switch
    {
        "enlace" => "Enlace de baja",
        "manual" => "Manual",
        OrigenRebote => "Rebote",
        OrigenSpam => "Spam",
        OrigenProveedor => "Baja en SendGrid",
        _ => origen ?? string.Empty
    };

    private static string? Trim500(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) { return null; }
        var t = v.Trim();
        return t.Length <= 500 ? t : t[..500];
    }

    private static string? Str(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) { return null; }
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }

    private static long? Num(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) { return null; }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) { return n; }
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) { return s; }
        return null;
    }
}
