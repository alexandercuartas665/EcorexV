using Ecorex.Application.Agendas;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0165 olas 3 y 4 en matriz dual PostgreSQL / SQL Server: servicio con dos requisitos (linea con cupo 2 +
/// inspectores), separar turnos hasta agotar, reprogramar, cancelar (libera), festivo del calendario de la entidad,
/// anticipacion, ocupacion en la semana del recurso y AISLAMIENTO cross-tenant.
/// </summary>
public abstract class AgendaTurnosTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected AgendaTurnosTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // "Ahora" fijo: jueves 12-nov-2026 08:00 en Bogota (13:00 UTC).
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Lunes16 = new(2026, 11, 16);
    private static readonly DateOnly Martes17 = new(2026, 11, 17);

    private static TimeOnly H(int h, int m = 0) => new(h, m);

    private sealed record Cda(Guid Tenant, Guid Servicio, Guid Linea, Guid InspA, Guid InspB);

    private async Task<Cda> SeedCdaAsync(string nombre)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var root = _fixture.CreateContext(tenantId: null))
        {
            root.Tenants.Add(new Tenant { Id = tenantId, Name = nombre, TimeZoneId = "America/Bogota" });
            await root.SaveChangesAsync();
        }
        var ag = Agendas(tenantId);
        await ag.CrearTiposSugeridosAsync();
        var tipos = await Agendas(tenantId).ListTiposAsync();
        var equipo = tipos.Single(t => t.Nombre == "Equipo").Id;
        var espacio = tipos.Single(t => t.Nombre == "Espacio").Id;
        var linea = (await Agendas(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Linea 1", null, equipo, null, null, false))).Id!.Value;
        var inspA = (await Agendas(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Inspector A", null, espacio, null, null, false))).Id!.Value;
        var inspB = (await Agendas(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Inspector B", null, espacio, null, null, false))).Id!.Value;

        var dias = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var plLinea = (await Agendas(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "RTM", null, true,
            dias.Select(d => new AgendaFranjaDto(d, H(7), H(17), 30, 2)).ToList()))).Id!.Value;
        var plInsp = (await Agendas(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "Inspectores", null, true,
            dias.Select(d => new AgendaFranjaDto(d, H(7), H(17), 30, 1)).ToList()))).Id!.Value;
        foreach (var (rec, pl) in new[] { (linea, plLinea), (inspA, plInsp), (inspB, plInsp) })
        {
            Assert.True((await Agendas(tenantId).ImportarPlantillaAsync(new ImportarPlantillaRequest(rec, pl, new DateOnly(2026, 11, 1), null, null))).Ok);
        }
        var servicio = await Turnos(tenantId).SaveServicioAsync(new SaveAgendaServicioRequest(null, "Revision tecnico-mecanica", null, 60, 60, 30, true, true,
            new[] { new AgendaRequisitoDto("Linea", new[] { linea }), new AgendaRequisitoDto("Inspector", new[] { inspA, inspB }) }));
        Assert.True(servicio.Ok, servicio.Error);
        return new Cda(tenantId, servicio.Id!.Value, linea, inspA, inspB);
    }

    [Fact]
    public async Task Cda_SepararHastaAgotar_Reprogramar_Cancelar()
    {
        var c = await SeedCdaAsync("CDA turnos");

        ReservarTurnoRequest R(int h, string cliente) => new(c.Servicio, Martes17, H(h), cliente, "3001234567", null);

        var t1 = await Turnos(c.Tenant).ReservarAsync(R(7, "Placa ABC123"));
        var t2 = await Turnos(c.Tenant).ReservarAsync(R(7, "Placa XYZ987"));
        Assert.True(t1.Ok, t1.Error);
        Assert.True(t2.Ok, t2.Error);
        // La linea admite 2 a la vez, pero ya no quedan inspectores a las 7:00.
        var t3 = await Turnos(c.Tenant).ReservarAsync(R(7, "Placa QQQ111"));
        Assert.False(t3.Ok);
        Assert.DoesNotContain(await Turnos(c.Tenant).HuecosAsync(c.Servicio, Martes17, 1), h => h.Inicio == H(7));

        var turno1 = (await Turnos(c.Tenant).GetTurnoAsync(t1.Id!.Value))!;
        var turno2 = (await Turnos(c.Tenant).GetTurnoAsync(t2.Id!.Value))!;
        Assert.Equal(2, turno1.Recursos.Count);
        Assert.NotEqual(turno1.Recursos.Single(r => r.Requisito == "Inspector").RecursoId, turno2.Recursos.Single(r => r.Requisito == "Inspector").RecursoId);

        // Reprogramar el 2 a las 9:00 libera un inspector a las 7:00.
        var rep = await Turnos(c.Tenant).ReprogramarAsync(new ReprogramarTurnoRequest(t2.Id!.Value, Martes17, H(9), "El cliente pidio mas tarde", turno2.Version));
        Assert.True(rep.Ok, rep.Error);
        turno2 = (await Turnos(c.Tenant).GetTurnoAsync(t2.Id!.Value))!;
        Assert.Equal(H(9), turno2.HoraInicio);
        Assert.Equal(1, turno2.Reprogramaciones);
        Assert.Contains("Reprogramado", turno2.Historial);
        Assert.True((await Turnos(c.Tenant).ReservarAsync(R(7, "Placa QQQ111"))).Ok);

        // Version vieja: rechazado.
        Assert.False((await Turnos(c.Tenant).ReprogramarAsync(new ReprogramarTurnoRequest(t2.Id!.Value, Martes17, H(11), null, turno2.Version - 1))).Ok);

        // Cancelar exige motivo y libera el cupo.
        Assert.False((await Turnos(c.Tenant).CambiarEstadoAsync(new CambiarEstadoTurnoRequest(t1.Id!.Value, AgendaTurnoEstado.Cancelado, null))).Ok);
        Assert.True((await Turnos(c.Tenant).CambiarEstadoAsync(new CambiarEstadoTurnoRequest(t1.Id!.Value, AgendaTurnoEstado.Cancelado, "Vehiculo varado"))).Ok);
        Assert.Contains(await Turnos(c.Tenant).HuecosAsync(c.Servicio, Martes17, 1), h => h.Inicio == H(7));
        // Un cancelado ya no se reprograma ni vuelve atras.
        Assert.False((await Turnos(c.Tenant).CambiarEstadoAsync(new CambiarEstadoTurnoRequest(t1.Id!.Value, AgendaTurnoEstado.Confirmado, null))).Ok);

        // La semana de la linea refleja los turnos tomados (no cancelados): 7:00 (1 turno x 2 bloques) + 9:00 (2 bloques).
        var semana = await Agendas(c.Tenant).SemanaAsync(c.Linea, Martes17);
        Assert.Equal(4, semana[1].CuposOcupados);
        Assert.Equal(40 - 4, semana[1].CuposDisponibles);

        var lista = await Turnos(c.Tenant).ListTurnosAsync(Lunes16, Martes17.AddDays(5));
        Assert.Equal(3, lista.Count);
        Assert.Single(lista, t => t.Estado == AgendaTurnoEstado.Cancelado);
    }

    [Fact]
    public async Task Calendario_YAnticipacion()
    {
        var c = await SeedCdaAsync("CDA calendario");
        // Festivo del calendario de la entidad: el lunes 16 no ofrece huecos.
        await using (var db = _fixture.CreateContext(c.Tenant))
        {
            db.TenantOperatingDays.Add(new TenantOperatingDay { TenantId = c.Tenant, Date = Lunes16, Reason = "Independencia de Cartagena" });
            await db.SaveChangesAsync();
        }
        Assert.Empty(await Turnos(c.Tenant).HuecosAsync(c.Servicio, Lunes16, 1));
        Assert.False((await Turnos(c.Tenant).ReservarAsync(new ReservarTurnoRequest(c.Servicio, Lunes16, H(8), "Cliente", null, null))).Ok);

        // Hoy jueves 12 a las 8:00 con anticipacion minima de 60 min: el primer hueco es 9:00; las 8:30 se rechazan.
        var hoy = new DateOnly(2026, 11, 12);
        var huecosHoy = await Turnos(c.Tenant).HuecosAsync(c.Servicio, hoy, 1);
        Assert.Equal(H(9), huecosHoy.First().Inicio);
        Assert.False((await Turnos(c.Tenant).ReservarAsync(new ReservarTurnoRequest(c.Servicio, hoy, H(8, 30), "Cliente", null, null))).Ok);
        // Mas alla de 30 dias: rechazado.
        Assert.False((await Turnos(c.Tenant).ReservarAsync(new ReservarTurnoRequest(c.Servicio, hoy.AddDays(40), H(8), "Cliente", null, null))).Ok);

        // Un recurso con turnos proximos no se puede quitar.
        Assert.True((await Turnos(c.Tenant).ReservarAsync(new ReservarTurnoRequest(c.Servicio, Martes17, H(8), "Cliente", null, null))).Ok);
        Assert.False((await Agendas(c.Tenant).DeleteRecursoAsync(c.Linea)).Ok);
        Assert.False((await Turnos(c.Tenant).DeleteServicioAsync(c.Servicio)).Ok);
    }

    [Fact]
    public async Task Aislamiento_UnTenantNoVeNiUsaTurnosDeOtro()
    {
        var a = await SeedCdaAsync("CDA iso A");
        var b = await SeedCdaAsync("CDA iso B");
        var ta = await Turnos(a.Tenant).ReservarAsync(new ReservarTurnoRequest(a.Servicio, Martes17, H(10), "Cliente A", null, null));
        Assert.True(ta.Ok);

        Assert.DoesNotContain(await Turnos(b.Tenant).ListServiciosAsync(), s => s.Id == a.Servicio);
        Assert.Empty(await Turnos(b.Tenant).ListTurnosAsync(Lunes16, Martes17.AddDays(5)));
        Assert.Null(await Turnos(b.Tenant).GetTurnoAsync(ta.Id!.Value));
        Assert.Empty(await Turnos(b.Tenant).HuecosAsync(a.Servicio, Martes17, 1));
        Assert.False((await Turnos(b.Tenant).ReservarAsync(new ReservarTurnoRequest(a.Servicio, Martes17, H(11), "Intruso", null, null))).Ok);
        Assert.False((await Turnos(b.Tenant).CambiarEstadoAsync(new CambiarEstadoTurnoRequest(ta.Id!.Value, AgendaTurnoEstado.Cancelado, "x"))).Ok);
        Assert.False((await Turnos(b.Tenant).ReprogramarAsync(new ReprogramarTurnoRequest(ta.Id!.Value, Martes17, H(12), null))).Ok);
        // Un servicio de B no puede apuntar a recursos de A.
        Assert.False((await Turnos(b.Tenant).SaveServicioAsync(new SaveAgendaServicioRequest(null, "Robo", null, 30, 0, 30, false, true,
            new[] { new AgendaRequisitoDto("Linea", new[] { a.Linea }) }))).Ok);

        Assert.Single(await Turnos(a.Tenant).ListTurnosAsync(Lunes16, Martes17.AddDays(5)));
    }

    [Fact]
    public async Task Novedad_SobreTurnoTomado_AvisaYMarcaConflicto()
    {
        var c = await SeedCdaAsync("CDA conflicto");
        var t = await Turnos(c.Tenant).ReservarAsync(new ReservarTurnoRequest(c.Servicio, Martes17, H(10), "Placa JKL456", null, null));
        Assert.True(t.Ok, t.Error);
        var turno = (await Turnos(c.Tenant).GetTurnoAsync(t.Id!.Value))!;
        Assert.Null(turno.Conflicto);

        // Incapacidad del inspector asignado de 9:30 a 11:00: la novedad se guarda, avisa y el turno queda marcado.
        var inspector = turno.Recursos.Single(r => r.Requisito == "Inspector").RecursoId;
        var nov = await Agendas(c.Tenant).SaveNovedadAsync(new SaveAgendaNovedadRequest(null, inspector, AgendaNovedadTipo.Incapacidad,
            Martes17, Martes17, H(9, 30), H(11), "Incapacidad medica"));
        Assert.True(nov.Ok, nov.Error);
        Assert.NotNull(nov.Aviso);
        Assert.StartsWith("1 turno", nov.Aviso);
        turno = (await Turnos(c.Tenant).GetTurnoAsync(t.Id!.Value))!;
        Assert.StartsWith("Incapacidad", turno.Conflicto);

        // Al reprogramarlo a un hueco libre, el conflicto desaparece.
        var huecos = await Turnos(c.Tenant).HuecosAsync(c.Servicio, Martes17, 1);
        var libre = huecos.First(h => h.Inicio >= H(11));
        Assert.True((await Turnos(c.Tenant).ReprogramarAsync(new ReprogramarTurnoRequest(turno.Id, Martes17, libre.Inicio, "Incapacidad del inspector", turno.Version))).Ok);
        Assert.Null((await Turnos(c.Tenant).GetTurnoAsync(t.Id!.Value))!.Conflicto);

        // Una novedad que no toca turnos no avisa.
        var otra = await Agendas(c.Tenant).SaveNovedadAsync(new SaveAgendaNovedadRequest(null, c.Linea, AgendaNovedadTipo.Mantenimiento,
            new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), null, null, null));
        Assert.Null(otra.Aviso);
    }

    private AgendaService Agendas(Guid tenantId) => new(_fixture.CreateContext(tenantId), new FixedTenant(tenantId));

    private AgendaTurnosService Turnos(Guid tenantId) => new(_fixture.CreateContext(tenantId), new FixedTenant(tenantId), () => Ahora);

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public Guid? UserId => null;
    }
}

public sealed class AgendaTurnosTests_Postgres
    : AgendaTurnosTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public AgendaTurnosTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

public sealed class AgendaTurnosTests_SqlServer
    : AgendaTurnosTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public AgendaTurnosTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
