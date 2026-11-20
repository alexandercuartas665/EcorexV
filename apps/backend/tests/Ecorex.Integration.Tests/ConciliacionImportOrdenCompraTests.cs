using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// La orden de compra que cita la factura DIAN (IdCompra del bot) llega a la parrilla de conciliacion, matriz dual:
/// - Renglon nuevo: toma la OC de la factura.
/// - Renglon DIAN existente SIN OC: se completa con la de la factura.
/// - Renglon DIAN existente CON OC (a mano o por regla): no se pisa.
/// - Fantasma NEWTON promovido a DIAN: conserva la OC que ya tenia; si no tenia, toma la de la factura.
/// </summary>
public abstract class ConciliacionImportOrdenCompraTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ConciliacionImportOrdenCompraTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Importar_LlenaOcVacias_YNoPisaLasExistentes()
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Conciliacion OC", Status = TenantStatus.Active });
            await ctx.SaveChangesAsync();
        }

        var fecha = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        Guid docId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var doc = new ConciliacionDianDocumento { TenantId = tenantId, Consecutivo = "CCD-T1", Anio = 2026, Mes = 11 };
            ctx.ConciliacionDianDocumentos.Add(doc);
            ctx.ConciliacionDianBotDummies.AddRange(
                Bot(tenantId, "cufe-nuevo", "OMP0000001", fecha),
                Bot(tenantId, "cufe-sin-oc", "OMP0000002", fecha),
                Bot(tenantId, "cufe-con-oc", "OMP0000003", fecha),
                Bot(tenantId, "cufe-fantasma", "OMP0000004", fecha),
                Bot(tenantId, "cufe-fantasma-vacio", "OMP0000005", fecha));
            ctx.ConciliacionDianRenglones.AddRange(
                Renglon(tenantId, doc.Id, "cufe-sin-oc", "", OrigenRenglonConciliacionDian.Dian, fecha),
                Renglon(tenantId, doc.Id, "cufe-con-oc", "MANUAL-1", OrigenRenglonConciliacionDian.Dian, fecha),
                Renglon(tenantId, doc.Id, "cufe-fantasma", "FANT-1", OrigenRenglonConciliacionDian.Newton, fecha),
                Renglon(tenantId, doc.Id, "cufe-fantasma-vacio", "", OrigenRenglonConciliacionDian.Newton, fecha));
            await ctx.SaveChangesAsync();
            docId = doc.Id;
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var svc = new ConciliacionDianService(ctx, _fixture.CreateFactory(tenantId), new FixedTenant(tenantId), null!);
            var nuevos = await svc.ImportarDesdeBotAsync(docId);
            Assert.Equal(1, nuevos);
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var oc = await ctx.ConciliacionDianRenglones.AsNoTracking()
                .ToDictionaryAsync(r => r.Cufe, r => (r.OrdenCompraSoldarco, r.Origen));
            Assert.Equal("OMP0000001", oc["cufe-nuevo"].OrdenCompraSoldarco);
            Assert.Equal("OMP0000002", oc["cufe-sin-oc"].OrdenCompraSoldarco);
            Assert.Equal("MANUAL-1", oc["cufe-con-oc"].OrdenCompraSoldarco);
            Assert.Equal("FANT-1", oc["cufe-fantasma"].OrdenCompraSoldarco);
            Assert.Equal(OrigenRenglonConciliacionDian.Dian, oc["cufe-fantasma"].Origen);
            Assert.Equal("OMP0000005", oc["cufe-fantasma-vacio"].OrdenCompraSoldarco);
        }
    }

    private static ConciliacionDianBotDummy Bot(Guid tenantId, string cufe, string oc, DateTimeOffset fecha) => new()
    {
        TenantId = tenantId,
        Cufe = cufe,
        TipoDoc = "01",
        FechaDoc = fecha,
        NombreEmisor = "PROVEEDOR SAS",
        NitEmisor = "900111222",
        PrefijoFolio = "FE-" + cufe,
        IdCompra = oc,
        TipoPago = "Credito",
        ProveedorTecnologico = "",
    };

    private static ConciliacionDianRenglon Renglon(Guid tenantId, Guid docId, string cufe, string oc,
        OrigenRenglonConciliacionDian origen, DateTimeOffset fecha) => new()
        {
            TenantId = tenantId,
            DocumentoId = docId,
            Estado = EstadoConciliacionDian.Conciliar,
            Origen = origen,
            Cufe = cufe,
            TipoDocDian = "01",
            FechaEmision = fecha,
            NombreProveedor = "PROVEEDOR SAS",
            NitProveedor = "900111222",
            NumFacturaProveedor = "FE-" + cufe,
            OrdenCompraSoldarco = oc,
            TipoPago = "Credito",
        };

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public Guid? UserId => null;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ConciliacionImportOrdenCompraTests_Postgres : ConciliacionImportOrdenCompraTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ConciliacionImportOrdenCompraTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ConciliacionImportOrdenCompraTests_SqlServer : ConciliacionImportOrdenCompraTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ConciliacionImportOrdenCompraTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
