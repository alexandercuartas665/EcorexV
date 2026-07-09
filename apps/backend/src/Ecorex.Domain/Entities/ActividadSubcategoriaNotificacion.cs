using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Notificacion de una subcategoria de actividad (000270): union M:N entre una
/// <see cref="ActividadSubcategoria"/> y un usuario del tenant (<see cref="TenantUser"/>)
/// que debe ser notificado cuando se crea una tarea de este concepto (calca del legacy
/// TIPO_TAR_N). Vive y muere con la subcategoria (Cascade). La FK al usuario es NO ACTION
/// (borrar un usuario no toca el catalogo). TENANT-SCOPED.
/// </summary>
public class ActividadSubcategoriaNotificacion : TenantEntity
{
    public Guid SubcategoriaId { get; set; }
    public ActividadSubcategoria? Subcategoria { get; set; }

    /// <summary>Usuario del tenant a notificar.</summary>
    public Guid TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }
}
