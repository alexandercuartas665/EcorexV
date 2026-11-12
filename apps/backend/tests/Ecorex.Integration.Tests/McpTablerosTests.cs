using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.DataLookups;
using Ecorex.Application.Directorio;
using Ecorex.Application.Mcp;
using Ecorex.Application.Organization;
using Ecorex.Application.Roles;
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
        await ctx.SaveChangesAsync();

        // Crear el flujo: nace con inicio -> fin ya conectados.
        var creado = Payload(await toolset.ExecuteAsync("crear_flujo", Args(new { nombre = "Aprobacion de compras", categoria = "Compras" }), me));
        var defId = creado.GetProperty("definicion_id").GetGuid();
        Guid NodeOf(JsonElement nodes, string tipo) =>
            nodes.EnumerateArray().First(n => n.GetProperty("tipo").GetString() == tipo).GetProperty("nodo_id").GetGuid();
        var inicio = NodeOf(creado.GetProperty("nodos"), "inicio");
        var fin = NodeOf(creado.GetProperty("nodos"), "fin");

        // Agregar un paso humano y encadenarlo inicio -> revisar -> fin.
        var rev = Payload(await toolset.ExecuteAsync("agregar_nodo", Args(new { flujo = defId, tipo = "tarea", nombre = "Revisar" }), me))
            .GetProperty("nodo_id").GetGuid();
        Assert.False((await toolset.ExecuteAsync("conectar_nodos", Args(new { desde_nodo_id = inicio, hasta_nodo_id = rev }), me)).IsError);
        Assert.False((await toolset.ExecuteAsync("conectar_nodos", Args(new { desde_nodo_id = rev, hasta_nodo_id = fin }), me)).IsError);

        // Encargado tomado de un campo del formulario + formulario obligatorio para cerrar (ADR-0077).
        Assert.False((await toolset.ExecuteAsync("asignar_encargado_nodo", Args(new { nodo_id = rev, origen = "campo_formulario", campo = "text_aprobador" }), me)).IsError);
        Assert.False((await toolset.ExecuteAsync("poner_formulario_a_nodo", Args(new { nodo_id = rev, formulario = "F-APROB", obligatorio = true }), me)).IsError);

        // El grafo refleja lo construido.
        var canvas = Payload(await toolset.ExecuteAsync("ver_flujo", Args(new { flujo = defId }), me));
        var revNode = canvas.GetProperty("nodos").EnumerateArray().First(n => n.GetProperty("nodo_id").GetGuid() == rev);
        Assert.Equal("campo_formulario", revNode.GetProperty("encargado_origen").GetString());
        Assert.Equal("text_aprobador", revNode.GetProperty("campo_encargado").GetString());
        Assert.Contains(revNode.GetProperty("formularios").EnumerateArray(), f => f.GetProperty("obligatorio").GetBoolean());

        // Un paso de inicio es unico: agregar otro inicio se rechaza (regla del servicio, no reimplementada).
        Assert.True((await toolset.ExecuteAsync("agregar_nodo", Args(new { flujo = defId, tipo = "inicio" }), me)).IsError);

        // Publicar y enganchar al concepto: queda listo para arrancar tareas.
        var pub = await toolset.ExecuteAsync("publicar_flujo", Args(new { flujo = defId }), me);
        Assert.False(pub.IsError, pub.Json);
        var link = Payload(await toolset.ExecuteAsync("enganchar_flujo_a_concepto", Args(new { concepto = "C-OC", flujo = defId }), me));
        Assert.True(link.GetProperty("publicado").GetBoolean());

        var reloaded = await ctx.ActividadSubcategorias.AsNoTracking().SingleAsync(s => s.Id == concepto.Id);
        Assert.Equal(defId, reloaded.WorkflowDefinitionId);
        Assert.True(await ctx.WorkflowDefinitions.AsNoTracking().AnyAsync(d => d.Id == defId && d.IsPublished));
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

    // ---- Helpers ----

    private (ActivityBoardService Boards, WorkflowEngine Engine, TableroMcpToolset Toolset) Build(
        EcorexDbContext ctx, ITenantContext tenantCtx, IFormAuthoringToolset? forms = null)
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
        var toolset = new TableroMcpToolset(ctx, boards, tasks, inbox, terceros, new DirectorioToolset(terceros, ctx),
            new TerceroVinculoService(ctx, ctx, tenant), new TerceroFieldService(ctx, tenant), new RolService(ctx, tenant, new AuditWriter(ctx)),
            forms, flowDesign, engine);
        return (boards, engine, toolset);
    }

    private static McpActor Actor(SeedData s, bool owner) => owner
        ? new McpActor(s.TenantId, s.OwnerUserId, s.OwnerPlatformId, "Owner Mcp", true, Guid.NewGuid())
        : new McpActor(s.TenantId, s.MemberUserId, s.MemberPlatformId, "Miembro Mcp", false, Guid.NewGuid());

    private static async Task<Guid> FirstColumnAsync(EcorexDbContext ctx, Guid boardId)
        => await ctx.TaskBoardColumns.AsNoTracking().Where(c => c.BoardId == boardId).OrderBy(c => c.SortOrder).Select(c => c.Id).FirstAsync();

    private static string Args(object o) => JsonSerializer.Serialize(o);

    private static JsonElement Payload(McpToolResult r)
    {
        Assert.False(r.IsError, r.Json);
        using var doc = JsonDocument.Parse(r.Json);
        return doc.RootElement.Clone();
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
        public IReadOnlySet<string> ReadOnlyTools { get; } = new HashSet<string>(StringComparer.Ordinal) { "list_forms" };

        public IReadOnlyList<AiToolSpec> GetSpecs() => new AiToolSpec[]
        {
            new("list_forms", "lista formularios", """{"type":"object","properties":{},"additionalProperties":false}"""),
            new("create_form", "crea un formulario", """{"type":"object","properties":{"title":{"type":"string"},"fail":{"type":"boolean"}},"additionalProperties":false}"""),
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
