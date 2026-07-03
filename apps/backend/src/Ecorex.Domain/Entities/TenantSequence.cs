using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Consecutivo por tenant y codigo (ej. code "T05" para numeros de tarea "T00001").
/// Reemplaza el patron legacy MAX+1: SequenceService lo incrementa con un UPDATE
/// condicional atomico (compare-and-swap con retry), seguro bajo concurrencia y
/// portable entre PostgreSQL y SQL Server (ADR-0013). TENANT-SCOPED.
/// Unico por (TenantId, Code).
/// </summary>
public class TenantSequence : TenantEntity
{
    /// <summary>Codigo del consecutivo (ej. "T05" = prefijo T con padding 5).</summary>
    public string Code { get; set; } = null!;

    /// <summary>Proximo valor a emitir.</summary>
    public long NextValue { get; set; } = 1;
}
