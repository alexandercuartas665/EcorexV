using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la FICHA DE EMPRESA (modulo 000072, adm_empresas, ADR-0026) en
/// matriz dual PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// el perfil de contacto/domicilio aditivo (City/Address/Phone/Email) que persiste y vuelve
/// en el detalle, el cambio de estado por la maquina de estados existente, y el listado de
/// usuarios del tenant ACOTADO a su tenant (cross-tenant del operador, sin fuga entre empresas).
/// </summary>
public abstract class TenantProfileTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected TenantProfileTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task UpdateProfile_PersisteCamposDeContacto_YVuelvenEnElDetalle()
    {
        var tenantId = await SeedTenantAsync("Perfil CRUD");
        await using var ctx = _fixture.CreateContext(tenantId: null);
        var service = new TenantAdminService(ctx, new AuditWriter(ctx), new NoOpMenuProvisioning());
        var actor = Guid.CreateVersion7();

        var updated = await service.UpdateProfileAsync(tenantId, new UpdateTenantProfileRequest(
            Name: "Comercial SAS",
            LegalName: "Comercial S.A.S",
            TaxId: "900555444-1",
            Country: "CO",
            Currency: "COP",
            LogoUrl: null,
            City: "Medellin",
            Address: "Calle 10 #20-30",
            Phone: "+57 604 111 2222",
            Email: "hola@comercial.local"), actor);

        Assert.NotNull(updated);
        Assert.Equal("Medellin", updated!.City);
        Assert.Equal("Calle 10 #20-30", updated.Address);
        Assert.Equal("+57 604 111 2222", updated.Phone);
        Assert.Equal("hola@comercial.local", updated.Email);

        // Persistido de verdad: un GET nuevo trae los mismos campos.
        var fetched = await service.GetAsync(tenantId);
        Assert.Equal("Medellin", fetched!.City);
        Assert.Equal("hola@comercial.local", fetched.Email);
        Assert.Equal("Comercial S.A.S", fetched.LegalName);

        // Y la fila real en BD tiene las columnas nuevas.
        var row = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);
        Assert.Equal("Medellin", row.City);
        Assert.Equal("Calle 10 #20-30", row.Address);

        // Vaciar un campo lo deja null (Normalize), sin pisar el resto.
        var cleared = await service.UpdateProfileAsync(tenantId, new UpdateTenantProfileRequest(
            Name: "Comercial SAS", LegalName: "Comercial S.A.S", TaxId: "900555444-1",
            Country: "CO", Currency: "COP", LogoUrl: null,
            City: "Medellin", Address: "   ", Phone: "+57 604 111 2222", Email: "hola@comercial.local"), actor);
        Assert.Null(cleared!.Address);
        Assert.Equal("Medellin", cleared.City);
    }

    [Fact]
    public async Task ChangeStatus_UsaLaMaquinaDeEstados_YSeReflejaEnLaFicha()
    {
        var tenantId = await SeedTenantAsync("Estado Ficha");
        await using var ctx = _fixture.CreateContext(tenantId: null);
        var service = new TenantAdminService(ctx, new AuditWriter(ctx), new NoOpMenuProvisioning());

        var suspended = await service.ChangeStatusAsync(
            tenantId, new ChangeTenantStatusRequest(TenantStatus.Suspended, "prueba"), Guid.CreateVersion7());
        Assert.Equal(TenantStatus.Suspended, suspended!.Status);
        Assert.Equal(TenantStatus.Suspended, (await service.GetAsync(tenantId))!.Status);
    }

    [Fact]
    public async Task ListUsers_EstaAcotadoAlTenant_SinFugaEntreEmpresas()
    {
        var tenantA = await SeedTenantAsync("Empresa A");
        var tenantB = await SeedTenantAsync("Empresa B");
        await SeedTenantUserAsync(tenantA, "owner@a.local", TenantRole.Owner);
        await SeedTenantUserAsync(tenantA, "asesor@a.local", TenantRole.Advisor);
        await SeedTenantUserAsync(tenantB, "owner@b.local", TenantRole.Owner);

        await using var ctx = _fixture.CreateContext(tenantId: null);
        var service = new TenantAdminService(ctx, new AuditWriter(ctx), new NoOpMenuProvisioning());

        var usersA = await service.ListUsersAsync(tenantA);
        var usersB = await service.ListUsersAsync(tenantB);

        // A ve SOLO sus 2 usuarios; B ve SOLO el suyo. Ningun correo se cruza.
        Assert.Equal(2, usersA.Count);
        Assert.All(usersA, u => Assert.EndsWith("@a.local", u.Email));
        Assert.Single(usersB);
        Assert.Equal("owner@b.local", usersB[0].Email);
        Assert.DoesNotContain(usersA, u => u.Email.EndsWith("@b.local"));

        // Orden estable por email (owner antes que asesor alfabeticamente).
        Assert.Equal("asesor@a.local", usersA[0].Email);
    }

    // =========================================================================

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task SeedTenantUserAsync(Guid tenantId, string email, TenantRole role)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        // TenantUser.PlatformUserId es FK a platform_users: hay que crear el PlatformUser primero.
        var user = new PlatformUser
        {
            Email = email,
            EmailVerified = true,
            DisplayName = email,
            Status = PlatformUserStatus.Active,
            PasswordHash = "x"
        };
        ctx.PlatformUsers.Add(user);
        ctx.TenantUsers.Add(new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = user.Id,
            Email = email,
            TenantRole = role,
            Status = PlatformUserStatus.Active
        });
        await ctx.SaveChangesAsync();
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class TenantProfileTests_Postgres
    : TenantProfileTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public TenantProfileTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class TenantProfileTests_SqlServer
    : TenantProfileTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public TenantProfileTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
