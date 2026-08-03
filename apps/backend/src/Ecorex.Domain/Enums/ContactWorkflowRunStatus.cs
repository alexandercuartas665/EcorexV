namespace Ecorex.Domain.Enums;

/// <summary>
/// Resultado de ejecutar UN paso de un <see cref="Entities.ContactWorkflow"/> sobre UN contacto en una
/// ventana (ADR-0056, Fase 2 = motor de ejecucion). Se persiste como string (seguro entre motores al
/// agregar valores al final). Es la base de la idempotencia: un (paso, ventana, contacto) con estado
/// Sent nunca se reenvia.
/// </summary>
public enum ContactWorkflowRunStatus
{
    /// <summary>Registrado pero aun sin resolver (reservado; el motor escribe estados finales).</summary>
    Pending = 0,

    /// <summary>El envio/accion se ejecuto con exito (o paso "no-envio" como Conectar).</summary>
    Sent = 1,

    /// <summary>La accion fallo (el servicio devolvio error o lanzo). Ver <c>Error</c>.</summary>
    Failed = 2,

    /// <summary>La accion no aplica al contacto (sin dato requerido o canal no disponible). Ver <c>Error</c>.</summary>
    Skipped = 3
}
