namespace Ecorex.Application.Tenancy;

/// <summary>
/// Cliente HTTP de YCloud (BSP oficial de WhatsApp, api.ycloud.com v2). Cubre envio de
/// mensajes y gestion de plantillas HSM. La autenticacion es una sola API key que se entrega
/// por llamada (header X-API-Key) ya descifrada por el caller: el cliente NO conoce la entidad
/// WhatsAppLine ni el ISecretProtector. Los errores HTTP se devuelven como result, sin throw.
///
/// Referencia: https://docs.ycloud.com/reference (send: POST /whatsapp/messages,
/// plantillas: POST/GET /whatsapp/templates). Los nombres exactos de campos conviene
/// confirmarlos contra el OpenAPI v2 oficial; la implementacion es tolerante a variaciones.
/// </summary>
public interface IYCloudApiClient
{
    /// <summary>Valida la API key (y opcionalmente el numero) consultando los phone numbers de
    /// la cuenta. Devuelve el numero/WABA detectado o un error si la key es invalida.</summary>
    Task<YCloudCheckResult> CheckAsync(string apiKey, string? phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Envia texto. <paramref name="fromPhone"/> es el sender registrado en YCloud y
    /// <paramref name="toPhone"/> el destino, ambos en formato internacional sin "+".</summary>
    Task<YCloudSendResult> SendTextAsync(string apiKey, string fromPhone, string toPhone, string text, CancellationToken cancellationToken = default);

    /// <summary>Envia media (imagen/video/audio/documento) por URL publica.</summary>
    Task<YCloudSendResult> SendMediaAsync(string apiKey, string fromPhone, string toPhone, YCloudMediaKind kind, string mediaUrl, string? caption, string? fileName, CancellationToken cancellationToken = default);

    /// <summary>ADR-0092: envia una PLANTILLA aprobada (HSM) para el primer contacto en frio (ventana de 24h
    /// cerrada). <paramref name="bodyParams"/> son los valores de las variables {{1}}, {{2}}... del cuerpo, en
    /// orden (para 'preguntar_whatsapp' suele ser un solo valor = la pregunta). <paramref name="language"/> es
    /// el codigo de idioma de la plantilla aprobada (ej. "es").</summary>
    Task<YCloudSendResult> SendTemplateAsync(string apiKey, string fromPhone, string toPhone, string templateName, string language, IReadOnlyList<string> bodyParams, CancellationToken cancellationToken = default);

    /// <summary>ADR-0096: envia una REACCION (emoji) al mensaje entrante identificado por <paramref name="messageId"/>
    /// (el wamid del mensaje del cliente). Un <paramref name="emoji"/> vacio QUITA la reaccion. Paridad con Evolution.</summary>
    Task<YCloudSendResult> SendReactionAsync(string apiKey, string fromPhone, string toPhone, string messageId, string emoji, CancellationToken cancellationToken = default);

    // === Plantillas HSM =======================================================
    /// <summary>Crea/somete una plantilla a revision de Meta a traves de YCloud.
    /// <paramref name="components"/> es el arreglo de componentes ya armado por el servicio
    /// (HEADER/BODY/FOOTER/BUTTONS con sus example values), serializable a JSON.</summary>
    Task<YCloudTemplateResult> CreateTemplateAsync(string apiKey, string wabaId, string name, string language, string category, object components, int? ttlSeconds, CancellationToken cancellationToken = default);

    /// <summary>Lista las plantillas de una WABA con su estado actual (para sincronizar).</summary>
    Task<YCloudTemplateListResult> ListTemplatesAsync(string apiKey, string wabaId, CancellationToken cancellationToken = default);
}

public enum YCloudMediaKind { Image, Video, Audio, Document }

public sealed record YCloudCheckResult(bool IsValid, string? VerifiedPhone, string? WabaId, string? Error);

public sealed record YCloudSendResult(bool IsSuccess, string? MessageId, string? Error);

/// <summary>Resultado de crear/someter una plantilla. <paramref name="Status"/> suele venir
/// PENDING al crear (Meta la revisa); APPROVED inmediato solo para AUTHENTICATION.</summary>
public sealed record YCloudTemplateResult(bool IsSuccess, string? Id, string? Status, string? Error);

/// <summary>Una plantilla HSM tal como la devuelve YCloud (formato Meta). Ademas del estado, trae el
/// contenido para poder IMPORTARLA (ADR-0097 / traer plantillas). Las variables del cuerpo son
/// POSICIONALES ({{1}}, {{2}}...); <paramref name="VariableExamples"/> son sus valores de ejemplo en
/// orden. <paramref name="HeaderFormat"/> es "TEXT"|"IMAGE"|"VIDEO"|"DOCUMENT" (o null si no hay header).</summary>
public sealed record YCloudTemplateStatus(
    string Name, string? Language, string Status, string? Id, string? RejectedReason,
    string? Category = null,
    string? HeaderFormat = null,
    string? HeaderText = null,
    string? BodyText = null,
    string? FooterText = null,
    IReadOnlyList<string>? VariableExamples = null);

public sealed record YCloudTemplateListResult(bool IsSuccess, IReadOnlyList<YCloudTemplateStatus> Items, string? Error);
