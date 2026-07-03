namespace Ecorex.Domain.Enums;

/// <summary>Tipo de recurso agendable del salon (Modelo de Datos seccion 5).</summary>
public enum ResourceKind
{
    /// <summary>Asesor de imagen: persona con identidad propia que puede tener precios personalizados.</summary>
    Image,
    /// <summary>Estacion / cabina / silla: puesto fisico que siempre usa el precio base del catalogo.</summary>
    Station
}
