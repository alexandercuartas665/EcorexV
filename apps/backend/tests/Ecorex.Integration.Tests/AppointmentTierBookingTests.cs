using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Ecorex.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ecorex.Integration.Tests;

/// <summary>
/// F2 del motor por intervalos (ADR-0009): la reserva usa la duracion y el precio del TIER por largo de
/// cabello cuando el servicio lo define, exige el largo (gate) para esos servicios, y hace snapshot del
/// buffer del recurso en la cita. Se prueba contra SaveBookingAsync (la unica ruta de reserva).
/// </summary>
public sealed class AppointmentTierBookingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using var ctx = CreateContext(null);
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Booking_TieredService_WithoutHairLength_IsRejected_ByGate()
    {
        var (tenant, resourceId, serviceId) = await SeedTieredAsync(bufferMinutes: 0);
        var agenda = CreateAgenda(tenant);

        var req = NewRequest(resourceId, serviceId, hair: null);
        var res = await agenda.SaveBookingAsync(req, Guid.NewGuid());

        Assert.False(res.Success);
        Assert.Contains("largo", res.Error ?? "", StringComparison.OrdinalIgnoreCase);

        await using var verify = CreateContext(tenant);
        Assert.Equal(0, await verify.Appointments.CountAsync());
    }

    [Fact]
    public async Task Booking_TieredService_WithHairLength_UsesTierDurationPriceAndBuffer()
    {
        var (tenant, resourceId, serviceId) = await SeedTieredAsync(bufferMinutes: 10);
        var agenda = CreateAgenda(tenant);

        // Tier Corto = 30 min / $40.000; el servicio base es 60 min / $0.
        var req = NewRequest(resourceId, serviceId, hair: HairLength.Corto);
        var res = await agenda.SaveBookingAsync(req, Guid.NewGuid());

        Assert.True(res.Success);

        await using var verify = CreateContext(tenant);
        var appt = await verify.Appointments.SingleAsync();
        Assert.Equal(30, appt.DurationMinutes);          // duracion del tier, no la base (60)
        Assert.Equal(40000m, appt.EstimatedValue);       // precio del tier, no el base ($0)
        Assert.Equal(10, appt.BufferMinutes);            // snapshot del buffer del recurso
    }

    private BookingRequest NewRequest(Guid resourceId, Guid serviceId, HairLength? hair) => new(
        AppointmentId: null,
        ResourceId: resourceId,
        Date: new DateOnly(2026, 6, 15),
        StartTime: new TimeOnly(9, 0),
        ClientName: "Sara Ramirez",
        ClientPhone: "573000000001",
        ClientId: null,
        ServiceIds: new[] { serviceId },
        Status: AppointmentStatus.Scheduled,
        Punctuality: Punctuality.Unknown,
        Notes: null,
        ChainSteps: Array.Empty<BookingChainStep>(),
        Chat: Array.Empty<BookingChatLine>(),
        RescheduledFromId: null,
        FieldValues: null,
        HairLength: hair);

    private async Task<(Guid tenant, Guid resourceId, Guid serviceId)> SeedTieredAsync(int bufferMinutes)
    {
        var tenant = Guid.CreateVersion7();
        var resourceId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        await using (var ctx = CreateContext(null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenant, Name = "Bella Nails Studio" });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Resources.Add(new Resource { Id = resourceId, TenantId = tenant, Name = "Ana Maria Reyes", Kind = ResourceKind.Image, BufferMinutes = bufferMinutes });
            ctx.Services.Add(new Service { Id = serviceId, TenantId = tenant, Name = "Terapia Capilar", DurationMinutes = 60, Price = 0m });
            ctx.ServicePriceTiers.Add(new ServicePriceTier { TenantId = tenant, ServiceId = serviceId, Length = HairLength.Corto, DurationMinutes = 30, Price = 40000m });
            ctx.ServicePriceTiers.Add(new ServicePriceTier { TenantId = tenant, ServiceId = serviceId, Length = HairLength.Medio, DurationMinutes = 45, Price = 60000m });
            ctx.ServicePriceTiers.Add(new ServicePriceTier { TenantId = tenant, ServiceId = serviceId, Length = HairLength.Largo, DurationMinutes = 60, Price = 80000m });
            ctx.ServicePriceTiers.Add(new ServicePriceTier { TenantId = tenant, ServiceId = serviceId, Length = HairLength.MuyLargo, DurationMinutes = 75, Price = 100000m });
            await ctx.SaveChangesAsync();
        }
        return (tenant, resourceId, serviceId);
    }

    private AgendaService CreateAgenda(Guid tenant)
        => new(CreateContext(tenant), new FixedTenantContext(tenant), new NoopAudit(), TimeProvider.System);

    private EcorexDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<EcorexDbContext>()
            .UseNpgsql(_db.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenantContext, TimeProvider.System))
            .Options;
        return new EcorexDbContext(options, tenantContext);
    }

    private sealed class FixedTenantContext(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId => null;
    }

    private sealed class NoopAudit : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human)
        { }
    }
}
