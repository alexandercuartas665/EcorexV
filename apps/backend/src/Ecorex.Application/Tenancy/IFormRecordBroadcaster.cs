namespace Ecorex.Application.Tenancy;

/// <summary>
/// Notifica en vivo la creacion de un registro de formulario-modulo (Formularios avanzados, ola F4,
/// doc 01 D6): la bandeja /m/{code} se actualiza sin recargar. La implementacion (SignalR) vive en
/// la capa web; la Application solo depende de esta abstraccion.
/// </summary>
public interface IFormRecordBroadcaster
{
    /// <summary>Un registro se confirmo en el formulario <paramref name="formCode"/> del tenant.</summary>
    Task RecordConfirmedAsync(Guid tenantId, string formCode, string recordNumber, CancellationToken cancellationToken = default);
}

/// <summary>Implementacion por defecto (no hace nada) para procesos sin SignalR (Api, tests).</summary>
public sealed class NoOpFormRecordBroadcaster : IFormRecordBroadcaster
{
    public Task RecordConfirmedAsync(Guid tenantId, string formCode, string recordNumber, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
