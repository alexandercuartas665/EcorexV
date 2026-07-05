using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del Cargador de contactos (modulo 000873, ADR-0024) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre: carga
/// transaccional de un CSV real (parser incluido) con duplicados detectados contra los leads
/// existentes del tenant y dentro del archivo + historial ContactImportBatch persistido con
/// los conteos, idempotencia al recargar el mismo archivo, y aislamiento cross-tenant del
/// historial y de la deteccion de duplicados.
/// </summary>
public abstract class ContactLoaderTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ContactLoaderTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Import_InsertsValidRows_SkipsDuplicatesAndInvalids_AndPersistsBatch()
    {
        var seed = await SeedTenantAsync("Loader Carga");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = BuildService(ctx, seed);

        // Lead preexistente del tenant: su telefono debe marcar duplicada a la fila del CSV.
        ctx.Leads.Add(new Lead
        {
            TenantId = seed.TenantId,
            ContactName = "Cliente Existente",
            ContactPhone = "+57 300 123 4567",
            StageId = seed.StageId,
            Status = LeadStatus.Open,
            StageChangedAt = DateTimeOffset.UtcNow
        });
        await ctx.SaveChangesAsync();

        var csv =
            "nombre,telefono,email,empresa,destino,valor_estimado\n" +
            "Ana Torres,3015550001,ana@acme.com,ACME,Bogota,\"2.500.000\"\n" +
            "Luis Gil,,luis@beta.com,Beta SAS,,\n" +
            "Repetido Telefono,3001234567,rep@x.com,,,\n" +
            "Repetido Email,3015550002,luis@beta.com,,,\n" +
            ",3015550003,sin.nombre@x.com,,,\n" +
            "Mail Malo,3015550004,no-es-email,,,\n";
        var parsed = CsvTableParser.Parse(csv);
        Assert.Empty(parsed.Errors);
        var mapping = ContactColumnMapping.AutoMap(parsed.Table.Headers);
        Assert.NotNull(mapping.Name);

        var result = await service.ImportAsync("contactos.csv", parsed.Table, mapping, seed.PlatformUserId);

        Assert.NotNull(result);
        Assert.Equal(6, result!.Total);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(2, result.Duplicates);
        Assert.Equal(2, result.Invalid);

        // Los insertados quedan en la PRIMERA etapa, asignados al importador, con email/empresa
        // en los campos configurables y actividad "lead.imported".
        var ana = await ctx.Leads.AsNoTracking().SingleAsync(l => l.ContactName == "Ana Torres");
        Assert.Equal(seed.StageId, ana.StageId);
        Assert.Equal(seed.TenantUserId, ana.AssignedToTenantUserId);
        Assert.Equal(2_500_000m, ana.EstimatedValue);
        var anaFields = JsonSerializer.Deserialize<Dictionary<string, string?>>(ana.FieldValuesJson!)!;
        Assert.Equal("ana@acme.com", anaFields["email"]);
        Assert.Equal("ACME", anaFields["empresa"]);
        Assert.Equal("Bogota", ana.Destination);
        Assert.True(await ctx.LeadActivities.AsNoTracking()
            .AnyAsync(a => a.LeadId == ana.Id && a.ActivityType == "lead.imported"));

        // Historial persistido con los conteos exactos.
        var batch = await ctx.ContactImportBatches.AsNoTracking().SingleAsync();
        Assert.Equal("contactos.csv", batch.FileName);
        Assert.Equal(6, batch.TotalRows);
        Assert.Equal(2, batch.Inserted);
        Assert.Equal(2, batch.Duplicates);
        Assert.Equal(2, batch.Invalid);

        // Idempotencia: recargar el mismo archivo no duplica nada (todo cae como duplicado).
        var again = await service.ImportAsync("contactos.csv", parsed.Table, mapping, seed.PlatformUserId);
        Assert.NotNull(again);
        Assert.Equal(0, again!.Inserted);
        Assert.Equal(4, again.Duplicates); // las 2 insertadas + las 2 duplicadas originales
        Assert.Equal(3, await ctx.Leads.CountAsync()); // existente + Ana + Luis
        Assert.Equal(2, await ctx.ContactImportBatches.CountAsync());
    }

    [Fact]
    public async Task ImportHistory_AndDuplicateDetection_AreTenantIsolated()
    {
        var seedA = await SeedTenantAsync("Loader Tenant A");
        var seedB = await SeedTenantAsync("Loader Tenant B");

        var csv = "nombre,telefono\nContacto Compartido,3025550009\n";
        var parsed = CsvTableParser.Parse(csv);
        var mapping = ContactColumnMapping.AutoMap(parsed.Table.Headers);

        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var serviceA = BuildService(ctxA, seedA);
            var resultA = await serviceA.ImportAsync("compartido.csv", parsed.Table, mapping, seedA.PlatformUserId);
            Assert.Equal(1, resultA!.Inserted);
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            var serviceB = BuildService(ctxB, seedB);

            // El historial del tenant A es invisible para B (filtro global por tenant).
            Assert.Empty(await serviceB.ListBatchesAsync());
            Assert.Empty(await ctxB.ContactImportBatches.AsNoTracking().ToListAsync());

            // Y el telefono cargado por A NO cuenta como duplicado en B: B puede insertarlo.
            var preview = await serviceB.ValidateAsync(parsed.Table, mapping);
            Assert.Equal(1, preview.Valid);
            Assert.Equal(0, preview.Duplicates);

            var resultB = await serviceB.ImportAsync("compartido.csv", parsed.Table, mapping, seedB.PlatformUserId);
            Assert.Equal(1, resultB!.Inserted);
        }

        // Cada tenant ve exactamente SU fila de historial.
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var batches = await ctxA.ContactImportBatches.AsNoTracking().ToListAsync();
            Assert.Single(batches);
            Assert.Equal(seedA.TenantId, batches[0].TenantId);
        }
    }

    // =========================================================================

    private ContactLoaderService BuildService(IApplicationDbContext ctx, SeedData seed) =>
        new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId), TimeProvider.System);

    /// <summary>Tenant minimo para el cargador: tenant + usuario + una etapa de pipeline.</summary>
    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid platformUserId, tenantUserId, stageId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platform = new PlatformUser
            {
                Email = $"importer-{tenantId:N}@loader.test",
                DisplayName = "Importer Loader",
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            ctx.PlatformUsers.Add(platform);

            var tenantUser = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = platform.Id,
                Email = platform.Email
            };
            ctx.TenantUsers.Add(tenantUser);

            var stage = new PipelineStage { TenantId = tenantId, Name = "LEAD", SortOrder = 0 };
            ctx.PipelineStages.Add(stage);
            ctx.PipelineStages.Add(new PipelineStage { TenantId = tenantId, Name = "CIERRE", SortOrder = 1 });

            await ctx.SaveChangesAsync();
            platformUserId = platform.Id;
            tenantUserId = tenantUser.Id;
            stageId = stage.Id;
        }

        return new SeedData(tenantId, platformUserId, tenantUserId, stageId);
    }

    private sealed record SeedData(Guid TenantId, Guid PlatformUserId, Guid TenantUserId, Guid StageId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ContactLoaderTests_Postgres
    : ContactLoaderTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ContactLoaderTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ContactLoaderTests_SqlServer
    : ContactLoaderTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ContactLoaderTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
