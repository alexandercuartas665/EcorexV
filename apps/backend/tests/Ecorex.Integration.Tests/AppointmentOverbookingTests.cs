using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Ecorex.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Hito critico del producto (Modulo 2.3): el motor de agenda NUNCA permite overbooking.
/// La garantia real es el exclusion constraint GiST (ck_appointments_no_overlap) que prohibe que dos
/// citas activas del mismo (tenant, recurso, fecha) crucen su intervalo [inicio, inicio+duracion+buffer).
/// Estos tests prueban que (a) dos reservas simultaneas en el mismo cupo dejan exactamente una cita,
/// (b) dos reservas que se SOLAPAN (distinta hora pero cruzan duracion) dejan exactamente una,
/// (c) dos citas PEGADAS (back-to-back) ambas entran, (d) el buffer bloquea la siguiente dentro del
/// margen, y (e) cancelar libera el cupo para volver a reservar.
/// </summary>
public sealed class AppointmentOverbookingTests : IAsyncLifetime
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
    public async Task TwoConcurrentBookings_OnSameSlot_OnlyOneSucceeds()
    {
        var (tenant, resourceId) = await SeedTenantWithResourceAsync();
        var date = new DateOnly(2026, 6, 10);
        var time = new TimeOnly(9, 0);

        async Task<bool> TryBookAsync()
        {
            await using var ctx = CreateContext(tenant);
            ctx.Appointments.Add(new Appointment
            {
                TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = time,
                DurationMinutes = 45, Status = AppointmentStatus.Scheduled
            });
            try { await ctx.SaveChangesAsync(); return true; }
            catch (DbUpdateException) { return false; } // 23505 unique_violation -> el cupo ya se ocupo
        }

        // Cinco recepcionistas/clientes/IA intentando el MISMO cupo a la vez.
        var results = await Task.WhenAll(TryBookAsync(), TryBookAsync(), TryBookAsync(), TryBookAsync(), TryBookAsync());

        Assert.Equal(1, results.Count(ok => ok));

        await using var verify = CreateContext(tenant);
        var rows = await verify.Appointments.CountAsync(a => a.ResourceId == resourceId && a.AppointmentDate == date && a.StartTime == time);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task CancellingAppointment_FreesTheSlot_ForRebooking()
    {
        var (tenant, resourceId) = await SeedTenantWithResourceAsync();
        var date = new DateOnly(2026, 6, 11);
        var time = new TimeOnly(10, 0);

        Guid firstId;
        await using (var ctx = CreateContext(tenant))
        {
            var a = new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = time, DurationMinutes = 45, Status = AppointmentStatus.Scheduled };
            ctx.Appointments.Add(a);
            await ctx.SaveChangesAsync();
            firstId = a.Id;
        }

        // Reservar el MISMO cupo mientras la primera sigue activa debe fallar.
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Appointments.Add(new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = time, DurationMinutes = 45, Status = AppointmentStatus.Scheduled });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // Cancelar la primera libera el cupo (el indice unico es parcial: excluye Cancelled).
        await using (var ctx = CreateContext(tenant))
        {
            var a = await ctx.Appointments.FirstAsync(x => x.Id == firstId);
            a.Status = AppointmentStatus.Cancelled;
            await ctx.SaveChangesAsync();
        }

        // Ahora si se puede volver a reservar el mismo cupo.
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Appointments.Add(new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = time, DurationMinutes = 45, Status = AppointmentStatus.Scheduled });
            await ctx.SaveChangesAsync();
        }

        await using (var verify = CreateContext(tenant))
        {
            var active = await verify.Appointments.CountAsync(a => a.ResourceId == resourceId && a.AppointmentDate == date && a.StartTime == time && a.Status != AppointmentStatus.Cancelled);
            Assert.Equal(1, active);
        }
    }

    [Fact]
    public async Task TwoConcurrentBookings_OnOverlappingIntervals_OnlyOneSucceeds()
    {
        var (tenant, resourceId) = await SeedTenantWithResourceAsync();
        var date = new DateOnly(2026, 6, 12);

        // A las 9:00 un servicio de 45 min ocupa [9:00, 9:45). Otra a las 9:30 (30 min) se CRUZA con ella,
        // aunque la hora de inicio sea distinta (el viejo UNIQUE(start_time) no lo habria detectado).
        async Task<bool> TryBookAsync(TimeOnly start, int duration)
        {
            await using var ctx = CreateContext(tenant);
            ctx.Appointments.Add(new Appointment
            {
                TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = start,
                DurationMinutes = duration, Status = AppointmentStatus.Scheduled
            });
            try { await ctx.SaveChangesAsync(); return true; }
            catch (DbUpdateException) { return false; } // 23P01 exclusion_violation -> se cruza con otra cita
        }

        var results = await Task.WhenAll(
            TryBookAsync(new TimeOnly(9, 0), 45),
            TryBookAsync(new TimeOnly(9, 30), 30));

        Assert.Equal(1, results.Count(ok => ok));

        await using var verify = CreateContext(tenant);
        var rows = await verify.Appointments.CountAsync(a => a.ResourceId == resourceId && a.AppointmentDate == date && a.Status == AppointmentStatus.Scheduled);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task AdjacentBookings_BackToBack_BothSucceed()
    {
        var (tenant, resourceId) = await SeedTenantWithResourceAsync();
        var date = new DateOnly(2026, 6, 13);

        // [9:00,9:30) y [9:30,10:00): pegadas. El rango es medio-abierto '[)' -> NO se cruzan.
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Appointments.Add(new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = new TimeOnly(9, 0), DurationMinutes = 30, Status = AppointmentStatus.Scheduled });
            ctx.Appointments.Add(new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = new TimeOnly(9, 30), DurationMinutes = 30, Status = AppointmentStatus.Scheduled });
            await ctx.SaveChangesAsync(); // no debe lanzar
        }

        await using var verify = CreateContext(tenant);
        var rows = await verify.Appointments.CountAsync(a => a.ResourceId == resourceId && a.AppointmentDate == date);
        Assert.Equal(2, rows);
    }

    [Fact]
    public async Task Buffer_BlocksNextAppointment_WithinTheMargin()
    {
        var (tenant, resourceId) = await SeedTenantWithResourceAsync();
        var date = new DateOnly(2026, 6, 14);

        // 9:00, 30 min de servicio + 15 min de buffer -> bloquea [9:00, 9:45).
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Appointments.Add(new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = new TimeOnly(9, 0), DurationMinutes = 30, BufferMinutes = 15, Status = AppointmentStatus.Scheduled });
            await ctx.SaveChangesAsync();
        }

        // 9:30 cae dentro del margen de limpieza -> rechazada.
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Appointments.Add(new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = new TimeOnly(9, 30), DurationMinutes = 30, Status = AppointmentStatus.Scheduled });
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // 9:45 (justo despues del buffer) -> aceptada.
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Appointments.Add(new Appointment { TenantId = tenant, ResourceId = resourceId, AppointmentDate = date, StartTime = new TimeOnly(9, 45), DurationMinutes = 30, Status = AppointmentStatus.Scheduled });
            await ctx.SaveChangesAsync();
        }

        await using var verify = CreateContext(tenant);
        var rows = await verify.Appointments.CountAsync(a => a.ResourceId == resourceId && a.AppointmentDate == date && a.Status == AppointmentStatus.Scheduled);
        Assert.Equal(2, rows);
    }

    private async Task<(Guid tenant, Guid resourceId)> SeedTenantWithResourceAsync()
    {
        var tenant = Guid.CreateVersion7();
        var resourceId = Guid.CreateVersion7();
        await using (var ctx = CreateContext(null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenant, Name = "Bella Nails Studio" });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = CreateContext(tenant))
        {
            ctx.Resources.Add(new Resource { Id = resourceId, TenantId = tenant, Name = "Ana Maria Reyes", Kind = ResourceKind.Image });
            await ctx.SaveChangesAsync();
        }
        return (tenant, resourceId);
    }

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
}
