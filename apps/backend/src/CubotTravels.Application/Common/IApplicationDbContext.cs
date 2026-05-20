using CubotTravels.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Common;

/// <summary>
/// Abstraccion del DbContext para los casos de uso de Application, sin acoplar a la
/// implementacion concreta de Infrastructure. Expone solo los conjuntos que la capa necesita.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<PlatformUser> PlatformUsers { get; }
    DbSet<TenantUser> TenantUsers { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantConfiguration> TenantConfigurations { get; }
    DbSet<TenantEvolutionConfig> TenantEvolutionConfigs { get; }
    DbSet<WhatsAppLine> WhatsAppLines { get; }
    DbSet<PipelineStage> PipelineStages { get; }
    DbSet<Lead> Leads { get; }
    DbSet<LeadActivity> LeadActivities { get; }
    DbSet<FollowUpTask> FollowUpTasks { get; }
    DbSet<SaasPlan> SaasPlans { get; }
    DbSet<SaasPlanLimit> SaasPlanLimits { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<TenantPayment> TenantPayments { get; }
    DbSet<SuperAdminAuditLog> SuperAdminAuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
