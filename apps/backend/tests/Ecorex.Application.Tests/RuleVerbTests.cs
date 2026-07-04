using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Rules;
using Ecorex.Application.Rules.Verbs;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests unitarios de los verbos del RulesEngine (FASE 4 ola 3, ADR-0016): params validos
/// e invalidos, acciones de UI devueltas y catalogo tipado (verbo no registrado = null en
/// FindVerb; el error tipado end-to-end se prueba en integracion dual). Los verbos son
/// puros o reciben fakes; sin base de datos.
/// </summary>
public class RuleVerbTests
{
    private static RuleContext BuildContext(
        string? paramsJson = null,
        Dictionary<string, string?>? formData = null,
        Guid? taskItemId = null)
    {
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (paramsJson is not null)
        {
            using var doc = JsonDocument.Parse(paramsJson);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                parameters[property.Name] = property.Value.Clone();
            }
        }
        return new RuleContext
        {
            TenantId = Guid.CreateVersion7(),
            RuleId = Guid.CreateVersion7(),
            TriggerKind = RuleTriggerKind.Manual,
            Params = parameters,
            FormData = formData ?? new Dictionary<string, string?>(StringComparer.Ordinal),
            TaskItemId = taskItemId
        };
    }

    // ---- PASAR_CAMPOS ----

    [Fact]
    public async Task PasarCampos_CopiesValues_AndReturnsSetFieldValueActions()
    {
        var verb = new PasarCamposVerb();
        var context = BuildContext(
            """{"mappings":[{"source":"a","target":"b"},{"source":"x","target":"y"}]}""",
            new Dictionary<string, string?> { ["a"] = "hola", ["x"] = null });

        var result = await verb.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.RecordsAffected);
        Assert.Equal(2, result.ActionList.Count);
        var first = result.ActionList[0];
        Assert.Equal(RuleActionKind.SetFieldValue, first.Kind);
        Assert.Equal("b", first.FieldCode);
        Assert.Equal("hola", first.Value);
        // El FormData del contexto queda actualizado (verbos encadenados lo ven).
        Assert.Equal("hola", context.FormData["b"]);
        Assert.Null(context.FormData["y"]);
    }

    [Theory]
    [InlineData(null)]                                     // sin params
    [InlineData("""{"mappings":"no-es-arreglo"}""")]       // tipo invalido
    [InlineData("""{"mappings":[{"source":"a"}]}""")]      // mapeo incompleto
    [InlineData("""{"mappings":[]}""")]                    // sin mapeos
    public async Task PasarCampos_WithInvalidParams_Fails(string? paramsJson)
    {
        var verb = new PasarCamposVerb();
        var result = await verb.ExecuteAsync(BuildContext(paramsJson), CancellationToken.None);
        Assert.False(result.Success);
        Assert.NotNull(result.Message);
        Assert.Empty(result.ActionList);
    }

    // ---- BLOQUEAR_CAMPO_XCONDICION ----

    [Theory]
    [InlineData("equals", "baja", "baja", "hide", RuleActionKind.HideField)]
    [InlineData("equals", "baja", "alta", "hide", RuleActionKind.ShowField)]     // inversa
    [InlineData("notEquals", "baja", "alta", "hide", RuleActionKind.HideField)]
    [InlineData("empty", null, null, "hide", RuleActionKind.HideField)]
    [InlineData("notEmpty", null, "algo", "show", RuleActionKind.ShowField)]
    public async Task BloquearCampo_EvaluatesCondition_AndReturnsUiAction(
        string op, string? compared, string? current, string effect, RuleActionKind expected)
    {
        var verb = new BloquearCampoPorCondicionVerb();
        var paramsJson = JsonSerializer.Serialize(new
        {
            sourceField = "origen",
            @operator = op,
            value = compared,
            targetField = "destino",
            effect
        });
        var context = BuildContext(paramsJson, new Dictionary<string, string?> { ["origen"] = current });

        var result = await verb.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var action = Assert.Single(result.ActionList);
        Assert.Equal(expected, action.Kind);
        Assert.Equal("destino", action.FieldCode);
    }

    [Fact]
    public async Task BloquearCampo_RequireEffect_TogglesSetRequired()
    {
        var verb = new BloquearCampoPorCondicionVerb();
        var paramsJson = """{"sourceField":"s","operator":"notEmpty","targetField":"t","effect":"require"}""";

        var whenTrue = await verb.ExecuteAsync(
            BuildContext(paramsJson, new Dictionary<string, string?> { ["s"] = "x" }), CancellationToken.None);
        var actionTrue = Assert.Single(whenTrue.ActionList);
        Assert.Equal(RuleActionKind.SetRequired, actionTrue.Kind);
        Assert.True(actionTrue.Required);

        var whenFalse = await verb.ExecuteAsync(
            BuildContext(paramsJson, new Dictionary<string, string?>()), CancellationToken.None);
        var actionFalse = Assert.Single(whenFalse.ActionList);
        Assert.Equal(RuleActionKind.SetRequired, actionFalse.Kind);
        Assert.False(actionFalse.Required);
    }

    [Theory]
    [InlineData("""{"operator":"equals","targetField":"t"}""")]                       // falta sourceField
    [InlineData("""{"sourceField":"s","operator":"contiene","targetField":"t"}""")]   // operador invalido
    [InlineData("""{"sourceField":"s","operator":"equals","targetField":"t","effect":"explotar"}""")]
    public async Task BloquearCampo_WithInvalidParams_Fails(string paramsJson)
    {
        var verb = new BloquearCampoPorCondicionVerb();
        var result = await verb.ExecuteAsync(BuildContext(paramsJson), CancellationToken.None);
        Assert.False(result.Success);
    }

    // ---- ASIGNAR_CONSECUTIVO ----

    [Fact]
    public async Task AsignarConsecutivo_EmitsNumber_SetsFieldAndAutoComplete()
    {
        var sequences = new FakeSequenceService("COT-00042");
        var verb = new AsignarConsecutivoVerb(sequences, db: null!);
        var context = BuildContext(
            """{"sequenceCode":"RUL","prefix":"COT-","padding":5,"targetField":"consecutivo","autoComplete":true}""");

        var result = await verb.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.True(result.AutoCompleteStep);
        Assert.Equal(1, result.RecordsAffected);
        var action = Assert.Single(result.ActionList);
        Assert.Equal(RuleActionKind.SetFieldValue, action.Kind);
        Assert.Equal("consecutivo", action.FieldCode);
        Assert.Equal("COT-00042", action.Value);
        Assert.Equal("RUL", sequences.LastCode);
        Assert.Equal("COT-", sequences.LastPrefix);
        Assert.Equal(5, sequences.LastPadding);
    }

    [Theory]
    [InlineData(null)]                                        // sin params
    [InlineData("""{"sequenceCode":"DEMASIADOLARGO"}""")]     // codigo > 10
    public async Task AsignarConsecutivo_WithInvalidParams_Fails(string? paramsJson)
    {
        var verb = new AsignarConsecutivoVerb(new FakeSequenceService("X"), db: null!);
        var result = await verb.ExecuteAsync(BuildContext(paramsJson), CancellationToken.None);
        Assert.False(result.Success);
    }

    // ---- GENERAR_TAREAS_DESDE_TABLA ----

    [Fact]
    public async Task GenerarTareas_FromTableField_CreatesOneTaskPerRow()
    {
        var tasks = new FakeTaskItemService();
        var verb = new GenerarTareasDesdeTablaVerb(tasks);
        var activityTypeId = Guid.CreateVersion7();
        var context = BuildContext(
            $$"""{"activityTypeId":"{{activityTypeId}}","tableField":"items","titlePrefix":"[Regla] "}""",
            new Dictionary<string, string?>
            {
                ["items"] = """[{"title":"Preparar entrega","description":"detalle"},{"title":"Facturar"}]"""
            });

        var result = await verb.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.RecordsAffected);
        Assert.Equal(2, tasks.Created.Count);
        Assert.Equal("[Regla] Preparar entrega", tasks.Created[0].Title);
        Assert.Equal("detalle", tasks.Created[0].Description);
        Assert.Equal(activityTypeId, tasks.Created[0].ActivityTypeId);
    }

    [Fact]
    public async Task GenerarTareas_FromFixedRows_UsesParamsRows()
    {
        var tasks = new FakeTaskItemService();
        var verb = new GenerarTareasDesdeTablaVerb(tasks);
        var context = BuildContext(
            $$"""{"activityTypeId":"{{Guid.CreateVersion7()}}","rows":[{"title":"Fila fija"}]}""");

        var result = await verb.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.RecordsAffected);
        Assert.Equal("Fila fija", Assert.Single(tasks.Created).Title);
    }

    [Theory]
    [InlineData("""{"tableField":"items"}""")]                          // sin activityTypeId
    [InlineData("""{"activityTypeId":"no-es-guid","rows":[]}""")]       // guid invalido
    public async Task GenerarTareas_WithInvalidParams_Fails(string paramsJson)
    {
        var verb = new GenerarTareasDesdeTablaVerb(new FakeTaskItemService());
        var result = await verb.ExecuteAsync(BuildContext(paramsJson), CancellationToken.None);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task GenerarTareas_WithMalformedTableJson_Fails()
    {
        var verb = new GenerarTareasDesdeTablaVerb(new FakeTaskItemService());
        var context = BuildContext(
            $$"""{"activityTypeId":"{{Guid.CreateVersion7()}}","tableField":"items"}""",
            new Dictionary<string, string?> { ["items"] = "esto-no-es-json" });
        var result = await verb.ExecuteAsync(context, CancellationToken.None);
        Assert.False(result.Success);
    }

    // ---- NOTIFICAR ----

    [Fact]
    public async Task Notificar_WithoutTask_SucceedsWithMessage()
    {
        var verb = new NotificarVerb(db: null!);
        var result = await verb.ExecuteAsync(
            BuildContext("""{"message":"Cotizacion lista","recipient":"ventas@demo.local"}"""),
            CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.RecordsAffected);
        Assert.Contains("Cotizacion lista", result.Message);
    }

    [Fact]
    public async Task Notificar_WithoutMessage_Fails()
    {
        var verb = new NotificarVerb(db: null!);
        var result = await verb.ExecuteAsync(BuildContext("{}"), CancellationToken.None);
        Assert.False(result.Success);
    }

    // ---- Registro tipado (catalogo) ----

    [Fact]
    public void VerbCatalog_ContainsTheFiveInitialVerbs_AndUnknownVerbIsNull()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRuleVerb, PasarCamposVerb>();
        services.AddScoped<IRuleVerb, BloquearCampoPorCondicionVerb>();
        services.AddScoped<IRuleVerb>(_ => new AsignarConsecutivoVerb(new FakeSequenceService("X"), db: null!));
        services.AddScoped<IRuleVerb>(_ => new GenerarTareasDesdeTablaVerb(new FakeTaskItemService()));
        services.AddScoped<IRuleVerb>(_ => new NotificarVerb(db: null!));
        using var provider = services.BuildServiceProvider();
        var engine = new RulesEngine(db: null!, tenantContext: new FakeTenantContext(), provider);

        var catalog = engine.GetVerbCatalog();

        Assert.Equal(5, catalog.Count);
        Assert.Contains(catalog, v => v.VerbName == "PASAR_CAMPOS");
        Assert.Contains(catalog, v => v.VerbName == "BLOQUEAR_CAMPO_XCONDICION");
        Assert.Contains(catalog, v => v.VerbName == "ASIGNAR_CONSECUTIVO");
        Assert.Contains(catalog, v => v.VerbName == "GENERAR_TAREAS_DESDE_TABLA");
        Assert.Contains(catalog, v => v.VerbName == "NOTIFICAR");
        // Todos declaran su contrato de parametros para la UI.
        Assert.All(catalog, v => Assert.NotEmpty(v.Params));
        // Verbo desconocido: null tipado (nunca reflexion sobre el nombre).
        Assert.Null(engine.FindVerb("EXECUTE_SQL"));
        // Case-insensitive (los nombres legacy venian en mayusculas).
        Assert.NotNull(engine.FindVerb("pasar_campos"));
    }

    // ---- Fakes ----

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
    }

    private sealed class FakeSequenceService(string next) : ISequenceService
    {
        public string? LastCode { get; private set; }
        public string? LastPrefix { get; private set; }
        public int LastPadding { get; private set; }

        public Task EnsureSequenceAsync(string code, CancellationToken cancellationToken = default)
        {
            LastCode = code;
            return Task.CompletedTask;
        }

        public Task<string> NextAsync(string code, string prefix, int padding, CancellationToken cancellationToken = default)
        {
            LastCode = code;
            LastPrefix = prefix;
            LastPadding = padding;
            return Task.FromResult(next);
        }
    }

    /// <summary>Fake de ITaskItemService: solo CreateAsync registra; el resto no se usa.</summary>
    private sealed class FakeTaskItemService : ITaskItemService
    {
        public List<CreateTaskItemRequest> Created { get; } = [];

        public Task<TaskCoreResult<TaskItemDetailDto>> CreateAsync(
            CreateTaskItemRequest request, Guid actorUserId, string actorName,
            CancellationToken cancellationToken = default)
        {
            Created.Add(request);
            var summary = new TaskItemSummaryDto(
                Guid.CreateVersion7(), $"T{Created.Count:00000}", request.Title,
                request.ActivityTypeId, null, request.Priority, TaskItemStatus.Pending,
                request.AssigneeTenantUserId, request.DueDate, request.ProjectId,
                request.Color, false, null, 1, DateTimeOffset.UtcNow, []);
            var detail = new TaskItemDetailDto(summary, request.Description,
                request.RequesterName, request.RequesterEmail, request.RequesterPhone,
                [], 0, [], [], [], []);
            return Task.FromResult(TaskCoreResult<TaskItemDetailDto>.Ok(detail));
        }

        public Task<TaskCoreResult<TaskItemDetailDto>> UpdateAsync(Guid taskId, UpdateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> ChangeStatusAsync(Guid taskId, TaskItemStatus newStatus, string? reason, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> AssignAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> UnassignAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> ArchiveAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemSummaryDto>> RestoreAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskItemTagDto>> ListTagsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemTagDto>> CreateTagAsync(string name, string? color, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> AttachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> DetachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemChecklistItemDto>> AddChecklistItemAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemChecklistItemDto>> ToggleChecklistItemAsync(Guid checklistItemId, bool isCompleted, Guid? completedByTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> RemoveChecklistItemAsync(Guid checklistItemId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> ReorderChecklistAsync(Guid taskId, IReadOnlyList<Guid> orderedItemIds, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> AddAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> RemoveAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemActivityDto>> AddCommentAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskItemAttachmentDto>> AddAttachmentAsync(AddTaskAttachmentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<bool>> DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskCoreResult<TaskWorkLogDto>> AddWorkLogAsync(AddTaskWorkLogRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TaskWorkLogDto>> ListWorkLogsAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> TotalSecondsAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<TaskItemSummaryDto>> ListAsync(TaskItemListFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TaskItemDetailDto?> GetDetailAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
