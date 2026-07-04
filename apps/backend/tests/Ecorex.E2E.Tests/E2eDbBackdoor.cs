using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Ecorex.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Backdoor de ARREGLO de datos contra la MISMA BD dev que usa la app bajo prueba.
/// Existe por una sola razon: el flujo demo COT-COM arranca en el paso "Requerimiento",
/// que NO tiene formulario asociado y HOY no tiene UI para completarse (la bandeja de
/// pasos del flujo es deuda declarada en ADR-0014). Para poder ejercitar de punta a punta
/// "Formularios del paso" (ADR-0015) el test completa ese primer paso usando el MISMO
/// motor (WorkflowEngine.CompleteStepAsync), no SQL a mano: si el motor cambia, esto
/// sigue siendo fiel. Los asserts de UI siguen siendo por navegador; esta clase solo
/// prepara y consulta estado del motor.
/// Espeja el patron de Ecorex.Integration.Tests (FixedTenantContext + opciones EF).
/// </summary>
internal sealed class E2eDbBackdoor
{
    private readonly string _connectionString;

    public E2eDbBackdoor(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Completa el paso current indicado (por BpmnElementId) del flujo de la tarea.</summary>
    /// <returns>null si quedo completado; el motivo del fallo en caso contrario.</returns>
    public async Task<string?> CompleteCurrentStepAsync(string taskNumber, string bpmnElementId)
    {
        var tenantId = await GetDemoTenantIdAsync();
        await using var ctx = CreateContext(tenantId);

        var task = await ctx.TaskItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Number == taskNumber);
        if (task is null)
        {
            return $"la tarea {taskNumber} no existe en el tenant demo";
        }
        if (task.WorkflowInstanceId is not Guid instanceId)
        {
            return $"la tarea {taskNumber} no tiene instancia de flujo";
        }

        var tenantUserId = await ctx.TenantUsers
            .OrderBy(u => u.CreatedAt)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();

        var engine = BuildEngine(ctx, tenantId);
        var steps = await engine.GetCurrentStepsAsync(instanceId);
        var step = steps.FirstOrDefault(s =>
            s.BpmnElementId == bpmnElementId && s.Status == WorkflowStepStatus.Pending);
        if (step is null)
        {
            var current = string.Join(", ", steps.Select(s => $"{s.BpmnElementId}:{s.Status}"));
            return $"el paso {bpmnElementId} no esta vigente (current: [{current}])";
        }

        var result = await engine.CompleteStepAsync(instanceId, step.Id, tenantUserId);
        return result.IsOk ? null : (result.Error ?? "CompleteStepAsync fallo sin mensaje");
    }

    /// <summary>BpmnElementId de los pasos current de la instancia de flujo de la tarea.</summary>
    public async Task<IReadOnlyList<string>> GetCurrentStepElementIdsAsync(string taskNumber)
    {
        var tenantId = await GetDemoTenantIdAsync();
        await using var ctx = CreateContext(tenantId);

        var task = await ctx.TaskItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Number == taskNumber);
        if (task?.WorkflowInstanceId is not Guid instanceId)
        {
            return [];
        }
        var engine = BuildEngine(ctx, tenantId);
        var steps = await engine.GetCurrentStepsAsync(instanceId);
        return steps.Select(s => s.BpmnElementId).ToList();
    }

    // ---- Infraestructura (patron de Ecorex.Integration.Tests) ----

    private async Task<Guid> GetDemoTenantIdAsync()
    {
        await using var ctx = CreateContext(tenantId: null);
        return await ctx.Tenants.IgnoreQueryFilters()
            .Where(t => t.Kind == TenantKind.Demo)
            .Select(t => t.Id)
            .SingleAsync();
    }

    private static WorkflowEngine BuildEngine(EcorexDbContext ctx, Guid tenantId)
        // Hook de reglas NoOp: el arreglo no necesita disparar ASIGNAR_CONSECUTIVO (la
        // regla autonoma demo tampoco pide autoComplete); broadcaster NoOp: sin SignalR.
        => new(ctx, new FixedTenantContext(tenantId), new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());

    private EcorexDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<EcorexDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenantContext, TimeProvider.System))
            .Options;
        return new EcorexDbContext(options, tenantContext);
    }

    private sealed class FixedTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}
