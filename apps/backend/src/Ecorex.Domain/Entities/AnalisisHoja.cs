using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>Hoja de un libro de indicadores (ADR-0158 D6): una grilla de widgets con nombre y orden.</summary>
public sealed class AnalisisHoja : TenantEntity, ISoftDeletable
{
    public Guid LibroId { get; set; }

    public AnalisisLibro? Libro { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public int Orden { get; set; }

    /// <summary>Filtros globales de la hoja (rango de fechas, tablero...) que se inyectan a cada widget (Ola 3).</summary>
    public string? FiltrosJson { get; set; }

    public List<AnalisisWidget> Widgets { get; set; } = new();

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
