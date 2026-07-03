namespace Ecorex.Domain.Common;

/// <summary>
/// Raiz de todas las entidades. Id es Guid v7 (ordenable por tiempo) generado en la aplicacion.
/// Los campos de auditoria los gestiona el interceptor de SaveChanges, no el codigo de negocio.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
