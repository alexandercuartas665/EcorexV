using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo entre un agente de IA y una linea de WhatsApp (capa 3). Entidad TENANT-SCOPED.
/// Una linea es atendida por a lo sumo UN agente (indice unico por TenantId+WhatsAppLineId).
/// Un agente puede atender varias lineas. Permite conectar/desconectar el agente de la linea
/// (IsConnected) y elegir si actua de forma autonoma o solo en modo sugerencia (AutoConfirm).
/// </summary>
public class AiAgentLineBinding : TenantEntity
{
    public Guid AgentId { get; set; }
    public AiAgent? Agent { get; set; }

    public Guid WhatsAppLineId { get; set; }
    public WhatsAppLine? WhatsAppLine { get; set; }

    /// <summary>El agente esta atendiendo (conectado a) esta linea ahora mismo.</summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// true: el agente ejecuta reservas/cancelaciones por si mismo (autonomo).
    /// false: modo sugerencia; deja la solicitud para que un asesor la confirme.
    /// </summary>
    public bool AutoConfirm { get; set; }
}
