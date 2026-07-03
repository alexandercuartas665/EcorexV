using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Servicio incluido en una cita (Modelo de Datos seccion 6). TENANT-SCOPED.
/// <see cref="PriceSnapshot"/> congela el precio efectivo (override del asesor o base) al reservar,
/// para que cambios futuros de precio no muevan los historicos.
/// </summary>
public class AppointmentServiceItem : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public Guid ServiceId { get; set; }
    public int SortOrder { get; set; }
    public decimal PriceSnapshot { get; set; }
}
