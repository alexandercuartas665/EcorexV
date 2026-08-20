namespace Ecorex.Domain.Enums;

/// <summary>
/// Origen del usuario ASIGNADO a un paso (Task) del flujo al activarse (ADR-0056). Determina como el
/// motor resuelve el encargado en runtime; hoy el default deja el cargo/dependencia y la bandeja
/// expande candidatos (comportamiento historico).
/// </summary>
public enum WorkflowAssigneeSource
{
    /// <summary>Por cargo/dependencia del nodo (WorkflowNodePolicy). Comportamiento historico: el paso
    /// nace sin asignado unico y la bandeja resuelve los candidatos del cargo. Es el default.</summary>
    Policy = 0,

    /// <summary>Hereda al INICIADOR del flujo (quien lanzo la actividad; el usuario del primer elemento).</summary>
    InheritStart = 1,

    /// <summary>Hereda al usuario del PASO ANTERIOR (quien ejecuto/estaba asignado en el predecesor por el
    /// camino real que activo este paso).</summary>
    InheritPrevious = 2,

    /// <summary>Toma el asignado del VALOR de un campo de un formulario ya diligenciado en un nodo anterior
    /// de la misma instancia (el campo apunta a un usuario: id o correo).</summary>
    FormField = 3
}
