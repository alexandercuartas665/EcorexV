using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Etiqueta (categoria) para agrupar tarjetas de flujos o de formularios en la galeria del tenant
/// (ADR-0097 Fase A). Tenant-scoped. <see cref="Scope"/> separa las etiquetas de flujos de las de
/// formularios. Una tarjeta puede tener VARIAS etiquetas (N:N via <see cref="FlowTag"/> / <see cref="FormTag"/>).
/// </summary>
public class CardTag : TenantEntity
{
    public CardTagScope Scope { get; set; } = CardTagScope.Flow;

    /// <summary>Nombre visible de la etiqueta. Unico por (TenantId, Scope, Name).</summary>
    public string Name { get; set; } = null!;

    /// <summary>Color opcional (hex corto, ej. "#6b4bd8") para el chip.</summary>
    public string? Color { get; set; }

    /// <summary>Orden de la etiqueta en la lista/gestion.</summary>
    public int SortOrder { get; set; }
}
