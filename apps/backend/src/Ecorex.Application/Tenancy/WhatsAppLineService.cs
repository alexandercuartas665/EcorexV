using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class WhatsAppLineService : IWhatsAppLineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ISecretProtector _secretProtector;

    public WhatsAppLineService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, TimeProvider timeProvider, ISecretProtector secretProtector)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _timeProvider = timeProvider;
        _secretProtector = secretProtector;
    }

    public async Task<IReadOnlyList<WhatsAppLineDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.WhatsAppLines
            .AsNoTracking()
            .OrderBy(l => l.InstanceName)
            .Select(l => new WhatsAppLineDto(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt,
                l.Provider, l.CloudPhoneNumberId, l.CloudBusinessAccountId, l.CloudAccessTokenEncrypted != null,
                l.YCloudPhoneNumberId, l.YCloudWabaId, l.YCloudApiKeyEncrypted != null))
            .ToListAsync(cancellationToken);
    }

    public async Task<WhatsAppLineDto?> CreateAsync(CreateWhatsAppLineRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var line = new WhatsAppLine
        {
            TenantId = tenantId,
            InstanceName = request.InstanceName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Status = WhatsAppLineStatus.Created,
            LastStatusAt = _timeProvider.GetUtcNow(),
            Provider = request.Provider,
            CloudPhoneNumberId = request.Provider == WhatsAppProvider.Cloud ? Clean(request.CloudPhoneNumberId) : null,
            CloudBusinessAccountId = request.Provider == WhatsAppProvider.Cloud ? Clean(request.CloudBusinessAccountId) : null,
            CloudAccessTokenEncrypted = request.Provider == WhatsAppProvider.Cloud && !string.IsNullOrWhiteSpace(request.CloudAccessToken)
                ? _secretProtector.Protect(request.CloudAccessToken.Trim())
                : null,
            YCloudPhoneNumberId = request.Provider == WhatsAppProvider.YCloud ? Clean(request.YCloudPhoneNumberId) : null,
            YCloudWabaId = request.Provider == WhatsAppProvider.YCloud ? Clean(request.YCloudWabaId) : null,
            YCloudApiKeyEncrypted = request.Provider == WhatsAppProvider.YCloud && !string.IsNullOrWhiteSpace(request.YCloudApiKey)
                ? _secretProtector.Protect(request.YCloudApiKey.Trim())
                : null
        };
        _db.WhatsAppLines.Add(line);

        _audit.Write(actorUserId, "whatsapp-line.create", nameof(WhatsAppLine), line.Id,
            previousValue: null,
            newValue: new { line.InstanceName, line.PhoneNumber, Provider = line.Provider.ToString() },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    public async Task<WhatsAppLineDto?> ChangeStatusAsync(Guid lineId, WhatsAppLineStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        var previous = line.Status;
        if (previous != status)
        {
            var now = _timeProvider.GetUtcNow();
            line.Status = status;
            line.LastStatusAt = now;
            if (status == WhatsAppLineStatus.Connected)
            {
                line.LastConnectedAt = now;
            }

            _audit.Write(actorUserId, "whatsapp-line.change-status", nameof(WhatsAppLine), line.Id,
                previousValue: new { Status = previous },
                newValue: new { Status = status },
                tenantId: line.TenantId);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(line);
    }

    public async Task<WhatsAppLineDto?> AssignAsync(Guid lineId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        if (tenantUserId is Guid userId)
        {
            // El filtro global garantiza que solo se valida contra usuarios del tenant activo.
            var belongs = await _db.TenantUsers.AnyAsync(tu => tu.Id == userId, cancellationToken);
            if (!belongs)
            {
                return null;
            }
        }

        line.AssignedToTenantUserId = tenantUserId;
        _audit.Write(actorUserId, "whatsapp-line.assign", nameof(WhatsAppLine), line.Id,
            previousValue: null,
            newValue: new { AssignedToTenantUserId = tenantUserId },
            tenantId: line.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    private static WhatsAppLineDto Map(WhatsAppLine l) =>
        new(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt,
            l.Provider, l.CloudPhoneNumberId, l.CloudBusinessAccountId, !string.IsNullOrEmpty(l.CloudAccessTokenEncrypted),
            l.YCloudPhoneNumberId, l.YCloudWabaId, !string.IsNullOrEmpty(l.YCloudApiKeyEncrypted));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
