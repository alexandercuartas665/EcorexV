using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Registro de consumo de IA del tenant (capa 3). Entidad TENANT-SCOPED. TODA ejecucion de IA del
/// cliente (prueba, chat, automatizacion) pasa por el modulo de consumo y deja un registro con
/// proveedor, modelo, tokens entrada/salida y costo estimado. Base para control de plan y reportes.
/// </summary>
public class AiUsageLog : TenantEntity
{
    /// <summary>Agente que origino el consumo (null si fue una ejecucion sin agente concreto).</summary>
    public Guid? AgentId { get; set; }

    public AiProvider Provider { get; set; }
    public string Model { get; set; } = "";

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }

    /// <summary>Costo estimado en USD (tabla de tarifas aproximada por proveedor).</summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>Origen del consumo: test, chat, automation.</summary>
    public string Source { get; set; } = "chat";

    public bool Success { get; set; }
}
