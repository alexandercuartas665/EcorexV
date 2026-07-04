namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de contenedor del arbol de un formulario dinamico (ADR-0015, ampliado por
/// ADR-0021 con los tipos del constructor del prototipo). Se persiste como string,
/// por lo que agregar valores al final es seguro entre motores.
/// </summary>
public enum FormContainerType
{
    /// <summary>Segmento visual (seccion con titulo) que agrupa preguntas. Legacy: se
    /// renderiza igual que <see cref="Section"/>.</summary>
    Segment = 0,
    /// <summary>Tabla/grid (reservado; sin UI de contenedor aun).</summary>
    Table,
    /// <summary>Fila: sus hijos se distribuyen en la grilla de 12 columnas (prototipo 'row').</summary>
    Row,
    /// <summary>Columna: sus hijos se apilan verticalmente (prototipo 'col').</summary>
    Col,
    /// <summary>Seccion con titulo (prototipo 'section'); equivalente visual de Segment.</summary>
    Section,
    /// <summary>Pestanas: cada contenedor hijo directo es una pestana (TabsJson = nombres).</summary>
    Tabs,
    /// <summary>Modal (prototipo 'modal'). El renderer lo pinta como seccion normal
    /// (TODO ADR-0021: apertura como dialogo real en una ola posterior).</summary>
    Modal
}
