using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

public sealed record ConversationDto(
    Guid Id,
    string ContactPhone,
    string? ContactName,
    Guid? LeadId,
    DateTimeOffset? LastMessageAt,
    Guid? WhatsAppLineId = null);

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    MessageDirection Direction,
    string Body,
    string MessageType,
    DateTimeOffset SentAt,
    MessageMediaType MediaType = MessageMediaType.None,
    string? MediaUrl = null,
    string? MediaMimeType = null,
    string? SentByName = null,
    string? Reaction = null);

/// <summary>Payload normalizado del webhook entrante (lo produce el Evolution Connector).</summary>
/// <param name="WhatsAppLineId">Linea que recibio el mensaje (se deduce del nombre de instancia). Necesaria para
/// separar conversaciones por (linea, contacto) y para que el agente de IA responda por la linea correcta.</param>
public sealed record IngestMessageRequest(
    string ContactPhone,
    string? ContactName,
    string ExternalMessageId,
    string Body,
    string? MessageType = null,
    DateTimeOffset? SentAt = null,
    Guid? WhatsAppLineId = null,
    MessageMediaType MediaType = MessageMediaType.None,
    string? MediaUrl = null,
    string? MediaMimeType = null);

public sealed record SendMessageRequest(string Body);

/// <summary>Resultado de enviar un mensaje por una linea WhatsApp (Evolution real).</summary>
public sealed record ChatSendResult(bool Ok, MessageDto? Message, string? Error);

/// <summary>Resultado de vaciar el historial: cuantas conversaciones y mensajes se borraron.</summary>
public sealed record ChatClearResult(int Conversations, int Messages);

/// <summary>Estado "sin responder" de una conversacion: mensajes entrantes tras la ultima respuesta y desde cuando espera.</summary>
public sealed record LeadChatStateDto(int UnansweredCount, DateTimeOffset WaitingSince);
