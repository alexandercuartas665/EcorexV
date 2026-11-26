using System.Text.Json;
using Ecorex.Application.Agents;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.DataLookups;
using Ecorex.Application.Directorio;
using Ecorex.Application.Mcp;
using Ecorex.Application.MenuConfig;
using Ecorex.Application.Organization;
using Ecorex.Application.Roles;
using Ecorex.Application.Scraping;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Servidor MCP de tableros (ADR-0159) en matriz dual PostgreSQL / SQL Server:
/// - Token PERSONAL: resuelve tenant+usuario; revocado, de otro alcance (admin) o de usuario inactivo NO resuelve.
/// - Visibilidad: la IA de un usuario NO ve tableros restringidos a otros ni sus tareas (responde "no encontre").
/// - Flujo: una tarea con flujo en curso NO se mueve de columna a mano; se avanza con tomar_paso/completar_paso,
///   y la bitacora queda a nombre del usuario "(via IA)".
/// </summary>
public abstract class McpTablerosTestsBase
{
    private const string FlowXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="mcp" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Mcp">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_Rev" name="Revision" />
            <bpmn:task id="Task_Cierre" name="Cierre" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_Rev" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_Rev" targetRef="Task_Cierre" />
            <bpmn:sequenceFlow id="F3" sourceRef="Task_Cierre" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private readonly TenantIsolationDbFixture _fixture;

    protected McpTablerosTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PersonalToken_ResolvesOwnerUser_AndRejectsRevokedAdminScopeAndInactive()
    {
        var seed = await SeedTenantAsync("Mcp Tokens");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var svc = new PersonalAccessTokenService(ctx, new AuditWriter(ctx));

        var issued = await svc.IssueAsync(seed.MemberUserId, seed.MemberPlatformId, "Claude portatil");
        Assert.StartsWith(ApiTokenHasher.TokenPrefix, issued.PlainToken);
        Assert.Equal(TenantApiToken.ScopeMcpTableros, issued.Token.Scope);

        var actor = await svc.ResolveAsync(issued.PlainToken, TenantApiToken.ScopeMcpTableros);
        Assert.NotNull(actor);
        Assert.Equal(seed.TenantId, actor!.TenantId);
        Assert.Equal(seed.MemberUserId, actor.TenantUserId);
        Assert.Equal(seed.MemberPlatformId, actor.PlatformUserId);
        Assert.False(actor.IsOwnerOrAdmin);

        // El hash, no el valor, es lo que se persiste.
        var row = await ctx.TenantApiTokens.AsNoTracking().SingleAsync(t => t.Id == issued.Token.Id);
        Assert.NotEqual(issued.PlainToken, row.TokenHash);
        Assert.NotNull(row.LastUsedAt);

        // Un token personal no sirve para otro alcance (p.ej. la API de configuracion "admin").
        Assert.Null(await svc.ResolveAsync(issued.PlainToken, TenantApiToken.ScopeAdmin));

        // Un token de tenant (admin, sin dueno) no abre el MCP.
        var adminPlain = ApiTokenHasher.Generate();
        ctx.TenantApiTokens.Add(new TenantApiToken { TenantId = seed.TenantId, Name = "script", TokenHash = ApiTokenHasher.Hash(adminPlain) });
        await ctx.SaveChangesAsync();
        Assert.Null(await svc.ResolveAsync(adminPlain, TenantApiToken.ScopeMcpTableros));

        // Otro usuario no puede revocar el token ajeno; el dueno si.
        Assert.False(await svc.RevokeAsync(seed.OwnerUserId, seed.OwnerPlatformId, issued.Token.Id));
        Assert.True(await svc.RevokeAsync(seed.MemberUserId, seed.MemberPlatformId, issued.Token.Id));
        Assert.Null(await svc.ResolveAsync(issued.PlainToken, TenantApiToken.ScopeMcpTableros));

        // Usuario desactivado: sus tokens dejan de resolver.
        var second = await svc.IssueAsync(seed.MemberUserId, seed.MemberPlatformId, "Gemini");
        var member = await ctx.TenantUsers.SingleAsync(u => u.Id == seed.MemberUserId);
        member.Status = PlatformUserStatus.Suspended;
        await ctx.SaveChangesAsync();
        Assert.Null(await svc.ResolveAsync(second.PlainToken, TenantApiToken.ScopeMcpTableros));

        Assert.Equal(2, (await svc.ListMineAsync(seed.MemberUserId)).Count);
    }

    [Fact]
    public async Task RestrictedBoard_IsInvisibleToOtherUsersAi_ButVisibleToOwner()
    {
        var seed = await SeedTenantAsync("Mcp Visibilidad");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, _, toolset) = Build(ctx, tenantCtx);

