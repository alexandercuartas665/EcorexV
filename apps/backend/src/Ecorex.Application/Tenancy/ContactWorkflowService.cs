using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Implementacion de <see cref="IContactWorkflowService"/> (ADR-0056, Fase 1). Aislamiento por el
/// filtro global del DbContext (nunca se filtra a mano por TenantId); el alta estampa el TenantId
/// del contexto. El guardado es atomico (un SaveChanges) y queda auditado. Sin motor de ejecucion:
/// aqui SOLO se persiste la configuracion del disenador.
/// </summary>
public sealed class ContactWorkflowService : IContactWorkflowService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ContactWorkflowService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<ContactWorkflowDto?> GetByFiltroAsync(Guid filtroId, CancellationToken cancellationToken = default)
    {
        var wf = await _db.ContactWorkflows.AsNoTracking()
            .Where(w => w.TerceroFiltroId == filtroId)
            .Select(w => new
            {
                w.Id,
                w.TerceroFiltroId,
                w.Nombre,
                w.Activo,
                Steps = w.Steps.OrderBy(s => s.Orden).Select(s => new
                {
                    s.Id,
                    s.StepType,
                    s.Label,
                    s.Orden,
                    s.ParamsJson,
                    Schedules = s.Schedules.Select(h => new
                    {
                        h.Id,
                        h.StartDate,
                        h.EndDate,
                        h.StartTime,
                        h.EndTime,
                        h.ActiveDays,
                        h.TemplateId,
                        h.AccountId,
                        h.RepeatEvery,
                        h.PackageSize
                    }).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (wf is null) { return null; }

        var steps = wf.Steps.Select(s => new ContactWorkflowStepDto(
            s.Id, s.StepType, s.Label, s.Orden,
            s.StepType == ContactWorkflowStepType.Llamada ? DeserializeCall(s.ParamsJson) : null,
            s.Schedules.Select(h => new ContactWorkflowScheduleDto(
                h.Id, h.StartDate, h.EndDate, h.StartTime, h.EndTime, h.ActiveDays,
                h.TemplateId, h.AccountId, h.RepeatEvery, h.PackageSize)).ToList())).ToList();

        return new ContactWorkflowDto(wf.Id, wf.TerceroFiltroId, wf.Nombre, wf.Activo, steps);
    }

    public async Task<ContactWorkflowResult<ContactWorkflowDto>> SaveAsync(
        Guid filtroId, SaveContactWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return ContactWorkflowResult<ContactWorkflowDto>.Invalid("No hay tenant activo.");
        }

        // El filtro debe existir para el tenant (el filtro global aplica el aislamiento).
        var filtro = await _db.TerceroFiltros.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == filtroId, cancellationToken);
        if (filtro is null)
        {
            return ContactWorkflowResult<ContactWorkflowDto>.NotFound("El filtro no existe.");
        }

        var steps = request.Steps ?? Array.Empty<SaveContactWorkflowStepRequest>();
        foreach (var s in steps)
        {
            if (!Enum.IsDefined(typeof(ContactWorkflowStepType), s.StepType))
            {
                return ContactWorkflowResult<ContactWorkflowDto>.Invalid("Tipo de accion no valido.");
            }
        }

        var nombre = string.IsNullOrWhiteSpace(request.Nombre)
            ? $"Acciones de {filtro.Nombre}"
            : request.Nombre.Trim();
        if (nombre.Length > 150) { nombre = nombre[..150]; }

        // Carga el workflow existente CON sus hijos para reemplazar por completo pasos+ventanas.
        var workflow = await _db.ContactWorkflows
            .Include(w => w.Steps).ThenInclude(s => s.Schedules)
            .FirstOrDefaultAsync(w => w.TerceroFiltroId == filtroId, cancellationToken);

        var isNew = workflow is null;
        if (workflow is null)
        {
            workflow = new ContactWorkflow
            {
                TenantId = tenantId,
                TerceroFiltroId = filtroId,
                Nombre = nombre
            };
            _db.ContactWorkflows.Add(workflow);
        }
        else
        {
            workflow.Nombre = nombre;
            // Reemplazo total: los pasos/ventanas son DETALLE del workflow (no agregados), se
            // borran fisicamente. Quitar las ventanas primero evita huerfanas si la cascada no aplica.
            foreach (var oldStep in workflow.Steps)
            {
                _db.ContactWorkflowSchedules.RemoveRange(oldStep.Schedules);
            }
            _db.ContactWorkflowSteps.RemoveRange(workflow.Steps);
            workflow.Steps.Clear();
        }
        workflow.Activo = request.Activo;

        var orden = 0;
        foreach (var s in steps)
        {
            var call = s.StepType == ContactWorkflowStepType.Llamada ? s.Call : null;
            var step = new ContactWorkflowStep
            {
                TenantId = tenantId,
                ContactWorkflowId = workflow.Id,
                StepType = s.StepType,
                Label = string.IsNullOrWhiteSpace(s.Label) ? DefaultLabel(s.StepType) : s.Label.Trim(),
                Orden = orden++,
                ParamsJson = SerializeCall(call)
            };
            foreach (var h in s.Schedules ?? Array.Empty<SaveContactWorkflowScheduleRequest>())
            {
                step.Schedules.Add(new ContactWorkflowSchedule
                {
                    TenantId = tenantId,
                    ContactWorkflowStepId = step.Id,
                    StartDate = h.StartDate,
                    EndDate = h.EndDate,
                    StartTime = h.StartTime,
                    EndTime = h.EndTime,
                    ActiveDays = string.IsNullOrWhiteSpace(h.ActiveDays) ? "1,2,3,4,5" : h.ActiveDays.Trim(),
                    TemplateId = string.IsNullOrWhiteSpace(h.TemplateId) ? null : h.TemplateId.Trim(),
                    AccountId = h.AccountId,
                    RepeatEvery = h.RepeatEvery,
                    PackageSize = h.PackageSize
                });
            }
            workflow.Steps.Add(step);
        }

        // Auditoria dentro de la MISMA transaccion (un SaveChanges): CLAUDE.md regla 5.
        _audit.Write(
            _tenant.UserId ?? Guid.Empty,
            isNew ? "contact-workflow.create" : "contact-workflow.update",
            nameof(ContactWorkflow),
            workflow.Id,
            null,
            new { workflow.TerceroFiltroId, workflow.Nombre, workflow.Activo, Steps = steps.Count },
            tenantId);

        await _db.SaveChangesAsync(cancellationToken);

        var dto = await GetByFiltroAsync(filtroId, cancellationToken);
        return dto is null
            ? ContactWorkflowResult<ContactWorkflowDto>.Invalid("No se pudo guardar el workflow.")
            : ContactWorkflowResult<ContactWorkflowDto>.Ok(dto);
    }

    private static string DefaultLabel(ContactWorkflowStepType type) => type switch
    {
        ContactWorkflowStepType.Conectar => "Conectar",
        ContactWorkflowStepType.MensajeRed => "Mensaje Red",
        ContactWorkflowStepType.WhatsApp => "WhatsApp",
        ContactWorkflowStepType.Email => "E-mail",
        ContactWorkflowStepType.Llamada => "Llamada CRM",
        _ => "Paso"
    };

    private static string? SerializeCall(ContactWorkflowCallParams? call)
    {
        if (call is null) { return null; }
        var vacioCrm = string.IsNullOrWhiteSpace(call.Comercial) && string.IsNullOrWhiteSpace(call.Prioridad)
            && string.IsNullOrWhiteSpace(call.Categoria) && string.IsNullOrWhiteSpace(call.Subcategoria);
        // Config IA: modo/agente/prompt extra/objetivo/formularios. Si tambien esta vacia, el paso no
        // tiene parametros (se guarda null); si hay algo (CRM O IA), se serializa completo.
        var vacioIa = string.IsNullOrWhiteSpace(call.Modo) && call.AgenteId is null
            && string.IsNullOrWhiteSpace(call.PromptExtra) && string.IsNullOrWhiteSpace(call.Objetivo)
            && (call.FormulariosPermitidos is null || call.FormulariosPermitidos.Count == 0);
        if (vacioCrm && vacioIa)
        {
            return null;
        }
        return JsonSerializer.Serialize(call, JsonOpts);
    }

    private static ContactWorkflowCallParams? DeserializeCall(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            return JsonSerializer.Deserialize<ContactWorkflowCallParams>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
