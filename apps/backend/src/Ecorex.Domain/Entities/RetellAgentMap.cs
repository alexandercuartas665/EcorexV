using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Mapa idempotente: hash del prompt COMPUESTO por ECOREX (SystemPrompt del AiAgent + PromptExtra + directiva
/// de objetivo) -> agente Retell (con su Retell LLM). El prompt del agente Retell es SIEMPRE el compuesto por
/// ECOREX: reemplaza cualquier prompt que trajera el agente Retell (requisito del producto). Prompts
/// identicos REUTILIZAN el mismo agente Retell (no se provisiona uno por llamada). TENANT-SCOPED.
/// </summary>
public class RetellAgentMap : TenantEntity
{
    /// <summary>SHA-256 (hex) del prompt compuesto + voz + idioma que define la identidad del agente Retell.</summary>
    public string PromptHash { get; set; } = null!;

    /// <summary>Retell LLM (Response Engine) creado con general_prompt = el prompt de ECOREX.</summary>
    public string RetellLlmId { get; set; } = null!;

    /// <summary>Agente Retell ligado a ese LLM.</summary>
    public string RetellAgentId { get; set; } = null!;

    /// <summary>AiAgent de origen (referencia; el prompt puede combinar SystemPrompt + PromptExtra).</summary>
    public Guid? AiAgentId { get; set; }

    public DateTimeOffset LastUsedAt { get; set; }
}
