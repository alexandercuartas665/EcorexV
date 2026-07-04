namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado gerencial de un tablero de actividades (prototipo ECOREX.dc.html: chip de estado
/// del indice de tableros). Es un rotulo manual del responsable, independiente del avance
/// calculado por columnas/checklist.
/// </summary>
public enum TaskBoardStatus
{
    /// <summary>El tablero va a tiempo respecto a su fecha limite.</summary>
    OnTime = 0,
    /// <summary>Trabajo en curso (default de los tableros nuevos).</summary>
    InProgress,
    /// <summary>En riesgo de incumplir la fecha limite.</summary>
    AtRisk,
    /// <summary>Trabajo terminado.</summary>
    Completed
}
