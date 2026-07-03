namespace Ecorex.Domain.Common;

/// <summary>
/// Entidades con concurrencia optimista portable (ADR-0013): la columna Version actua como
/// ConcurrencyToken en ambos motores (PostgreSQL y SQL Server). El AuditableTenantInterceptor
/// la incrementa automaticamente en cada modificacion; el codigo de negocio NUNCA la escribe.
/// Se eligio sobre xmin/rowversion para que el modelo, las migraciones y los DTOs sean
/// identicos entre proveedores (el token viaja como long en la API).
/// </summary>
public interface IVersioned
{
    long Version { get; set; }
}
