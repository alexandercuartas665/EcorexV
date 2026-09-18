using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Notifications;

namespace Ecorex.Application.Workflows;

/// <summary>A quien se dirige una regla de notificacion de nodo. Para grupo/Telegram el destino va en la
/// propia regla (jid/chatId), asi que este campo solo aplica a Correo y WhatsApp (plantilla).</summary>
public enum NodeNotifyRecipient
{
    /// <summary>El usuario asignado al PASO (el que debe actuar). Si el paso quedo por cargo sin dueno unico, no hay a quien avisar.</summary>
    StepAssignee = 0,
    /// <summary>Un usuario del tenant elegido explicitamente (UsuarioId).</summary>
    Usuario = 1
}

/// <summary>
/// Una regla de notificacion de un nodo: cuando el paso LLEGA (se vuelve actual), avisa por un canal a un
/// destinatario, con un mensaje por plantilla (tokens {tarea.x} / {form.x}) y opcionalmente el enlace a la tarea.
/// </summary>
public sealed record NodeNotifyRule(
    NotifyChannel Canal = NotifyChannel.Correo,
    NodeNotifyRecipient Destino = NodeNotifyRecipient.StepAssignee,
    // Requerido si Destino = Usuario (Correo/WhatsApp).
    Guid? UsuarioId = null,
    // WhatsApp plantilla: linea YCloud desde la que se envia + nombre de la plantilla HSM + idioma.
    Guid? LineaId = null,
    string? Plantilla = null,
    string? Idioma = null,
    // Correo: asunto (tokens permitidos).
    string? Asunto = null,
    // WhatsAppGrupo: jid del grupo de Evolution ("...@g.us").
    Guid? LineaGrupoId = null,
    string? GrupoJid = null,
    // Telegram: chat_id destino (bot del tenant).
    string? ChatId = null,
    // Cuerpo del mensaje (tokens {tarea.x} / {form.x}). Aplica a Correo, Grupo y Telegram. En WhatsApp
    // plantilla NO se usa (el HSM es rigido; los tokens llenan sus variables por nombre).
    string? Mensaje = null,
    // Adjunta el enlace (deep-link) a la tarea al final del mensaje. No aplica a WhatsApp plantilla.
    bool IncluirEnlace = false,
    // WhatsApp plantilla: binding de CADA variable de la plantilla a una expresion con tokens
    // (variableDeLaPlantilla -> "texto con {tarea.x} / {form.x} / {sistema.fecha}"). Si una variable no tiene
    // binding, se conserva el llenado automatico por nombre (compatibilidad hacia atras). Null = sin bindings.
    IReadOnlyDictionary<string, string>? Variables = null);

/// <summary>Reglas de notificacion de un nodo. Se serializa a <see cref="Domain.Entities.WorkflowNode.NotifyJson"/>.</summary>
public sealed record NodeNotifyConfig(IReadOnlyList<NodeNotifyRule>? Reglas = null)
{
    public static readonly NodeNotifyConfig Empty = new(Array.Empty<NodeNotifyRule>());

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Parsea el JSON almacenado. Null/vacio/invalido => sin reglas.</summary>
    public static NodeNotifyConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Empty; }
        try
        {
            var cfg = JsonSerializer.Deserialize<NodeNotifyConfig>(json, JsonOpts);
            return cfg is null ? Empty : cfg with { Reglas = cfg.Reglas ?? Array.Empty<NodeNotifyRule>() };
        }
        catch { return Empty; }
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>true si no hay ninguna regla configurada.</summary>
    [JsonIgnore]
    public bool IsEmpty => Reglas is null || Reglas.Count == 0;
}
