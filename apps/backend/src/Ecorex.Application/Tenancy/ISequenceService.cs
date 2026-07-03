namespace Ecorex.Application.Tenancy;

/// <summary>
/// Consecutivos por tenant concurrente-safe (TenantSequence, ADR-0013). Reemplaza el patron
/// legacy MAX+1, que duplica numeros bajo concurrencia.
/// </summary>
public interface ISequenceService
{
    /// <summary>
    /// Garantiza que la fila del consecutivo exista para el tenant activo. Llamar ANTES de
    /// abrir la transaccion del caso de uso: asi una violacion de unicidad por carrera de
    /// creacion no aborta la transaccion principal (en PostgreSQL cualquier error la envenena).
    /// Idempotente y tolerante a carreras.
    /// </summary>
    Task EnsureSequenceAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite el siguiente valor del consecutivo, formateado como prefix + valor con padding
    /// (ej. NextAsync("T05", "T", 5) -> "T00042"). Incrementa TenantSequence con un UPDATE
    /// condicional atomico (compare-and-swap con retry) via LINQ, sin SQL crudo: portable
    /// entre PostgreSQL y SQL Server. Participa en la transaccion ambiente del caso de uso,
    /// de modo que un rollback devuelve el numero (no quedan huecos por fallos).
    /// </summary>
    Task<string> NextAsync(string code, string prefix, int padding, CancellationToken cancellationToken = default);
}
