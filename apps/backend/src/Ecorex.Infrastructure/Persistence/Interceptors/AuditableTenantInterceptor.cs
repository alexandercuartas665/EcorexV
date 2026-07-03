using Ecorex.Application.Common;
using Ecorex.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ecorex.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Sella campos de auditoria (CreatedAt/By, UpdatedAt/By) y asigna TenantId a las entidades
/// tenant-scoped recien agregadas que aun no lo tengan, usando el ITenantContext. Esto evita
/// inserciones cruzadas por olvido de setear el tenant.
/// </summary>
public sealed class AuditableTenantInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public AuditableTenantInterceptor(ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var userId = _tenantContext.UserId;

        foreach (EntityEntry<BaseEntity> entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= userId;

                    if (entry.Entity is TenantEntity added
                        && added.TenantId == Guid.Empty
                        && _tenantContext.TenantId is Guid tenantId)
                    {
                        added.TenantId = tenantId;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;

                    // Concurrencia optimista portable (ADR-0013): la Version se incrementa en cada
                    // modificacion; EF usa el valor ORIGINAL (el leido) en el WHERE del UPDATE, asi
                    // un cambio concurrente produce DbUpdateConcurrencyException.
                    if (entry.Entity is IVersioned versioned)
                    {
                        versioned.Version++;
                    }

                    break;
            }
        }
    }
}
