using Ecorex.Application.Agendas;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0165 (modulo de Agendas, olas 1 y 2) en matriz dual PostgreSQL / SQL Server: tipos sugeridos, recursos,
/// agendas base, importar una plantilla como copia independiente, novedades + calendario en la disponibilidad,
/// validaciones, concurrencia optimista, borrado logico y AISLAMIENTO cross-tenant.
/// </summary>
public abstract class AgendasTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected AgendasTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    private static AgendaFranjaDto F(DayOfWeek d, int hi, int hf, int iv = 30, int cupo = 1) =>
        new(d, new TimeOnly(hi, 0), new TimeOnly(hf, 0), iv, cupo);

    private static readonly AgendaFranjaDto[] Oficina =
    {
        F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Monday, 14, 18),
        F(DayOfWeek.Tuesday, 8, 12), F(DayOfWeek.Tuesday, 14, 18)
    };

    [Fact]
    public async Task Plantilla_SeImportaComoCopiaIndependiente()
    {
        var tenantId = await SeedTenantAsync("Agendas importar");
        Assert.Equal(6, await Service(tenantId).CrearTiposSugeridosAsync());
        Assert.Equal(0, await Service(tenantId).CrearTiposSugeridosAsync()); // idempotente

        var tipos = await Service(tenantId).ListTiposAsync();
        var espacio = tipos.Single(t => t.Nombre == "Espacio");
        var rec = await Service(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Sala de juntas", "Aforo 8", espacio.Id, null, null, AgenteHabilitado: true));
        Assert.True(rec.Ok, rec.Error);

        var pl = await Service(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "Jornada oficina", null, true, Oficina));
        Assert.True(pl.Ok, pl.Error);
        var imp = await Service(tenantId).ImportarPlantillaAsync(new ImportarPlantillaRequest(rec.Id!.Value, pl.Id!.Value, new DateOnly(2026, 11, 1), null, null));
        Assert.True(imp.Ok, imp.Error);

        var vig = Assert.Single(await Service(tenantId).ListVigenciasAsync(rec.Id!.Value));
        Assert.Equal("Jornada oficina", vig.Nombre);
        Assert.Equal(32, vig.CuposSemana);
        Assert.False(vig.DifiereDeOrigen);

        // Editar la copia (el martes solo en la manana) no toca la plantilla.
        var editadas = Oficina.Where(f => !(f.Dia == DayOfWeek.Tuesday && f.Inicio.Hour == 14)).ToList();
        Assert.True((await Service(tenantId).SaveVigenciaAsync(new SaveVigenciaRequest(vig.Id, "Oficina Sala", vig.Desde, null, editadas))).Ok);
        vig = Assert.Single(await Service(tenantId).ListVigenciasAsync(rec.Id!.Value));
        Assert.Equal(24, vig.CuposSemana);
        Assert.True(vig.DifiereDeOrigen);
        Assert.Equal(32, (await Service(tenantId).ListPlantillasAsync()).Single().CuposSemana);

        // Quitar la plantilla (borrado logico) no toca la agenda del recurso.
        Assert.True((await Service(tenantId).DeletePlantillaAsync(pl.Id!.Value)).Ok);
        Assert.Empty(await Service(tenantId).ListPlantillasAsync());
        vig = Assert.Single(await Service(tenantId).ListVigenciasAsync(rec.Id!.Value));
        Assert.Equal(24, vig.CuposSemana);
        await using (var db = _fixture.CreateContext(tenantId))
        {
            Assert.True(await db.AgendaPlantillas.IgnoreQueryFilters().AnyAsync(p => p.Id == pl.Id && p.IsDeleted));
        }

        var recurso = (await Service(tenantId).GetRecursoAsync(rec.Id!.Value))!;
        Assert.Equal("Espacio", recurso.TipoNombre);
        Assert.True(recurso.AgenteHabilitado);
    }

    [Fact]
    public async Task Semana_DescuentaFestivoYNovedades()
    {
        var tenantId = await SeedTenantAsync("Agendas semana");
        await Service(tenantId).CrearTiposSugeridosAsync();
        var equipo = (await Service(tenantId).ListTiposAsync()).Single(t => t.Nombre == "Equipo");
        var rec = await Service(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Cortadora laser", null, equipo.Id, null, null, false));
        var pl = await Service(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "Planta", null, true,
            new[] { F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Tuesday, 8, 12), F(DayOfWeek.Wednesday, 8, 12, cupo: 2) }));
        await Service(tenantId).ImportarPlantillaAsync(new ImportarPlantillaRequest(rec.Id!.Value, pl.Id!.Value, new DateOnly(2026, 1, 1), null, "Planta"));

        // Lunes 16-nov-2026 festivo; martes mantenimiento 10:00-12:00.
        await using (var db = _fixture.CreateContext(tenantId))
        {
            db.TenantOperatingDays.Add(new TenantOperatingDay { TenantId = tenantId, Date = new DateOnly(2026, 11, 16), Reason = "Festivo" });
            await db.SaveChangesAsync();
        }
        Assert.True((await Service(tenantId).SaveNovedadAsync(new SaveAgendaNovedadRequest(null, rec.Id!.Value, AgendaNovedadTipo.Mantenimiento,
            new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 17), new TimeOnly(10, 0), new TimeOnly(12, 0), "Cambio de boquilla"))).Ok);
        // Hora a medias se rechaza.
        Assert.False((await Service(tenantId).SaveNovedadAsync(new SaveAgendaNovedadRequest(null, rec.Id!.Value, AgendaNovedadTipo.Otro,
            new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 18), new TimeOnly(10, 0), null, null))).Ok);

        var semana = await Service(tenantId).SemanaAsync(rec.Id!.Value, new DateOnly(2026, 11, 19));
        Assert.Equal(7, semana.Count);
        Assert.Equal(new DateOnly(2026, 11, 16), semana[0].Fecha);
        Assert.Equal("Festivo", semana[0].Cierre);
        Assert.Equal(0, semana[0].CuposDisponibles);
        Assert.Equal(8, semana[1].CuposTotales);
        Assert.Equal(4, semana[1].CuposDisponibles);
        Assert.Equal(16, semana[2].CuposDisponibles); // cupo 2 en paralelo
        Assert.Equal(0, semana[3].CuposTotales);       // jueves sin franjas
    }

    [Fact]
    public async Task Validaciones_YConcurrencia()
    {
        var tenantId = await SeedTenantAsync("Agendas validar");
        await Service(tenantId).CrearTiposSugeridosAsync();
        var persona = (await Service(tenantId).ListTiposAsync()).Single(t => t.Nombre == "Persona");

        // Un tipo que se vincula a usuario exige elegirlo.
        var sinUsuario = await Service(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Asesor", null, persona.Id, null, null, false));
        Assert.False(sinUsuario.Ok);
        Assert.Contains("usuario", sinUsuario.Error);

        // Franjas que se cruzan.
        var cruce = await Service(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "Mala", null, true,
            new[] { F(DayOfWeek.Monday, 8, 12), F(DayOfWeek.Monday, 11, 13) }));
        Assert.False(cruce.Ok);

        // Nombre duplicado (sin importar mayusculas).
        Assert.True((await Service(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "Turno A", null, true, Oficina))).Ok);
        Assert.False((await Service(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "turno a", null, true, Oficina))).Ok);

        // Concurrencia optimista: guardar con una Version vieja se rechaza.
        var pl = (await Service(tenantId).ListPlantillasAsync()).Single();
        Assert.True((await Service(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(pl.Id, pl.Nombre, "v2", true, Oficina, pl.Version))).Ok);
        var vieja = await Service(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(pl.Id, pl.Nombre, "v3", true, Oficina, pl.Version));
        Assert.False(vieja.Ok);
        Assert.Contains("Otra persona", vieja.Error);

        // Duplicar crea "(copia)".
        var dup = await Service(tenantId).DuplicarPlantillaAsync(pl.Id);
        Assert.True(dup.Ok, dup.Error);
        Assert.Contains((await Service(tenantId).ListPlantillasAsync()), p => p.Nombre == "Turno A (copia)" && p.CuposSemana == 32);

        // Un tipo en uso no se puede quitar.
        var espacio = (await Service(tenantId).ListTiposAsync()).Single(t => t.Nombre == "Espacio");
        await Service(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Sala", null, espacio.Id, null, null, false));
        Assert.False((await Service(tenantId).DeleteTipoAsync(espacio.Id)).Ok);
    }

    [Fact]
    public async Task Aislamiento_UnTenantNoVeNiUsaLasAgendasDeOtro()
    {
        var a = await SeedTenantAsync("Agendas iso A");
        var b = await SeedTenantAsync("Agendas iso B");
        await Service(a).CrearTiposSugeridosAsync();
        var tipoA = (await Service(a).ListTiposAsync()).Single(t => t.Nombre == "Espacio");
        var recA = await Service(a).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Sala A", null, tipoA.Id, null, null, false));
        var plA = await Service(a).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "Plantilla A", null, true, Oficina));
        await Service(a).ImportarPlantillaAsync(new ImportarPlantillaRequest(recA.Id!.Value, plA.Id!.Value, new DateOnly(2026, 1, 1), null, null));

        Assert.Empty(await Service(b).ListTiposAsync());
        Assert.Empty(await Service(b).ListRecursosAsync());
        Assert.Empty(await Service(b).ListPlantillasAsync());
        Assert.Null(await Service(b).GetRecursoAsync(recA.Id!.Value));
        Assert.Empty(await Service(b).ListVigenciasAsync(recA.Id!.Value));

        // B no puede usar ids de A: ni su tipo, ni su plantilla, ni su recurso.
        await Service(b).CrearTiposSugeridosAsync();
        var tipoB = (await Service(b).ListTiposAsync()).Single(t => t.Nombre == "Espacio");
        Assert.False((await Service(b).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Intruso", null, tipoA.Id, null, null, false))).Ok);
        var recB = await Service(b).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Sala B", null, tipoB.Id, null, null, false));
        Assert.False((await Service(b).ImportarPlantillaAsync(new ImportarPlantillaRequest(recB.Id!.Value, plA.Id!.Value, new DateOnly(2026, 1, 1), null, null))).Ok);
        Assert.False((await Service(b).DeleteRecursoAsync(recA.Id!.Value)).Ok);
        Assert.False((await Service(b).SaveNovedadAsync(new SaveAgendaNovedadRequest(null, recA.Id!.Value, AgendaNovedadTipo.Otro,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), null, null, null))).Ok);

        // A sigue intacto.
        Assert.Single(await Service(a).ListRecursosAsync());
        Assert.Single(await Service(a).ListVigenciasAsync(recA.Id!.Value));
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var root = _fixture.CreateContext(tenantId: null);
        root.Tenants.Add(new Tenant { Id = tenantId, Name = name, TimeZoneId = "America/Bogota" });
        await root.SaveChangesAsync();
        return tenantId;
    }

    /// <summary>Servicio con un contexto NUEVO por llamada (como cada request/circuito), atado al tenant.</summary>
    private AgendaService Service(Guid tenantId) => new(_fixture.CreateContext(tenantId), new FixedTenant(tenantId));

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public Guid? UserId => null;
    }
}

public sealed class AgendasTests_Postgres
    : AgendasTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public AgendasTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

public sealed class AgendasTests_SqlServer
    : AgendasTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public AgendasTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
