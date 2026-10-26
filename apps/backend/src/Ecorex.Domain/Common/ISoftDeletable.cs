namespace Ecorex.Domain.Common;

/// <summary>
/// Entidad con BORRADO LOGICO (soft-delete): nunca se borra la fila fisicamente; se marca IsDeleted.
/// El filtro global del DbContext excluye las marcadas (igual que el filtro por tenant), asi que las
/// consultas normales no las ven; para verlas/restaurarlas se usa IgnoreQueryFilters. Protege datos
/// valiosos (p.ej. el jsonb de una respuesta de formulario) de una perdida silenciosa.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedByTenantUserId { get; set; }
}
