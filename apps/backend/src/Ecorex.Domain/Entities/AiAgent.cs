using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Agente de IA configurable del tenant (capa 3). Entidad TENANT-SCOPED. Define proveedor,
/// modelo, prompt de sistema y si esta en produccion. Los recursos (AiAgentResource) son los
/// archivos/datos que el agente puede usar para responder al cliente.
/// </summary>
public class AiAgent : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Rol/tipo descriptivo (copiloto, clasificador, seguimiento, etc.). Libre.</summary>
    public string? Role { get; set; }

    public AiProvider Provider { get; set; } = AiProvider.Claude;

    /// <summary>Modelo concreto del proveedor (opcional; si vacio se usa el por defecto).</summary>
    public string? Model { get; set; }

    public string SystemPrompt { get; set; } = "";

    /// <summary>En produccion (encendido) o apagado.</summary>
    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Herramientas (function calling / "MCP") DESHABILITADAS para este agente (jsonb, lista de nombres).
    /// Null o vacio = todas las herramientas registradas estan habilitadas (compatibilidad hacia atras).
    /// </summary>
    public string? DisabledToolsJson { get; set; }

    /// <summary>
    /// WHITELIST DURA de tableros que este agente puede usar al crear tareas (jsonb, arreglo de GUID =
    /// board ids). Null o vacio = SIN restriccion (todos los tableros no archivados del tenant), que
    /// preserva el comportamiento historico. Con 1+ ids, la herramienta 'crear_tarea' solo admite esos
    /// tableros y 'listar_tableros' solo los muestra.
    /// </summary>
    public string? AllowedBoardIdsJson { get; set; }

    /// <summary>
    /// Historial de versiones de los prompts (red de seguridad). Cada "Guardar cambios" guarda una
    /// instantanea {prompt base + prompts enrutados}, conservando las ultimas 5. Permite restaurar.
    /// Formato: arreglo JSON de { savedAt, basePrompt, prompts:[{ name, rule, body, sortOrder }] }.
    /// </summary>
    public string? PromptHistoryJson { get; set; }

    /// <summary>
    /// Reacciones automaticas (emoji) a los mensajes del cliente, SIN pasar por el LLM (cero
    /// tokens). Si esta activo, el dispatcher reacciona a ~N de cada M mensajes entrantes con
    /// un emoji tomado al azar de ReactionEmojis. Un mensaje que ya tiene reaccion no recibe
    /// otra. Portado desde CUBOT.redmanager.
    /// </summary>
    public bool ReactionsEnabled { get; set; }

    /// <summary>Numerador de la frecuencia (ej. 3 de cada 4 -> N=3). Se aplica como probabilidad.</summary>
    public int ReactionRatioN { get; set; } = 3;

    /// <summary>Denominador de la frecuencia (ej. 3 de cada 4 -> M=4).</summary>
    public int ReactionRatioM { get; set; } = 4;

    /// <summary>Emojis para reaccionar al azar, separados por coma. Configurable por UI.</summary>
    public string? ReactionEmojis { get; set; }

    /// <summary>
    /// Configuracion de CIERRE del agente (jsonb): que hacer cuando se cierra la atencion (por crear
    /// una actividad/lead, o por el marcador [[cierre]]). Guarda si debe OLVIDAR al cliente (reset de
    /// memoria no destructivo) y la lista de ALERTAS a disparar (WhatsApp/correo a un usuario). Null o
    /// vacio = sin acciones de cierre (compatibilidad hacia atras). Ver AgentCierreConfig.
    /// </summary>
    public string? CierreJson { get; set; }
}
