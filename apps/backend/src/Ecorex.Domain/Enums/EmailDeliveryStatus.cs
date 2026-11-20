namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado REAL de entrega de un correo enviado por el motor de acciones (ADR-0188), segun los eventos
/// que reporta el proveedor (SendGrid Event Webhook). Distinto de <see cref="ContactWorkflowRunStatus"/>:
/// Sent solo dice que el servidor SMTP acepto el correo; esto dice que paso despues.
///
/// El orden numerico es la PRECEDENCIA: un evento solo sube el estado (los webhooks llegan desordenados y
/// un "processed" tardio no debe tapar un "delivered"). Se persiste como string.
/// </summary>
public enum EmailDeliveryStatus
{
    /// <summary>Sin eventos del proveedor (sin webhook configurado o aun no llegan).</summary>
    None = 0,

    /// <summary>El proveedor lo recibio y lo puso en cola.</summary>
    Processed = 1,

    /// <summary>El servidor destino lo aplazo temporalmente; el proveedor reintenta.</summary>
    Deferred = 2,

    /// <summary>Entregado al buzon del destinatario.</summary>
    Delivered = 3,

    /// <summary>El destinatario lo abrio (requiere Open Tracking en el proveedor).</summary>
    Opened = 4,

    /// <summary>El destinatario hizo clic en un enlace (requiere Click Tracking).</summary>
    Clicked = 5,

    /// <summary>El destinatario se dio de baja desde el enlace del proveedor.</summary>
    Unsubscribed = 6,

    /// <summary>El destinatario lo marco como spam: queda suprimido.</summary>
    SpamReport = 7,

    /// <summary>El servidor destino lo rechazo (bloqueo temporal o rebote); ver detalle.</summary>
    Bounced = 8,

    /// <summary>El proveedor no lo envio (direccion suprimida o invalida); ver detalle.</summary>
    Dropped = 9
}
