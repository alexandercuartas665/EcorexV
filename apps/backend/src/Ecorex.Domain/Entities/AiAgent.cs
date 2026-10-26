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

    /// <summary>
    /// Configuracion de REACTIVACION / seguimiento del agente (jsonb): revive contactos dormidos (dejaron de
    /// responder sin cerrar). Guarda si esta habilitada y una lista de PASOS: tras X horas de inactividad
    /// (desde el ultimo mensaje del cliente) se envia un mensaje; dentro de la ventana de 24h de Meta se usa
    /// texto libre, fuera de ella una plantilla aprobada (HSM). Null o vacio = sin reactivacion. Ver
    /// AgentReactivacionConfig.
    /// </summary>
    public string? ReactivacionJson { get; set; }

    /// <summary>
    /// ASIGNACION ROUND-ROBIN del agente (jsonb): lista ORDENADA de TenantUser ids a los que el agente reparte
    /// las tareas/actividades que crea (crear_tarea / crear_actividad), rotando una ronda por cada registro
    /// (A, B, A, B...). Null o vacio = sin lista -> se cae al pool de asesores del tenant marcados
    /// AssignableByAgent (comportamiento anterior). Ver AgentAssigneePicker.
    /// </summary>
    public string? AssigneeUserIdsJson { get; set; }

    /// <summary>Cursor de rotacion del reparto round-robin (<see cref="AssigneeUserIdsJson"/>): indice del
    /// PROXIMO usuario de la lista a asignar. Avanza (modulo el tamano de la lista) en cada asignacion.</summary>
    public int AssigneeCursor { get; set; }

    /// <summary>
    /// CONCEPTO (ActividadSubcategoria 000270) POR DEFECTO que el agente adjunta a las tareas que crea con
    /// crear_tarea. FK logica a actividad_subcategorias (SET NULL al borrar; no cascade). Nullable =
    /// retrocompatibilidad: agente sin default se comporta EXACTO como hoy (tarea sin concepto, sin checklist).
    /// Con un concepto fijado (y valido, mismo tenant, no archivado), CreateAsync siembra el checklist del
    /// concepto (ActividadSubcategoria.Chequeo). El tablero sigue siendo el que elige el agente.
    /// </summary>
    public Guid? DefaultSubcategoriaId { get; set; }
}
