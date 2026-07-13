using Ecorex.Application.Tenancy;

namespace Ecorex.Integration.Tests;

/// <summary>No-op de <see cref="IFormRecordBroadcaster"/> para tests (ola F4): no emite SignalR.</summary>
internal sealed class NoOpFormRecordBroadcaster : IFormRecordBroadcaster
{
    public Task RecordConfirmedAsync(Guid tenantId, string formCode, string recordNumber, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
