namespace Ecorex.Domain.Enums;

/// <summary>Origen de un registro de tiempo trabajado en una tarea.</summary>
public enum WorkLogKind
{
    /// <summary>Tiempo capturado por el cronometro de la UI.</summary>
    Timer = 0,
    /// <summary>Tiempo ingresado manualmente por el usuario.</summary>
    Manual
}
