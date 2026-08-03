namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de accion de un paso del disenador de acciones por filtro de contactos (ADR-0056, port del
/// ucWorkflowDesigner legacy). Las 5 acciones de la paleta. Se persiste como string (seguro entre
/// motores al agregar valores al final). La EJECUCION de cada tipo es Fase 2 (motor + scheduler).
/// </summary>
public enum ContactWorkflowStepType
{
    /// <summary>Conectar / Enlace: paso "no-envio" (marca al contacto como enlazado). Sin params.</summary>
    Conectar = 0,

    /// <summary>Mensaje Red / Notificacion: responde por el dispatcher del agente/chat. Sin params en Fase 1.</summary>
    MensajeRed = 1,

    /// <summary>WhatsApp / Directo: envia por el conector de WhatsApp. Sin params propios en Fase 1.</summary>
    WhatsApp = 2,

    /// <summary>E-mail / SMTP: envia por el remitente de correo. Sin params propios en Fase 1.</summary>
    Email = 3,

    /// <summary>Llamada CRM / Gestion: crea una gestion CRM. UNICO tipo con campos (ParamsJson).</summary>
    Llamada = 4
}
