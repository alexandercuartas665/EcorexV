using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Auth;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Backfill del item de menu "Plantillas de correo" (v0.16.340) en matriz dual PostgreSQL / SQL Server:
/// - Se inserta justo debajo de "Plantillas WhatsApp" (mismo padre, aunque la seccion se haya renombrado) y corre
///   a los hermanos siguientes un puesto.
/// - Hereda la visibilidad del item de WhatsApp.
/// - Las vistas sin "Plantillas WhatsApp" (menus recortados por cargo) no se tocan.
/// - Es idempotente: correrlo dos veces no duplica.
/// </summary>
public abstract class PlantillasCorreoMenuBackfillTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected PlantillasCorreoMenuBackfillTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Backfill_InsertaDebajoDeWhatsApp_RespetaVistasRecortadas_YNoDuplica()
    {
        var tenantId = Guid.CreateVersion7();
        Guid completoId, recortadaId, renombradaId;
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Menu Plantillas Correo" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var completo = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = true };
            var recortada = new MenuView { TenantId = tenantId, Name = "Auxiliar", IsDefault = false };
            var renombrada = new MenuView { TenantId = tenantId, Name = "Agentes", IsDefault = false };
            ctx.MenuViews.AddRange(completo, recortada, renombrada);
            await ctx.SaveChangesAsync();
            (completoId, recortadaId, renombradaId) = (completo.Id, recortada.Id, renombrada.Id);

            // Vista completa: seccion ia con WhatsApp en el puesto 5 y voz despues.
            var ia = Node(tenantId, completo.Id, null, MenuNodeKind.Section, "Infraestructura IA", "ia", 1);
            ctx.MenuNodes.Add(ia);
            ctx.MenuNodes.AddRange(
                Node(tenantId, completo.Id, ia.Id, MenuNodeKind.Item, "Bitacora del agente", "bitacora-agente", 4),
                Node(tenantId, completo.Id, ia.Id, MenuNodeKind.Item, "Plantillas WhatsApp", "plantillas-whatsapp", 5),
                Node(tenantId, completo.Id, ia.Id, MenuNodeKind.Item, "Configuracion de voz", "config-voz", 6));

            // Vista recortada: sin Plantillas WhatsApp -> no se toca.
            var tareas = Node(tenantId, recortada.Id, null, MenuNodeKind.Section, "Mis Procesos", "misproc", 1);
            ctx.MenuNodes.Add(tareas);
            ctx.MenuNodes.Add(Node(tenantId, recortada.Id, tareas.Id, MenuNodeKind.Item, "Mis tareas", "actividades", 2));

            // Vista con la seccion renombrada y WhatsApp OCULTO: se ancla al item, hereda la visibilidad.
            var agentes = Node(tenantId, renombrada.Id, null, MenuNodeKind.Section, "Configuracion Agentes IA", "cfg-agentes", 1);
            ctx.MenuNodes.Add(agentes);
            var waOculto = Node(tenantId, renombrada.Id, agentes.Id, MenuNodeKind.Item, "Plantillas WhatsApp", "plantillas-whatsapp", 2);
            waOculto.IsVisible = false;
            ctx.MenuNodes.Add(waOculto);
            await ctx.SaveChangesAsync();
        }

        // Dos corridas (arranques sucesivos): la segunda no debe duplicar ni volver a correr hermanos.
        for (var i = 0; i < 2; i++)
        {
            await using var ctx = _fixture.CreateContext(tenantId: null);
            var seeder = new DatabaseSeeder(ctx, new Pbkdf2PasswordHasher(), NullLogger<DatabaseSeeder>.Instance);
            await seeder.EnsurePlantillasCorreoMenuItemAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var completoNodes = await ctx.MenuNodes.AsNoTracking().Where(n => n.MenuViewId == completoId).ToListAsync();
            var correo = Assert.Single(completoNodes, n => n.Route == "plantillas-correo");
            var wa = completoNodes.Single(n => n.Route == "plantillas-whatsapp");
            var voz = completoNodes.Single(n => n.Route == "config-voz");
            Assert.Equal("Plantillas de correo", correo.Name);
            Assert.Equal(wa.ParentId, correo.ParentId);
            Assert.Equal(MenuNodeKind.Item, correo.Kind);
            Assert.Equal(MenuNodeState.Ready, correo.State);
            Assert.True(correo.IsVisible);
            Assert.Equal(wa.SortOrder + 1, correo.SortOrder);
            Assert.Equal(correo.SortOrder + 1, voz.SortOrder);

            Assert.False(await ctx.MenuNodes.AsNoTracking().AnyAsync(n => n.MenuViewId == recortadaId && n.Route == "plantillas-correo"));

            var renombradaNodes = await ctx.MenuNodes.AsNoTracking().Where(n => n.MenuViewId == renombradaId).ToListAsync();
            var correoOculto = Assert.Single(renombradaNodes, n => n.Route == "plantillas-correo");
            var waRen = renombradaNodes.Single(n => n.Route == "plantillas-whatsapp");
            Assert.Equal(waRen.ParentId, correoOculto.ParentId);
            Assert.False(correoOculto.IsVisible);
        }
    }

    /// <summary>v0.16.343 (ADR-0180): "Maquetas" queda justo debajo de "Administrar actividades" en cada vista; no duplica.</summary>
    [Fact]
    public async Task BackfillMaquetas_InsertaDebajoDeAdministrarActividades_YNoDuplica()
    {
        var tenantId = Guid.CreateVersion7();
        Guid viewId;
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Menu Maquetas" });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = true };
            ctx.MenuViews.Add(view);
            await ctx.SaveChangesAsync();
            viewId = view.Id;
            var act = Node(tenantId, view.Id, null, MenuNodeKind.Section, "Sistema - Actividades", "act", 1);
            ctx.MenuNodes.Add(act);
            ctx.MenuNodes.AddRange(
                Node(tenantId, view.Id, act.Id, MenuNodeKind.Item, "Tableros", "tableros", 1),
                Node(tenantId, view.Id, act.Id, MenuNodeKind.Item, "Administrar actividades", "actividades", 2),
                Node(tenantId, view.Id, act.Id, MenuNodeKind.Item, "Conceptos", "conceptos", 3));
            await ctx.SaveChangesAsync();
        }

        for (var i = 0; i < 2; i++)
        {
            await using var ctx = _fixture.CreateContext(tenantId: null);
            var seeder = new DatabaseSeeder(ctx, new Pbkdf2PasswordHasher(), NullLogger<DatabaseSeeder>.Instance);
            await seeder.EnsureMaquetasMenuItemAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var nodes = await ctx.MenuNodes.AsNoTracking().Where(n => n.MenuViewId == viewId).ToListAsync();
            var maquetas = Assert.Single(nodes, n => n.Route == "maqueta");
            var actividades = nodes.Single(n => n.Route == "actividades");
            Assert.Equal("Maquetas", maquetas.Name);
            Assert.Equal(actividades.ParentId, maquetas.ParentId);
            Assert.Equal(actividades.SortOrder + 1, maquetas.SortOrder);
            Assert.Equal(maquetas.SortOrder + 1, nodes.Single(n => n.Route == "conceptos").SortOrder);
        }
    }

    private static MenuNode Node(Guid tenantId, Guid viewId, Guid? parentId, MenuNodeKind kind, string name, string route, int sort)
        => new()
        {
            TenantId = tenantId,
            MenuViewId = viewId,
            ParentId = parentId,
            Kind = kind,
            Name = name,
            Route = route,
            State = MenuNodeState.Ready,
            IsVisible = true,
            SortOrder = sort
        };
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class PlantillasCorreoMenuBackfillTests_Postgres : PlantillasCorreoMenuBackfillTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public PlantillasCorreoMenuBackfillTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class PlantillasCorreoMenuBackfillTests_SqlServer : PlantillasCorreoMenuBackfillTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public PlantillasCorreoMenuBackfillTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
