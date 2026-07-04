namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de tablero (ADR-0020). Distingue los tableros Kanban del CRM heredado (tarjetas
/// TaskCard) de los tableros de ACTIVIDADES unificados del prototipo, cuyas tarjetas son
/// TaskItem de primera clase. Los tableros existentes quedan como CrmLegacy por default.
/// </summary>
public enum TaskBoardKind
{
    /// <summary>Tablero del CRM heredado: tarjetas TaskCard (kanban generico).</summary>
    CrmLegacy = 0,
    /// <summary>Tablero de actividades unificado (000636): tarjetas = TaskItem.</summary>
    Activities = 1
}
