using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Tenancy;

/// <summary>Canal por el que se envia una alerta de cierre. Ola 1: WhatsApp (plantilla HSM a un usuario) y
/// Correo. Ola 2: WhatsAppGrupo (texto a un grupo de Evolution). Telegram llega en una ola posterior.</summary>
public enum CierreCanal
{
    WhatsApp = 0,
    Correo = 1,
    /// <summary>Ola 2: texto plano a un GRUPO de WhatsApp (Evolution), via el jid "...@g.us".</summary>
    WhatsAppGrupo = 2,
    /// <summary>Ola 3: mensaje a un chat/grupo de TELEGRAM (bot del tenant), via chat_id.</summary>
    Telegram = 3
}

/// <summary>A quien se le envia la alerta de cierre.</summary>
public enum CierreDestino
{
    /// <summary>Usuario asignado a la LINEA de la conversacion (el "comercial" que la atiende).</summary>
    Asignado = 0,
    /// <summary>Un usuario del tenant elegido explicitamente (UsuarioId).</summary>
    Usuario = 1
}

/// <summary>
/// Una alerta a disparar al cerrar la atencion. Todo se resuelve en el servidor de forma determinista
/// (sin herramienta/MCP): destinatario = usuario asignado de la linea o un usuario elegido; el canal
/// decide como se envia.
/// </summary>
public sealed record AgentCierreAlerta(
    CierreCanal Canal = CierreCanal.Correo,
    CierreDestino Destino = CierreDestino.Asignado,
    // Requerido si Destino = Usuario: el TenantUser.Id destino.
    Guid? UsuarioId = null,
    // --- WhatsApp (plantilla HSM) / WhatsAppGrupo ---
    // Linea DESDE la que se envia (YCloud para plantilla; Evolution para grupo). Null = la de la conversacion.
    Guid? LineaId = null,
    string? Plantilla = null,
    string? Idioma = null,
    // --- Correo ---
    string? Asunto = null,
    // --- WhatsAppGrupo (Ola 2) ---
    // Jid del grupo de Evolution ("...@g.us") al que se envia el texto del cierre.
    string? GrupoJid = null,
    // --- Telegram (Ola 3) ---
    // chat_id de Telegram (un usuario que le escribio al bot, o un grupo con el bot dentro).
    string? ChatId = null);

/// <summary>
/// Configuracion de CIERRE del agente. Se serializa a <see cref="Domain.Entities.AiAgent.CierreJson"/>.
/// </summary>
public sealed record AgentCierreConfig(
    // Al cerrar, "olvidar" al cliente: reinicio de contexto NO destructivo (el agente saluda desde cero,
    // el historial sigue visible para humanos).
    bool Olvidar = false,
    IReadOnlyList<AgentCierreAlerta>? Alertas = null)
{
    public static readonly AgentCierreConfig Empty = new(false, Array.Empty<AgentCierreAlerta>());

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Parsea el JSON almacenado. Null/vacio/invalido => configuracion vacia (sin acciones).</summary>
    public static AgentCierreConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Empty; }
        try
        {
            var cfg = JsonSerializer.Deserialize<AgentCierreConfig>(json, JsonOpts);
            return cfg is null ? Empty : cfg with { Alertas = cfg.Alertas ?? Array.Empty<AgentCierreAlerta>() };
        }
        catch { return Empty; }
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>true si no hay nada que hacer al cerrar (ni olvidar ni alertas).</summary>
    [JsonIgnore]
    public bool IsNoop => !Olvidar && (Alertas is null || Alertas.Count == 0);
}
