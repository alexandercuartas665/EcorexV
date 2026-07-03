namespace Ecorex.Domain.Enums;

/// <summary>A que entidad del salon pertenece un campo configurable.</summary>
public enum SalonFieldScope
{
    /// <summary>Campo que se captura en cada cita (cambia visita a visita).</summary>
    Appointment,
    /// <summary>Campo del cliente que persiste entre visitas (ficha del cliente).</summary>
    Client,
    /// <summary>Campo de un articulo/producto del salon.</summary>
    Product
}
