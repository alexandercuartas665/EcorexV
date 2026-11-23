using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// CEREBRO DIGITAL (ADR-0200): conjunto de notas markdown enlazadas al estilo Obsidian, con grafo, adjuntos y chat con
/// Claude via MCP. Nacio dentro de las maquetas (ADR-0183) y desde la ola 1 de ADR-0200 es un modulo propio: un cerebro
/// puede vivir solo (base de conocimiento, procesos, cliente) o vincularse a una o varias maquetas
/// (<see cref="MockupProjectBrain"/>). Las notas (<see cref="MockupNote"/>) cuelgan del cerebro.
/// Los cerebros migrados desde una maqueta conservan el Id del proyecto, asi los adjuntos del gestor de archivos
/// (llave = id del duenio) siguen en su sitio sin mover binarios.
/// Tenant-scoped + soft-delete (archivar) + concurrencia optimista.
/// </summary>
public sealed class Brain : TenantEntity, ISoftDeletable, IVersioned
{
    /// <summary>Identificador corto en URL (/cerebros/{slug}). Unico por tenant entre los activos.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Categoria del tenant (opcional).</summary>
    public Guid? CategoryId { get; set; }

    public BrainCategory? Category { get; set; }

    public string? CreatedByName { get; set; }

    /// <summary>Cerebro plantilla de plataforma del que se sembro (ola 3). Null = cerebro propio del tenant.</summary>
    public Guid? SourceTemplateId { get; set; }

    /// <summary>Sembrado BLOQUEADO: solo lectura en el tenant (ni Owner/Admin edita); lo administra la plataforma.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Version de la plantilla aplicada por ultima vez (sembrados).</summary>
    public int? TemplateVersion { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedByTenantUserId { get; set; }

    public long Version { get; set; }
}

/// <summary>Categoria de cerebros del tenant (ADR-0200): agrupa y filtra la galeria. La administra Owner/Admin.</summary>
public sealed class BrainCategory : TenantEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Color de acento de la tarjeta (#RRGGBB).</summary>
    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedByTenantUserId { get; set; }
}

/// <summary>
/// Vinculo N:M maqueta - cerebro (ADR-0200). El de menor <see cref="SortOrder"/> es el cerebro PRINCIPAL de la
/// maqueta: el que abre la pestana Cerebro del editor y el que usan las herramientas MCP que reciben "proyecto".
/// Es una fila de enlace (no un agregado): desvincular la borra.
/// </summary>
public sealed class MockupProjectBrain : TenantEntity
{
    public Guid MockupProjectId { get; set; }

    public MockupProject? MockupProject { get; set; }

    public Guid BrainId { get; set; }

    public Brain? Brain { get; set; }

    public int SortOrder { get; set; }
}
