using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// CEREBRO PLANTILLA de PLATAFORMA (ADR-0200, ola 3): lo cura el Super Admin y se SIEMBRA en los tenants como un
/// <see cref="Brain"/> propio de cada empresa (<see cref="Brain.SourceTemplateId"/>). NO es tenant-scoped: es metadato
/// de plataforma (como ReportTemplate, ADR-0062) y solo lo toca PlatformOperator, auditado. Bloqueado = el cerebro
/// sembrado es de SOLO LECTURA en el tenant y se re-sincroniza en cada publicacion; libre = copia editable que ya no
/// se sincroniza.
/// </summary>
public sealed class BrainTemplate : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Categoria con la que llega al tenant (se crea alli si no existe, por nombre).</summary>
    public string? CategoryName { get; set; }

    public string? CategoryColor { get; set; }

    /// <summary>true = los cerebros sembrados quedan de solo lectura y se sincronizan al publicar.</summary>
    public bool IsLocked { get; set; } = true;

    /// <summary>A quien se siembra: <see cref="BrainTemplateTargets"/> (all | plans | tenants).</summary>
    public string TargetMode { get; set; } = BrainTemplateTargets.All;

    /// <summary>Ids de planes o de tenants (JSON array) segun <see cref="TargetMode"/>.</summary>
    public string? TargetIdsJson { get; set; }

    /// <summary>Version publicada (0 = nunca publicada). Cada publicacion la incrementa.</summary>
    public int PublishedVersion { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>
    /// Es la GUIA de uso de los MCP (ADR-0204): la herramienta guia_ecorex del MCP de tableros la sirve a cualquier IA
    /// conectada. Solo una plantilla la tiene activa (al marcar una se desmarca la anterior en la misma transaccion).
    /// </summary>
    public bool IsMcpGuide { get; set; }

    public ICollection<BrainTemplateNote> Notes { get; set; } = new List<BrainTemplateNote>();
}

/// <summary>Nota de un cerebro plantilla (markdown tipo Obsidian, mismas reglas de titulo que las notas del tenant).</summary>
public sealed class BrainTemplateNote : BaseEntity
{
    public Guid TemplateId { get; set; }

    public BrainTemplate? Template { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Titulo normalizado: llave de sincronizacion con las notas del cerebro sembrado.</summary>
    public string TitleKey { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}

/// <summary>Modos de destino de la siembra de un cerebro plantilla.</summary>
public static class BrainTemplateTargets
{
    public const string All = "all";
    public const string Plans = "plans";
    public const string Tenants = "tenants";

    public static bool IsValid(string? mode) => mode is All or Plans or Tenants;
}
