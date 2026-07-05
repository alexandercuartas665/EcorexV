using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests de la costura de cierre comercial (ADR-0028): el toolset "crear_lead" depende de IAgentLeadSink,
/// no de Lead/CRM. Se verifica que:
/// - con el sink No-Op el toolset responde OK y NO crea ningun lead;
/// - con el adaptador CRM (PipelineLeadSink) el toolset crea el lead exactamente como antes
///   (mismo contrato del tool y misma unidad de negocio por canal).
/// El DbContext solo se usa para el default de telefono por conversacion (rama no ejercitada aqui,
/// pues AiToolRunContext.ConversationId es null), por eso se pasa null! de forma segura.
/// </summary>
public class AgentLeadSinkTests
{
    private static JsonElement Run(PipelineToolset toolset, string argsJson, out AgentToolResult result)
    {
        result = toolset.ExecuteAsync("crear_lead", argsJson, Guid.CreateVersion7(), autonomous: true)
            .GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(result.Json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void CrearLead_ConSinkNoOp_RespondeOkYNoCreaLead()
    {
        var sink = new NoOpAgentLeadSink();
        var toolset = new PipelineToolset(sink, db: null!);

        var payload = Run(toolset, """{"cliente_nombre":"Familia Perez","tipo_cliente":"b2b"}""", out var result);

        Assert.False(result.SessionCompleted);
        Assert.True(payload.GetProperty("ok").GetBoolean());
        // No-Op no crea lead: no hay id (null) pero tampoco error.
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("lead_id").ValueKind);
        Assert.False(payload.TryGetProperty("error", out _));
    }

    [Fact]
    public void CrearLead_ConAdaptadorCrm_CreaLeadYMapeaUnidadDeNegocio()
    {
        var leads = new FakeLeadService();
        var units = new FakeBusinessUnitService(
            new BusinessUnitDto(Guid.CreateVersion7(), "Ventas B2B", "#000", BusinessUnitModalKind.Generic, 1, true),
            new BusinessUnitDto(Guid.CreateVersion7(), "Cursos", "#111", BusinessUnitModalKind.Generic, 2, true));
        var sink = new PipelineLeadSink(leads, units);
        var toolset = new PipelineToolset(sink, db: null!);

        var payload = Run(toolset,
            """{"cliente_nombre":"Acme SA","cliente_telefono":"+573001112233","tipo_cliente":"b2b","resumen":"Suministro mensual"}""",
            out var result);

        Assert.True(payload.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.String, payload.GetProperty("lead_id").ValueKind); // Guid serializado
        Assert.Equal("Ventas B2B", payload.GetProperty("unidad_negocio").GetString());

        // El lead se creo exactamente una vez con el nombre y la unidad B2B, y quedo una nota con el resumen.
        var created = Assert.Single(leads.Created);
        Assert.Equal("Acme SA", created.ContactName);
        Assert.Equal("+573001112233", created.ContactPhone);
        Assert.Equal(units.All[0].Id, created.BusinessUnitId);
        Assert.Contains(leads.Notes, n => n.Content.Contains("Suministro mensual"));
    }

    [Fact]
    public void CrearLead_SinNombre_DevuelveError_SinTocarElSink()
    {
        var leads = new FakeLeadService();
        var sink = new PipelineLeadSink(leads, new FakeBusinessUnitService());
        var toolset = new PipelineToolset(sink, db: null!);

        var payload = Run(toolset, """{"tipo_cliente":"b2b"}""", out _);

        Assert.False(payload.GetProperty("ok").GetBoolean());
        Assert.True(payload.TryGetProperty("error", out _));
        Assert.Empty(leads.Created);
    }

    // ===== Fakes =====

    private sealed class FakeLeadService : ILeadService
    {
        public List<CreateLeadRequest> Created { get; } = new();
        public List<(Guid LeadId, string Content, string Color)> Notes { get; } = new();

        public Task<LeadDto?> CreateAsync(CreateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
        {
            Created.Add(request);
            var dto = new LeadDto(Guid.CreateVersion7(), request.ContactName, request.ContactPhone, request.Destination,
                request.EstimatedValue, request.Currency, Guid.CreateVersion7(), LeadStatus.Open, null,
                DateTimeOffset.UtcNow, new Dictionary<string, string?>(), request.BusinessUnitId, DateTimeOffset.UtcNow);
            return Task.FromResult<LeadDto?>(dto);
        }

        public Task<LeadNoteDto?> AddNoteAsync(Guid leadId, string content, string color, Guid actorUserId, CancellationToken cancellationToken = default)
        {
            Notes.Add((leadId, content, color));
            return Task.FromResult<LeadNoteDto?>(new LeadNoteDto(Guid.CreateVersion7(), content, color, DateTimeOffset.UtcNow, null));
        }

        // Miembros no ejercitados por estos tests.
        public Task<IReadOnlyList<LeadDto>> ListAsync(Guid? stageId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LeadDetailDto?> GetAsync(Guid leadId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LeadDto?> UpdateAsync(Guid leadId, UpdateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LeadDto?> MoveAsync(Guid leadId, MoveLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LeadDto?> AssignAsync(Guid leadId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ArchiveAsync(Guid leadId, string reason, string? note, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LeadDto?> UnarchiveAsync(Guid leadId, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ArchivedLeadDto>> ListArchivedAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PurgeArchivedResult?> PurgeArchivedHistoryAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LeadNoteDto>> ListNotesAsync(Guid leadId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LeadFileDto>> ListFilesAsync(Guid leadId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LeadFileDto?> AddFileAsync(Guid leadId, string fileName, string url, string contentType, long sizeBytes, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string?> DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeBusinessUnitService : IBusinessUnitService
    {
        public BusinessUnitDto[] All { get; }
        public FakeBusinessUnitService(params BusinessUnitDto[] units) => All = units;

        public Task<IReadOnlyList<BusinessUnitDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BusinessUnitDto>>(All);

        public Task EnsureDefaultsAsync(Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BusinessUnitDto?> CreateAsync(SaveBusinessUnitRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BusinessUnitDto?> UpdateAsync(Guid id, SaveBusinessUnitRequest request, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
