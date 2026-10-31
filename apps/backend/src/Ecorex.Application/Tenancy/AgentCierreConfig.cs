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
    Usuario = 1,
    /// <summary>Usuario asignado a la TAREA (AssigneeTenantUserId). Solo aplica a los AVISOS de tarea
    /// asignada (<see cref="AgentCierreConfig.AvisosTarea"/>), que SI conocen la tarea; en el cierre de
    /// conversacion no hay tarea en contexto, asi que ahi se ignora.</summary>
    UsuarioTarea = 2
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
    string? ChatId = null,
    // --- Binding de variables de la plantilla HSM (mismo patron que NodeNotifyRule.Variables) ---
    // Mapa VARIABLE de la plantilla (su "token", p.ej. "1" o "nombre") -> EXPRESION de token a resolver
    // (p.ej. "{tarea.numero}", "{usuario.nombre}"). Las variables SIN entrada conservan el llenado
    // automatico por nombre (compat. hacia atras). Se usa sobre todo en los avisos de tarea asignada,
    // cuyas plantillas suelen pedir variables posicionales (1/2/3) que el llenado por nombre no cubre.
    IReadOnlyDictionary<string, string>? Variables = null);

/// <summary>
/// Configuracion de CIERRE del agente. Se serializa a <see cref="Domain.Entities.AiAgent.CierreJson"/>.
/// </summary>
public sealed record AgentCierreConfig(
    // Al cerrar, "olvidar" al cliente: reinicio de contexto NO destructivo (el agente saluda desde cero,
    // el historial sigue visible para humanos).
    bool Olvidar = false,
    IReadOnlyList<AgentCierreAlerta>? Alertas = null,
    // AVISOS de "tarea asignada": se disparan cuando el agente CREA Y ASIGNA una actividad/tarea
    // (crear_actividad / crear_tarea), NO al cerrar la conversacion. Conocen la tarea y su asignado, asi
    // que su destino natural es CierreDestino.UsuarioTarea y sus plantillas llevan tokens de {tarea.*} /
    // {usuario.*}. Viven en el MISMO CierreJson (sin migracion), pero con disparador y contexto propios.
    IReadOnlyList<AgentCierreAlerta>? AvisosTarea = null)
{
    public static readonly AgentCierreConfig Empty = new(false, Array.Empty<AgentCierreAlerta>(), Array.Empty<AgentCierreAlerta>());

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
            return cfg is null ? Empty : cfg with
            {
                Alertas = cfg.Alertas ?? Array.Empty<AgentCierreAlerta>(),
                AvisosTarea = cfg.AvisosTarea ?? Array.Empty<AgentCierreAlerta>()
            };
        }
        catch { return Empty; }
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>true si no hay nada que hacer: ni olvidar, ni alertas de cierre, ni avisos de tarea.</summary>
    [JsonIgnore]
    public bool IsNoop => !Olvidar
        && (Alertas is null || Alertas.Count == 0)
        && (AvisosTarea is null || AvisosTarea.Count == 0);
}
