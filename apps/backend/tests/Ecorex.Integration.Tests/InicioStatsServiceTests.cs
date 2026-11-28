using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Indicadores del inicio contra BD real: la proyeccion (columna de cierre via navegacion, ventana de fechas
/// en UTC) traduce en ambos motores, el alcance "mio" filtra por asignado, un tablero restringido no revela su
/// nombre en el alcance equipo y no se cruzan tenants.
/// </summary>
public abstract class InicioStatsServiceTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected InicioStatsServiceTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.CreateCustomTimeZone("bog", TimeSpan.FromHours(-5), "bog", "bog");

    private sealed record Seed(Guid TenantId, Guid MeId);

    private async Task<Seed> SeedAsync(string name, DateTimeOffset now)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        await using var c = _fixture.CreateContext(tenantId);
        TenantUser NewUser(string tag)
        {
            var p = new PlatformUser { Email = $"{tag}-{tenantId:N}@inicio.test", EmailVerified = true, Status = PlatformUserStatus.Active };
            c.PlatformUsers.Add(p);
            var u = new TenantUser { TenantId = tenantId, PlatformUserId = p.Id, Email = p.Email };
            c.TenantUsers.Add(u);
            return u;
        }
        var meId = NewUser("yo").Id;
        var otherId = NewUser("otro").Id;
        var ventas = new TaskBoard { TenantId = tenantId, Name = "Ventas" };
        var gerencia = new TaskBoard { TenantId = tenantId, Name = "Gerencia", AllowedUserIdsJson = $"[\"{otherId}\"]" };
        c.TaskBoards.AddRange(ventas, gerencia);
        var abierta = new TaskBoardColumn { TenantId = tenantId, BoardId = ventas.Id, Name = "En curso" };
        var cierre = new TaskBoardColumn { TenantId = tenantId, BoardId = ventas.Id, Name = "Cerrado", IsDone = true };
        var gAbierta = new TaskBoardColumn { TenantId = tenantId, BoardId = gerencia.Id, Name = "En curso" };
        c.TaskBoardColumns.AddRange(abierta, cierre, gAbierta);

        var utc = now.ToUniversalTime();
        TaskItem T(string n, Guid? who, Guid board, Guid col, TaskItemStatus st, DateTimeOffset? entered = null) => new()
        {
            TenantId = tenantId,
            Number = n,
            Title = n,
            Priority = TaskPriority.Medium,
            Status = st,
            AssigneeTenantUserId = who,
            BoardId = board,
            ColumnId = col,
            ColumnEnteredAt = entered,
            CreatedAt = utc.AddDays(-1),
        };
        c.TaskItems.AddRange(
            T("T1", meId, ventas.Id, abierta.Id, TaskItemStatus.Active),
            T("T2", meId, ventas.Id, cierre.Id, TaskItemStatus.Done, utc.AddDays(-2)),
            T("T3", otherId, ventas.Id, cierre.Id, TaskItemStatus.Done, utc.AddHours(-3)),
            T("T4", otherId, gerencia.Id, gAbierta.Id, TaskItemStatus.InProgress));
        await c.SaveChangesAsync();
        return new Seed(tenantId, meId);
    }

    [Fact]
    public async Task Calcula_Mio_Y_Equipo_Con_Columna_De_Cierre_Y_Restriccion()
    {
        var now = DateTimeOffset.UtcNow;
        var s = await SeedAsync("Inicio Stats A", now);

        await using var ctx = _fixture.CreateContext(s.TenantId);
        var r = await new InicioStatsService(ctx).GetAsync(s.MeId, canSeeRestricted: false, Bogota, now);

        Assert.Equal(2, r.Equipo.Activas);
        Assert.Equal(2, r.Equipo.Cerradas7Dias);
        Assert.Contains(new InicioBoardCountDto(InicioStatsCalculator.OtrosTableros, 1), r.Equipo.ActivasPorTablero);
        Assert.DoesNotContain(r.Equipo.ActivasPorTablero, x => x.Board == "Gerencia");
        Assert.Equal(new[] { new InicioBoardCountDto("Ventas", 2) }, r.Equipo.CerradasPorTablero);

        Assert.NotNull(r.Mio);
        Assert.Equal(1, r.Mio!.Activas);
        Assert.Equal(1, r.Mio.Cerradas7Dias);
        Assert.Equal(new[] { new InicioBoardCountDto("Ventas", 1) }, r.Mio.ActivasPorTablero);

        // Owner/Admin: el restringido aparece con su nombre.
        var admin = await new InicioStatsService(ctx).GetAsync(s.MeId, canSeeRestricted: true, Bogota, now);
        Assert.Contains(new InicioBoardCountDto("Gerencia", 1), admin.Equipo.ActivasPorTablero);
    }

    [Fact]
    public async Task No_Cruza_Tenants()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync("Inicio Stats B1", now);
        var b = await SeedAsync("Inicio Stats B2", now);

        await using var ctx = _fixture.CreateContext(b.TenantId);
        var r = await new InicioStatsService(ctx).GetAsync(null, canSeeRestricted: true, Bogota, now);

        Assert.Equal(2, r.Equipo.Activas);
        Assert.Equal(4, r.Equipo.UltimosDias.Nuevas.Sum());
        Assert.Null(r.Mio);
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class InicioStatsServiceTests_Postgres
    : InicioStatsServiceTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public InicioStatsServiceTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (corre en el CI).</summary>
public sealed class InicioStatsServiceTests_SqlServer
    : InicioStatsServiceTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public InicioStatsServiceTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
