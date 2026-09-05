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
}
