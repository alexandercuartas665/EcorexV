using Ecorex.Application.Admin;
using Ecorex.Application.Agendas;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0167 (recordatorios de turnos) en matriz dual PostgreSQL / SQL Server: validacion de la regla (plantilla de la
/// linea, aprobada si es YCloud/Cloud, variables con dato), envio por la plantilla con los datos del turno, una sola
/// vez por turno y horario (aun con varias pasadas), nuevo envio tras reprogramar, omisiones con motivo, historial
/// del turno y AISLAMIENTO cross-tenant.
/// </summary>
public abstract class AgendaRecordatoriosTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected AgendaRecordatoriosTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // "Ahora" = jueves 12-nov-2026 08:00 Bogota (13:00 UTC).
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 13, 0, 0, TimeSpan.Zero);
    private DateTimeOffset _reloj = Ahora;

    private static TimeOnly H(int h, int m = 0) => new(h, m);

    private sealed record Base(Guid Tenant, Guid Servicio, Guid Linea, Guid Plantilla, Guid PlantillaYCloudBorrador, Guid LineaYCloud);

    private async Task<Base> SeedAsync(string nombre)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var root = _fixture.CreateContext(tenantId: null))
        {
            root.Tenants.Add(new Tenant { Id = tenantId, Name = nombre, TimeZoneId = "America/Bogota" });
            await root.SaveChangesAsync();
        }
        await Agendas(tenantId).CrearTiposSugeridosAsync();
        var espacio = (await Agendas(tenantId).ListTiposAsync()).Single(t => t.Nombre == "Espacio").Id;
        var recurso = (await Agendas(tenantId).SaveRecursoAsync(new SaveAgendaRecursoRequest(null, "Consultorio 3", null, espacio, null, null, false))).Id!.Value;
        var dias = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var pl = (await Agendas(tenantId).SavePlantillaAsync(new SaveAgendaPlantillaRequest(null, "Consulta", null, true,
            dias.Select(d => new AgendaFranjaDto(d, H(7), H(17), 20, 1)).ToList()))).Id!.Value;
        await Agendas(tenantId).ImportarPlantillaAsync(new ImportarPlantillaRequest(recurso, pl, new DateOnly(2026, 11, 1), null, null));
        var servicio = (await Turnos(tenantId).SaveServicioAsync(new SaveAgendaServicioRequest(null, "Consulta medicina general", null, 20, 0, 60, true, true,
            new[] { new AgendaRequisitoDto("Consultorio", new[] { recurso }) }))).Id!.Value;

        await using var db = _fixture.CreateContext(tenantId);
        var linea = new WhatsAppLine { TenantId = tenantId, InstanceName = "Recepcion", PhoneNumber = "573000000001", Status = WhatsAppLineStatus.Connected, Provider = WhatsAppProvider.Emulator };
        var lineaYc = new WhatsAppLine { TenantId = tenantId, InstanceName = "Comercial", PhoneNumber = "573000000002", Status = WhatsAppLineStatus.Connected, Provider = WhatsAppProvider.YCloud };
        db.WhatsAppLines.AddRange(linea, lineaYc);
        var plantilla = new WhatsAppTemplate
        {
            TenantId = tenantId, WhatsAppLineId = linea.Id, Name = "recordatorio_turno", Language = "es", Status = WhatsAppTemplateStatus.Approved,
            BodyText = "Hola {{cliente}}, te recordamos tu turno el {{fecha}} a las {{hora}}. Responde SI para confirmar.",
            VariablesJson = """[{"token":"cliente","example":"Ana"},{"token":"fecha","example":"martes"},{"token":"hora","example":"3 pm"}]"""
        };
        var borrador = new WhatsAppTemplate
        {
            TenantId = tenantId, WhatsAppLineId = lineaYc.Id, Name = "recordatorio_borrador", Language = "es", Status = WhatsAppTemplateStatus.Draft,
            BodyText = "Hola {{cliente}}", VariablesJson = """[{"token":"cliente","example":"Ana"}]"""
        };
        db.WhatsAppTemplates.AddRange(plantilla, borrador);
        await db.SaveChangesAsync();
        return new Base(tenantId, servicio, linea.Id, plantilla.Id, borrador.Id, lineaYc.Id);
    }

    private SaveAgendaRecordatorioReglaRequest Regla(Base b, int minutos, bool soloNoConfirmado = false, Guid? plantilla = null, Guid? linea = null,
        IReadOnlyDictionary<string, string>? variables = null) =>
        new(null, $"WhatsApp {minutos} min", true, Array.Empty<Guid>(), minutos, null, null, AgendaRecordatorioFueraHorario.Adelantar, soloNoConfirmado,
            AgendaRecordatorioCanal.WhatsApp, linea ?? b.Linea, plantilla ?? b.Plantilla, variables ?? new Dictionary<string, string>());

    [Fact]
    public async Task Regla_SeValidaContraLineaPlantillaYVariables()
    {
        var b = await SeedAsync("Rec validar");
        var fake = new FakeConnector();
        // Plantilla de otra linea.
        Assert.False((await Recordatorios(b.Tenant, fake).SaveReglaAsync(Regla(b, 60, linea: b.LineaYCloud))).Ok);
        // YCloud con plantilla en borrador: no se puede.
        var borrador = await Recordatorios(b.Tenant, fake).SaveReglaAsync(Regla(b, 60, plantilla: b.PlantillaYCloudBorrador, linea: b.LineaYCloud));
        Assert.False(borrador.Ok);
        Assert.Contains("aprobada", borrador.Error);
        // Llamada aun no.
        Assert.False((await Recordatorios(b.Tenant, fake).SaveReglaAsync(Regla(b, 60) with { Canal = AgendaRecordatorioCanal.Llamada })).Ok);
        // Horario a medias.
        Assert.False((await Recordatorios(b.Tenant, fake).SaveReglaAsync(Regla(b, 60) with { HoraDesde = H(7) })).Ok);
        // Bien: las variables cliente/fecha/hora se sugieren solas.
        var ok = await Recordatorios(b.Tenant, fake).SaveReglaAsync(Regla(b, 60));
        Assert.True(ok.Ok, ok.Error);
        var regla = Assert.Single(await Recordatorios(b.Tenant, fake).ListReglasAsync());
        Assert.Equal("{turno.hora}", regla.Variables["hora"]);
    }

    [Fact]
    public async Task Procesar_EnviaUnaVez_ReprogramarReenvia_YOmiteConMotivo()
    {
        var b = await SeedAsync("Rec enviar");
        var fake = new FakeConnector();
        Assert.True((await Recordatorios(b.Tenant, fake).SaveReglaAsync(Regla(b, 60))).Ok);

        // Turno el martes 17 a las 9:00 (reservado hoy jueves).
        var martes = new DateOnly(2026, 11, 17);
        var t = await Turnos(b.Tenant).ReservarAsync(new ReservarTurnoRequest(b.Servicio, martes, H(9), "Ana Perez", "3104567890", null));
        Assert.True(t.Ok, t.Error);

        // Hoy no toca.
        Assert.Equal(0, (await Recordatorios(b.Tenant, fake).ProcesarAsync()).Enviados);
        Assert.Empty(fake.Envios);

        // El martes a las 8:00 (1 hora antes) toca: se envia con los datos del turno.
        _reloj = new DateTimeOffset(2026, 11, 17, 13, 0, 0, TimeSpan.Zero);
        var c1 = await Recordatorios(b.Tenant, fake).ProcesarAsync();
        Assert.Equal(1, c1.Enviados);
        var envio = Assert.Single(fake.Envios);
        Assert.Equal("recordatorio_turno", envio.Plantilla);
        Assert.Equal("3104567890", envio.Telefono);
        Assert.Equal("Ana Perez", envio.Parametros[0]);
        Assert.Equal("9:00 a. m.", envio.Parametros[2]);

        // Otra pasada (o un segundo worker): no se repite.
        Assert.Equal(0, (await Recordatorios(b.Tenant, fake).ProcesarAsync()).Enviados);
        Assert.Single(fake.Envios);
        var turno = (await Turnos(b.Tenant).GetTurnoAsync(t.Id!.Value))!;
        Assert.Contains("Recordatorio por WhatsApp enviado", turno.Historial);
        var registro = Assert.Single(await Recordatorios(b.Tenant, fake).ListEnviosAsync());
        Assert.Equal(AgendaRecordatorioEnvioEstado.Enviado, registro.Estado);
        Assert.Contains("Ana Perez", registro.Detalle);

        // Reprogramado a las 9:40 con tiempo (a las 8:00 decide moverlo): el nuevo horario genera su propio recordatorio a las 8:40.
        Assert.True((await Turnos(b.Tenant).ReprogramarAsync(new ReprogramarTurnoRequest(turno.Id, martes, H(9, 40), "Llega tarde", turno.Version))).Ok);
        Assert.Equal(0, (await Recordatorios(b.Tenant, fake).ProcesarAsync()).Enviados); // 8:00: aun no toca el de 8:40
        _reloj = new DateTimeOffset(2026, 11, 17, 13, 41, 0, TimeSpan.Zero);
        Assert.Equal(1, (await Recordatorios(b.Tenant, fake).ProcesarAsync()).Enviados);
        Assert.Equal(2, fake.Envios.Count);

        // Turno reservado DESPUES del momento del aviso (a las 8:41 para las 9:20): se omite con motivo, no se envia.
        var tardio = await Turnos(b.Tenant).ReservarAsync(new ReservarTurnoRequest(b.Servicio, martes, H(9, 20), "Luis", "3001112233", null));
        Assert.True(tardio.Ok, tardio.Error);
        var c3 = await Recordatorios(b.Tenant, fake).ProcesarAsync();
        Assert.Equal(1, c3.Omitidos);
        Assert.Equal(2, fake.Envios.Count);
        Assert.Contains(await Recordatorios(b.Tenant, fake).ListEnviosAsync(), e => e.Cliente == "Luis" && e.Estado == AgendaRecordatorioEnvioEstado.Omitido);
    }

    [Fact]
    public async Task SoloSiNoConfirmado_FalloDelProveedor_YAislamiento()
    {
        var a = await SeedAsync("Rec iso A");
        var bTenant = await SeedAsync("Rec iso B");
        var fake = new FakeConnector();
        Assert.True((await Recordatorios(a.Tenant, fake).SaveReglaAsync(Regla(a, 60, soloNoConfirmado: true))).Ok);
        var martes = new DateOnly(2026, 11, 17);
        var confirmado = await Turnos(a.Tenant).ReservarAsync(new ReservarTurnoRequest(a.Servicio, martes, H(9), "Confirmado", "3000000001", null));
        var pendiente = await Turnos(a.Tenant).ReservarAsync(new ReservarTurnoRequest(a.Servicio, martes, H(9, 20), "Pendiente", "3000000002", null));
        var t1 = (await Turnos(a.Tenant).GetTurnoAsync(confirmado.Id!.Value))!;
        Assert.True((await Turnos(a.Tenant).CambiarEstadoAsync(new CambiarEstadoTurnoRequest(t1.Id, AgendaTurnoEstado.Confirmado, null, t1.Version))).Ok);

        // B no ve las reglas ni los envios de A, y su pasada no toca turnos de A.
        Assert.Empty(await Recordatorios(bTenant.Tenant, fake).ListReglasAsync());
        _reloj = new DateTimeOffset(2026, 11, 17, 13, 30, 0, TimeSpan.Zero); // 8:30: tocan el de 9:00 y el de 9:20
        Assert.Equal(0, (await Recordatorios(bTenant.Tenant, fake).ProcesarAsync()).Enviados);

        // El proveedor falla: queda Fallido con motivo y no se reintenta solo (no se le escribe dos veces al cliente).
        fake.Falla = true;
        var c = await Recordatorios(a.Tenant, fake).ProcesarAsync();
        Assert.Equal(1, c.Omitidos);   // el confirmado (solo si no confirmo)
        Assert.Equal(1, c.Fallidos);   // el pendiente: el proveedor no lo acepto
        var envios = await Recordatorios(a.Tenant, fake).ListEnviosAsync();
        Assert.Contains(envios, e => e.Cliente == "Pendiente" && e.Estado == AgendaRecordatorioEnvioEstado.Fallido && e.Detalle!.Contains("proveedor caido"));
        Assert.Empty(await Recordatorios(bTenant.Tenant, fake).ListEnviosAsync());
        Assert.Contains("No se pudo enviar el recordatorio", (await Turnos(a.Tenant).GetTurnoAsync(pendiente.Id!.Value))!.Historial);
    }

    private AgendaService Agendas(Guid tenantId) => new(_fixture.CreateContext(tenantId), new FixedTenant(tenantId));

    private AgendaTurnosService Turnos(Guid tenantId) => new(_fixture.CreateContext(tenantId), new FixedTenant(tenantId), () => _reloj);

    private AgendaRecordatoriosService Recordatorios(Guid tenantId, FakeConnector fake) =>
        new(_fixture.CreateContext(tenantId), new FixedTenant(tenantId), fake, null, () => _reloj);

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public Guid? UserId => null;
    }

    /// <summary>Conector de WhatsApp de prueba: registra los envios de plantilla; el resto no se usa.</summary>
    private sealed class FakeConnector : IWhatsAppConnectorService
    {
        public List<(Guid Linea, string Telefono, string Plantilla, IReadOnlyList<string> Parametros)> Envios { get; } = new();
        public bool Falla { get; set; }

        public Task<LineSendResult> SendTemplateAsync(Guid lineId, string phone, string templateName, string language, IReadOnlyList<string> bodyParams, Guid actorUserId, string? headerMediaType = null, string? headerMediaUrl = null, string? headerMediaFileName = null, string? attachmentBase64 = null, string? attachmentMime = null, string? attachmentFileName = null, IReadOnlyList<WhatsAppUrlButtonParam>? urlButtons = null, CancellationToken cancellationToken = default)
        {
            if (Falla)
            {
                return Task.FromResult(new LineSendResult(false, "proveedor caido"));
            }
            Envios.Add((lineId, phone, templateName, bodyParams));
            return Task.FromResult(new LineSendResult(true, null, "wamid-" + Envios.Count));
        }

        public Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> DeleteMessageForEveryoneAsync(Guid lineId, string phone, string messageId, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LineSendResult> SendReactionAsync(Guid lineId, string phone, string externalMessageId, string emoji, string? remoteJid = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvolutionMediaResult> FetchInboundMediaAsync(Guid lineId, string messageKeyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WhatsAppDeliveryProbeResult> ProbeDeliveryStatusAsync(Guid lineId, string externalId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

public sealed class AgendaRecordatoriosTests_Postgres
    : AgendaRecordatoriosTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public AgendaRecordatoriosTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

public sealed class AgendaRecordatoriosTests_SqlServer
    : AgendaRecordatoriosTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public AgendaRecordatoriosTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
