using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.External;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Gobernanza del conector de datos externos (ADR-0064) en matriz dual PostgreSQL / SQL Server.
///
/// Lo que fijan estas pruebas (los criterios de seguridad del prompt):
/// - Un tenant SIN concesion NO ve la fuente externa en su catalogo y NO puede ejecutarla (el reader
///   lanza antes de tocar nada; el executor jamas se invoca).
/// - El tenant CONCEDIDO si la ve y la puede describir.
/// - La cadena de conexion se guarda CIFRADA: en la tabla nunca aparece el texto en claro.
///
/// Las entidades del conector son de PLATAFORMA (globales): se siembran con contexto de tenant nulo. El
/// aislamiento del DATO externo no lo da un filtro por tenant (vive en otra base) sino la CONCESION
/// explicita, que es justo lo que se verifica aqui.
/// </summary>
public abstract class ExternalConnectorGovernanceTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ExternalConnectorGovernanceTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GrantedTenantSeesSource_UngrantedDoesNot()
    {
        var granted = await SeedTenantAsync("Concedido");
        var ungranted = await SeedTenantAsync("Sin concesion");
        var (_, dataSetId) = await SeedExternalSourceAsync(grantTo: granted);
        var externalKey = ExternalReportReader.KeyFor(dataSetId);

        // Tenant CONCEDIDO: ve y describe la fuente.
        await using (var ctx = _fixture.CreateContext(granted))
        {
            var reader = ExternalTestDoubles.Reader(ctx);
            Assert.True(await reader.IsGrantedAsync(dataSetId, granted));
            Assert.NotNull(await reader.DescribeAsync(dataSetId, granted));

            var catalog = BuildCatalog(ctx, granted);
            var sources = await catalog.GetSourcesAsync();
            Assert.Contains(sources, s => s.Key == externalKey && s.Kind == ReportSourceKind.External);
        }

        // Tenant SIN concesion: no la ve, no la describe, y ejecutar LANZA (sin invocar el executor).
        await using (var ctx = _fixture.CreateContext(ungranted))
        {
            var reader = ExternalTestDoubles.Reader(ctx);
            Assert.False(await reader.IsGrantedAsync(dataSetId, ungranted));
            Assert.Null(await reader.DescribeAsync(dataSetId, ungranted));

            var catalog = BuildCatalog(ctx, ungranted);
            var sources = await catalog.GetSourcesAsync();
            Assert.DoesNotContain(sources, s => s.Key == externalKey);

            await Assert.ThrowsAsync<ReportValidationException>(() =>
                reader.QueryAsync(dataSetId, new ExternalRunContext(ungranted), inputs: null));
        }
    }

    [Fact]
    public async Task RevokedGrant_RemovesAccess()
    {
        var tenant = await SeedTenantAsync("Revocacion");
        var (sourceId, dataSetId) = await SeedExternalSourceAsync(grantTo: tenant);

        await using (var ctx = _fixture.CreateContext(tenant))
        {
            Assert.True(await ExternalTestDoubles.Reader(ctx).IsGrantedAsync(dataSetId, tenant));
        }

        // Revocar la concesion (accion de plataforma; contexto de tenant nulo).
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var grant = ctx.ExternalDataSourceGrants.Single(g => g.ExternalDataSourceId == sourceId && g.TenantId == tenant);
            ctx.ExternalDataSourceGrants.Remove(grant);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenant))
        {
            Assert.False(await ExternalTestDoubles.Reader(ctx).IsGrantedAsync(dataSetId, tenant));
        }
    }

    [Fact]
    public async Task ConnectionString_IsStoredEncrypted_NeverPlaintext()
    {
        var tenant = await SeedTenantAsync("Cifrado");
        // Valor FICTICIO de prueba (no es una credencial real): la asercion de mas abajo verifica que
        // NUNCA se guarde en claro. Marcado para gitleaks para que no lo confunda con un secreto real.
        const string secret = "Server=db3dev;User Id=lector;Password=SUPERSECRETO;"; // gitleaks:allow

        Guid sourceId;
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var svc = new ExternalDataSourceService(ctx, new ReversibleProtector(), new NoAudit(), new UnusedExecutor());
            sourceId = await svc.CreateSourceAsync(
                new SaveExternalDataSourceRequest("db3dev", null, ExternalDataProvider.SqlServer, secret, IsEnabled: true),
                actorUserId: Guid.NewGuid());
        }

        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            var stored = ctx.ExternalDataSources.Single(s => s.Id == sourceId);
            Assert.NotNull(stored.ConnectionStringEncrypted);
            Assert.DoesNotContain("SUPERSECRETO", stored.ConnectionStringEncrypted);
            Assert.NotEqual(secret, stored.ConnectionStringEncrypted);
        }
    }

    // ---- Helpers ----

    private ReportCatalog BuildCatalog(EcorexDbContext ctx, Guid tenantId)
    {
        var native = new IReportableSource[] { new TaskItemReportSource(ctx) };
        return new ReportCatalog(native, new ContainerReportReader(ctx), ExternalTestDoubles.Reader(ctx), new FormResponseReportReader(ctx), new TestTenantContext(tenantId), ctx);
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<(Guid SourceId, Guid DataSetId)> SeedExternalSourceAsync(Guid grantTo)
    {
        var sourceId = Guid.CreateVersion7();
        var dataSetId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.ExternalDataSources.Add(new ExternalDataSource
        {
            Id = sourceId,
            Name = "db3dev - Maravilla",
            Provider = ExternalDataProvider.SqlServer,
            ConnectionStringEncrypted = "cifrado-de-prueba",
            IsReadOnly = true,
            IsEnabled = true
        });
        ctx.ExternalDataSets.Add(new ExternalDataSet
        {
            Id = dataSetId,
            ExternalDataSourceId = sourceId,
            Name = "ENCARGADOS",
            CommandText = "SELECT id_encargado, nombre FROM encargados WHERE sucursal = @sucursal",
            FieldsJson = ExternalDataJson.SerializeFields(new[]
            {
                new ExternalDataSetField("id_encargado", ExternalDataParameterType.Int),
                new ExternalDataSetField("nombre", ExternalDataParameterType.String)
            }),
            IsEnabled = true
        });
        ctx.ExternalDataSourceGrants.Add(new ExternalDataSourceGrant
        {
            Id = Guid.CreateVersion7(),
            ExternalDataSourceId = sourceId,
            TenantId = grantTo,
            IsEnabled = true
        });
        await ctx.SaveChangesAsync();
        return (sourceId, dataSetId);
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }

    private sealed class ReversibleProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
        public string Unprotect(string ciphertext) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext["enc:".Length..]));
    }

    private sealed class NoAudit : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human)
        {
        }
    }

    private sealed class UnusedExecutor : IExternalQueryExecutor
    {
        public Task<ReportDataSet> ExecuteAsync(ExternalQuery query, CancellationToken ct = default) =>
            throw new InvalidOperationException("No debe ejecutarse.");

        public Task<string?> TestConnectionAsync(ExternalDataProvider provider, string connectionString, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ExternalConnectorGovernanceTests_Postgres
    : ExternalConnectorGovernanceTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ExternalConnectorGovernanceTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ExternalConnectorGovernanceTests_SqlServer
    : ExternalConnectorGovernanceTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ExternalConnectorGovernanceTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
