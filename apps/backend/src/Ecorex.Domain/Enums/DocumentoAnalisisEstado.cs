namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de un documento dentro del Analizador de documentos. En esta fase (solo carga) un
/// documento nace Cargado o, si algo falla al subirlo al almacenamiento, queda en Error. Los
/// estados EnAnalisis/Analizado quedan reservados para la fase siguiente (motor de analisis),
/// de modo que la parrilla y la entidad no cambian cuando se enganche ese motor.
/// </summary>
public enum DocumentoAnalisisEstado
{
    /// <summary>El binario quedo guardado en el almacenamiento y registrado. Listo para analizar.</summary>
    Cargado = 0,

    /// <summary>Encolado para analisis (fase siguiente). No se usa todavia.</summary>
    EnAnalisis = 1,

    /// <summary>Analisis terminado (fase siguiente). No se usa todavia.</summary>
    Analizado = 2,

    /// <summary>La carga (o, mas adelante, el analisis) fallo. El motivo queda en MotivoError.</summary>
    Error = 9
}
