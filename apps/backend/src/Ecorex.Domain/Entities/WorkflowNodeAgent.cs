using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asigna un AGENTE DE IA a un nodo de flujo (ola 1 de agentes en nodos), el equivalente de
/// <see cref="WorkflowNodePolicy"/> (que liga el nodo con una Dependencia/Cargo) pero con un
/// atendedor no humano. Un nodo tiene a lo sumo UN agente (indice unico por TenantId+NodeId):
/// si hicieran falta varios agentes, el paso deberia partirse en varios nodos, porque el
/// resultado de un paso es uno solo.
///
/// El <see cref="Autonomy"/> vive AQUI y no en el AiAgent: el mismo agente puede cerrar solo
/// un paso trivial y, en otro nodo, limitarse a proponer.
///
/// Coexiste con la policy de cargo: un nodo puede tener agente Y cargos (el cargo es quien
/// confirma cuando el modo es Proposes, o el plan B si el agente falla). En esta ola solo se
/// modela y se asigna; la EJECUCION del agente es la ola 2. TENANT-SCOPED.
/// </summary>
public class WorkflowNodeAgent : TenantEntity
{
    /// <summary>Nodo atendido. FK en cascada: el vinculo vive y muere con el nodo.</summary>
    public Guid NodeId { get; set; }
    public WorkflowNode? Node { get; set; }

    /// <summary>Agente de IA del tenant que atiende el paso. FK NO ACTION (restrict).</summary>
    public Guid AiAgentId { get; set; }
    public AiAgent? AiAgent { get; set; }

    /// <summary>Si el agente cierra el paso o solo propone y una persona confirma.</summary>
    public WorkflowAgentAutonomy Autonomy { get; set; } = WorkflowAgentAutonomy.Proposes;

    // ---- ADR-0091: recursos para CONSEGUIR datos al llenar el formulario del paso ----
    // Permiso EXPLICITO por nodo: el agente solo obtiene una herramienta si su recurso esta configurado aqui.

    /// <summary>Cliente COLMENA (navegador on-prem) que el agente puede usar para 'buscar_web'. FK a
    /// <see cref="DataClient"/> (restrict, nullable). Null = sin herramienta de busqueda web.</summary>
    public Guid? ColmenaClientId { get; set; }
    public DataClient? ColmenaClient { get; set; }

    /// <summary>Perfil persistente del navegador para scraping LOGUEADO (ej. "linkedin"). Se pasa como
    /// SessionKey a la orden Colmena. Null = sesion efimera.</summary>
    public string? ColmenaSessionKey { get; set; }

    /// <summary>Agente de VOZ (prompt de la llamada) que el agente puede usar para 'llamar_telefono' via
    /// Retell. FK a <see cref="AiAgent"/> (restrict, nullable). Null = sin herramienta de llamada.</summary>
    public Guid? VoiceAiAgentId { get; set; }
    public AiAgent? VoiceAiAgent { get; set; }

    /// <summary>Ola 2 (ADR-0190): LINEA de voz (numero saliente de /config-voz) desde la que se coloca la llamada.
    /// FK a <see cref="RetellVoiceLine"/> (restrict, nullable). Null = la linea por defecto del tenant (el
    /// comportamiento actual de PlaceCallAsync, que la elige sola). Espejo de <see cref="WhatsAppLineId"/>.</summary>
    public Guid? VoiceLineId { get; set; }
    public RetellVoiceLine? VoiceLine { get; set; }

    /// <summary>Ola 2 (ADR-0190): canales que ESTE nodo permite usar al agente (flags Voz|WhatsApp|Web|Correo).
    /// Null = todos los configurados permitidos (RETROCOMPATIBLE). Con valor, el gating de cada herramienta exige
    /// que el canal este marcado AQUI ademas de tener su recurso configurado. Ver <see cref="WorkflowAgentChannel"/>.</summary>
    public WorkflowAgentChannel? AllowedChannels { get; set; }

    // ---- Ola 3 (ADR-0190): politica de reintento de la LLAMADA DE VOZ (sin contacto / buzon) ----

    /// <summary>Maximo de llamadas de voz que el nodo coloca antes de rendirse con la voz (incluida la primera).
    /// Cuando "sin contacto" (no contesta / buzon / ocupado / fallo de marcado), el runner reintenta por su cuenta
    /// hasta este tope, sin invocar al LLM. Agotado el tope, cae al comportamiento normal (el agente decide: WhatsApp
    /// si esta permitido, u OnFailure). Null o &lt;=1 = una sola llamada (sin auto-reintento). Espejo de
    /// <see cref="FailureRetries"/> pero para la llamada.</summary>
    public int? MaxVoiceCalls { get; set; }

    /// <summary>Minutos a esperar entre una llamada "sin contacto" y la siguiente (medidos desde que termino la
    /// anterior). Null = valor por defecto del runner. Solo aplica cuando <see cref="MaxVoiceCalls"/> &gt; 1.</summary>
    public int? VoiceRetryMinutes { get; set; }