        var privado = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Gerencia privada",
            AllowedUserIds: new[] { seed.OwnerUserId }), seed.OwnerPlatformId, "Owner")).Value!;
        var publico = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Soporte"), seed.OwnerPlatformId, "Owner")).Value!;
        var colPriv = await FirstColumnAsync(ctx, privado.Id);
        var secreta = (await boards.QuickCreateTaskAsync(new QuickCreateTaskRequest(privado.Id, colPriv, "Presupuesto confidencial"),
            seed.OwnerPlatformId, "Owner")).Value!.Item.Number;

        var member = Actor(seed, owner: false);
        var owner = Actor(seed, owner: true);

        // listar_tableros: el miembro solo ve "Soporte"; el owner ve ambos.
        var memberBoards = Payload(await toolset.ExecuteAsync("listar_tableros", "{}", member));
        var names = memberBoards.GetProperty("tableros").EnumerateArray().Select(b => b.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Soporte", names);
        Assert.DoesNotContain("Gerencia privada", names);
        var ownerBoards = Payload(await toolset.ExecuteAsync("listar_tableros", "{}", owner));
        Assert.Equal(2, ownerBoards.GetProperty("tableros").GetArrayLength());

        // ver_tarea / comentar de una tarea del tablero privado: "no encontre" (no revela que existe).
        var view = await toolset.ExecuteAsync("ver_tarea", Args(new { numero = secreta }), member);
        Assert.True(view.IsError);
        Assert.Contains("No encontre", view.Json);
        var comment = await toolset.ExecuteAsync("comentar", Args(new { numero = secreta, texto = "hola" }), member);
        Assert.True(comment.IsError);
        var board = await toolset.ExecuteAsync("ver_tablero", Args(new { tablero = "Gerencia privada" }), member);
        Assert.True(board.IsError);

        // buscar_tareas tampoco la devuelve al miembro, si al owner.
        var memberSearch = Payload(await toolset.ExecuteAsync("buscar_tareas", Args(new { texto = "confidencial" }), member));
        Assert.Equal(0, memberSearch.GetProperty("total").GetInt32());
        var ownerSearch = Payload(await toolset.ExecuteAsync("buscar_tareas", Args(new { texto = "confidencial" }), owner));
        Assert.Equal(1, ownerSearch.GetProperty("total").GetInt32());

        // El owner si la ve.
        var ownerView = await toolset.ExecuteAsync("ver_tarea", Args(new { numero = secreta }), owner);
        Assert.False(ownerView.IsError, ownerView.Json);

        // El miembro crea, comenta y gestiona checklist en el tablero publico (queda a su nombre "via IA").
        var created = Payload(await toolset.ExecuteAsync("crear_tarea",
            Args(new { tablero = "Soporte", titulo = "Revisar impresora", prioridad = "alta", asignado = seed.MemberEmail }), member));
        var numero = created.GetProperty("numero").GetString()!;
        Assert.Equal("Por hacer", created.GetProperty("columna").GetString());
        Assert.False((await toolset.ExecuteAsync("comentar", Args(new { numero, texto = "Ya llame al proveedor" }), member)).IsError);
        Assert.False((await toolset.ExecuteAsync("checklist", Args(new { numero, accion = "agregar", texto = "Cambiar toner" }), member)).IsError);
        Assert.False((await toolset.ExecuteAsync("checklist", Args(new { numero, accion = "marcar", item = "1" }), member)).IsError);
        var moved = await toolset.ExecuteAsync("mover_tarea", Args(new { numero, columna = "En progreso" }), member);
        Assert.False(moved.IsError, moved.Json);

        var task = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Number == numero);
        Assert.Equal(seed.MemberUserId, task.AssigneeTenantUserId);
        Assert.Equal(publico.Id, task.BoardId);
        var detail = Payload(await toolset.ExecuteAsync("ver_tarea", Args(new { numero }), member));
        Assert.Equal("En progreso", detail.GetProperty("columna").GetString());
        Assert.True(detail.GetProperty("checklist")[0].GetProperty("hecho").GetBoolean());
        Assert.Contains(detail.GetProperty("bitacora").EnumerateArray(),
            a => (a.GetProperty("autor").GetString() ?? "").EndsWith("(via IA)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TerminarTarea_LlevaUnaTareaPendienteAHecho_PorLaMaquinaDeEstados()
    {
        var seed = await SeedTenantAsync("Mcp Terminar");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, _, toolset) = Build(ctx, tenantCtx);

        await boards.CreateBoardAsync(new CreateActivityBoardRequest("Soporte"), seed.OwnerPlatformId, "Owner");
        var member = Actor(seed, owner: false);

        var created = Payload(await toolset.ExecuteAsync("crear_tarea",
            Args(new { tablero = "Soporte", titulo = "Ajuste de facturacion" }), member));
        var numero = created.GetProperty("numero").GetString()!;
        Assert.Equal("Por hacer", created.GetProperty("columna").GetString());

        // Mover a la columna de cierre NO basta: la maquina no permite Pending -> Done, la tarjeta se mueve con aviso.
        var movedOnly = Payload(await toolset.ExecuteAsync("mover_tarea", Args(new { numero, columna = "Completado" }), member));
        Assert.Equal("Completado", movedOnly.GetProperty("columna").GetString());
        Assert.Equal("pendiente", movedOnly.GetProperty("estado").GetString());

        // terminar_tarea SI la deja en Hecho, caminando por InProgress.
        var done = Payload(await toolset.ExecuteAsync("terminar_tarea", Args(new { numero }), member));
        Assert.Equal("terminada", done.GetProperty("estado").GetString());
        var task = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Number == numero);
        Assert.Equal(TaskItemStatus.Done, task.Status);

        // Idempotente: repetir no falla y avisa que ya estaba terminada.
        var again = Payload(await toolset.ExecuteAsync("terminar_tarea", Args(new { numero }), member));
        Assert.Equal("terminada", again.GetProperty("estado").GetString());
        Assert.Contains("ya estaba", again.GetProperty("aviso").GetString() ?? "");
    }

    [Fact]
    public async Task TerminarTarea_TareaConFlujoEnCurso_SeRechaza()
    {
        var seed = await SeedTenantAsync("Mcp Terminar Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, engine, toolset) = Build(ctx, tenantCtx);

        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("MCP-FIN", "Flujo Fin", FlowXml))).Value!;
        Assert.True((await engine.PublishAsync(imported.Id)).IsOk);
        var type = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        type.WorkflowDefinitionId = imported.Id;
        await ctx.SaveChangesAsync();

        await boards.CreateBoardAsync(new CreateActivityBoardRequest("Procesos"), seed.OwnerPlatformId, "Owner");
        var owner = Actor(seed, owner: true);
        var created = Payload(await toolset.ExecuteAsync("crear_tarea", Args(new { tablero = "Procesos", titulo = "Con flujo" }), owner));
        Assert.True(created.GetProperty("flujo_iniciado").GetBoolean());
        var numero = created.GetProperty("numero").GetString()!;

        var res = await toolset.ExecuteAsync("terminar_tarea", Args(new { numero }), owner);
        Assert.True(res.IsError);
        Assert.Contains("completar_paso", res.Json);
    }

    [Fact]
    public async Task TaskWithRunningFlow_CannotBeMovedByHand_AndAdvancesWithCompletarPaso()
    {
        var seed = await SeedTenantAsync("Mcp Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, engine, toolset) = Build(ctx, tenantCtx);

        // Flujo Revision -> Cierre; el miembro es candidato (por cargo) de ambos pasos.
        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("MCP-01", "Flujo MCP", FlowXml))).Value!;
        Assert.True((await engine.PublishAsync(imported.Id)).IsOk);
        var type = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        type.WorkflowDefinitionId = imported.Id;
        await ctx.SaveChangesAsync();

        var org = new OrgUnitService(ctx, tenantCtx);
        var dep = (await org.CreateAsync(new SaveOrgUnitRequest("Operaciones", Classifier: OrgUnitClassifier.Dependencia))).Value!;
        var cargo = (await org.CreateAsync(new SaveOrgUnitRequest("Analista", ParentId: dep.Id, Classifier: OrgUnitClassifier.Cargo))).Value!;
        Assert.True((await org.CreateAsync(new SaveOrgUnitRequest("Analista Persona", ParentId: cargo.Id,
            Classifier: OrgUnitClassifier.Funcionario, TenantUserId: seed.MemberUserId))).IsOk);
        var policies = new WorkflowNodePolicyService(ctx, tenantCtx);
        foreach (var n in imported.Nodes.Where(n => n.BpmnElementId is "Task_Rev" or "Task_Cierre"))
        {
            Assert.True((await policies.AddNodePolicyAsync(n.Id, cargo.Id)).IsOk);
        }

        await boards.CreateBoardAsync(new CreateActivityBoardRequest("Procesos"), seed.OwnerPlatformId, "Owner");
        var member = Actor(seed, owner: false);

        var created = Payload(await toolset.ExecuteAsync("crear_tarea", Args(new { tablero = "Procesos", titulo = "Solicitud con flujo" }), member));
        Assert.True(created.GetProperty("flujo_iniciado").GetBoolean());
        var numero = created.GetProperty("numero").GetString()!;

        // Regla clave: no se mueve de columna a mano.
        var move = await toolset.ExecuteAsync("mover_tarea", Args(new { numero, columna = "Completado" }), member);
        Assert.True(move.IsError);
        Assert.Contains("completar_paso", move.Json);

        // El paso aparece en sus pendientes y en ver_tarea.
        var pending = Payload(await toolset.ExecuteAsync("mis_pasos_pendientes", "{}", member));
        Assert.Equal(1, pending.GetProperty("total").GetInt32());
        var flow = Payload(await toolset.ExecuteAsync("ver_tarea", Args(new { numero }), member)).GetProperty("flujo");
        Assert.Equal("Revision", flow.GetProperty("pasos_actuales")[0].GetProperty("paso").GetString());

        // Tomar y cerrar: avanza a Cierre.
        var claim = await toolset.ExecuteAsync("tomar_paso", Args(new { numero }), member);
        Assert.False(claim.IsError, claim.Json);
        var done = Payload(await toolset.ExecuteAsync("completar_paso", Args(new { numero, comentario = "Revisado por la IA" }), member));
        Assert.Equal("Revision", done.GetProperty("paso_cerrado").GetString());
        Assert.Equal("Cierre", done.GetProperty("siguientes_pasos")[0].GetProperty("paso").GetString());
    }

    /// <summary>
    /// Regresion del iniciador (ADR-0159): los llamadores pasan el id de PLATAFORMA al crear la tarea; el motor
    /// debe normalizarlo a TenantUser para que un paso "hereda al iniciador" quede asignado a quien la creo
    /// (antes quedaba sin encargado y nadie podia atenderlo). Tenant que clasifica solo por CONCEPTO.
    /// </summary>
    [Fact]
    public async Task InheritStartFlow_AssignsCreator_AndAiClosesItByConcept()
    {
        var seed = await SeedTenantAsync("Mcp Iniciador");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, engine, toolset) = Build(ctx, tenantCtx);

        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("MCP-02", "Flujo iniciador", FlowXml))).Value!;
        var design = new WorkflowDesignService(ctx, engine);
        foreach (var n in imported.Nodes.Where(n => n.BpmnElementId is "Task_Rev" or "Task_Cierre"))
        {
            Assert.True((await design.SetNodeAssigneeAsync(n.Id, WorkflowAssigneeSource.InheritStart, null)).IsOk);
        }
        Assert.True((await engine.PublishAsync(imported.Id)).IsOk);

        // Sin tipos de actividad activos: se clasifica por concepto (ligado al flujo y al tablero).
        var type = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        type.IsArchived = true;
        var board = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Ventas"), seed.OwnerPlatformId, "Owner")).Value!;
        var cat = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-90", Nombre = "Pruebas" };
        ctx.ActividadCategorias.Add(cat);
        ctx.ActividadSubcategorias.Add(new ActividadSubcategoria
        {
            TenantId = seed.TenantId,
            CategoriaId = cat.Id,
            Codigo = "CAT-90-01",
            Nombre = "Cotizacion IA",
            WorkflowDefinitionId = imported.Id,
            TaskBoardId = board.Id
        });
        await ctx.SaveChangesAsync();

        var member = Actor(seed, owner: false);
        var noConcept = await toolset.ExecuteAsync("crear_tarea", Args(new { tablero = "Ventas", titulo = "x" }), member);
        Assert.False(noConcept.IsError, noConcept.Json); // un solo concepto en el tablero -> se usa ese

        var created = Payload(await toolset.ExecuteAsync("crear_tarea", Args(new { concepto = "Cotizacion IA", titulo = "Cotizar portatiles" }), member));
        Assert.True(created.GetProperty("flujo_iniciado").GetBoolean());
        var numero = created.GetProperty("numero").GetString()!;

        // El paso quedo asignado al creador: es "mio" sin tener que tomarlo, y lo cierra hasta el final.
        var pending = Payload(await toolset.ExecuteAsync("mis_pasos_pendientes", "{}", member)).GetProperty("pasos");
        Assert.Contains(pending.EnumerateArray(), p => p.GetProperty("tarea").GetString() == numero && p.GetProperty("es_mio").GetBoolean());
        Assert.False((await toolset.ExecuteAsync("completar_paso", Args(new { numero }), member)).IsError);
        var last = Payload(await toolset.ExecuteAsync("completar_paso", Args(new { numero }), member));
        Assert.Equal("Cierre", last.GetProperty("paso_cerrado").GetString());
    }

    /// <summary>
    /// Grilla del Directorio (ADR-0207): con IncludeAllFields el listado trae TODOS los campos de ficha (no solo los
    /// marcados como filtro, sin separadores) y los datos de contacto; sin la marca conserva el comportamiento de antes.
    /// </summary>
    [Fact]
    public async Task DirectorioListado_IncludeAllFields_TraeTodosLosCamposDeFicha()
    {
        var seed = await SeedTenantAsync("Directorio Grilla");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenant = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        ctx.TerceroFieldDefinitions.Add(new TerceroFieldDefinition
        {
            TenantId = seed.TenantId,
            FichaKey = "cliente",
            FieldKey = "segmento",
            Label = "Segmento",
            FieldType = TerceroFieldType.Text,
            ShowInFilter = true
        });
        ctx.TerceroFieldDefinitions.Add(new TerceroFieldDefinition
        {
            TenantId = seed.TenantId,
            FichaKey = "cliente",
            FieldKey = "subsector",
            Label = "Subsector",
            FieldType = TerceroFieldType.Text
        });
        ctx.TerceroFieldDefinitions.Add(new TerceroFieldDefinition
        {
            TenantId = seed.TenantId,
            FichaKey = "cliente",
            FieldKey = "sep",
            Label = "Separador",
            FieldType = TerceroFieldType.Separator
        });
        ctx.Terceros.Add(new Tercero
        {
            TenantId = seed.TenantId,
            Nombre = "Grilla SAS",
            Tipo = TerceroTipo.Empresa,
            Perfiles = TerceroPerfil.Cliente,
            IdValor = "900777",
            Email = "ventas@grilla.test",
            Telefono = "3001234567",
            FichasJson = "{\"cliente\":{\"segmento\":\"Oro\",\"subsector\":\"Metalmecanica\",\"sep\":\"x\"}}"
        });
        await ctx.SaveChangesAsync();
        var svc = new TerceroService(ctx, _fixture.CreateFactory(seed.TenantId), tenant, new DirectoryVariantService(ctx, tenant),
            new DataLookupService(ctx, new DataContainerService(ctx, tenant)));

        var clasico = Assert.Single(await svc.ListAsync(new TerceroListFilter()), r => r.Nombre == "Grilla SAS");
        Assert.Equal("Oro", clasico.Filtrables!["segmento"]);
        Assert.False(clasico.Filtrables.ContainsKey("subsector"));

        var grilla = Assert.Single(await svc.ListAsync(new TerceroListFilter(IncludeAllFields: true)), r => r.Nombre == "Grilla SAS");
        Assert.Equal("Oro", grilla.Filtrables!["segmento"]);
        Assert.Equal("Metalmecanica", grilla.Filtrables["subsector"]);
        Assert.False(grilla.Filtrables.ContainsKey("sep"));
        Assert.Equal("ventas@grilla.test", grilla.Email);
        Assert.Equal("3001234567", grilla.Telefono);
    }

    /// <summary>
    /// Gestion de contactos por la IA (addendum ADR-0159): edita solo lo pedido (campos fijos y configurables),
    /// no deja duplicar NIT ni telefono, liga personas a la empresa sin duplicarlas, deja rastro en la ficha y
    /// en la auditoria, y la baja es logica y reversible.
    /// </summary>
    [Fact]
    public async Task ContactManagement_EditsWithoutDuplicating_LinksPeople_AndInactivatesReversibly()
    {
        var seed = await SeedTenantAsync("Mcp Contactos");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (boards, _, toolset) = Build(ctx, tenantCtx);
        ctx.TerceroFieldDefinitions.Add(new TerceroFieldDefinition
        {
            TenantId = seed.TenantId,
            FichaKey = "comercial",
            FieldKey = "segmento",
            Label = "Segmento",
            FieldType = TerceroFieldType.Text
        });
        foreach (var fichaKey in new[] { "base", "fiscal" })
        {
            ctx.TerceroFieldDefinitions.Add(new TerceroFieldDefinition
            {
                TenantId = seed.TenantId,
                FichaKey = fichaKey,
                FieldKey = "direccion",
                Label = "Direccion",
                FieldType = TerceroFieldType.Text
            });
        }
        await ctx.SaveChangesAsync();
        var me = Actor(seed, owner: true);

        Assert.False((await toolset.ExecuteAsync("crear_contacto", Args(new { nombre = "ACME SAS", tipo = "empresa", identificacion = "900111" }), me)).IsError);
        Assert.False((await toolset.ExecuteAsync("crear_contacto", Args(new { nombre = "Otra SAS", tipo = "empresa", identificacion = "900222", telefono = "3001112233" }), me)).IsError);

        // Edita solo lo pedido: telefono + campo configurable (por etiqueta).
        var edit = await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "acme sas", telefono = "300 999 8877", campos = new { Segmento = "Oro" } }), me);
        Assert.Equal(2, Payload(edit).GetProperty("cambios").GetArrayLength());
        Assert.NotNull(edit.AuditBefore);
        Assert.NotNull(edit.AuditAfter);
        var acme = await ctx.Terceros.AsNoTracking().SingleAsync(t => t.Nombre == "ACME SAS");
        Assert.Equal("3009998877", acme.Telefono);
        Assert.Equal("900111", acme.IdValor);
        Assert.Contains("\"segmento\":\"Oro\"", acme.FichasJson!.Replace(" ", ""));

        // Campo repetido en dos fichas: se pide ficha.clave y con eso se guarda.
        var ambiguous = await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "900111", campos = new { Direccion = "Calle 1" } }), me);
        Assert.True(ambiguous.IsError);
        Assert.Contains("fiscal.direccion", ambiguous.Json);
        Assert.False((await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "900111", campos = new Dictionary<string, string> { ["fiscal.direccion"] = "Calle 1" } }), me)).IsError);
        Assert.Contains("\"fiscal\":{\"direccion\":\"Calle1\"}", (await ctx.Terceros.AsNoTracking().SingleAsync(t => t.Id == acme.Id)).FichasJson!.Replace(" ", ""));

        // No duplica: NIT o telefono de otro contacto; sin cambios o con un campo inventado no guarda nada.
        var dupNit = await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "900111", identificacion = "900222" }), me);
        Assert.True(dupNit.IsError);
        Assert.Contains("Otra SAS", dupNit.Json);
        Assert.True((await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "900111", telefono = "+57 300 111 2233" }), me)).IsError);
        Assert.True((await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "900111", telefono = "3009998877" }), me)).IsError);
        Assert.True((await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "900111", campos = new { inventado = "x" } }), me)).IsError);

        // Persona dentro de la empresa, sin duplicarla al repetir.
        var first = Payload(await toolset.ExecuteAsync("contacto_interno", Args(new { empresa = "ACME SAS", nombre = "Laura Compras", cargo = "Jefe de compras", email = "laura@acme.test" }), me));
        Assert.False(first.GetProperty("ya_existia").GetBoolean());
        var again = Payload(await toolset.ExecuteAsync("contacto_interno", Args(new { empresa = "ACME SAS", nombre = "Laura C.", email = "LAURA@acme.test" }), me));
        Assert.True(again.GetProperty("ya_existia").GetBoolean());
        Assert.Equal(1, await ctx.Terceros.CountAsync(t => t.Tipo == TerceroTipo.Persona));

        var ficha = Payload(await toolset.ExecuteAsync("ver_contacto", Args(new { contacto = "ACME SAS" }), me));
        Assert.Contains(ficha.GetProperty("vinculados").EnumerateArray(), v => v.GetProperty("nombre").GetString() == "Laura Compras");
        Assert.Contains(ficha.GetProperty("campos").EnumerateArray(), c => c.GetProperty("valor").GetString() == "Oro");
        Assert.True(ficha.GetProperty("total_notas").GetInt32() >= 2); // rastro de la edicion y del contacto agregado

        // Persona suelta ligada a la empresa: queda como contacto PRINCIPAL (visible tambien en el Directorio Clasico).
        Assert.False((await toolset.ExecuteAsync("crear_contacto", Args(new { nombre = "Mario Suelto", tipo = "persona", telefono = "3157778899" }), me)).IsError);
        Assert.False((await toolset.ExecuteAsync("vincular_persona_empresa", Args(new { persona = "Mario Suelto", empresa = "ACME SAS", cargo = "Gerente" }), me)).IsError);
        var mario = await ctx.Terceros.AsNoTracking().SingleAsync(t => t.Nombre == "Mario Suelto");
        Assert.Equal(acme.Id, mario.EmpresaId);
        Assert.Equal("Gerente", mario.Cargo);
        Assert.False((await toolset.ExecuteAsync("vincular_persona_empresa", Args(new { persona = "Mario Suelto", empresa = "ACME SAS", accion = "desvincular" }), me)).IsError);
        Assert.Null((await ctx.Terceros.AsNoTracking().SingleAsync(t => t.Id == mario.Id)).EmpresaId);

        Assert.False((await toolset.ExecuteAsync("vincular_persona_empresa", Args(new { persona = "Laura Compras", empresa = "ACME SAS", accion = "desvincular" }), me)).IsError);
        Assert.Equal(0, Payload(await toolset.ExecuteAsync("ver_contacto", Args(new { contacto = "ACME SAS" }), me)).GetProperty("vinculados").GetArrayLength());

        Assert.False((await toolset.ExecuteAsync("nota_contacto", Args(new { contacto = "ACME SAS", texto = "Prefiere llamadas en la tarde", tipo = "Llamada" }), me)).IsError);
        var notas = Payload(await toolset.ExecuteAsync("nota_contacto", Args(new { contacto = "ACME SAS", accion = "listar" }), me)).GetProperty("notas");
        Assert.Contains(notas.EnumerateArray(), n => n.GetProperty("tipo").GetString() == "Llamada" && n.GetProperty("autor").GetString()!.EndsWith("(via IA)"));

        // Tareas del contacto.
        await boards.CreateBoardAsync(new CreateActivityBoardRequest("Ventas"), seed.OwnerPlatformId, "Owner");
        var numero = Payload(await toolset.ExecuteAsync("crear_tarea", Args(new { tablero = "Ventas", titulo = "Cotizar ACME" }), me)).GetProperty("numero").GetString()!;
        Assert.False((await toolset.ExecuteAsync("vincular_contacto", Args(new { numero, contacto = "ACME SAS" }), me)).IsError);
        var tareas = Payload(await toolset.ExecuteAsync("tareas_del_contacto", Args(new { contacto = "ACME SAS" }), me));
        Assert.Equal(numero, tareas.GetProperty("tareas")[0].GetProperty("numero").GetString());

        // Busqueda con filtros y por digitos del telefono.
        Assert.Equal(1, Payload(await toolset.ExecuteAsync("buscar_contacto", Args(new { texto = "acme", tipo = "empresa" }), me)).GetProperty("total").GetInt32());
        Assert.Equal(1, Payload(await toolset.ExecuteAsync("buscar_contacto", Args(new { texto = "9998877" }), me)).GetProperty("total").GetInt32());

        // Baja logica: exige motivo, desaparece de la busqueda y se reactiva; nunca se borra.
        Assert.True((await toolset.ExecuteAsync("inactivar_contacto", Args(new { contacto = "ACME SAS", motivo = "" }), me)).IsError);
        var off = await toolset.ExecuteAsync("inactivar_contacto", Args(new { contacto = "ACME SAS", motivo = "Cerro la empresa" }), me);
        Assert.False(off.IsError, off.Json);
        Assert.Equal(TerceroEstado.Inactivo, (await ctx.Terceros.AsNoTracking().SingleAsync(t => t.Id == acme.Id)).Estado);
        Assert.Equal(0, Payload(await toolset.ExecuteAsync("buscar_contacto", Args(new { texto = "acme", tipo = "empresa" }), me)).GetProperty("total").GetInt32());
        Assert.False((await toolset.ExecuteAsync("reactivar_contacto", Args(new { contacto = "ACME SAS", motivo = "Reabrio" }), me)).IsError);
        Assert.Equal(TerceroEstado.Activo, (await ctx.Terceros.AsNoTracking().SingleAsync(t => t.Id == acme.Id)).Estado);
    }

    /// <summary>La IA respeta los permisos del Directorio del usuario (misma matriz de roles que la pantalla).</summary>
    [Fact]
    public async Task ContactTools_RespectDirectoryPermissionsOfTheUser()
    {
        var seed = await SeedTenantAsync("Mcp Permisos Dir");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx);
        var owner = Actor(seed, owner: true);
        Assert.False((await toolset.ExecuteAsync("crear_contacto", Args(new { nombre = "Cliente Uno", identificacion = "800100" }), owner)).IsError);

        // Rol de solo lectura en el Directorio para el miembro.
        var rol = new Rol { TenantId = seed.TenantId, Name = "Solo consulta" };
        ctx.Roles.Add(rol);
        ctx.RolPermisos.Add(new RolPermiso { TenantId = seed.TenantId, RolId = rol.Id, ModuleKey = DirectorioSubPermisos.ModuleRoute, CanView = true });
        var member = await ctx.TenantUsers.SingleAsync(u => u.Id == seed.MemberUserId);
        member.RolId = rol.Id;
        await ctx.SaveChangesAsync();
        var reader = Actor(seed, owner: false);

        Assert.False((await toolset.ExecuteAsync("buscar_contacto", Args(new { texto = "cliente" }), reader)).IsError);
        Assert.False((await toolset.ExecuteAsync("ver_contacto", Args(new { contacto = "800100" }), reader)).IsError);
        var denied = await toolset.ExecuteAsync("editar_contacto", Args(new { contacto = "800100", ciudad = "Cali" }), reader);
        Assert.True(denied.IsError);
        Assert.Contains("permiso", denied.Json);
        Assert.True((await toolset.ExecuteAsync("crear_contacto", Args(new { nombre = "Nueva SAS" }), reader)).IsError);
        Assert.True((await toolset.ExecuteAsync("inactivar_contacto", Args(new { contacto = "800100", motivo = "x" }), reader)).IsError);
        Assert.True((await toolset.ExecuteAsync("contacto_interno", Args(new { empresa = "800100", nombre = "Pedro" }), reader)).IsError);
        Assert.Null((await ctx.Terceros.AsNoTracking().SingleAsync(t => t.IdValor == "800100")).Ciudad);
    }

    /// <summary>
    /// Autoria de FLUJOS por la IA (addendum ADR-0159): crea un flujo nuevo, le agrega un paso, lo conecta,
    /// le pone encargado por campo de formulario y un formulario obligatorio, lo publica y lo engancha a un
    /// concepto para que las tareas lo arranquen. Capa delgada sobre IWorkflowDesignService / IWorkflowEngine.
    /// </summary>
    [Fact]
    public async Task FlowAuthoring_BuildsConnectsPublishesAndHooksToConcept()
    {
        var seed = await SeedTenantAsync("Mcp Autoria Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx);
        var me = Actor(seed, owner: true);

        // Un formulario activo (para vincular al paso) y un concepto (para enganchar el flujo al final).
        var form = new FormDefinition { TenantId = seed.TenantId, Code = "F-APROB", Title = "Aprobacion", Status = FormStatus.Active };
        var cat = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-9", Nombre = "Compras" };
        ctx.FormDefinitions.Add(form);
        ctx.ActividadCategorias.Add(cat);
        await ctx.SaveChangesAsync();
        var concepto = new ActividadSubcategoria { TenantId = seed.TenantId, CategoriaId = cat.Id, Codigo = "C-OC", Nombre = "Orden de compra" };
        ctx.ActividadSubcategorias.Add(concepto);
        ctx.OrgUnits.Add(new OrgUnit { TenantId = seed.TenantId, Name = "Jefe de compras", Classifier = OrgUnitClassifier.Cargo });
        await ctx.SaveChangesAsync();

        // Crear el flujo: nace con inicio -> fin YA conectados (1 arista).
        var creado = Payload(await toolset.ExecuteAsync("crear_flujo", Args(new { nombre = "Aprobacion de compras", categoria = "Compras" }), me));
        var defId = creado.GetProperty("definicion_id").GetGuid();
        Guid NodeOf(JsonElement nodes, string tipo) =>
            nodes.EnumerateArray().First(n => n.GetProperty("tipo").GetString() == tipo).GetProperty("nodo_id").GetGuid();
        var inicio = NodeOf(creado.GetProperty("nodos"), "inicio");
        var fin = NodeOf(creado.GetProperty("nodos"), "fin");
        Assert.Equal(1, creado.GetProperty("aristas").GetArrayLength());

        // INSERTAR el paso entre inicio y fin: reemplaza la conexion directa (si quedara, el motor iria en paralelo al fin).
        var ins = Payload(await toolset.ExecuteAsync("agregar_nodo",
            Args(new { flujo = defId, tipo = "tarea", nombre = "Revisar", desde_nodo_id = inicio, hasta_nodo_id = fin }), me));
        var rev = ins.GetProperty("nodo_id").GetGuid();
        Assert.True(ins.GetProperty("insertado_entre").GetProperty("conexion_directa_quitada").GetBoolean());

        // Encargado por politica SIN cargo: la revision lo avisa y publicar se niega sin forzar.
        Assert.False((await toolset.ExecuteAsync("asignar_encargado_nodo", Args(new { nodo_id = rev, origen = "politica" }), me)).IsError);
        var conAviso = Payload(await toolset.ExecuteAsync("ver_flujo", Args(new { flujo = defId }), me));
        Assert.Contains(conAviso.GetProperty("avisos").EnumerateArray(), a => a.GetString()!.Contains("politica"));
        var refused = await toolset.ExecuteAsync("publicar_flujo", Args(new { flujo = defId }), me);
        Assert.True(refused.IsError);
        Assert.Contains("forzar", refused.Json);
        Assert.False(await ctx.WorkflowDefinitions.AsNoTracking().AnyAsync(d => d.Id == defId && d.IsPublished));

        // Cargo inexistente -> responde las opciones validas; el real se asigna.
        var badCargo = await toolset.ExecuteAsync("asignar_cargo_nodo", Args(new { nodo_id = rev, cargo = "No existe" }), me);
        Assert.True(badCargo.IsError);
        Assert.Contains("Jefe de compras", badCargo.Json);
        Assert.False((await toolset.ExecuteAsync("asignar_cargo_nodo", Args(new { nodo_id = rev, cargo = "jefe de compras" }), me)).IsError);
        Assert.True((await toolset.ExecuteAsync("asignar_encargado_nodo", Args(new { nodo_id = rev, origen = "campo_formulario" }), me)).IsError); // falta 'campo'

        // Formulario obligatorio para cerrar (ADR-0077).
        Assert.False((await toolset.ExecuteAsync("poner_formulario_a_nodo", Args(new { nodo_id = rev, formulario = "F-APROB", obligatorio = true }), me)).IsError);

        // Una rama paralela involuntaria se avisa y se corrige con eliminar_conexion.
        var extra = Payload(await toolset.ExecuteAsync("conectar_nodos", Args(new { desde_nodo_id = inicio, hasta_nodo_id = fin }), me))
            .GetProperty("arista_id").GetGuid();
        var paralelo = Payload(await toolset.ExecuteAsync("ver_flujo", Args(new { flujo = defId }), me));
        Assert.Contains(paralelo.GetProperty("avisos").EnumerateArray(), a => a.GetString()!.Contains("PARALELO"));
        Assert.False((await toolset.ExecuteAsync("eliminar_conexion", Args(new { arista_id = extra }), me)).IsError);

        // El grafo queda limpio: inicio -> Revisar -> fin, con cargo y formulario, sin avisos.
        var canvas = Payload(await toolset.ExecuteAsync("ver_flujo", Args(new { flujo = defId }), me));
        Assert.False(canvas.TryGetProperty("avisos", out _), canvas.ToString());
        Assert.Equal(2, canvas.GetProperty("aristas").GetArrayLength());
        var revNode = canvas.GetProperty("nodos").EnumerateArray().First(n => n.GetProperty("nodo_id").GetGuid() == rev);
        Assert.Equal("politica", revNode.GetProperty("encargado_origen").GetString());
        Assert.Contains(revNode.GetProperty("cargos").EnumerateArray(), c => c.GetString() == "Jefe de compras");
        Assert.Contains(revNode.GetProperty("formularios").EnumerateArray(), f => f.GetProperty("obligatorio").GetBoolean());

        // Un paso de inicio es unico: agregar otro inicio se rechaza (regla del servicio, no reimplementada).
        Assert.True((await toolset.ExecuteAsync("agregar_nodo", Args(new { flujo = defId, tipo = "inicio" }), me)).IsError);

        // Publicar (sin avisos ya no hace falta forzar) y enganchar al concepto: listo para arrancar tareas.
        var pub = await toolset.ExecuteAsync("publicar_flujo", Args(new { flujo = defId }), me);
        Assert.False(pub.IsError, pub.Json);
        var link = Payload(await toolset.ExecuteAsync("enganchar_flujo_a_concepto", Args(new { concepto = "C-OC", flujo = defId }), me));
        Assert.True(link.GetProperty("publicado").GetBoolean());

        var reloaded = await ctx.ActividadSubcategorias.AsNoTracking().SingleAsync(s => s.Id == concepto.Id);
        Assert.Equal(defId, reloaded.WorkflowDefinitionId);
        Assert.True(await ctx.WorkflowDefinitions.AsNoTracking().AnyAsync(d => d.Id == defId && d.IsPublished));

        // Modificar el flujo PUBLICADO: version borrador nueva (ids nuevos, vinculos copiados), se edita y se publica;
        // el concepto pasa solo a la version nueva (el motor re-apunta los conceptos al publicar).
        Assert.True((await toolset.ExecuteAsync("agregar_nodo", Args(new { flujo = defId, tipo = "tarea" }), me)).IsError); // publicado = no editable
        var v2 = Payload(await toolset.ExecuteAsync("nueva_version_flujo", Args(new { flujo = defId }), me));
        var defV2 = v2.GetProperty("definicion_id").GetGuid();
        Assert.NotEqual(defId, defV2);
        var revV2 = NodeOf(v2.GetProperty("nodos"), "tarea");
        Assert.False((await toolset.ExecuteAsync("renombrar_nodo", Args(new { nodo_id = revV2, nombre = "Revisar y aprobar" }), me)).IsError);
        var pubV2 = await toolset.ExecuteAsync("publicar_flujo", Args(new { flujo = defV2 }), me);
        Assert.False(pubV2.IsError, pubV2.Json); // cargo y formulario se copiaron: sin avisos
        Assert.Equal(defV2, (await ctx.ActividadSubcategorias.AsNoTracking().SingleAsync(s => s.Id == concepto.Id)).WorkflowDefinitionId);
    }

    /// <summary>
    /// ADR-0211 (ola 1): la IA deja un flujo FUNCIONANDO con la configuracion por nodo, una herramienta por propiedad:
    /// compuerta que decide una persona, reinicio, tablero/columna + concepto de cierre, plazo y arranque, color y nota
    /// (PATCH: cambiar uno no borra el otro), avisos con enlaces publicos de decision por salida, formulario obligatorio
    /// por salida / sin crear al llegar / quitar, salto a otro flujo, editar, pausar/reanudar y desenganchar.
    /// </summary>
    [Fact]
    public async Task FlowConfig_NodoCompuertaHumana_Avisos_Formularios_CicloDeVida_ADR0211()
    {
        var seed = await SeedTenantAsync("Mcp Config Flujo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx);
        var me = Actor(seed, owner: true);

        var form = new FormDefinition { TenantId = seed.TenantId, Code = "F-RECH", Title = "Motivo de rechazo", Status = FormStatus.Active };
        var cat = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-CF", Nombre = "Compras CF" };
        var board = new TaskBoard
        {
            TenantId = seed.TenantId,
            Name = "Compras CF",
            Kind = TaskBoardKind.Activities,
            CloseReasonsJson = "[\"Aprobada\",\"Rechazada\"]"
        };
        ctx.FormDefinitions.Add(form);
        ctx.ActividadCategorias.Add(cat);
        ctx.TaskBoards.Add(board);
        await ctx.SaveChangesAsync();
        ctx.TaskBoardColumns.Add(new TaskBoardColumn { TenantId = seed.TenantId, BoardId = board.Id, Name = "Por hacer", SortOrder = 1 });
        ctx.TaskBoardColumns.Add(new TaskBoardColumn { TenantId = seed.TenantId, BoardId = board.Id, Name = "Cerrado", SortOrder = 2, IsDone = true });
        var concepto = new ActividadSubcategoria { TenantId = seed.TenantId, CategoriaId = cat.Id, Codigo = "C-CF", Nombre = "Compra CF" };
        ctx.ActividadSubcategorias.Add(concepto);
        await ctx.SaveChangesAsync();

        async Task<JsonElement> Ok(string tool, object args)
        {
            var r = await toolset.ExecuteAsync(tool, Args(args), me);
            Assert.False(r.IsError, $"{tool}: {r.Json}");
            return Payload(r);
        }
        async Task<string> Fail(string tool, object args)
        {
            var r = await toolset.ExecuteAsync(tool, Args(args), me);
            Assert.True(r.IsError, $"{tool} debia fallar: {r.Json}");
            return r.Json;
        }
        JsonElement NodeIn(JsonElement canvas, Guid id) =>
            canvas.GetProperty("nodos").EnumerateArray().First(n => n.GetProperty("nodo_id").GetGuid() == id);

        // Grafo: inicio -> Revisar -> Decision (compuerta) -> fin | Rechazado (otro fin).
        var creado = await Ok("crear_flujo", new { nombre = "Compra con decision", categoria = "Compras" });
        var defId = creado.GetProperty("definicion_id").GetGuid();
        var nodos = creado.GetProperty("nodos").EnumerateArray().ToList();
        var inicio = nodos.First(n => n.GetProperty("tipo").GetString() == "inicio").GetProperty("nodo_id").GetGuid();
        var fin = nodos.First(n => n.GetProperty("tipo").GetString() == "fin").GetProperty("nodo_id").GetGuid();
        var rev = (await Ok("agregar_nodo", new { flujo = defId, tipo = "tarea", nombre = "Revisar", desde_nodo_id = inicio, hasta_nodo_id = fin })).GetProperty("nodo_id").GetGuid();
        await Ok("asignar_encargado_nodo", new { nodo_id = rev, origen = "hereda_inicio" });
        var gw = (await Ok("agregar_nodo", new { flujo = defId, tipo = "compuerta", nombre = "Decision", desde_nodo_id = rev, hasta_nodo_id = fin })).GetProperty("nodo_id").GetGuid();
        var rech = (await Ok("agregar_nodo", new { flujo = defId, tipo = "fin", nombre = "Rechazado" })).GetProperty("nodo_id").GetGuid();
        await Ok("conectar_nodos", new { desde_nodo_id = gw, hasta_nodo_id = rech });

        // Compuerta automatica sin condiciones: aviso. Con decision humana + encargado, el aviso desaparece.
        var c1 = await Ok("ver_flujo", new { flujo = defId });
        Assert.Contains(c1.GetProperty("avisos").EnumerateArray(), a => a.GetString()!.Contains("se resuelve SOLA"));
        Assert.Contains(c1.GetProperty("avisos").EnumerateArray(), a => a.GetString()!.Contains("SIN NOMBRE"));
        Assert.False(NodeIn(c1, gw).GetProperty("decision_humana").GetBoolean());

        // Nombre de la salida (opcion de la decision): en compuerta automatica sugiere la condicion.
        var toRech = c1.GetProperty("aristas").EnumerateArray()
            .First(e => e.GetProperty("desde").GetGuid() == gw && e.GetProperty("hasta").GetGuid() == rech).GetProperty("arista_id").GetGuid();
        var named = await Ok("nombre_salida", new { arista_id = toRech, nombre = "Rechazada" });
        Assert.Contains("approval == 'Rechazada'", named.GetProperty("sugerencia").GetString());
        Assert.Equal("Rechazada", (await ctx.WorkflowEdges.AsNoTracking().SingleAsync(e => e.Id == toRech)).Name);
        await Ok("decision_humana_nodo", new { nodo_id = gw, activo = true });
        Assert.Contains("politica", (await Ok("ver_flujo", new { flujo = defId })).GetProperty("avisos").ToString());
        await Ok("asignar_encargado_nodo", new { nodo_id = gw, origen = "hereda_inicio" });
        Assert.Contains("inicio no admite", await Fail("decision_humana_nodo", new { nodo_id = inicio, activo = true }));

        // Reinicio (PATCH: conserva la decision humana).
        await Ok("reinicio_nodo", new { nodo_id = rech, reiniciar_en_nodo_id = rev });
        Assert.True((await ctx.WorkflowNodes.AsNoTracking().SingleAsync(n => n.Id == gw)).AllowsAssignment);

        // Tablero + columna de cierre + concepto (valida contra los del tablero).
        var tb = await Ok("tablero_destino_nodo", new { nodo_id = fin, tablero = "compras cf", columna = "Cerrado" });
        Assert.True(tb.GetProperty("columna_de_cierre").GetBoolean());
        Assert.Contains("Aprobada", await Fail("concepto_cierre_nodo", new { nodo_id = fin, concepto = "No existe" }));
        await Ok("concepto_cierre_nodo", new { nodo_id = fin, concepto = "Aprobada" });
        Assert.Contains("Por hacer", await Fail("tablero_destino_nodo", new { nodo_id = rev, tablero = "Compras CF", columna = "Inexistente" }));

        // Plazo / arranque y color / nota (cambiar el color NO borra la nota).
        Assert.Contains("habiles", (await Ok("plazo_nodo", new { nodo_id = rev, dias = 2, dias_habiles = true })).GetProperty("valor").GetString());
        await Ok("tiempo_arranque_nodo", new { nodo_id = rev, horas = 1 });
        await Ok("nota_nodo", new { nodo_id = rev, nota = "Revisa los soportes" });
        await Ok("color_nodo", new { nodo_id = rev, color = "green" });
        var revDb = await ctx.WorkflowNodes.AsNoTracking().SingleAsync(n => n.Id == rev);
        Assert.Equal("green", revDb.Color);
        Assert.Equal("Revisa los soportes", revDb.Note);

        // Avisos al llegar: correo al cliente con un enlace de decision por salida de la compuerta.
        Assert.Contains("compuerta", await Fail("agregar_notificacion_nodo", new
        {
            nodo_id = gw,
            canal = "correo",
            destino = "contacto_tarea",
            mensaje = "x",
            enlaces_decision = new[] { new { variable = "a", salida_nodo_id = rev } }
        }));
        Assert.Contains("linea_id", await Fail("agregar_notificacion_nodo", new { nodo_id = gw, canal = "whatsapp" }));
        var noti = await Ok("agregar_notificacion_nodo", new
        {
            nodo_id = gw,
            canal = "correo",
            destino = "contacto_tarea",
            asunto = "Tu compra {tarea.titulo}",
            mensaje = "Aprueba: {aprobar} o rechaza: {rechazar}",
            enlaces_decision = new object[]
            {
                new { variable = "aprobar", salida_nodo_id = fin, captura = "firma" },
                new { variable = "rechazar", salida_nodo_id = rech, captura = "observacion", observacion_obligatoria = true }
            }
        });
        Assert.Equal(1, noti.GetProperty("numero").GetInt32());
        var cfg = NodeNotifyConfig.Parse((await ctx.WorkflowNodes.AsNoTracking().SingleAsync(n => n.Id == gw)).NotifyJson);
        var regla = Assert.Single(cfg.Reglas!);
        Assert.Equal(NodeNotifyRecipient.ContactoTarea, regla.Destino);
        Assert.Equal(2, regla.EnlacesDecision!.Count);
        Assert.Equal(WorkflowDecisionCapture.Observation, regla.EnlacesDecision.Single(l => l.TargetNodeId == rech).Capture);

        // Formulario de la compuerta: obligatorio SOLO al rechazar y sin crearse al llegar.
        await Ok("poner_formulario_a_nodo", new { nodo_id = gw, formulario = "F-RECH" });
        Assert.Contains("no es una salida", await Fail("formulario_obligatorio_por_salida", new { nodo_id = gw, formulario = "F-RECH", salidas = new[] { rev } }));
        await Ok("formulario_obligatorio_por_salida", new { nodo_id = gw, formulario = "F-RECH", salidas = new[] { rech } });
        await Ok("formulario_crear_al_llegar", new { nodo_id = gw, formulario = "F-RECH", activo = false });

        // Reglas: una que no existe da error claro con la herramienta para descubrirlas.
        Assert.True((await Ok("listar_reglas_disponibles", new { })).GetProperty("total").GetInt32() >= 0);
        Assert.Contains("listar_reglas_disponibles", await Fail("agregar_regla_nodo", new { nodo_id = rev, regla = "No existe" }));

        // Salto a otro flujo y edicion parcial del flujo (conserva el nombre).
        await Ok("crear_flujo", new { nombre = "Despacho CF" });
        Assert.Equal("Despacho CF", (await Ok("salto_a_flujo_nodo", new { nodo_id = rev, flujo_destino = "Despacho CF" })).GetProperty("salta_a").GetString());
        await Ok("editar_flujo", new { flujo = defId, descripcion = "Compra con aprobacion del cliente" });

        // ver_flujo muestra TODO lo configurado.
        var full = await Ok("ver_flujo", new { flujo = defId });
        Assert.False(full.TryGetProperty("avisos", out _), full.ToString());
        Assert.Equal("Compra con decision", full.GetProperty("nombre").GetString());
        Assert.Equal("Compra con aprobacion del cliente", full.GetProperty("descripcion").GetString());
        var g = NodeIn(full, gw);
        Assert.True(g.GetProperty("decision_humana").GetBoolean());
        Assert.Equal(2, g.GetProperty("avisos_al_llegar")[0].GetProperty("detalle").GetProperty("enlaces_decision").GetArrayLength());
        var gf = g.GetProperty("formularios")[0];
        Assert.Equal("Rechazado", gf.GetProperty("obligatorio_en_salidas")[0].GetString());
        Assert.False(gf.GetProperty("crear_al_llegar").GetBoolean());
        var f = NodeIn(full, fin);
        Assert.Equal("Compras CF", f.GetProperty("tablero_destino").GetString());
        Assert.Equal("Cerrado", f.GetProperty("columna_destino").GetString());
        Assert.Equal("Aprobada", f.GetProperty("concepto_cierre").GetString());
        var r = NodeIn(full, rev);
        Assert.Contains("habiles", r.GetProperty("plazo").GetString());
        Assert.Equal("Despacho CF", r.GetProperty("salta_a_flujo").GetString());
        Assert.Equal("Revisar", NodeIn(full, rech).GetProperty("reinicia_en").GetString());

        // Quitar aviso y formulario.
        Assert.Equal(0, (await Ok("quitar_notificacion_nodo", new { nodo_id = gw, numero = 1 })).GetProperty("quedan").GetInt32());
        await Fail("quitar_notificacion_nodo", new { nodo_id = gw, numero = 1 });
        await Ok("quitar_formulario_nodo", new { nodo_id = gw, formulario = "F-RECH" });

        // Publicar, pausar/reanudar, enganchar/desenganchar. Publicado: la decision humana exige borrador.
        await Ok("publicar_flujo", new { flujo = defId });
        Assert.Contains("nueva_version_flujo", await Fail("decision_humana_nodo", new { nodo_id = gw, activo = false }));
        await Ok("pausar_flujo", new { flujo = defId });
        Assert.True((await ctx.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == defId)).IsPaused);
        await Ok("reanudar_flujo", new { flujo = defId });
        Assert.False((await ctx.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == defId)).IsPaused);
        await Ok("enganchar_flujo_a_concepto", new { concepto = "C-CF", flujo = defId });
        Assert.Equal("Compra con decision", (await Ok("desenganchar_flujo_de_concepto", new { concepto = "C-CF" })).GetProperty("flujo_quitado").GetString());
        Assert.Null((await ctx.ActividadSubcategorias.AsNoTracking().SingleAsync(s => s.Id == concepto.Id)).WorkflowDefinitionId);
        await Fail("desenganchar_flujo_de_concepto", new { concepto = "C-CF" });
    }

    /// <summary>
    /// ADR-0211 ola 3: agente de IA del paso, una herramienta por propiedad. Asignar (avisa si esta apagado), recursos
    /// como PATCH (cambiar uno no borra otro), fallo, canales, cadencia con ventana horaria, tope, traspaso por salida
    /// de compuerta, ver y quitar. ver_flujo muestra el agente de cada paso.
    /// </summary>
    [Fact]
    public async Task FlowAgent_AsignarConfigurarSeguimientoYQuitar_ADR0211()
    {
        var seed = await SeedTenantAsync("Mcp Agente Nodo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx);
        var me = Actor(seed, owner: true);
        ctx.AiAgents.Add(new AiAgent { TenantId = seed.TenantId, Name = "Asistente Cobranza", SystemPrompt = "x", Provider = AiProvider.DeepSeek, IsActive = false });
        await ctx.SaveChangesAsync();

        async Task<JsonElement> Ok(string tool, object args)
        {
            var r = await toolset.ExecuteAsync(tool, Args(args), me);
            Assert.False(r.IsError, $"{tool}: {r.Json}");
            return Payload(r);
        }
        async Task<string> Fail(string tool, object args)
        {
            var r = await toolset.ExecuteAsync(tool, Args(args), me);
            Assert.True(r.IsError, $"{tool} debia fallar: {r.Json}");
            return r.Json;
        }

        var creado = await Ok("crear_flujo", new { nombre = "Cobro con agente" });
        var defId = creado.GetProperty("definicion_id").GetGuid();
        var nodos = creado.GetProperty("nodos").EnumerateArray().ToList();
        var inicio = nodos.First(n => n.GetProperty("tipo").GetString() == "inicio").GetProperty("nodo_id").GetGuid();
        var fin = nodos.First(n => n.GetProperty("tipo").GetString() == "fin").GetProperty("nodo_id").GetGuid();
        var cobrar = (await Ok("agregar_nodo", new { flujo = defId, tipo = "tarea", nombre = "Cobrar", desde_nodo_id = inicio, hasta_nodo_id = fin })).GetProperty("nodo_id").GetGuid();
        var gw = (await Ok("agregar_nodo", new { flujo = defId, tipo = "compuerta", nombre = "Pago?", desde_nodo_id = cobrar, hasta_nodo_id = fin })).GetProperty("nodo_id").GetGuid();

        // Opciones y asignacion (el agente esta apagado: aviso).
        var opciones = await Ok("opciones_agente_nodo", new { });
        Assert.Contains(opciones.GetProperty("agentes").EnumerateArray(), a => a.GetProperty("nombre").GetString() == "Asistente Cobranza");
        Assert.Contains("asignalo primero", await Fail("instrucciones_agente_nodo", new { nodo_id = cobrar, instrucciones = "x" }));
        Assert.Contains("inicio", await Fail("agente_nodo", new { nodo_id = inicio, agente = "Asistente Cobranza" }));
        var asignado = await Ok("agente_nodo", new { nodo_id = cobrar, agente = "cobranza", autonomia = "autonomo" });
        Assert.Contains("APAGADO", asignado.GetProperty("aviso").GetString());

        // Recursos como PATCH: las instrucciones sobreviven a los cambios siguientes.
        await Ok("instrucciones_agente_nodo", new { nodo_id = cobrar, instrucciones = "Confirma la fecha de pago con el cliente." });
        await Ok("correo_agente_nodo", new { nodo_id = cobrar, activo = true });
        await Ok("canales_agente_nodo", new { nodo_id = cobrar, canales = new[] { "whatsapp", "correo" } });
        Assert.Contains("ruta", await Fail("fallo_agente_nodo", new { nodo_id = cobrar, accion = "tomar_ruta" }));
        await Ok("fallo_agente_nodo", new { nodo_id = cobrar, accion = "tomar_ruta", ruta = "Sin respuesta" });
        Assert.Contains("linea de WhatsApp", await Fail("whatsapp_agente_nodo", new { nodo_id = cobrar, linea = "No existe" }));
        var link = await ctx.WorkflowNodeAgents.AsNoTracking().SingleAsync(a => a.NodeId == cobrar);
        Assert.Equal(WorkflowAgentAutonomy.Autonomous, link.Autonomy);
        Assert.Equal("Confirma la fecha de pago con el cliente.", link.ExtraPrompt);
        Assert.True(link.CanSendEmail);
        Assert.Equal(WorkflowAgentChannel.WhatsApp | WorkflowAgentChannel.Correo, link.AllowedChannels);
        Assert.Equal(WorkflowAgentFailureAction.TakeRoute, link.OnFailure);
        Assert.Equal("Sin respuesta", link.FailureRoute);

        // Cadencia de seguimiento con ventana horaria y tope de preguntas.
        Assert.Contains("HH:MM", await Fail("seguimiento_agente_nodo", new { nodo_id = cobrar, esperas = new[] { new { dias = 0 } }, ventana = new { inicio = "18:00", fin = "08:00" } }));
        await Ok("seguimiento_agente_nodo", new
        {
            nodo_id = cobrar,
            esperas = new object[] { new { minutos = 0 }, new { dias = 1 }, new { dias = 2 } },
            ventana = new { inicio = "08:00", fin = "18:00", dias = new[] { 1, 2, 3, 4, 5 }, festivos = true }
        });
        await Ok("tope_preguntas_agente_nodo", new { nodo_id = cobrar, tope = 4 });
        var node = await ctx.WorkflowNodes.AsNoTracking().SingleAsync(n => n.Id == cobrar);
        Assert.Equal(3, StepSlaList.ReadRows(node.AgentFollowUpJson).Count);
        Assert.True(PulseSendWindow.Read(node.AgentFollowUpJson).Enabled);
        Assert.Equal(4, node.AgentMaxAsks);

        // Traspaso por salida (solo compuertas, y a una salida real). La compuerta debe ser de decision humana.
        Assert.Contains("decision_humana_nodo", await Fail("agente_nodo", new { nodo_id = gw, agente = "Asistente Cobranza" }));
        await Ok("decision_humana_nodo", new { nodo_id = gw, activo = true });
        await Ok("agente_nodo", new { nodo_id = gw, agente = "Asistente Cobranza" });
        Assert.Contains("compuertas", await Fail("traspaso_salida_agente_nodo", new { nodo_id = cobrar, salida_nodo_id = fin, nota = "x" }));
        await Fail("traspaso_salida_agente_nodo", new { nodo_id = gw, salida_nodo_id = cobrar, nota = "x" });
        await Ok("traspaso_salida_agente_nodo", new { nodo_id = gw, salida_nodo_id = fin, nota = "El cliente ya pago: agradece y cierra." });

        // Ver: todo junto; ver_flujo muestra el agente del paso.
        var ver = await Ok("ver_agente_nodo", new { nodo_id = cobrar });
        Assert.Equal("autonomo", ver.GetProperty("autonomia").GetString());
        Assert.Equal(3, ver.GetProperty("seguimiento").GetProperty("esperas").GetArrayLength());
        Assert.Equal("tomar_ruta", ver.GetProperty("si_falla").GetString());
        var verGw = await Ok("ver_agente_nodo", new { nodo_id = gw });
        Assert.Equal(1, verGw.GetProperty("traspasos").GetArrayLength());
        var flujo = await Ok("ver_flujo", new { flujo = defId });
        Assert.Contains(flujo.GetProperty("nodos").EnumerateArray(),
            n => n.GetProperty("nodo_id").GetGuid() == cobrar && n.GetProperty("agente_ia").GetString()!.Contains("Asistente Cobranza"));

        // Quitar: el paso vuelve a ser humano.
        await Ok("quitar_agente_nodo", new { nodo_id = cobrar });
        Assert.False(await ctx.WorkflowNodeAgents.AsNoTracking().AnyAsync(a => a.NodeId == cobrar));
        Assert.Contains("solo gente", (await Ok("ver_agente_nodo", new { nodo_id = cobrar })).GetProperty("nota").GetString());
    }

    /// <summary>ADR-0211 ola 2: las 33 herramientas de reglas/opciones de formularios se exponen solo con sus servicios,
    /// sus esquemas son validos, lectura/destructivas son subconjunto de lo expuesto y todas despachan.</summary>
    [Fact]
    public async Task McpCatalog_ConFormConfig_EsConsistente_YDespacha()
    {
        var seed = await SeedTenantAsync("Mcp Catalogo FormConfig");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, sin) = Build(ctx, tenantCtx);
        var (_, _, con) = Build(ctx, tenantCtx, withFormConfig: true);
        var owner = Actor(seed, owner: true);

        var nuevas = con.GetSpecs().Select(s => s.Name).Except(sin.GetSpecs().Select(s => s.Name)).ToList();
        Assert.Equal(33, nuevas.Count);
        Assert.Contains("diseno_campo", nuevas);
        Assert.Contains("renombrar_campo", nuevas);
        Assert.Contains("crear_regla", nuevas);
        Assert.Contains("regla_al_enviar", nuevas);
        Assert.Contains("campo_bloqueado", nuevas);
        Assert.All(con.ReadOnlyTools, n => Assert.Contains(n, con.GetSpecs().Select(s => s.Name)));
        Assert.All(con.DestructiveTools, n => Assert.Contains(n, con.GetSpecs().Select(s => s.Name)));
        Assert.Empty(con.ReadOnlyTools.Intersect(con.DestructiveTools));
        foreach (var s in con.GetSpecs().Where(s => nuevas.Contains(s.Name)))
        {
            using var schema = JsonDocument.Parse(s.ParametersJsonSchema);
            var props = schema.RootElement.GetProperty("properties");
            if (schema.RootElement.TryGetProperty("required", out var req))
            {
                foreach (var r in req.EnumerateArray()) { Assert.True(props.TryGetProperty(r.GetString()!, out _), $"{s.Name}: {r}"); }
            }
            var res = await con.ExecuteAsync(s.Name, "{}", owner);
            Assert.DoesNotContain("Herramienta desconocida", res.Json);
        }
    }

    /// <summary>
    /// ADR-0211 ola 2: reglas y opciones de formularios por MCP, una herramienta por propiedad. Catalogo de verbos con
    /// parametros, crear regla (valida verbo/parametros/campos), estado, regla AL ENVIAR (idempotente por verbo), regla
    /// en un campo + parametros del vinculo, condicion entre campos, opciones del formulario (PATCH: ninguna apaga otra),
    /// versiones y propiedades de campo (bloqueado/oculto/default dinamico/permisos por rol).
    /// </summary>
    [Fact]
    public async Task FormConfig_Reglas_Opciones_Versiones_Campos_ADR0211()
    {
        var seed = await SeedTenantAsync("Mcp Config Form");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx, withFormConfig: true);
        var me = Actor(seed, owner: true);
        var forms = new Ecorex.Application.Forms.FormDefinitionService(ctx, tenantCtx, new MenuConfigService(ctx, tenantCtx), new SequenceService(ctx, tenantCtx));
        var def = (await forms.CreateAsync(new Ecorex.Application.Forms.CreateFormDefinitionRequest("F-CFG", "Solicitud CFG"))).Value!;
        foreach (var (code, label, type) in new[] { ("tipo", "Tipo", FormControlType.Text), ("motivo", "Motivo", FormControlType.Text),
                     ("total", "Total", FormControlType.Number), ("firma", "Firma", FormControlType.Text), ("codigo", "Codigo", FormControlType.Text) })
        {
            Assert.True((await forms.AddQuestionAsync(def.Id, new Ecorex.Application.Forms.SaveFormQuestionRequest(null, code, label, type))).IsOk);
        }

        async Task<JsonElement> Ok(string tool, object args)
        {
            var r = await toolset.ExecuteAsync(tool, Args(args), me);
            Assert.False(r.IsError, $"{tool}: {r.Json}");
            return Payload(r);
        }
        async Task<string> Fail(string tool, object args)
        {
            var r = await toolset.ExecuteAsync(tool, Args(args), me);
            Assert.True(r.IsError, $"{tool} debia fallar: {r.Json}");
            return r.Json;
        }

        // Catalogo de verbos (con detalle de parametros).
        var verbos = await Ok("listar_verbos_regla", new { });
        Assert.Equal(4, verbos.GetProperty("total").GetInt32());
        var notificar = await Ok("listar_verbos_regla", new { verbo = "notificar" });
        Assert.Contains(notificar.GetProperty("parametros").EnumerateArray(), p => p.GetProperty("nombre").GetString() == "message" && p.GetProperty("obligatorio").GetBoolean());
        Assert.Contains("PASAR_CAMPOS", await Fail("listar_verbos_regla", new { verbo = "NO_EXISTE" }));

        // Crear regla: valida verbo, nombre de parametro y tipo.
        Assert.Contains("listar_verbos_regla", await Fail("crear_regla", new { nombre = "x", verbo = "NO_EXISTE" }));
        Assert.Contains("no tiene el parametro", await Fail("crear_regla", new { nombre = "x", verbo = "NOTIFICAR", parametros = new { mensaje = "hola" } }));
        Assert.Contains("true o false", await Fail("crear_regla", new { nombre = "x", verbo = "NOTIFICAR", parametros = new { message = "hola", autoComplete = "quizas" } }));
        Assert.Contains("message", await Fail("crear_regla", new { nombre = "x", verbo = "NOTIFICAR", parametros = new { recipient = "a@b.c" } }));
        var regla = await Ok("crear_regla", new { nombre = "Avisar cambio de tipo", verbo = "NOTIFICAR", formulario = "F-CFG", parametros = new { message = "Cambio el tipo" } });
        Assert.Equal("activa", regla.GetProperty("estado").GetString());
        var reglaId = regla.GetProperty("regla_id").GetGuid();
        var doc = await ctx.RuleDocuments.AsNoTracking().SingleAsync(d => d.DocumentCode == "FRMRULES-F-CFG");
        Assert.Equal(doc.Id, (await ctx.Rules.AsNoTracking().SingleAsync(x => x.Id == reglaId)).DocumentId);
        Assert.Equal("FRMRULES-F-CFG", doc.DocumentCode);
        Assert.Equal(RuleStatus.Active, doc.Status);

        // Parametros, estado y nombre (una herramienta por propiedad).
        await Ok("parametros_regla", new { regla = reglaId, parametros = new { message = "El tipo cambio", recipient = "ops@ejemplo.test" } });
        await Ok("estado_regla", new { regla = reglaId, estado = "desarrollo" });
        await Ok("renombrar_regla", new { regla = reglaId, nombre = "Avisar tipo" });
        var r1 = await ctx.Rules.AsNoTracking().SingleAsync(x => x.Id == reglaId);
        Assert.Equal(RuleStatus.Development, r1.Status);
        Assert.Equal("Avisar tipo", r1.Name);
        Assert.Contains("El tipo cambio", r1.ParamsJson);

        // Regla en un campo (avisa que no esta activa) + parametros propios del vinculo.
        var link = await Ok("regla_en_campo", new { formulario = "F-CFG", campo = "tipo", regla = "Avisar tipo" });
        Assert.Contains("ACTIVA", link.GetProperty("aviso").GetString());
        await Fail("regla_en_campo", new { formulario = "F-CFG", campo = "tipo", regla = "Avisar tipo" }); // ya la tiene
        await Ok("parametros_regla_en_campo", new { formulario = "F-CFG", campo = "tipo", regla = "Avisar tipo", parametros = new { message = "Solo en este formulario" } });

        // Regla al enviar: idempotente por verbo (repetir actualiza, no duplica).
        Assert.Contains("message", await Fail("regla_al_enviar", new { formulario = "F-CFG", verbo = "NOTIFICAR", nombre = "Aviso envio" }));
        await Ok("regla_al_enviar", new { formulario = "F-CFG", verbo = "NOTIFICAR", nombre = "Aviso envio", parametros = new { message = "Llego una solicitud" } });
        await Ok("regla_al_enviar", new { formulario = "F-CFG", verbo = "NOTIFICAR", nombre = "Aviso envio v2", parametros = new { message = "Llego una solicitud nueva" } });

        // Condicion entre campos.
        await Ok("condicion_entre_campos", new { formulario = "F-CFG", si_campo = "tipo", operador = "igual", valor = "Rechazo", efecto = "requerir", campo = "motivo" });
        Assert.Contains("valor", await Fail("condicion_entre_campos", new { formulario = "F-CFG", si_campo = "tipo", operador = "igual", efecto = "requerir", campo = "motivo" }));

        // Vista consolidada de las reglas del formulario.
        var todas = await Ok("reglas_de_formulario", new { formulario = "Solicitud CFG" });
        var alEnviar = Assert.Single(todas.GetProperty("al_enviar").EnumerateArray());
        Assert.Equal("Aviso envio v2", alEnviar.GetProperty("regla").GetString());
        var enCampos = todas.GetProperty("en_campos").EnumerateArray().ToList();
        Assert.Equal(2, enCampos.Count(c => c.GetProperty("campo").GetString() == "tipo"));
        Assert.Contains(enCampos, c => c.GetProperty("parametros_del_vinculo").ValueKind == JsonValueKind.Object);

        // Quitar de un campo y del envio.
        await Ok("quitar_regla_de_campo", new { formulario = "F-CFG", campo = "tipo", regla = "Avisar tipo" });
        await Ok("quitar_regla_al_enviar", new { formulario = "F-CFG", regla = "NOTIFICAR" });
        Assert.Empty(await ctx.FormSubmitRules.AsNoTracking().Where(x => x.DefinitionId == def.Id).ToListAsync());

        // Referencias por id: el concepto se da por CODIGO y se guarda su Guid; uno inexistente se rechaza.
        var cat = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-REF", Nombre = "Ref" };
        ctx.ActividadCategorias.Add(cat);
        await ctx.SaveChangesAsync();
        var sub = new ActividadSubcategoria { TenantId = seed.TenantId, CategoriaId = cat.Id, Codigo = "C-LEAD", Nombre = "Lead web" };
        ctx.ActividadSubcategorias.Add(sub);
        await ctx.SaveChangesAsync();
        Assert.Contains((await Ok("listar_conceptos", new { texto = "C-LEAD" })).GetProperty("conceptos").EnumerateArray(),
            c => c.GetProperty("concepto_id").GetGuid() == sub.Id);
        var refRule = await Ok("crear_regla", new { nombre = "Crear lead", verbo = "REFS_TEST", parametros = new { subcategoriaId = "c-lead" } });
        Assert.Contains(sub.Id.ToString(), (await ctx.Rules.AsNoTracking().SingleAsync(x => x.Id == refRule.GetProperty("regla_id").GetGuid())).ParamsJson);
        Assert.Contains("concepto", await Fail("crear_regla", new { nombre = "x", verbo = "REFS_TEST", parametros = new { subcategoriaId = "NO-EXISTE" } }));
        Assert.Contains("usuario", await Fail("crear_regla", new { nombre = "x", verbo = "REFS_TEST", parametros = new { subcategoriaId = "C-LEAD", assigneeUserId = "nadie@ejemplo.test" } }));

        // Ver y eliminar la regla (sin historial).
        var ver = await Ok("ver_regla", new { regla = reglaId });
        Assert.Equal("NOTIFICAR", ver.GetProperty("verbo").GetString());
        Assert.Equal(0, (await Ok("historial_regla", new { regla = reglaId })).GetProperty("total").GetInt32());
        await Ok("eliminar_regla", new { regla = reglaId });
        Assert.False(await ctx.Rules.AsNoTracking().AnyAsync(x => x.Id == reglaId));

        // Opciones del formulario: cada una cambia SOLO lo suyo.
        await Ok("ocultar_barra_envio", new { formulario = "F-CFG", activo = true });
        await Ok("formulario_reportable", new { formulario = "F-CFG", activo = true });
        await Ok("codigo_automatico_formulario", new { formulario = "F-CFG", activo = true, campo = "codigo", ancho = 5 });
        await Ok("registro_editable", new { formulario = "F-CFG", activo = true });
        await Ok("cierre_automatico_formulario", new { formulario = "F-CFG", campo = "firma" });
        var f1 = await ctx.FormDefinitions.AsNoTracking().SingleAsync(f => f.Id == def.Id);
        Assert.True(f1.HideSubmitBar);
        Assert.True(f1.IsReportable);
        Assert.True(f1.AutoCodeEnabled);
        Assert.Equal("codigo", f1.AutoCodeTargetFieldCode);
        Assert.Equal(5, f1.AutoCodePadWidth);
        Assert.Contains("keepEditable", f1.ThemeJson);
        Assert.Contains("notEmpty", f1.CloseRuleJson);
        await Ok("ocultar_barra_envio", new { formulario = "F-CFG", activo = false });
        var f2 = await ctx.FormDefinitions.AsNoTracking().SingleAsync(f => f.Id == def.Id);
        Assert.False(f2.HideSubmitBar);
        Assert.True(f2.IsReportable && f2.AutoCodeEnabled); // no se apagaron

        // Propiedades de campo (PATCH: una no borra otra).
        await Ok("campo_bloqueado", new { formulario = "F-CFG", campo = "total", activo = true });
        await Ok("valor_por_defecto_dinamico", new { formulario = "F-CFG", campo = "total", valor = "hoy" });
        await Ok("permisos_campo_por_rol", new { formulario = "F-CFG", campo = "total", ocultar_a = new[] { "Advisor" }, solo_lectura_a = new[] { "Supervisor" } });
        Assert.Contains("Rol", await Fail("permisos_campo_por_rol", new { formulario = "F-CFG", campo = "total", ocultar_a = new[] { "Jefe" } }));
        Assert.Contains("Subform", await Fail("subformulario_de_campo", new { formulario = "F-CFG", campo = "total", subformulario = "F-CFG" }));
        var qTotal = await ctx.FormQuestions.AsNoTracking().SingleAsync(q => q.DefinitionId == def.Id && q.FieldCode == "total");
        Assert.True(qTotal.IsReadOnly); // no editable para todos (ADR-0212), no el candado de layout
        Assert.False(qTotal.IsLocked);
        Assert.Equal(FormDefaultDynamic.Today, qTotal.DefaultDynamic);
        Assert.Contains("Advisor", qTotal.FieldVisibilityJson);

        // Versiones: guardar, cambiar, restaurar.
        var ver1 = await Ok("guardar_version_formulario", new { formulario = "F-CFG", nombre = "Antes de cambios" });
        var vid = ver1.GetProperty("version_id").GetGuid();
        Assert.Contains((await Ok("listar_versiones_formulario", new { formulario = "F-CFG" })).GetProperty("versiones").EnumerateArray(),
            v => v.GetProperty("version_id").GetGuid() == vid);
        await Ok("campo_oculto", new { formulario = "F-CFG", campo = "motivo", activo = true });
        await Ok("restaurar_version_formulario", new { formulario = "F-CFG", version = vid });
        var motivos = await ctx.FormQuestions.AsNoTracking().Where(q => q.DefinitionId == def.Id && q.FieldCode == "motivo").ToListAsync();
        Assert.NotEmpty(motivos);
        Assert.All(motivos, q => Assert.False(q.IsHidden));

        // Enlaces: sin emitir, lista vacia; revocar uno ajeno falla.
        Assert.Equal(0, (await Ok("listar_enlaces_formulario", new { formulario = "F-CFG" })).GetProperty("total").GetInt32());
        await Fail("revocar_enlace_formulario", new { formulario = "F-CFG", enlace = Guid.NewGuid() });

        // Diseno del control (ADR-0212): mezcla claves, descarta lo invalido, quita y limpia.
        await Ok("campo_bloqueado", new { formulario = "F-CFG", campo = "total", activo = true });
        await Ok("diseno_campo", new { formulario = "F-CFG", campo = "total", estilo = new { labelBold = true, labelColor = "#FF0000", radius = 99 } });
        var st1 = await Ok("diseno_campo", new { formulario = "F-CFG", campo = "total", estilo = new { boxBg = "rojo", valueSize = 14 } });
        var estilo = st1.GetProperty("estilo");
        Assert.True(estilo.GetProperty("labelBold").GetBoolean());
        Assert.Equal("#ff0000", estilo.GetProperty("labelColor").GetString());
        Assert.Equal(40, estilo.GetProperty("radius").GetInt32()); // acotado
        Assert.Equal(14, estilo.GetProperty("valueSize").GetInt32());
        Assert.False(estilo.TryGetProperty("boxBg", out _)); // color invalido descartado
        Assert.Contains("no valida", await Fail("diseno_campo", new { formulario = "F-CFG", campo = "total", estilo = new { fuente = "Arial" } }));
        var st2 = await Ok("diseno_campo", new { formulario = "F-CFG", campo = "total", quitar = new[] { "radius" } });
        Assert.False(st2.GetProperty("estilo").TryGetProperty("radius", out _));
        var qStyled = await ctx.FormQuestions.AsNoTracking().SingleAsync(q => q.DefinitionId == def.Id && q.FieldCode == "total");
        Assert.True(qStyled.IsReadOnly); // el diseno no borra otras propiedades
        await Ok("diseno_campo", new { formulario = "F-CFG", campo = "total", limpiar = true });
        Assert.Null((await ctx.FormQuestions.AsNoTracking().SingleAsync(q => q.DefinitionId == def.Id && q.FieldCode == "total")).StyleJson);

        // Renombrar el nombre interno: simulacion primero (no guarda), luego aplicar.
        var sim = await Ok("renombrar_campo", new { formulario = "F-CFG", campo = "total", nuevo_codigo = "total_general" });
        Assert.False(sim.GetProperty("aplicado").GetBoolean());
        Assert.True(await ctx.FormQuestions.AsNoTracking().AnyAsync(q => q.DefinitionId == def.Id && q.FieldCode == "total"));
        var ren = await Ok("renombrar_campo", new { formulario = "F-CFG", campo = "total", nuevo_codigo = "total_general", aplicar = true });
        Assert.True(ren.GetProperty("aplicado").GetBoolean());
        Assert.True(await ctx.FormQuestions.AsNoTracking().AnyAsync(q => q.DefinitionId == def.Id && q.FieldCode == "total_general"));
        Assert.False(await ctx.FormQuestions.AsNoTracking().AnyAsync(q => q.DefinitionId == def.Id && q.FieldCode == "total"));
    }

    /// <summary>La IA respeta los permisos del usuario al CREAR flujos y formularios: un lector solo ve; sin
    /// permiso de edicion no crea ni cambia. El puente de formularios reenvia al toolset existente COMO el usuario
    /// y mapea sus errores estructurados (ok:false) a error del MCP.</summary>
    [Fact]
    public async Task AuthoringTools_RespectFlowAndFormPermissions_AndBridgeForms()
    {
        var seed = await SeedTenantAsync("Mcp Autoria Permisos");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var fakeForms = new FakeFormAuthoring();
        var (_, _, toolset) = Build(ctx, tenantCtx, fakeForms);

        // Owner (sin rol = sin restriccion) puede todo: crea un flujo y, por el puente, un formulario.
        var owner = Actor(seed, owner: true);
        var defId = Payload(await toolset.ExecuteAsync("crear_flujo", Args(new { nombre = "Flujo Owner" }), owner)).GetProperty("definicion_id").GetGuid();
        Assert.False((await toolset.ExecuteAsync("create_form", Args(new { title = "Form Owner" }), owner)).IsError);
        Assert.Contains(fakeForms.Calls, c => c.Tool == "create_form" && c.Actor == seed.OwnerPlatformId); // corre COMO el usuario
        // Error estructurado del toolset de formularios (ok:false) => error del MCP.
        Assert.True((await toolset.ExecuteAsync("create_form", Args(new { title = "x", fail = true }), owner)).IsError);

        // Rol del miembro: solo Ver en flujos; Ver en formularios (sin Editar).
        var rol = new Rol { TenantId = seed.TenantId, Name = "Consulta procesos" };
        ctx.Roles.Add(rol);
        ctx.RolPermisos.Add(new RolPermiso { TenantId = seed.TenantId, RolId = rol.Id, ModuleKey = "flujos", CanView = true });
        ctx.RolPermisos.Add(new RolPermiso { TenantId = seed.TenantId, RolId = rol.Id, ModuleKey = "formularios", CanView = true });
        var member = await ctx.TenantUsers.SingleAsync(u => u.Id == seed.MemberUserId);
        member.RolId = rol.Id;
        await ctx.SaveChangesAsync();
        var reader = Actor(seed, owner: false);

        // Ver si; crear/editar no (flujos).
        Assert.False((await toolset.ExecuteAsync("listar_flujos", "{}", reader)).IsError);
        Assert.False((await toolset.ExecuteAsync("ver_flujo", Args(new { flujo = defId }), reader)).IsError);
        var crearDenied = await toolset.ExecuteAsync("crear_flujo", Args(new { nombre = "No permitido" }), reader);
        Assert.True(crearDenied.IsError);
        Assert.Contains("permiso", crearDenied.Json);
        Assert.True((await toolset.ExecuteAsync("agregar_nodo", Args(new { flujo = defId, tipo = "tarea" }), reader)).IsError);

        // Formularios: list (solo lectura) si; create_form (escritura) no, y NO llega al toolset.
        var callsBefore = fakeForms.Calls.Count;
        Assert.False((await toolset.ExecuteAsync("list_forms", "{}", reader)).IsError);
        Assert.True((await toolset.ExecuteAsync("create_form", Args(new { title = "No" }), reader)).IsError);
        Assert.DoesNotContain(fakeForms.Calls.Skip(callsBefore), c => c.Tool == "create_form");

        // Las herramientas de autoria aparecen en el catalogo cuando sus servicios estan inyectados.
        var specNames = toolset.GetSpecs().Select(s => s.Name).ToHashSet();
        Assert.Contains("crear_flujo", specNames);
        Assert.Contains("create_form", specNames);
        Assert.Contains("enganchar_flujo_a_concepto", specNames);
        Assert.DoesNotContain("create_form", toolset.ReadOnlyTools); // escritura: se audita
        Assert.Contains("list_forms", toolset.ReadOnlyTools);        // lectura: no se audita
    }

    /// <summary>
    /// SEGURIDAD del puente de formularios: el toolset de autoria (ADR-0067) trae herramientas de PLATAFORMA que
    /// solo deben salir por /api/mgmt. list_tenants lee TODOS los tenants (IgnoreQueryFilters): por el token
    /// personal seria una fuga cross-tenant (regla #1). Tampoco se exponen las de menu. Lista PERMITIDA.
    /// </summary>
    [Fact]
    public async Task FormBridge_HidesCrossTenantAndMenuTools()
    {
        var seed = await SeedTenantAsync("Mcp Puente Seguro");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var fake = new FakeFormAuthoring();
        var (_, _, toolset) = Build(ctx, tenantCtx, fake);
        var owner = Actor(seed, owner: true); // Owner = sin restriccion de permisos: ni asi debe ver estas.

        var names = toolset.GetSpecs().Select(s => s.Name).ToHashSet();
        foreach (var hidden in new[] { "list_tenants", "list_menu_nodes", "set_module" })
        {
            Assert.DoesNotContain(hidden, names);
            Assert.DoesNotContain(hidden, toolset.ReadOnlyTools);
            var r = await toolset.ExecuteAsync(hidden, "{}", owner);
            Assert.True(r.IsError);
            Assert.Contains("desconocida", r.Json);
        }
        Assert.DoesNotContain(fake.Calls, c => c.Tool is "list_tenants" or "list_menu_nodes" or "set_module");

        // Las permitidas si pasan, y la destructiva se anuncia como tal.
        Assert.Contains("create_form", names);
        Assert.Contains("delete_question", toolset.DestructiveTools);
    }

    /// <summary>
    /// Integridad del CATALOGO del MCP personal (guarda contra regresiones de documentacion/contrato): nombres
    /// unicos, descripcion util, JSON-Schema valido de tipo objeto con 'required' coherente, conjuntos de solo
    /// lectura/destructivas dentro de lo expuesto, toda herramienta listada se despacha y todo ok:false es error.
    /// </summary>
    [Fact]
    public async Task McpCatalog_IsConsistent_AndEveryToolDispatches()
    {
        var seed = await SeedTenantAsync("Mcp Catalogo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx, new FakeFormAuthoring());
        var owner = Actor(seed, owner: true);

        var specs = toolset.GetSpecs();
        var names = specs.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16 + 1 + 12 + 16 + 23 + 14 + 3, specs.Count); // nucleo + guia_ecorex + directorio + flujos + formularios permitidos del doble

        foreach (var s in specs)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Description), $"{s.Name}: sin descripcion");
            Assert.True(s.Description!.Length >= 25, $"{s.Name}: descripcion demasiado corta");
            using var schema = JsonDocument.Parse(s.ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            var props = schema.RootElement.TryGetProperty("properties", out var p) ? p : default;
            if (schema.RootElement.TryGetProperty("required", out var req))
            {
                foreach (var r in req.EnumerateArray())
                {
                    Assert.True(props.ValueKind == JsonValueKind.Object && props.TryGetProperty(r.GetString()!, out _),
                        $"{s.Name}: 'required' {r} no esta en properties");
                }
            }
        }

        Assert.All(toolset.ReadOnlyTools, n => Assert.Contains(n, names));
        Assert.All(toolset.DestructiveTools, n => Assert.Contains(n, names));
        Assert.Empty(toolset.ReadOnlyTools.Intersect(toolset.DestructiveTools));
        Assert.Contains("guia_ecorex", toolset.ReadOnlyTools); // ADR-0204: manual de uso, solo lectura y sin gate

        // 'nota' y 'buscar' son excluyentes.
        Assert.True((await toolset.ExecuteAsync("guia_ecorex", """{"nota":"a","buscar":"b"}""", owner)).IsError);

        foreach (var name in names)
        {
            var r = await toolset.ExecuteAsync(name, "{}", owner);
            Assert.DoesNotContain("Herramienta desconocida", r.Json);
            using var doc = JsonDocument.Parse(r.Json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                Assert.True(r.IsError, $"{name}: ok=false sin marcar error");
            }
        }
    }

    // ===================== Clasificacion por lotes (ADR-0197) =====================

    [Fact]
    public async Task ClasificarLote_SoloConRunnerYContenedores_YDespachaConLaTablaResuelta()
    {
        var seed = await SeedTenantAsync("Mcp Clasificacion");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var owner = Actor(seed, owner: true);

        // Sin runner la herramienta no existe (ni se despacha).
        var (_, _, sinRunner) = Build(ctx, tenantCtx, withContainers: true);
        Assert.DoesNotContain("clasificar_lote", sinRunner.GetSpecs().Select(s => s.Name));
        Assert.True((await sinRunner.ExecuteAsync("clasificar_lote", "{}", owner)).IsError);

        var runner = new FakeClasificador();
        var (_, _, toolset) = Build(ctx, tenantCtx, withContainers: true, clasificador: runner);
        var specs = toolset.GetSpecs();
        Assert.Contains("clasificar_lote", specs.Select(s => s.Name));
        Assert.DoesNotContain("clasificar_lote", toolset.ReadOnlyTools);      // escribe: se audita
        Assert.DoesNotContain("clasificar_lote", toolset.DestructiveTools);
        using (var schema = JsonDocument.Parse(specs.First(s => s.Name == "clasificar_lote").ParametersJsonSchema))
        {
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }

        Payload(await toolset.ExecuteAsync("crear_contenedor_datos", Args(new { nombre = "SECOP Mcp" }), owner));
        Payload(await toolset.ExecuteAsync("agregar_tabla",
            Args(new { contenedor = "SECOP Mcp", nombre = "Tecnologia", columnas = new[] { new { nombre = "id_proceso", tipo = "texto", requerido = false } } }), owner));
        var tableId = await ctx.DataContainers.AsNoTracking().Where(t => t.Name == "Tecnologia").Select(t => t.Id).SingleAsync();

        var r = Payload(await toolset.ExecuteAsync("clasificar_lote", Args(new
        {
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            agente = "Clasificador",
            columna_clave = "id_proceso",
            columna_marca = "clasificado_en",
            columnas_salida = new[] { "sector" },
            limite = 7
        }), owner));
        Assert.Equal(3, r.GetProperty("restantes").GetInt32());
        Assert.Equal(2, r.GetProperty("actualizados").GetInt32());
        var req = runner.Last!;
        Assert.Equal(tableId, req.TableId);
        Assert.Equal(new[] { "sector" }, req.ColumnasSalida);
        Assert.Null(req.ColumnasEntrada);
        Assert.Equal(7, req.Limite);

        // Error del runner -> error MCP; tabla inexistente -> error sin llamar al runner.
        runner.Fail = true;
        Assert.True((await toolset.ExecuteAsync("clasificar_lote", Args(new { contenedor = "SECOP Mcp", tabla = "Tecnologia", agente = "x", columna_clave = "id_proceso", columna_marca = "m", columnas_salida = new[] { "sector" } }), owner)).IsError);
        var llamadas = runner.Calls;
        Assert.True((await toolset.ExecuteAsync("clasificar_lote", Args(new { contenedor = "SECOP Mcp", tabla = "NoExiste", agente = "x", columna_clave = "id_proceso", columna_marca = "m", columnas_salida = new[] { "sector" } }), owner)).IsError);
        Assert.Equal(llamadas, runner.Calls);
    }

    // ===================== Paso de clasificacion en la importacion (ADR-0199) =====================

    [Fact]
    public async Task PasoClasificacion_ToolsExpuestas_ConfiguraSolicitaLimpiaYQuita()
    {
        var seed = await SeedTenantAsync("Mcp Paso Clasificacion");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var owner = Actor(seed, owner: true);
        var fake = new FakeClasificador();

        // Sin el servicio del paso: clasificar_lote y limpiar_marca si, las del paso no.
        var (_, _, sinPaso) = Build(ctx, tenantCtx, withContainers: true, clasificador: fake);
        var namesSin = sinPaso.GetSpecs().Select(s => s.Name).ToList();
        Assert.Contains("limpiar_marca", namesSin);
        Assert.DoesNotContain("configurar_paso_clasificacion", namesSin);
        Assert.True((await sinPaso.ExecuteAsync("configurar_paso_clasificacion", "{}", owner)).IsError);

        var svc = new Ecorex.Application.DataContainers.ImportClassificationService(ctx, tenantCtx, fake, TimeProvider.System);
        var (_, _, toolset) = Build(ctx, tenantCtx, withContainers: true, clasificador: fake, importClassify: svc);
        var specs = toolset.GetSpecs();
        var names = specs.Select(s => s.Name).ToList();
        foreach (var t in new[] { "configurar_paso_clasificacion", "quitar_paso_clasificacion", "ejecutar_clasificacion_ahora" })
        {
            Assert.Contains(t, names);
            Assert.DoesNotContain(t, toolset.ReadOnlyTools);
            using var schema = JsonDocument.Parse(specs.First(s => s.Name == t).ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.True(specs.First(s => s.Name == t).Description.Length >= 25);
        }
        Assert.Equal(namesSin.Count + 3, names.Count);
        Assert.Contains("limpiar_marca", toolset.DestructiveTools);
        Assert.All(toolset.ReadOnlyTools, n => Assert.Contains(n, names));

        // Contenedor con tabla, conector API a esa tabla y programacion cron que lo dispara.
        Payload(await toolset.ExecuteAsync("crear_contenedor_datos", Args(new { nombre = "SECOP Paso" }), owner));
        Payload(await toolset.ExecuteAsync("agregar_tabla", Args(new
        {
            contenedor = "SECOP Paso",
            nombre = "Procesos",
            columnas = new[]
            {
                new { nombre = "id_proceso", tipo = "texto", requerido = false },
                new { nombre = "nombre_proceso", tipo = "texto", requerido = false },
                new { nombre = "sector", tipo = "texto", requerido = false },
                new { nombre = "clasificado_en", tipo = "texto", requerido = false }
            }
        }), owner));
        Payload(await toolset.ExecuteAsync("guardar_conector", Args(new
        {
            contenedor = "SECOP Paso",
            nombre = "Socrata",
            tipo = "api",
            destino = "Procesos",
            endpoint = "https://example.test/api"
        }), owner));
        Payload(await toolset.ExecuteAsync("guardar_programacion_importacion", Args(new
        {
            contenedor = "SECOP Paso",
            nombre = "Recientes diaria",
            tipo = "cron",
            cron = "0 5 * * *",
            modo = "upsert",
            columna_clave = "id_proceso",
            conector = "Socrata"
        }), owner));
        var tableId = await ctx.DataContainers.AsNoTracking().Where(t => t.Name == "Procesos").Select(t => t.Id).SingleAsync();
        var agent = new AiAgent { TenantId = seed.TenantId, Name = "Clasificador General", SystemPrompt = "x", Provider = AiProvider.DeepSeek, IsActive = false };
        ctx.AiAgents.Add(agent);
        await ctx.SaveChangesAsync();

        // Ejecutar sin paso: error claro.
        Assert.True((await toolset.ExecuteAsync("ejecutar_clasificacion_ahora", Args(new { contenedor = "SECOP Paso", programacion = "Recientes diaria" }), owner)).IsError);

        // Configurar: tabla por defecto = destino del conector; agente apagado = aviso.
        var conf = Payload(await toolset.ExecuteAsync("configurar_paso_clasificacion", Args(new
        {
            contenedor = "SECOP Paso",
            programacion = "Recientes diaria",
            agente = "Clasificador General",
            columna_clave = "id_proceso",
            columna_marca = "clasificado_en",
            columnas_salida = new[] { "sector" },
            columnas_entrada = new[] { "nombre_proceso" },
            limite = 50,
            max_lotes = 500
        }), owner));
        Assert.Equal("Procesos", conf.GetProperty("tabla").GetString());
        Assert.Contains("APAGADO", conf.GetProperty("aviso").GetString());
        var paso = conf.GetProperty("paso_clasificacion");
        Assert.Equal(tableId, paso.GetProperty("tabla_id").GetGuid());
        Assert.Equal(agent.Id.ToString(), paso.GetProperty("agente_id").GetString());
        Assert.Equal(200, paso.GetProperty("max_lotes").GetInt32()); // se acota al tope

        // Columna inexistente o salida = clave: error sin guardar.
        Assert.True((await toolset.ExecuteAsync("configurar_paso_clasificacion", Args(new
        {
            contenedor = "SECOP Paso",
            programacion = "Recientes diaria",
            agente = "Clasificador General",
            columna_clave = "id_proceso",
            columna_marca = "no_existe",
            columnas_salida = new[] { "sector" }
        }), owner)).IsError);
        Assert.True((await toolset.ExecuteAsync("configurar_paso_clasificacion", Args(new
        {
            contenedor = "SECOP Paso",
            programacion = "Recientes diaria",
            agente = "Clasificador General",
            columna_clave = "id_proceso",
            columna_marca = "clasificado_en",
            columnas_salida = new[] { "id_proceso" }
        }), owner)).IsError);

        // listar_programaciones_importacion muestra el paso.
        var lista = Payload(await toolset.ExecuteAsync("listar_programaciones_importacion", Args(new { contenedor = "SECOP Paso" }), owner));
        var prog = lista.GetProperty("programaciones")[0];
        Assert.Equal("clasificado_en", prog.GetProperty("paso_clasificacion").GetProperty("columna_marca").GetString());
        Assert.False(prog.TryGetProperty("clasificacion_pedida", out _)); // null = se omite

        // Ejecutar ahora: deja la solicitud para el worker.
        Payload(await toolset.ExecuteAsync("ejecutar_clasificacion_ahora", Args(new { contenedor = "SECOP Paso", programacion = "Recientes diaria" }), owner));
        var proc = await ctx.ImportProcesses.AsNoTracking().SingleAsync(p => p.Name == "Recientes diaria");
        Assert.NotNull(proc.ClassifyRequestedAt);

        // limpiar_marca: despacha al servicio con la tabla, salidas y filtro.
        var limpio = Payload(await toolset.ExecuteAsync("limpiar_marca", Args(new
        {
            contenedor = "SECOP Paso",
            tabla = "Procesos",
            columna_marca = "clasificado_en",
            columnas_salida = new[] { "sector" },
            filtro = new Dictionary<string, string> { ["sector"] = "otro" }
        }), owner));
        Assert.Equal(4, limpio.GetProperty("filas").GetInt32());
        Assert.Equal(tableId, fake.LastLimpiar!.TableId);
        Assert.Equal("otro", fake.LastLimpiar.Filtro!["sector"]);
        Assert.Equal(new[] { "sector" }, fake.LastLimpiar.ColumnasSalida);

        // rellenar_columna (ADR-0201): expuesta (no destructiva), despacha tabla/columna/plantilla y solo_vacias.
        Assert.Contains("rellenar_columna", names);
        Assert.DoesNotContain("rellenar_columna", toolset.DestructiveTools);
        Assert.True((await toolset.ExecuteAsync("rellenar_columna", Args(new { contenedor = "SECOP Paso", tabla = "Procesos", columna = "sector" }), owner)).IsError);
        var relleno = Payload(await toolset.ExecuteAsync("rellenar_columna", Args(new
        {
            contenedor = "SECOP Paso",
            tabla = "Procesos",
            columna = "sector",
            plantilla = "{id_proceso}|{nombre_proceso}"
        }), owner));
        Assert.Equal(5, relleno.GetProperty("rellenadas").GetInt32());
        Assert.Equal(tableId, fake.LastRellenar!.TableId);
        Assert.Equal("{id_proceso}|{nombre_proceso}", fake.LastRellenar.Plantilla);
        Assert.True(fake.LastRellenar.SoloVacias);
        Payload(await toolset.ExecuteAsync("rellenar_columna", Args(new
        {
            contenedor = "SECOP Paso",
            tabla = "Procesos",
            columna = "sector",
            plantilla = "{id_proceso}",
            solo_vacias = false
        }), owner));
        Assert.False(fake.LastRellenar.SoloVacias);

        // Quitar: limpia paso y solicitud.
        Payload(await toolset.ExecuteAsync("quitar_paso_clasificacion", Args(new { contenedor = "SECOP Paso", programacion = "Recientes diaria" }), owner));
        proc = await ctx.ImportProcesses.AsNoTracking().SingleAsync(p => p.Name == "Recientes diaria");
        Assert.Null(proc.ClassifyStepJson);
        Assert.Null(proc.ClassifyRequestedAt);
    }

    // ===================== Reportes e indicadores (ADR-0198) =====================

    [Fact]
    public async Task Reportes_FuentePorContenedorYTabla_CreaPanelEIndicadores_QueCorren()
    {
        var seed = await SeedTenantAsync("Mcp Reportes");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var owner = Actor(seed, owner: true);

        // Sin el motor de reportes las herramientas no existen.
        var (_, _, sinReportes) = Build(ctx, tenantCtx);
        Assert.DoesNotContain("crear_reporte", sinReportes.GetSpecs().Select(s => s.Name));
        Assert.True((await sinReportes.ExecuteAsync("crear_reporte", "{}", owner)).IsError);

        var (_, _, toolset) = Build(ctx, tenantCtx, withReports: true);
        var names = toolset.GetSpecs().Select(s => s.Name).ToList();
        foreach (var t in new[] { "listar_fuentes_reporte", "ver_fuente_reporte", "listar_reportes", "ver_reporte", "crear_reporte",
            "listar_libros_indicadores", "ver_libro_indicadores", "crear_libro_indicadores", "agregar_indicador" })
        {
            Assert.Contains(t, names);
        }
        Assert.Contains("listar_fuentes_reporte", toolset.ReadOnlyTools);
        Assert.DoesNotContain("crear_reporte", toolset.ReadOnlyTools);
        Assert.DoesNotContain("agregar_indicador", toolset.ReadOnlyTools);
        Assert.Equal(sinReportes.GetSpecs().Count + 13, toolset.GetSpecs().Count); // ADR-0206: + editar/archivar reporte, eliminar indicador/libro
        Assert.All(toolset.ReadOnlyTools, n => Assert.Contains(n, names));
        foreach (var spec in toolset.GetSpecs().Where(s => s.Name.Contains("reporte") || s.Name.Contains("indicador")))
        {
            Assert.True(spec.Description.Length >= 25, $"{spec.Name}: descripcion demasiado corta");
            using var schema = JsonDocument.Parse(spec.ParametersJsonSchema);
            var props = schema.RootElement.GetProperty("properties");
            if (schema.RootElement.TryGetProperty("required", out var req))
            {
                Assert.All(req.EnumerateArray(), r => Assert.True(props.TryGetProperty(r.GetString()!, out _), $"{spec.Name}: required {r}"));
            }
            if (spec.Name is "crear_reporte" or "agregar_indicador")
            {
                Assert.Equal("object", props.GetProperty("consulta").GetProperty("type").GetString());
            }
        }

        // Contenedor con DOS tablas (como SECOP): cada tabla es su propia fuente.
        var (tecnologiaId, _) = await SeedSecopAsync(ctx, seed.TenantId);

        var fuentes = Payload(await toolset.ExecuteAsync("listar_fuentes_reporte", Args(new { tipo = "contenedor" }), owner));
        var secop = fuentes.GetProperty("fuentes").EnumerateArray().Where(f => f.GetProperty("contenedor").GetString() == "SECOP Mcp").ToList();
        Assert.Equal(2, secop.Count);
        Assert.Contains(secop, f => f.GetProperty("fuente").GetString() == $"container:{tecnologiaId}" && f.GetProperty("tabla").GetString() == "Tecnologia");

        var campos = Payload(await toolset.ExecuteAsync("ver_fuente_reporte", Args(new { contenedor = "SECOP Mcp", tabla = "Tecnologia" }), owner));
        Assert.Equal($"container:{tecnologiaId}", campos.GetProperty("fuente").GetString());
        Assert.Contains(campos.GetProperty("campos").EnumerateArray(), c => c.GetProperty("campo").GetString() == "precio_base" && c.GetProperty("sumable").GetBoolean());
        // Sin tabla en un contenedor de dos tablas: pide la tabla.
        Assert.True((await toolset.ExecuteAsync("ver_fuente_reporte", Args(new { contenedor = "SECOP Mcp" }), owner)).IsError);

        // "Oportunidades tech por departamento": filtro + agrupacion + conteo ordenado.
        var rep = Payload(await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "Oportunidades tech por departamento",
            categoria = "SECOP",
            grafico = "barras",
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            consulta = new
            {
                filtros = new[] { new { campo = "es_tecnologia", operador = "igual", valor = "si" } },
                agrupar_por = "departamento",
                orden = new[] { new { campo = "conteo", desc = true } }
            }
        }), owner));
        var reporteId = Guid.Parse(rep.GetProperty("reporte_id").GetString()!);
        Assert.Equal(2, rep.GetProperty("resultado").GetProperty("filas").GetInt32());
        var def = await ctx.ReportDefinitions.AsNoTracking().SingleAsync(d => d.Id == reporteId);
        Assert.Equal($"container:{tecnologiaId}", def.SourceKey);
        Assert.Equal("SECOP", def.Category);

        // El reporte guardado corre por el motor (galeria): Antioquia 2, Cundinamarca 1.
        var run = await new Ecorex.Application.Reporting.ReportDefinitionService(ctx, tenantCtx,
            ReportServices(ctx, tenantCtx).Data!).RunAsync(reporteId);
        Assert.NotNull(run);
        Assert.Equal(2, run!.DataSet.RowCount);
        Assert.Equal("Antioquia", run.DataSet.Rows[0][0]);
        Assert.Equal(2L, Convert.ToInt64(run.DataSet.Rows[0][1]));

        var listado = Payload(await toolset.ExecuteAsync("listar_reportes", "{}", owner));
        Assert.Contains(listado.GetProperty("reportes").EnumerateArray(), r => r.GetProperty("reporte_id").GetString() == reporteId.ToString());
        var visto = Payload(await toolset.ExecuteAsync("ver_reporte", Args(new { reporte = "Oportunidades tech" }), owner));
        Assert.Equal(2, visto.GetProperty("resultado").GetProperty("filas").GetInt32());

        // "Monto por sector": suma de un campo decimal.
        var monto = Payload(await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "Monto por sector",
            fuente = $"container:{tecnologiaId}",
            grafico = "torta",
            consulta = new { agrupar_por = "sector", agregaciones = new[] { new { funcion = "sumar", campo = "precio_base" } } }
        }), owner));
        Assert.Equal(2, monto.GetProperty("resultado").GetProperty("filas").GetInt32());

        // Errores: suma sobre texto, campo inexistente, grafico sin agrupar. No guardan nada.
        var antes = await ctx.ReportDefinitions.CountAsync();
        Assert.True((await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "x",
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            consulta = new { agrupar_por = "sector", agregaciones = new[] { new { funcion = "sumar", campo = "departamento" } } }
        }), owner)).IsError);
        Assert.True((await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "x",
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            consulta = new { campos = new[] { "no_existe" } }
        }), owner)).IsError);
        Assert.True((await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "x",
            grafico = "barras",
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            consulta = new { campos = new[] { "sector" } }
        }), owner)).IsError);
        Assert.Equal(antes, await ctx.ReportDefinitions.CountAsync());

        // Libro de indicadores: KPI + barras; una hoja nueva por nombre.
        var libro = Payload(await toolset.ExecuteAsync("crear_libro_indicadores", Args(new { nombre = "SECOP tablero", descripcion = "prueba" }), owner));
        var libroId = Guid.Parse(libro.GetProperty("libro_id").GetString()!);
        var kpi = Payload(await toolset.ExecuteAsync("agregar_indicador", Args(new
        {
            libro = "SECOP tablero",
            titulo = "Procesos tech",
            tipo = "kpi",
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            consulta = new
            {
                filtros = new[] { new { campo = "es_tecnologia", operador = "igual", valor = "si" } },
                agregaciones = new[] { new { funcion = "contar" } }
            }
        }), owner));
        Assert.Equal(3m, kpi.GetProperty("resultado").GetProperty("valor").GetDecimal());
        var barras = Payload(await toolset.ExecuteAsync("agregar_indicador", Args(new
        {
            libro = libroId.ToString(),
            hoja = "Por modalidad",
            titulo = "Procesos por modalidad",
            tipo = "barras",
            ancho = 3,
            fuente = $"container:{tecnologiaId}",
            consulta = new { agrupar_por = "modalidad" }
        }), owner));
        Assert.True(barras.GetProperty("hoja_creada").GetBoolean());
        Assert.Equal(2, barras.GetProperty("resultado").GetProperty("total").GetInt32());

        // Errores: kpi con agrupacion; barras sin agrupar. No agregan widgets.
        Assert.True((await toolset.ExecuteAsync("agregar_indicador", Args(new
        {
            libro = "SECOP tablero",
            titulo = "x",
            tipo = "kpi",
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            consulta = new { agrupar_por = "sector" }
        }), owner)).IsError);
        Assert.True((await toolset.ExecuteAsync("agregar_indicador", Args(new
        {
            libro = "SECOP tablero",
            titulo = "x",
            tipo = "barras",
            contenedor = "SECOP Mcp",
            tabla = "Tecnologia",
            consulta = new { campos = new[] { "sector" } }
        }), owner)).IsError);

        var verLibro = Payload(await toolset.ExecuteAsync("ver_libro_indicadores", Args(new { libro = "SECOP tablero" }), owner));
        var hojas = verLibro.GetProperty("hojas").EnumerateArray().ToList();
        Assert.Equal(2, hojas.Count);
        Assert.Equal(2, hojas.Sum(h => h.GetProperty("widgets").GetArrayLength()));

        // Los widgets guardados corren con el runner de la hoja.
        var svc = ReportServices(ctx, tenantCtx);
        var detalle = await svc.Analisis!.ObtenerLibroAsync(libroId, new Ecorex.Application.Analisis.AnalisisActor(seed.OwnerPlatformId, true));
        foreach (var w in detalle!.Hojas.SelectMany(h => h.Widgets))
        {
            var res = await svc.Runner!.RunAsync(w, new Ecorex.Application.Reporting.ReportContext(seed.TenantId));
            Assert.True(res.Ok, res.Error);
        }

        var libros = Payload(await toolset.ExecuteAsync("listar_libros_indicadores", "{}", owner));
        Assert.Contains(libros.GetProperty("libros").EnumerateArray(), l => l.GetProperty("libro_id").GetString() == libroId.ToString());
    }

    // ===================== ADR-0206: filas por MCP + reportes por periodo + editar/archivar =====================

    [Fact]
    public async Task Filas_PeriodoMes_EditarArchivar_EliminarIndicador_ADR0206()
    {
        var seed = await SeedTenantAsync("Mcp Ventas Mes");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var owner = Actor(seed, owner: true);
        var (_, _, toolset) = Build(ctx, tenantCtx, withContainers: true, withReports: true);

        Assert.Contains("ver_filas", toolset.ReadOnlyTools);
        Assert.DoesNotContain("agregar_filas", toolset.ReadOnlyTools);
        Assert.Contains("archivar_reporte", toolset.DestructiveTools);
        Assert.Contains("eliminar_indicador", toolset.DestructiveTools);
        Assert.DoesNotContain("editar_reporte", toolset.ReadOnlyTools);

        // Contenedor "Pedidos" con una tabla: fecha + total (decimal) + cliente (obligatorio) + unidades (numero).
        var model = new DataModel { TenantId = seed.TenantId, Name = "Pedidos Mcp" };
        ctx.DataModels.Add(model);
        var tabla = new DataContainer { TenantId = seed.TenantId, Name = "Pedidos", ModelId = model.Id };
        ctx.DataContainers.Add(tabla);
        ctx.DataContainerColumns.AddRange(
            new DataContainerColumn { TenantId = seed.TenantId, ContainerId = tabla.Id, Name = "pedido", Type = DataContainerColumnType.Text, SortOrder = 0 },
            new DataContainerColumn { TenantId = seed.TenantId, ContainerId = tabla.Id, Name = "fecha", Type = DataContainerColumnType.Date, SortOrder = 1 },
            new DataContainerColumn { TenantId = seed.TenantId, ContainerId = tabla.Id, Name = "total", Type = DataContainerColumnType.Decimal, SortOrder = 2 },
            new DataContainerColumn { TenantId = seed.TenantId, ContainerId = tabla.Id, Name = "cliente", Type = DataContainerColumnType.Text, SortOrder = 3, IsRequired = true },
            new DataContainerColumn { TenantId = seed.TenantId, ContainerId = tabla.Id, Name = "unidades", Type = DataContainerColumnType.Number, SortOrder = 4 });
        await ctx.SaveChangesAsync();

        // agregar_filas: un lote con UN error no guarda nada (todo o nada).
        var malo = await toolset.ExecuteAsync("agregar_filas", Args(new
        {
            contenedor = "Pedidos Mcp",
            filas = new object[]
            {
                new { pedido = "A1", fecha = "2026-01-05", total = "100.50", cliente = "Ana", unidades = "2" },
                new { pedido = "A2", fecha = "05/01/2026", total = "1.234,5", cliente = "Beto", unidades = "x", color = "rojo" }
            }
        }), owner);
        Assert.True(malo.IsError);
        Assert.Contains("no es una fecha", ErrorText(malo));
        Assert.Contains("no es un decimal", ErrorText(malo));
        Assert.Contains("'color' no existe", ErrorText(malo));
        Assert.Equal(0, await ctx.DataContainerRows.CountAsync(r => r.ContainerId == tabla.Id));
        // Falta una obligatoria.
        Assert.True((await toolset.ExecuteAsync("agregar_filas", Args(new
        {
            contenedor = "Pedidos Mcp",
            filas = new[] { new { pedido = "A0", fecha = "2026-01-01", total = "1" } }
        }), owner)).IsError);

        var ok = Payload(await toolset.ExecuteAsync("agregar_filas", Args(new
        {
            contenedor = "Pedidos Mcp",
            tabla = "Pedidos",
            filas = new object[]
            {
                new { pedido = "A1", fecha = "2026-01-05", total = 100.5, cliente = "Ana", unidades = 2 },
                new { pedido = "A2", fecha = "2026-01-31", total = 50, cliente = "Beto", unidades = 1 },
                new { pedido = "A3", fecha = "2026-02-01", total = 25.25, cliente = "Ana", unidades = 3 },
                new { pedido = "A4", fecha = "2026-03-15", total = 10, cliente = "Caro", unidades = 1 }
            }
        }), owner));
        Assert.Equal(4, ok.GetProperty("creadas").GetInt32());

        // Upsert por clave: A4 se actualiza, A5 se crea.
        var up = Payload(await toolset.ExecuteAsync("agregar_filas", Args(new
        {
            contenedor = "Pedidos Mcp",
            columna_clave = "pedido",
            filas = new object[] { new { pedido = "A4", total = 12 }, new { pedido = "A5", fecha = "2026-03-20", total = 8, cliente = "Dani" } }
        }), owner));
        Assert.Equal(1, up.GetProperty("actualizadas").GetInt32());
        Assert.Equal(1, up.GetProperty("creadas").GetInt32());

        // ver_filas: filtro por texto + limite.
        var filas = Payload(await toolset.ExecuteAsync("ver_filas", Args(new
        {
            contenedor = "Pedidos Mcp",
            filtros = new[] { new { columna = "cliente", valor = "ana" } },
            limite = 10
        }), owner));
        Assert.Equal(2, filas.GetProperty("total").GetInt32());
        Assert.All(filas.GetProperty("filas").EnumerateArray(), f => Assert.Equal("Ana", f.GetProperty("cliente").GetString()));
        var a4 = Payload(await toolset.ExecuteAsync("ver_filas", Args(new { contenedor = "Pedidos Mcp", filtros = new[] { new { columna = "pedido", valor = "A4" } } }), owner));
        Assert.Equal("12", a4.GetProperty("filas")[0].GetProperty("total").GetString());

        // Reporte "Ventas por mes": suma de total agrupada por MES de la fecha, en orden cronologico.
        var rep = Payload(await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "Ventas por mes",
            categoria = "PRUEBA IA Comercial",
            grafico = "barras",
            contenedor = "Pedidos Mcp",
            consulta = new { agrupar_por = "fecha", periodo = "mes", agregaciones = new[] { new { funcion = "sumar", campo = "total" } } }
        }), owner));
        var reporteId = Guid.Parse(rep.GetProperty("reporte_id").GetString()!);
        var muestra = rep.GetProperty("resultado").GetProperty("muestra").EnumerateArray().ToList();
        Assert.Equal(new[] { "2026-01", "2026-02", "2026-03" }, muestra.Select(r => r[0].GetString()).ToArray());
        Assert.Equal(150.5m, muestra[0][1].GetDecimal());
        Assert.Equal(20m, muestra[2][1].GetDecimal()); // 12 + 8

        // El reporte guardado conserva el periodo al correr desde la galeria.
        var run = await new Ecorex.Application.Reporting.ReportDefinitionService(ctx, tenantCtx,
            ReportServices(ctx, tenantCtx).Data!).RunAsync(reporteId);
        Assert.Equal(3, run!.DataSet.RowCount);
        Assert.Equal("2026-01", run.DataSet.Rows[0][0]);

        // Periodo sobre un campo que no es fecha: error claro y no guarda.
        var antes = await ctx.ReportDefinitions.CountAsync();
        var noFecha = await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "x",
            contenedor = "Pedidos Mcp",
            consulta = new { agrupar_por = "cliente", periodo = "mes" }
        }), owner);
        Assert.True(noFecha.IsError);
        Assert.Contains("solo aplica a un campo fecha", ErrorText(noFecha));
        Assert.Equal(antes, await ctx.ReportDefinitions.CountAsync());

        // Trimestre y anio tambien.
        var tri = Payload(await toolset.ExecuteAsync("crear_reporte", Args(new
        {
            nombre = "Pedidos por trimestre",
            contenedor = "Pedidos Mcp",
            consulta = new { agrupar_por = "fecha", periodo = "trimestre" }
        }), owner));
        Assert.Equal("2026-T1", tri.GetProperty("resultado").GetProperty("muestra")[0][0].GetString());
        Assert.Equal(5L, tri.GetProperty("resultado").GetProperty("muestra")[0][1].GetInt64());

        // editar_reporte PARCIAL: solo el nombre (la consulta y la categoria quedan).
        var ed = Payload(await toolset.ExecuteAsync("editar_reporte", Args(new { reporte = reporteId.ToString(), nombre = "Ventas mensuales" }), owner));
        Assert.Equal(new[] { "nombre" }, ed.GetProperty("cambios").EnumerateArray().Select(c => c.GetString()).ToArray());
        var def = await ctx.ReportDefinitions.AsNoTracking().SingleAsync(d => d.Id == reporteId);
        Assert.Equal("Ventas mensuales", def.Name);
        Assert.Equal("PRUEBA IA Comercial", def.Category);
        Assert.Contains("Month", def.SpecJson);

        // editar_reporte: grafico + consulta por ANIO (se ejecuta antes de guardar).
        var ed2 = Payload(await toolset.ExecuteAsync("editar_reporte", Args(new
        {
            reporte = "Ventas mensuales",
            grafico = "linea",
            consulta = new { agrupar_por = "fecha", periodo = "anio", agregaciones = new[] { new { funcion = "sumar", campo = "total" } } }
        }), owner));
        Assert.Equal(1, ed2.GetProperty("resultado").GetProperty("filas").GetInt32());
        Assert.True((await toolset.ExecuteAsync("editar_reporte", Args(new { reporte = "Ventas mensuales" }), owner)).IsError); // sin cambios
        Assert.True((await toolset.ExecuteAsync("editar_reporte", Args(new
        {
            reporte = "Ventas mensuales",
            consulta = new { agrupar_por = "no_existe" }
        }), owner)).IsError);
        Assert.Contains("Year", (await ctx.ReportDefinitions.AsNoTracking().SingleAsync(d => d.Id == reporteId)).SpecJson);

        // archivar_reporte: sale de la galeria; si era el ultimo de su categoria, la categoria no queda visible.
        var arch = Payload(await toolset.ExecuteAsync("archivar_reporte", Args(new { reporte = "Ventas mensuales" }), owner));
        Assert.True(arch.GetProperty("ok").GetBoolean());
        var listado = Payload(await toolset.ExecuteAsync("listar_reportes", "{}", owner));
        Assert.DoesNotContain(listado.GetProperty("reportes").EnumerateArray(), r => r.GetProperty("reporte_id").GetString() == reporteId.ToString());
        Assert.DoesNotContain(listado.GetProperty("reportes").EnumerateArray(), r => r.TryGetProperty("categoria", out var c) && c.GetString() == "PRUEBA IA Comercial");
        Assert.Equal(Ecorex.Domain.Enums.ReportDefinitionStatus.Archived,
            (await ctx.ReportDefinitions.AsNoTracking().SingleAsync(d => d.Id == reporteId)).Status);

        // Indicador por mes + eliminar_indicador + eliminar_libro_indicadores.
        Payload(await toolset.ExecuteAsync("crear_libro_indicadores", Args(new { nombre = "Ventas libro" }), owner));
        var w = Payload(await toolset.ExecuteAsync("agregar_indicador", Args(new
        {
            libro = "Ventas libro",
            titulo = "Ventas por mes",
            tipo = "linea",
            contenedor = "Pedidos Mcp",
            consulta = new { agrupar_por = "fecha", periodo = "mes", agregaciones = new[] { new { funcion = "sumar", campo = "total" } } }
        }), owner));
        var widgetId = w.GetProperty("widget_id").GetString()!;
        Payload(await toolset.ExecuteAsync("eliminar_indicador", Args(new { indicador = widgetId }), owner));
        var libro = Payload(await toolset.ExecuteAsync("ver_libro_indicadores", Args(new { libro = "Ventas libro" }), owner));
        Assert.Equal(0, libro.GetProperty("hojas").EnumerateArray().Sum(h => h.GetProperty("widgets").GetArrayLength()));
        Payload(await toolset.ExecuteAsync("eliminar_libro_indicadores", Args(new { libro = "Ventas libro" }), owner));
        Assert.True((await toolset.ExecuteAsync("ver_libro_indicadores", Args(new { libro = "Ventas libro" }), owner)).IsError);
    }

    /// <summary>Contenedor "SECOP Mcp" con tablas Procesos y Tecnologia; Tecnologia con 4 procesos (3 tech).</summary>
    private static async Task<(Guid Tecnologia, Guid Procesos)> SeedSecopAsync(EcorexDbContext ctx, Guid tenantId)
    {
        var model = new DataModel { TenantId = tenantId, Name = "SECOP Mcp" };
        ctx.DataModels.Add(model);
        var procesos = new DataContainer { TenantId = tenantId, Name = "Procesos", ModelId = model.Id };
        var tec = new DataContainer { TenantId = tenantId, Name = "Tecnologia", ModelId = model.Id };
        ctx.DataContainers.AddRange(procesos, tec);
        var cols = new Dictionary<string, DataContainerColumn>();
        var order = 0;
        foreach (var (n, t) in new[] { ("id_proceso", DataContainerColumnType.Text), ("departamento", DataContainerColumnType.Text),
            ("es_tecnologia", DataContainerColumnType.Text), ("sector", DataContainerColumnType.Text),
            ("modalidad", DataContainerColumnType.Text), ("precio_base", DataContainerColumnType.Decimal) })
        {
            cols[n] = new DataContainerColumn { TenantId = tenantId, ContainerId = tec.Id, Name = n, Type = t, SortOrder = order++ };
        }
        ctx.DataContainerColumns.AddRange(cols.Values);
        ctx.DataContainerColumns.Add(new DataContainerColumn { TenantId = tenantId, ContainerId = procesos.Id, Name = "id_proceso", SortOrder = 0 });
        var rows = new[]
        {
            new[] { "P1", "Antioquia", "si", "Salud", "Licitacion", "100" },
            new[] { "P2", "Antioquia", "si", "Educacion", "Minima cuantia", "50.5" },
            new[] { "P3", "Cundinamarca", "si", "Salud", "Licitacion", "200" },
            new[] { "P4", "Cundinamarca", "no", "Salud", "Licitacion", "10" }
        };
        var keys = new[] { "id_proceso", "departamento", "es_tecnologia", "sector", "modalidad", "precio_base" };
        foreach (var r in rows)
        {
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = tec.Id };
            ctx.DataContainerRows.Add(row);
            for (var i = 0; i < keys.Length; i++)
            {
                ctx.DataContainerCells.Add(new DataContainerCell { TenantId = tenantId, RowId = row.Id, ColumnId = cols[keys[i]].Id, Value = r[i] });
            }
        }
        await ctx.SaveChangesAsync();
        return (tec.Id, procesos.Id);
    }

    /// <summary>Doble del runner (sus pruebas reales viven en ContenedorClasificacionTests): verifica SOLO el puente MCP.</summary>
    private sealed class FakeClasificador : IContenedorClasificacionService
    {
        public LimpiarMarcaRequest? LastLimpiar { get; private set; }

        public Task<LimpiarMarcaResultado> LimpiarMarcaAsync(LimpiarMarcaRequest request, CancellationToken ct = default)
        {
            LastLimpiar = request;
            return Task.FromResult(new LimpiarMarcaResultado(true, null, Filas: 4, Celdas: 8));
        }

        public RellenarColumnaRequest? LastRellenar { get; private set; }

        public Task<RellenarColumnaResultado> RellenarColumnaAsync(RellenarColumnaRequest request, CancellationToken ct = default)
        {
            LastRellenar = request;
            return Task.FromResult(new RellenarColumnaResultado(true, null, Rellenadas: 5, SinCambio: 1, Restantes: 0, Muestra: new[] { "a|b" }));
        }

        public ClasificarLoteRequest? Last { get; private set; }
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<ClasificarLoteResultado> ClasificarLoteAsync(ClasificarLoteRequest request, CancellationToken ct = default)
        {
            Calls++;
            Last = request;
            return Task.FromResult(Fail
                ? ClasificarLoteResultado.Falla("forzado")
                : new ClasificarLoteResultado(true, null, Procesados: 2, Actualizados: 2, Restantes: 3, Agente: "Clasificador", Modelo: "m"));
        }
    }

    // ---- Helpers ----

    private (ActivityBoardService Boards, WorkflowEngine Engine, TableroMcpToolset Toolset) Build(
        EcorexDbContext ctx, ITenantContext tenantCtx, IFormAuthoringToolset? forms = null, IScrapeFlowService? scrape = null,
        bool withContainers = false, bool withAgents = false, IContenedorClasificacionService? clasificador = null,
        bool withReports = false, Ecorex.Application.DataContainers.IImportClassificationService? importClassify = null,
        bool withFormConfig = false)
    {
        var engine = new WorkflowEngine(ctx, tenantCtx, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());
        var tasks = new TaskItemService(ctx, tenantCtx, new SequenceService(ctx, tenantCtx), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
        var boards = new ActivityBoardService(ctx, tenantCtx, new SequenceService(ctx, tenantCtx), tasks, new AuditWriter(ctx), new NodeAssigneeResolver(ctx),
            _fixture.CreateFactory(tenantCtx.TenantId));
        var flowDesign = new WorkflowDesignService(ctx, engine);
        var inbox = new WorkflowInboxService(ctx, tenantCtx, new NodeAssigneeResolver(ctx), engine, flowDesign, new NoOpAgentStepRunner());
        var tenant = tenantCtx;
        var terceros = new TerceroService(ctx, _fixture.CreateFactory(tenant.TenantId), tenant, new DirectoryVariantService(ctx, tenant),
            new DataLookupService(ctx, new DataContainerService(ctx, tenant)));
        IDataModelService? dataModel = null;
        IDataImportConfigService? dataImport = null;
        IDataContainerModuleService? dataModule = null;
        IMenuConfigService? menu = null;
        if (withContainers)
        {
            var containers = new DataContainerService(ctx, tenant);
            dataModel = new DataModelService(ctx, tenant, containers);
            dataImport = new DataImportConfigService(ctx, tenant, new PlainSecrets(), new NoOpAgentClients());
            menu = new MenuConfigService(ctx, tenant);
            dataModule = new DataContainerModuleService(ctx, menu);
        }
        IAiAgentService? agents = withAgents ? new AiAgentService(ctx, tenant, new AuditWriter(ctx)) : null;
        var rep = withReports ? ReportServices(ctx, tenant) : default;
        var fc = withFormConfig ? FormConfigServices(ctx, tenant) : default;
        var toolset = new TableroMcpToolset(ctx, boards, tasks, inbox, terceros, new DirectorioToolset(terceros, ctx),
            new TerceroVinculoService(ctx, ctx, tenant), new TerceroFieldService(ctx, tenant), new RolService(ctx, tenant, new AuditWriter(ctx)),
            forms, flowDesign, engine, new WorkflowNodePolicyService(ctx, tenant), scrape, dataModel, dataImport, dataModule, menu, agents,
            clasificador,
            reportCatalog: rep.Catalog, reportData: rep.Data, reportDefs: rep.Defs, analisis: rep.Analisis, widgetRunner: rep.Runner,
            importClassify: importClassify,
            rulesEngine: fc.Engine, ruleDocs: fc.Docs, formDefs: fc.Forms, formSnapshots: fc.Snapshots, formTokens: fc.Tokens);
        return (boards, engine, toolset);
    }

    /// <summary>ADR-0211 ola 2: motor de reglas (verbos PASAR_CAMPOS, BLOQUEAR_CAMPO_XCONDICION, NOTIFICAR) + servicios
    /// reales de reglas, formularios, versiones y enlaces.</summary>
    private static (Ecorex.Application.Rules.IRulesEngine? Engine, Ecorex.Application.Rules.IRuleDocumentService? Docs,
        Ecorex.Application.Forms.IFormDefinitionService? Forms, Ecorex.Application.Forms.Builder.IFormSnapshotService? Snapshots,
        Ecorex.Application.Forms.IFormTokenService? Tokens) FormConfigServices(EcorexDbContext ctx, ITenantContext tenant)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Ecorex.Application.Rules.IRuleVerb>(services, new Ecorex.Application.Rules.Verbs.PasarCamposVerb());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Ecorex.Application.Rules.IRuleVerb>(services, new Ecorex.Application.Rules.Verbs.BloquearCampoPorCondicionVerb());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Ecorex.Application.Rules.IRuleVerb>(services, new Ecorex.Application.Rules.Verbs.NotificarVerb(ctx));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Ecorex.Application.Rules.IRuleVerb>(services, new RefsTestVerb());
        var rules = new Ecorex.Application.Rules.RulesEngine(ctx, tenant, Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services));
        var forms = new Ecorex.Application.Forms.FormDefinitionService(ctx, tenant, new MenuConfigService(ctx, tenant), new SequenceService(ctx, tenant));
        return (rules, new Ecorex.Application.Rules.RuleDocumentService(ctx, tenant, rules), forms,
            new Ecorex.Application.Forms.Builder.FormSnapshotService(ctx, forms), new Ecorex.Application.Forms.FormTokenService(ctx, tenant));
    }

    /// <summary>Motor de reportes + libros de indicadores reales (sin fuentes nativas: bastan contenedores).</summary>
    private (Ecorex.Application.Reporting.IReportCatalog? Catalog, Ecorex.Application.Reporting.IReportDataSource? Data,
        Ecorex.Application.Reporting.IReportDefinitionService? Defs, Ecorex.Application.Analisis.IAnalisisService? Analisis,
        Ecorex.Application.Analisis.IAnalisisWidgetRunner? Runner) ReportServices(EcorexDbContext ctx, ITenantContext tenant)
    {
        var native = Array.Empty<Ecorex.Application.Reporting.IReportableSource>();
        var containers = new Ecorex.Application.Reporting.Sources.ContainerReportReader(ctx);
        var external = ExternalTestDoubles.Reader(ctx);
        var formsReader = new Ecorex.Application.Reporting.Sources.FormResponseReportReader(ctx);
        var catalog = new Ecorex.Application.Reporting.ReportCatalog(native, containers, external, formsReader, tenant, ctx);
        var data = new Ecorex.Application.Reporting.ReportDataSource(catalog, native, containers, external, formsReader, tenant);
        var registry = new Ecorex.Application.Analisis.IndicadorCuradoRegistry(Array.Empty<Ecorex.Application.Analisis.IIndicadorCurado>());
        return (catalog, data, new Ecorex.Application.Reporting.ReportDefinitionService(ctx, tenant, data, catalog: catalog),
            new Ecorex.Application.Analisis.AnalisisService(_fixture.CreateFactory(tenant.TenantId!.Value), registry),
            new Ecorex.Application.Analisis.AnalisisWidgetRunner(data, registry, catalog));
    }

    private static McpActor Actor(SeedData s, bool owner) => owner
        ? new McpActor(s.TenantId, s.OwnerUserId, s.OwnerPlatformId, "Owner Mcp", true, Guid.NewGuid())
        : new McpActor(s.TenantId, s.MemberUserId, s.MemberPlatformId, "Miembro Mcp", false, Guid.NewGuid());

    private static async Task<Guid> FirstColumnAsync(EcorexDbContext ctx, Guid boardId)
        => await ctx.TaskBoardColumns.AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.SortOrder).Select(c => c.Id).FirstAsync();

    private static string Args(object o) => JsonSerializer.Serialize(o);

    /// <summary>Texto del error de una herramienta (sin escapes JSON).</summary>
    private static string ErrorText(McpToolResult r)
    {
        Assert.True(r.IsError, r.Json);
        using var doc = JsonDocument.Parse(r.Json);
        return doc.RootElement.GetProperty("error").GetString() ?? string.Empty;
    }

    private static JsonElement Payload(McpToolResult r)
    {
        Assert.False(r.IsError, r.Json);
        using var doc = JsonDocument.Parse(r.Json);
        return doc.RootElement.Clone();
    }

    // ===================== Extraccion de datos (drones) =====================

    [Fact]
    public async Task McpCatalog_ConExtraccion_EsConsistente_YDespacha()
    {
        var seed = await SeedTenantAsync("Mcp Extraccion Catalogo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var scrape = new ScrapeFlowService(ctx, tenantCtx, new PlainSecrets());
        var (_, _, toolset) = Build(ctx, tenantCtx, new FakeFormAuthoring(), scrape);
        var owner = Actor(seed, owner: true);

        var specs = toolset.GetSpecs();
        var names = specs.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16 + 1 + 12 + 16 + 23 + 14 + 3 + 17, specs.Count); // nucleo + directorio + flujos + formularios + EXTRACCION

        // Las 17 de extraccion estan expuestas, 6 de lectura y 4 destructivas, sin solape.
        foreach (var n in new[] { "listar_drones", "ver_dron", "crear_dron", "agregar_paso", "guardar_programacion", "eliminar_dron" })
        {
            Assert.Contains(n, names);
        }
        Assert.Contains("listar_drones", toolset.ReadOnlyTools);
        Assert.DoesNotContain("crear_dron", toolset.ReadOnlyTools); // escritura: se audita
        Assert.Contains("eliminar_dron", toolset.DestructiveTools);
        Assert.Empty(toolset.ReadOnlyTools.Intersect(toolset.DestructiveTools));

        foreach (var s in specs.Where(x => x.Name.Contains("dron") || x.Name.Contains("paso") || x.Name.Contains("extraccion") || x.Name.Contains("programaci") || x.Name.Contains("variable") || x.Name.Contains("corrida")))
        {
            Assert.True(s.Description!.Length >= 25, $"{s.Name}: descripcion corta");
            using var schema = JsonDocument.Parse(s.ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }

        // Toda herramienta de extraccion despacha con "{}" (sin "Herramienta desconocida"); las que exigen
        // argumentos devuelven ok:false marcado como error (validacion limpia, no excepcion).
        foreach (var name in new[] { "listar_drones", "listar_destinos_extraccion", "listar_reglas_extraccion",
            "ver_dron", "crear_dron", "agregar_paso", "guardar_programacion", "eliminar_dron" })
        {
            var r = await toolset.ExecuteAsync(name, "{}", owner);
            Assert.DoesNotContain("Herramienta desconocida", r.Json);
            using var doc = JsonDocument.Parse(r.Json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                Assert.True(r.IsError, $"{name}: ok=false sin marcar error");
            }
        }
    }

    [Fact]
    public async Task Extraccion_CrearDron_ArmarPasos_Variables_Programacion_Corridas_Borrar()
    {
        var seed = await SeedTenantAsync("Mcp Drones");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var scrape = new ScrapeFlowService(ctx, tenantCtx, new PlainSecrets());
        var (_, _, toolset) = Build(ctx, tenantCtx, scrape: scrape);
        var owner = Actor(seed, owner: true);

        // Crear el dron (cabecera).
        var crear = Payload(await toolset.ExecuteAsync("crear_dron",
            Args(new { nombre = "Dron DIAN prueba", descripcion = "demo", url_inicio = "https://portal.test/login", estado = "activo" }), owner));
        var dronId = crear.GetProperty("dron_id").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(dronId));

        // Dos pasos: navegar y extraer.
        var p1 = Payload(await toolset.ExecuteAsync("agregar_paso",
            Args(new { dron = dronId, tipo = "navegar", nombre = "Ir al login", url = "https://portal.test/login" }), owner));
        var paso1 = p1.GetProperty("paso_id").GetString()!;
        Assert.Equal("navegar", p1.GetProperty("tipo").GetString());
        var p2 = Payload(await toolset.ExecuteAsync("agregar_paso",
            Args(new { dron = dronId, tipo = "extraer", nombre = "Sacar filas", script = "return [];", out_var = "filas" }), owner));
        var paso2 = p2.GetProperty("paso_id").GetString()!;
        Assert.Equal(1, p2.GetProperty("orden").GetInt32()); // se agrega al final

        // Variables: una normal y una secreta (no se devuelve el valor).
        Payload(await toolset.ExecuteAsync("definir_variable", Args(new { dron = dronId, nombre = "USUARIO", valor = "juan" }), owner));
        Payload(await toolset.ExecuteAsync("definir_variable", Args(new { dron = dronId, nombre = "CLAVE", valor = "secreta123", secreta = true }), owner));

        // Programacion cron.
        var prog = Payload(await toolset.ExecuteAsync("guardar_programacion",
            Args(new { dron = dronId, tipo = "cron", cron = "0 6 * * *", activa = true, nombre = "Diaria 6am" }), owner));
        var progId = prog.GetProperty("programacion_id").GetString()!;
        Assert.Equal("cron", prog.GetProperty("tipo").GetString());

        // ver_dron: 2 pasos, 2 variables (la secreta sin valor), 1 programacion.
        var ver = Payload(await toolset.ExecuteAsync("ver_dron", Args(new { dron = "Dron DIAN prueba" }), owner));
        Assert.Equal(2, ver.GetProperty("pasos").GetArrayLength());
        Assert.Equal(2, ver.GetProperty("variables").GetArrayLength());
        Assert.Equal(1, ver.GetProperty("programaciones").GetArrayLength());
        var secreta = ver.GetProperty("variables").EnumerateArray().First(v => v.GetProperty("nombre").GetString() == "CLAVE");
        Assert.True(secreta.GetProperty("secreta").GetBoolean());
        // El valor de una secreta NO se expone: el serializador omite la propiedad null (WhenWritingNull).
        Assert.False(secreta.TryGetProperty("valor", out var secretVal) && secretVal.ValueKind != JsonValueKind.Null,
            "la variable secreta no debe exponer 'valor'");
        Assert.True(secreta.GetProperty("tiene_valor").GetBoolean());

        // listar_drones / listar_corridas (vacia aun).
        var lista = Payload(await toolset.ExecuteAsync("listar_drones", "{}", owner));
        Assert.Contains(lista.GetProperty("drones").EnumerateArray(), d => d.GetProperty("dron_id").GetString() == dronId);
        var corridas = Payload(await toolset.ExecuteAsync("listar_corridas", Args(new { dron = dronId }), owner));
        Assert.Equal(0, corridas.GetProperty("corridas").GetArrayLength());

        // Reordenar (invertir) y editar un paso.
        Payload(await toolset.ExecuteAsync("reordenar_pasos", Args(new { dron = dronId, pasos = new[] { paso2, paso1 } }), owner));
        var edit = Payload(await toolset.ExecuteAsync("editar_paso", Args(new { paso_id = paso1, nombre = "Ir al login (v2)" }), owner));
        Assert.Equal("Ir al login (v2)", edit.GetProperty("nombre").GetString());

        // Borrar todo.
        var varId = ver.GetProperty("variables").EnumerateArray().First(v => v.GetProperty("nombre").GetString() == "USUARIO").GetProperty("variable_id").GetString()!;
        Payload(await toolset.ExecuteAsync("eliminar_variable", Args(new { variable_id = varId }), owner));
        Payload(await toolset.ExecuteAsync("eliminar_programacion", Args(new { programacion_id = progId }), owner));
        Payload(await toolset.ExecuteAsync("eliminar_paso", Args(new { paso_id = paso1 }), owner));
        Payload(await toolset.ExecuteAsync("eliminar_dron", Args(new { dron = dronId }), owner));

        // Ya no existe.
        var verMuerto = await toolset.ExecuteAsync("ver_dron", Args(new { dron = dronId }), owner);
        Assert.True(verMuerto.IsError);
    }

    // ===================== Contenedores de datos (ADR-0194) =====================

    [Fact]
    public async Task McpCatalog_ConContenedores_EsConsistente_YDespacha()
    {
        var seed = await SeedTenantAsync("Mcp Contenedores Catalogo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var scrape = new ScrapeFlowService(ctx, tenantCtx, new PlainSecrets());
        var (_, _, toolset) = Build(ctx, tenantCtx, new FakeFormAuthoring(), scrape, withContainers: true);
        var owner = Actor(seed, owner: true);

        var specs = toolset.GetSpecs();
        var names = specs.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16 + 1 + 12 + 16 + 23 + 14 + 3 + 17 + 26, specs.Count); // nucleo + directorio + flujos + formularios + extraccion + CONTENEDORES

        // Las 26 de contenedores estan expuestas (ADR-0206: + agregar_filas, ver_filas); 8 de lectura y 7 destructivas, sin solape.
        foreach (var n in new[] { "listar_contenedores_datos", "crear_contenedor_datos", "agregar_tabla",
            "agregar_columna", "agregar_relacion", "guardar_conector", "guardar_programacion_importacion",
            "publicar_contenedor", "eliminar_contenedor_datos" })
        {
            Assert.Contains(n, names);
        }
        Assert.Contains("listar_contenedores_datos", toolset.ReadOnlyTools);
        Assert.DoesNotContain("crear_contenedor_datos", toolset.ReadOnlyTools); // escritura: se audita
        Assert.Contains("eliminar_contenedor_datos", toolset.DestructiveTools);
        Assert.Contains("despublicar_contenedor", toolset.DestructiveTools);
        Assert.Empty(toolset.ReadOnlyTools.Intersect(toolset.DestructiveTools));

        foreach (var s in specs.Where(x => x.Name.Contains("contenedor") || x.Name.Contains("tabla")
            || x.Name.Contains("columna") || x.Name.Contains("relacion") || x.Name.Contains("conector")
            || x.Name.Contains("importacion") || x.Name.Contains("publicacion")))
        {
            Assert.True(s.Description!.Length >= 25, $"{s.Name}: descripcion corta");
            using var schema = JsonDocument.Parse(s.ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }

        // Toda herramienta de contenedores despacha con "{}" (sin "Herramienta desconocida"); las que exigen
        // argumentos devuelven ok:false marcado como error (validacion limpia, no excepcion).
        foreach (var name in new[] { "listar_contenedores_datos", "ver_contenedor_datos", "crear_contenedor_datos",
            "agregar_tabla", "agregar_columna", "agregar_relacion", "guardar_conector", "guardar_programacion_importacion",
            "listar_corridas_importacion", "publicar_contenedor", "eliminar_contenedor_datos" })
        {
            var r = await toolset.ExecuteAsync(name, "{}", owner);
            Assert.DoesNotContain("Herramienta desconocida", r.Json);
            using var doc = JsonDocument.Parse(r.Json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                Assert.True(r.IsError, $"{name}: ok=false sin marcar error");
            }
        }
    }

    [Fact]
    public async Task Contenedores_CrearEstructurarProgramarPublicar_Borrar()
    {
        var seed = await SeedTenantAsync("Mcp Contenedores");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx, withContainers: true);
        var owner = Actor(seed, owner: true);

        // Crear contenedor.
        var crear = Payload(await toolset.ExecuteAsync("crear_contenedor_datos",
            Args(new { nombre = "FOMAG", descripcion = "licitaciones" }), owner));
        var contId = crear.GetProperty("contenedor_id").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(contId));

        // Tabla con columnas + una segunda tabla (para la relacion).
        var t1 = Payload(await toolset.ExecuteAsync("agregar_tabla",
            Args(new
            {
                contenedor = contId,
                nombre = "Convocatorias",
                columnas = new[]
            {
                new { nombre = "codigo", tipo = "texto", requerido = true },
                new { nombre = "objeto", tipo = "texto", requerido = false },
                new { nombre = "fecha", tipo = "fecha", requerido = false }
            }
            }), owner));
        Assert.Equal(3, t1.GetProperty("columnas").GetInt32());
        Payload(await toolset.ExecuteAsync("agregar_tabla", Args(new { contenedor = contId, nombre = "Oferentes" }), owner));

        // Columnas: agregar, editar, eliminar.
        Payload(await toolset.ExecuteAsync("agregar_columna",
            Args(new { contenedor = "FOMAG", tabla = "Convocatorias", nombre = "estado", tipo = "texto" }), owner));
        var ed = Payload(await toolset.ExecuteAsync("editar_columna",
            Args(new { contenedor = "FOMAG", tabla = "Convocatorias", columna = "estado", requerido = true }), owner));
        Assert.Equal("estado", ed.GetProperty("columna").GetString());
        Payload(await toolset.ExecuteAsync("eliminar_columna",
            Args(new { contenedor = "FOMAG", tabla = "Convocatorias", columna = "estado" }), owner));

        // Relacion entre las dos tablas.
        var rel = Payload(await toolset.ExecuteAsync("agregar_relacion",
            Args(new { contenedor = "FOMAG", origen = "Convocatorias", destino = "Oferentes", cardinalidad = "n1", nombre = "oferente" }), owner));
        Assert.Equal("n1", rel.GetProperty("cardinalidad").GetString());

        // Conector SIN secreto (el MCP no recibe credenciales).
        var conn = Payload(await toolset.ExecuteAsync("guardar_conector",
            Args(new { contenedor = "FOMAG", nombre = "FOMAG web", tipo = "api", destino = "Convocatorias", endpoint = "https://www.fomag.gov.co/contratacion/" }), owner));
        Assert.False(conn.GetProperty("tiene_credenciales").GetBoolean());

        // Programacion cron + bitacora vacia.
        var prog = Payload(await toolset.ExecuteAsync("guardar_programacion_importacion",
            Args(new { contenedor = "FOMAG", nombre = "Diaria", tipo = "cron", cron = "0 6 * * *", modo = "replace" }), owner));
        Assert.Equal("cron", prog.GetProperty("programacion").GetProperty("tipo").GetString());
        var corridas = Payload(await toolset.ExecuteAsync("listar_corridas_importacion",
            Args(new { contenedor = "FOMAG", proceso = "Diaria" }), owner));
        Assert.Equal(0, corridas.GetProperty("corridas").GetArrayLength());

        // ver_contenedor_datos: 2 tablas, Convocatorias con 3 columnas (estado ya borrada), 1 relacion.
        var ver = Payload(await toolset.ExecuteAsync("ver_contenedor_datos", Args(new { contenedor = "FOMAG" }), owner));
        Assert.Equal(2, ver.GetProperty("tablas").GetArrayLength());
        Assert.Equal(1, ver.GetProperty("relaciones").GetArrayLength());
        var conv = ver.GetProperty("tablas").EnumerateArray().First(t => t.GetProperty("nombre").GetString() == "Convocatorias");
        Assert.Equal(3, conv.GetProperty("columnas").GetArrayLength());

        // Publicar / despublicar (necesita una vista de menu y una SECCION padre: un elemento no cuelga de la raiz).
        var menu = new MenuConfigService(ctx, tenantCtx);
        var view = await menu.CreateViewAsync("Menu", isDefault: true);
        await menu.CreateNodeAsync(view.Value!.Id, null, MenuNodeKind.Section, "Datos");
        var pub = Payload(await toolset.ExecuteAsync("publicar_contenedor",
            Args(new { contenedor = "FOMAG", tabla = "Convocatorias", padre = "Datos" }), owner));
        Assert.True(pub.GetProperty("publicada").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(pub.GetProperty("ruta").GetString()));
        var despub = Payload(await toolset.ExecuteAsync("despublicar_contenedor",
            Args(new { contenedor = "FOMAG", tabla = "Convocatorias" }), owner));
        Assert.False(despub.GetProperty("publicada").GetBoolean());

        // Borrar el contenedor completo. Se limpia el rastreador para simular el DbContext fresco de cada
        // request (en prod la relacion no esta tracked; la cascada de BD la borra al eliminar el modelo).
        ctx.ChangeTracker.Clear();
        Payload(await toolset.ExecuteAsync("eliminar_contenedor_datos", Args(new { contenedor = contId }), owner));
        var verMuerto = await toolset.ExecuteAsync("ver_contenedor_datos", Args(new { contenedor = contId }), owner);
        Assert.True(verMuerto.IsError);
    }

    /// <summary>ADR-0203: ver_conector muestra la config completa SIN secretos y guardar_conector en edicion
    /// solo cambia lo enviado (antes omitir mapeo_json/destino los borraba).</summary>
    [Fact]
    public async Task VerConector_SinSecretos_YEditarSoloEndpoint_ConservaMapeoDestinoYHeaders()
    {
        var seed = await SeedTenantAsync("Mcp Ver Conector");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx, withContainers: true);
        var owner = Actor(seed, owner: true);

        Assert.Contains("ver_conector", toolset.ReadOnlyTools);

        Payload(await toolset.ExecuteAsync("crear_contenedor_datos", Args(new { nombre = "SECOP II" }), owner));
        Payload(await toolset.ExecuteAsync("agregar_tabla", Args(new
        {
            contenedor = "SECOP II",
            nombre = "Procesos",
            columnas = new[] { new { nombre = "id_proceso", tipo = "texto", requerido = false } }
        }), owner));

        const string mapeo = """{"arrayPath":null,"paging":{"mode":"Offset","offsetParam":"$offset","limitParam":"$limit","startValue":0,"pageSize":50000,"maxPages":20},"fields":[{"column":"id_proceso","path":"id_del_proceso"}]}""";
        Payload(await toolset.ExecuteAsync("guardar_conector", Args(new
        {
            contenedor = "SECOP II",
            nombre = "SECOP II - Recientes",
            tipo = "api",
            destino = "Procesos",
            endpoint = "https://www.datos.gov.co/resource/p6dx-8zbt.json?$$app_token=SECRETO123",
            mapeo_json = mapeo
        }), owner));

        // Headers estaticos puestos desde la pantalla (el MCP no los recibe).
        var entity = await ctx.DataConnectors.SingleAsync(c => c.Name == "SECOP II - Recientes");
        entity.HeadersJson = """[{"name":"X-App-Token","value":"OTRO-SECRETO"}]""";
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // ver_conector: config completa, endpoint con el token enmascarado, headers solo por nombre.
        var ver = await toolset.ExecuteAsync("ver_conector", Args(new { contenedor = "SECOP II", conector = "SECOP II - Recientes" }), owner);
        Assert.DoesNotContain("SECRETO123", ver.Json);
        Assert.DoesNotContain("OTRO-SECRETO", ver.Json);
        var v = Payload(ver);
        Assert.Equal("api", v.GetProperty("tipo").GetString());
        Assert.Equal("Procesos", v.GetProperty("destino").GetString());
        var api = v.GetProperty("api");
        Assert.True(api.GetProperty("endpoint_enmascarado").GetBoolean());
        Assert.Contains("$$app_token=***", api.GetProperty("endpoint").GetString());
        Assert.Equal("X-App-Token", api.GetProperty("headers")[0].GetString());
        Assert.Equal("Offset", v.GetProperty("mapeo").GetProperty("paging").GetProperty("mode").GetString());
        Assert.False(v.TryGetProperty("bd", out _)); // los nulos no se serializan

        // Reenviar el endpoint enmascarado se rechaza (romperia la fuente).
        Assert.True((await toolset.ExecuteAsync("guardar_conector", Args(new
        {
            contenedor = "SECOP II",
            conector = "SECOP II - Recientes",
            endpoint = api.GetProperty("endpoint").GetString()
        }), owner)).IsError);

        // Editar SOLO el endpoint (sin nombre/tipo/destino/mapeo): lo demas se conserva.
        const string nuevo = "https://www.datos.gov.co/resource/p6dx-8zbt.json?$order=fecha_de_publicacion_del DESC&$limit=5000";
        Payload(await toolset.ExecuteAsync("guardar_conector", Args(new
        {
            contenedor = "SECOP II",
            conector = "SECOP II - Recientes",
            endpoint = nuevo
        }), owner));
        ctx.ChangeTracker.Clear();
        var after = await ctx.DataConnectors.AsNoTracking().SingleAsync(c => c.Name == "SECOP II - Recientes");
        Assert.Equal(nuevo, after.EndpointUrl);
        // Postgres guarda el mapeo como jsonb (reordena claves): se compara por contenido, no por texto.
        using (var esperado = JsonDocument.Parse(mapeo))
        using (var guardado = JsonDocument.Parse(after.MappingJson!))
        {
            Assert.True(JsonElement.DeepEquals(esperado.RootElement, guardado.RootElement));
        }
        Assert.NotNull(after.ContainerId);
        Assert.Equal(ConnectorKind.RestApi, after.Kind);
        Assert.Contains("OTRO-SECRETO", after.HeadersJson); // se conserva, aunque no se muestre

        // Crear sin tipo: error claro. Conector inexistente: error.
        Assert.True((await toolset.ExecuteAsync("guardar_conector", Args(new { contenedor = "SECOP II", nombre = "Sin tipo" }), owner)).IsError);
        Assert.True((await toolset.ExecuteAsync("ver_conector", Args(new { contenedor = "SECOP II", conector = "No existe" }), owner)).IsError);
    }

    // ===================== Agentes de IA (ADR-0195) =====================

    [Fact]
    public async Task McpCatalog_ConAgentes_EsConsistente_YDespacha()
    {
        var seed = await SeedTenantAsync("Mcp Agentes Catalogo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var scrape = new ScrapeFlowService(ctx, tenantCtx, new PlainSecrets());
        var (_, _, toolset) = Build(ctx, tenantCtx, new FakeFormAuthoring(), scrape, withContainers: true, withAgents: true);
        var owner = Actor(seed, owner: true);

        var specs = toolset.GetSpecs();
        var names = specs.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16 + 1 + 12 + 16 + 23 + 14 + 3 + 17 + 26 + 17, specs.Count); // ... + AGENTES

        // Las 17 de agentes estan expuestas; 3 de lectura y 3 destructivas, sin solape.
        foreach (var n in new[] { "listar_agentes", "ver_agente", "crear_agente", "editar_agente",
            "agente_lector_medios", "encender_agente", "apagar_agente", "duplicar_agente",
            "agregar_prompt_agente", "agregar_recurso_agente", "eliminar_agente" })
        {
            Assert.Contains(n, names);
        }
        Assert.Contains("listar_agentes", toolset.ReadOnlyTools);
        Assert.DoesNotContain("crear_agente", toolset.ReadOnlyTools); // escritura: se audita
        Assert.Contains("eliminar_agente", toolset.DestructiveTools);
        Assert.Contains("eliminar_recurso_agente", toolset.DestructiveTools);
        Assert.Contains("eliminar_prompt_agente", toolset.DestructiveTools);
        Assert.Empty(toolset.ReadOnlyTools.Intersect(toolset.DestructiveTools));

        foreach (var s in specs.Where(x => x.Name.Contains("agente") || x.Name.Contains("prompt")
            || x.Name.Contains("recurso") || x.Name.Contains("lector")))
        {
            Assert.True(s.Description!.Length >= 25, $"{s.Name}: descripcion corta");
            using var schema = JsonDocument.Parse(s.ParametersJsonSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }

        // Toda herramienta de agentes despacha con "{}" (sin "Herramienta desconocida"); las que exigen
        // argumentos devuelven ok:false marcado como error (validacion limpia, no excepcion).
        foreach (var name in new[] { "listar_agentes", "ver_agente", "historial_prompts_agente", "crear_agente",
            "editar_agente", "agente_lector_medios", "encender_agente", "apagar_agente", "duplicar_agente",
            "restaurar_version_prompt", "agregar_recurso_agente", "editar_recurso_agente", "agregar_prompt_agente",
            "editar_prompt_agente", "eliminar_agente", "eliminar_recurso_agente", "eliminar_prompt_agente" })
        {
            var r = await toolset.ExecuteAsync(name, "{}", owner);
            Assert.DoesNotContain("Herramienta desconocida", r.Json);
            using var doc = JsonDocument.Parse(r.Json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                Assert.True(r.IsError, $"{name}: ok=false sin marcar error");
            }
        }
    }

    [Fact]
    public async Task Agentes_CrearConfigurarDuplicar_Borrar()
    {
        var seed = await SeedTenantAsync("Mcp Agentes");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.OwnerPlatformId);
        var (_, _, toolset) = Build(ctx, tenantCtx, withAgents: true);
        var owner = Actor(seed, owner: true);

        // Crear agente DeepSeek: nace APAGADO y sin herramientas deshabilitadas (todas habilitadas).
        var crear = Payload(await toolset.ExecuteAsync("crear_agente",
            Args(new { nombre = "Inspector FOMAG prueba", rol = "Analista de pliegos", proveedor = "deepseek", prompt = "Analiza pliegos." }), owner));
        var agentId = crear.GetProperty("agente_id").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(agentId));
        Assert.Equal("deepseek", crear.GetProperty("proveedor").GetString());
        Assert.False(crear.GetProperty("encendido").GetBoolean());

        // ver_agente: proveedor deepseek, apagado, SIN herramientas deshabilitadas (agregar_filas_a_contenedor queda habilitada).
        var ver = Payload(await toolset.ExecuteAsync("ver_agente", Args(new { agente = "Inspector FOMAG prueba" }), owner));
        Assert.Equal("deepseek", ver.GetProperty("proveedor").GetString());
        Assert.False(ver.GetProperty("encendido").GetBoolean());
        Assert.Equal(0, ver.GetProperty("herramientas_deshabilitadas").GetArrayLength());
        Assert.True(ver.TryGetProperty("aviso_herramientas", out var aviso) && aviso.ValueKind == JsonValueKind.String,
            "con 0 herramientas deshabilitadas debe venir el aviso de que todas estan habilitadas");

        // editar_agente: prompt de sistema + apagar una herramienta. El resto se conserva.
        var edit = Payload(await toolset.ExecuteAsync("editar_agente",
            Args(new { agente = agentId, prompt = "Extrae objeto, presupuesto y fecha de cierre.", herramientas_deshabilitadas = new[] { "crear_tarea" } }), owner));
        Assert.Equal("deepseek", edit.GetProperty("proveedor").GetString()); // conservado
        var ver2 = Payload(await toolset.ExecuteAsync("ver_agente", Args(new { agente = agentId }), owner));
        Assert.Equal("Extrae objeto, presupuesto y fecha de cierre.", ver2.GetProperty("prompt").GetString());
        Assert.Equal(1, ver2.GetProperty("herramientas_deshabilitadas").GetArrayLength());

        // Lector de medios (como lee cada PDF).
        var media = Payload(await toolset.ExecuteAsync("agente_lector_medios",
            Args(new { agente = agentId, prompt = "Lee el PDF del pliego y vuelca los campos." }), owner));
        Assert.False(media.GetProperty("generico").GetBoolean());

        // Prompt enrutado + recurso (pdf).
        var prompt = Payload(await toolset.ExecuteAsync("agregar_prompt_agente",
            Args(new { agente = agentId, nombre = "Convocatoria", regla = "pliego", cuerpo = "Responde en JSON." }), owner));
        var promptId = prompt.GetProperty("prompt_id").GetString()!;
        var recurso = Payload(await toolset.ExecuteAsync("agregar_recurso_agente",
            Args(new { agente = agentId, nombre = "Plantilla pliego", tipo = "pdf", detalle = "Estructura esperada" }), owner));
        var recursoId = recurso.GetProperty("recurso_id").GetString()!;

        // ver_agente: 1 prompt enrutado y 1 recurso.
        var ver3 = Payload(await toolset.ExecuteAsync("ver_agente", Args(new { agente = agentId }), owner));
        Assert.Equal(1, ver3.GetProperty("prompts_enrutados").GetArrayLength());
        Assert.Equal(1, ver3.GetProperty("recursos").GetArrayLength());

        // Editar prompt + recurso (reemplazo por campo: lo omitido se conserva).
        var pe = Payload(await toolset.ExecuteAsync("editar_prompt_agente", Args(new { prompt_id = promptId, cuerpo = "Responde solo con los campos." }), owner));
        Assert.Equal("Convocatoria", pe.GetProperty("nombre").GetString()); // nombre conservado
        Payload(await toolset.ExecuteAsync("editar_recurso_agente", Args(new { recurso_id = recursoId, detalle = "v2" }), owner));

        // Encender / apagar.
        var on = Payload(await toolset.ExecuteAsync("encender_agente", Args(new { agente = agentId }), owner));
        Assert.True(on.GetProperty("encendido").GetBoolean());
        var off = Payload(await toolset.ExecuteAsync("apagar_agente", Args(new { agente = agentId }), owner));
        Assert.False(off.GetProperty("encendido").GetBoolean());

        // Duplicar: la copia queda apagada y con otro id.
        var dup = Payload(await toolset.ExecuteAsync("duplicar_agente", Args(new { agente = agentId }), owner));
        var dupId = dup.GetProperty("agente_id").GetString()!;
        Assert.NotEqual(agentId, dupId);
        Assert.False(dup.GetProperty("encendido").GetBoolean());

        // Borrar prompt, recurso y el agente original.
        Payload(await toolset.ExecuteAsync("eliminar_prompt_agente", Args(new { prompt_id = promptId }), owner));
        Payload(await toolset.ExecuteAsync("eliminar_recurso_agente", Args(new { recurso_id = recursoId }), owner));
        Payload(await toolset.ExecuteAsync("eliminar_agente", Args(new { agente = agentId }), owner));
        var verMuerto = await toolset.ExecuteAsync("ver_agente", Args(new { agente = agentId }), owner);
        Assert.True(verMuerto.IsError);

        // La copia sigue existiendo (se borro solo el original).
        var verDup = await toolset.ExecuteAsync("ver_agente", Args(new { agente = dupId }), owner);
        Assert.False(verDup.IsError);
    }

    private sealed class PlainSecrets : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    /// <summary>Clientes remotos no se usan en estas pruebas de contenedor (solo conectores/procesos/corridas).</summary>
    private sealed class NoOpAgentClients : IAgentClientService
    {
        public Task<IReadOnlyList<AgentClientDto>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentClientDto>>(Array.Empty<AgentClientDto>());
        public Task<(AgentClientDto Client, AgentClientSecretDto? Secret)> SaveAsync(SaveAgentClientRequest req, Guid actorUserId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<AgentClientSecretDto?> RotateSecretAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
            => Task.FromResult<AgentClientSecretDto?>(null);
        public Task<bool> RevokeAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> DeleteAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var ownerP = new PlatformUser { Email = $"owner-{tenantId:N}@mcp.test", DisplayName = "Owner Mcp", EmailVerified = true, Status = PlatformUserStatus.Active };
            var memberP = new PlatformUser { Email = $"member-{tenantId:N}@mcp.test", DisplayName = "Miembro Mcp", EmailVerified = true, Status = PlatformUserStatus.Active };
            ctx.PlatformUsers.AddRange(ownerP, memberP);
            var owner = new TenantUser { TenantId = tenantId, PlatformUserId = ownerP.Id, Email = ownerP.Email, TenantRole = TenantRole.Owner, Status = PlatformUserStatus.Active };
            var member = new TenantUser { TenantId = tenantId, PlatformUserId = memberP.Id, Email = memberP.Email, TenantRole = TenantRole.Advisor, Status = PlatformUserStatus.Active };
            ctx.TenantUsers.AddRange(owner, member);
            var type = new ActivityType { TenantId = tenantId, Category = "General", Name = "Mcp" };
            ctx.ActivityTypes.Add(type);
            await ctx.SaveChangesAsync();
            return new SeedData(tenantId, owner.Id, ownerP.Id, member.Id, memberP.Id, memberP.Email, type.Id);
        }
    }

    private sealed record SeedData(Guid TenantId, Guid OwnerUserId, Guid OwnerPlatformId, Guid MemberUserId, Guid MemberPlatformId, string MemberEmail, Guid ActivityTypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }

    /// <summary>Doble del toolset de autoria de formularios: el toolset REAL (ADR-0067) ya tiene sus pruebas;
    /// aqui se verifica SOLO el puente del MCP (permiso, "corre como el usuario" y mapeo de ok:false a error).</summary>
    private sealed class FakeFormAuthoring : IFormAuthoringToolset
    {
        public string GroupKey => "form-authoring";
        public string GroupLabel => "Autoria de formularios";
        public List<(string Tool, Guid Actor)> Calls { get; } = new();
        public IReadOnlySet<string> ReadOnlyTools { get; } = new HashSet<string>(StringComparer.Ordinal) { "list_forms", "list_tenants", "list_menu_nodes" };

        // Incluye herramientas que el toolset REAL tiene pero que el MCP personal NO debe exponer (cross-tenant/menu).
        public IReadOnlyList<AiToolSpec> GetSpecs() => new AiToolSpec[]
        {
            new("list_forms", "Lista los formularios del tenant con su estado.", """{"type":"object","properties":{},"additionalProperties":false}"""),
            new("create_form", "Crea un formulario nuevo en borrador con su titulo.", """{"type":"object","properties":{"title":{"type":"string"},"fail":{"type":"boolean"}},"additionalProperties":false}"""),
            new("delete_question", "Borra una pregunta de un formulario en borrador.", """{"type":"object","properties":{"question_id":{"type":"string"}},"additionalProperties":false}"""),
            new("list_tenants", "Lista TODOS los tenants de la plataforma.", """{"type":"object","properties":{},"additionalProperties":false}"""),
            new("list_menu_nodes", "Lista los nodos del menu del tenant.", """{"type":"object","properties":{},"additionalProperties":false}"""),
            new("set_module", "Promueve un formulario a modulo del menu.", """{"type":"object","properties":{"form_id":{"type":"string"}},"additionalProperties":false}"""),
        };

        public Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
        {
            Calls.Add((toolName, actorUserId));
            var fail = argumentsJson.Contains("\"fail\":true", StringComparison.Ordinal);
            var json = fail
                ? "{\"ok\":false,\"error\":\"forzado\"}"
                : "{\"ok\":true,\"tool\":\"" + toolName + "\"}";
            return Task.FromResult(new AgentToolResult(json, false));
        }
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class McpTablerosTests_Postgres : McpTablerosTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public McpTablerosTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class McpTablerosTests_SqlServer : McpTablerosTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public McpTablerosTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Verbo de prueba (ADR-0211): parametros por referencia (concepto/usuario) para probar su traduccion a Guid.</summary>
internal sealed class RefsTestVerb : Ecorex.Application.Rules.IRuleVerb
{
    public string Name => "REFS_TEST";

    public Ecorex.Application.Rules.RuleVerbDescriptor Descriptor { get; } = new("REFS_TEST", "Prueba de referencias",
        "Verbo de prueba con parametros que guardan ids.",
        new[]
        {
            new Ecorex.Application.Rules.RuleVerbParamDescriptor("subcategoriaId", "Concepto", Ecorex.Application.Rules.RuleParamType.Text, true),
            new Ecorex.Application.Rules.RuleVerbParamDescriptor("assigneeUserId", "Asignado", Ecorex.Application.Rules.RuleParamType.Text, false)
        });

    public Task<Ecorex.Application.Rules.RuleVerbResult> ExecuteAsync(Ecorex.Application.Rules.RuleContext context, CancellationToken cancellationToken)
        => Task.FromResult(Ecorex.Application.Rules.RuleVerbResult.Ok());
}
