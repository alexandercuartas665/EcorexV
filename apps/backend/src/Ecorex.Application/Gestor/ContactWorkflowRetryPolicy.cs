using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Regla de reintento del motor de acciones por filtro (ADR-0056, 2026-11-15). La fila de dedupe
/// (paso, ventana, contacto, dia) ya no bloquea el dia entero cuando el intento no entrego:
/// <list type="bullet">
///   <item><b>Sent</b>: nunca se repite (anti-spam intacto).</item>
///   <item><b>Failed</b> (se intento entregar y el proveedor fallo): se reintenta hasta
///         <see cref="MaxFailedAttempts"/> intentos en total, separados al menos <see cref="MinDelay"/>.</item>
///   <item><b>Skipped</b> (no se intento entregar: falta configuracion o dato del contacto): se reevalua
///         cada <see cref="MinDelay"/> sin tope, porque no salio ningun mensaje; asi una correccion de la
///         configuracion se aplica el mismo dia sin esperar a manana.</item>
/// </list>
/// El reintento ACTUALIZA la misma fila (Attempts++), nunca inserta otra.
/// </summary>
public static class ContactWorkflowRetryPolicy
{
    /// <summary>Intentos totales permitidos para una fila Failed (el primero incluido).</summary>
    public const int MaxFailedAttempts = 3;

    /// <summary>Espera minima entre dos intentos sobre la misma fila.</summary>
    public static readonly TimeSpan MinDelay = TimeSpan.FromMinutes(15);

    public static bool ShouldRetry(
        ContactWorkflowRunStatus status, int attempts, DateTimeOffset lastAttemptUtc, DateTimeOffset nowUtc)
    {
        var waited = nowUtc - lastAttemptUtc >= MinDelay;
        return status switch
        {
            ContactWorkflowRunStatus.Failed => waited && attempts < MaxFailedAttempts,
            ContactWorkflowRunStatus.Skipped => waited,
            _ => false
        };
    }
}
