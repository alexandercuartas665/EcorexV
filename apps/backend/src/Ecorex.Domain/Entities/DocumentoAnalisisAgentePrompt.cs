using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Configuracion del Analizador de documentos para UN agente de IA del tenant: el "prompt principal
/// de analisis" que el modulo usara con ese agente. El agente NO se crea ni se edita aqui (eso vive en
/// el modulo de Agentes); esta fila solo referencia un <see cref="AgentId"/> existente y guarda el
/// prompt que vive EN EL MODULO (no toca el SystemPrompt global del agente). Un unico agente por tenant
/// queda marcado <see cref="IsActive"/> = el agente activo del Analizador.
///
/// Tenant-scoped por el filtro global. Borrado REAL (no soft-delete): es configuracion reemplazable y
/// asi el indice unico (TenantId, AgentId) no choca al re-agregar un agente quitado.
/// </summary>
public sealed class DocumentoAnalisisAgentePrompt : TenantEntity
{
    /// <summary>Agente de IA del tenant (AiAgent.Id) al que pertenece este prompt. Solo referencia; no lo crea.</summary>
    public Guid AgentId { get; set; }

    /// <summary>Prompt principal de analisis que el modulo usara con este agente (vive en el modulo).</summary>
    public string PromptPrincipal { get; set; } = string.Empty;

    /// <summary>Si este es el agente ACTIVO del Analizador. Solo uno por tenant (lo garantiza el servicio).</summary>
    public bool IsActive { get; set; }
}
