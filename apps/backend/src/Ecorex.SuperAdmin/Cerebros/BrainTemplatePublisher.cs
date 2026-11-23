using Ecorex.Application.Integraciones.Cerebro;
using Ecorex.Application.Tenancy;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.Cerebros;

/// <summary>Resultado de sembrar en una empresa.</summary>
public sealed record BrainTemplateTenantResult(Guid TenantId, string TenantName, string Outcome, string? BrainSlug, int Created,
    int Updated, int Archived, string? Error);

/// <summary>Resultado de una publicacion completa.</summary>
public sealed record BrainTemplatePublishResult(int Version, IReadOnlyList<BrainTemplateTenantResult> Tenants)
{
    public int Count(string outcome) => Tenants.Count(t => t.Outcome == outcome);
}

/// <summary>
/// Siembra de cerebros plantilla (ADR-0200, ola 3). Prepara la publicacion (version + destinos, auditado) y aplica el
/// contenido EN EL CONTEXTO DE CADA TENANT (<see cref="AmbientTenantContext"/> + scope propio): cada empresa es su
/// propia transaccion y el filtro global por tenant sigue activo, asi una empresa nunca toca datos de otra. Si una
/// empresa falla, se reporta y se sigue con las demas.
/// </summary>
public sealed class BrainTemplatePublisher(IServiceScopeFactory scopes, ILogger<BrainTemplatePublisher> log)
{
    public async Task<TaskCoreResult<BrainTemplatePublishResult>> PublishAsync(Guid templateId, Guid platformUserId, CancellationToken ct = default)
    {
        TaskCoreResult<BrainTemplatePublishPlan> plan;
        await using (var scope = scopes.CreateAsyncScope())
        {
            plan = await scope.ServiceProvider.GetRequiredService<IBrainTemplateService>().PreparePublishAsync(templateId, platformUserId, ct);
        }
        if (!plan.IsOk || plan.Value is null)
        {
            return TaskCoreResult<BrainTemplatePublishResult>.NotFound(plan.Error);
        }

        var results = new List<BrainTemplateTenantResult>();
        foreach (var tenant in plan.Value.Tenants)
        {
            try
            {
                using var ambient = AmbientTenantContext.Begin(tenant.Id);
                await using var scope = scopes.CreateAsyncScope();
                var r = await scope.ServiceProvider.GetRequiredService<IBrainService>()
                    .ApplyTemplateAsync(plan.Value.Snapshot, tenant.Id, platformUserId, ct);
                results.Add(new BrainTemplateTenantResult(tenant.Id, tenant.Name, r.Outcome, r.BrainSlug, r.NotesCreated, r.NotesUpdated,
                    r.NotesArchived, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Fallo al sembrar el cerebro plantilla {TemplateId} en el tenant {TenantId}", templateId, tenant.Id);
                results.Add(new BrainTemplateTenantResult(tenant.Id, tenant.Name, "error", null, 0, 0, 0, "No se pudo sembrar en esta empresa."));
            }
        }
        return TaskCoreResult<BrainTemplatePublishResult>.Ok(new BrainTemplatePublishResult(plan.Value.Snapshot.Version, results));
    }
}