    /// <summary>Hora (0-23, zona horaria del TENANT) a partir de la cual se permite colocar llamadas. Null = sin
    /// restriccion de inicio. Si una llamada toca fuera de ventana, el reintento se difiere al proximo inicio de
    /// ventana en vez de colocarse de inmediato. Ver <see cref="CallWindowEndHour"/>.</summary>
    public int? CallWindowStartHour { get; set; }

    /// <summary>Hora (1-24, zona horaria del TENANT) hasta la cual se permite colocar llamadas (exclusiva: 18 = hasta
    /// las 17:59). Null = sin restriccion de fin. Ver <see cref="CallWindowStartHour"/>.</summary>
    public int? CallWindowEndHour { get; set; }

    // ---- ADR-0092: preguntar por WhatsApp para conseguir datos (pausa/reanudacion, como la llamada) ----

    /// <summary>Linea WhatsApp desde la que el agente puede usar 'preguntar_whatsapp'. FK a
    /// <see cref="WhatsAppLine"/> (restrict, nullable). Null = sin herramienta de WhatsApp.</summary>
    public Guid? WhatsAppLineId { get; set; }
    public WhatsAppLine? WhatsAppLine { get; set; }

    /// <summary>Nombre de la PLANTILLA aprobada (HSM) para el PRIMER contacto en frio (ventana de 24h cerrada):
    /// su unica variable de cuerpo {{1}} recibe la pregunta del agente. Null = solo texto libre (ventana abierta).</summary>
    public string? WhatsAppTemplateName { get; set; }

    /// <summary>Codigo de idioma de la plantilla (ej. "es"). Solo aplica si <see cref="WhatsAppTemplateName"/>
    /// tiene valor. Null con plantilla presente = se asume "es".</summary>
    public string? WhatsAppTemplateLang { get; set; }

    /// <summary>Minutos de "frio" tras el ultimo mensaje ENTRANTE del cliente para usar la PLANTILLA en vez de
    /// texto libre. Null = comportamiento por defecto (YCloud/Cloud: la ventana de 24h de Meta; Evolution/Emulator:
    /// siempre texto libre). Con valor: la conversacion se considera "en frio" pasado ese tiempo y se usa la
    /// plantilla; aplica tambien a Evolution (para emular la ventana y poder probar). En YCloud/Cloud se capa a
    /// 1440 (24h) porque Meta igual corta el texto libre pasadas 24h.</summary>
    public int? ColdWindowMinutes { get; set; }

    /// <summary>Binding de los PARAMETROS de la plantilla de frio: JSON { "<variable>": "<expresion>" } donde la
    /// expresion admite tokens ({tarea.cliente}, {tarea.numero}, {empresa}, {sistema.fecha}, {form.x}), texto fijo
    /// o el token especial {agente.mensaje} (= la pregunta que el agente genero). Al enviar en frio, cada variable
    /// de la plantilla (en el orden de su VariablesJson) se resuelve con este mapa. Null/vacio = compatibilidad:
    /// la pregunta del agente va a la primera variable.</summary>
    public string? WhatsAppTemplateVarsJson { get; set; }

    /// <summary>URL publica del ARCHIVO del encabezado (header media: logo/imagen/documento/video) a enviar con la
    /// plantilla de frio, configurado POR NODO (header dinamico). Null = se usa el header fijo de la plantilla (si
    /// tiene). El tipo de media sale del HeaderType de la plantilla.</summary>
    public string? WhatsAppTemplateHeaderMediaUrl { get; set; }

    // ---- ADR-0093: potenciar el agente en el nodo (instrucciones por paso + correo) ----

    /// <summary>Instrucciones EXTRA para el agente SOLO en este paso, que se anteponen a su prompt de sistema
    /// base. Es donde se le dice QUE hacer y COMO usar sus herramientas aqui (ej. que URL abrir con Colmena y
    /// que extraer). Null = solo el prompt base del agente.</summary>
    public string? ExtraPrompt { get; set; }

    /// <summary>Si el agente puede ENVIAR correos en este paso (herramienta 'enviar_correo', ADR-0093). Usa el
    /// correo saliente configurado del tenant; el FROM no lo elige el agente. Default false (permiso explicito).</summary>
    public bool CanSendEmail { get; set; }

    // ---- Politica de FALLO del agente (que hacer si no logra resolver el paso) ----

    /// <summary>Que hacer si el agente no logra resolver el paso. Default ReturnToHuman.</summary>
    public WorkflowAgentFailureAction OnFailure { get; set; } = WorkflowAgentFailureAction.ReturnToHuman;

    /// <summary>Cuando OnFailure = Retry: cuantas veces reintentar al agente (una por ciclo del worker)
    /// antes de rendirse. 0 = sin reintentos.</summary>
    public int FailureRetries { get; set; }

    /// <summary>Cuando OnFailure = TakeRoute: nombre de la ruta/opcion de salida con la que se cierra el
    /// paso para que el motor enrute por esa rama de contingencia. Null si no aplica.</summary>
    public string? FailureRoute { get; set; }
}
