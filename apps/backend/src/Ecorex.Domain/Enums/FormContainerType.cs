namespace Ecorex.Domain.Enums;

/// <summary>Tipo de contenedor del arbol de un formulario dinamico (ADR-0015).</summary>
public enum FormContainerType
{
    /// <summary>Segmento visual (seccion con titulo) que agrupa preguntas.</summary>
    Segment = 0,
    /// <summary>Tabla/grid (reservado para el tier de controles GridDetail; sin UI aun).</summary>
    Table
}
