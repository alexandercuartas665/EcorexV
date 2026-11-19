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
                h.TemplateId, h.AccountId, h.RepeatEvery, h.PackageSize)).ToList(),
            s.StepType == ContactWorkflowStepType.WhatsApp ? DeserializeWhatsApp(s.ParamsJson) : null,
            s.StepType == ContactWorkflowStepType.Email ? DeserializeEmail(s.ParamsJson) : null)).ToList();

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
        nombre = SurrogateText.SafeTruncate(nombre, 150)!;

        // Carga el workflow existente CON sus hijos para actualizarlos en sitio (upsert por Id).
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
        }
        workflow.Activo = request.Activo;

        // Upsert por Id (2026-11-15): un paso/ventana que ya existe se ACTUALIZA en sitio. Antes se borraban y
        // recreaban todos, y como ContactWorkflowRun cuelga del paso con cascada, cada guardado borraba el
        // historial de ejecuciones Y reiniciaba el dedupe del dia (re-guardar volvia a enviar a todo el segmento).
        // Solo se borran fisicamente los pasos/ventanas que el usuario quito (son detalle, no agregados).
        var keptStepIds = steps.Where(s => s.Id is Guid).Select(s => s.Id!.Value).ToHashSet();
        foreach (var oldStep in workflow.Steps.Where(x => !keptStepIds.Contains(x.Id)).ToList())
        {
            _db.ContactWorkflowSchedules.RemoveRange(oldStep.Schedules);
            _db.ContactWorkflowSteps.Remove(oldStep);
            workflow.Steps.Remove(oldStep);
        }

        var orden = 0;
        foreach (var s in steps)
        {
            var paramsJson = s.StepType switch
            {
                ContactWorkflowStepType.Llamada => SerializeCall(s.Call),
                ContactWorkflowStepType.WhatsApp => SerializeWhatsApp(s.WhatsApp),
                ContactWorkflowStepType.Email => SerializeEmail(s.Email),
                _ => null
            };
            var label = string.IsNullOrWhiteSpace(s.Label) ? DefaultLabel(s.StepType) : s.Label.Trim();

            var step = s.Id is Guid sid ? workflow.Steps.FirstOrDefault(x => x.Id == sid) : null;
            if (step is null)
            {
                step = new ContactWorkflowStep { TenantId = tenantId, ContactWorkflowId = workflow.Id };
                // Add explicito: con Id (Guid v7) ya asignado, descubrirlo solo por la navegacion podria
                // tomarlo como existente (UPDATE de 0 filas).
                _db.ContactWorkflowSteps.Add(step);
                workflow.Steps.Add(step);
            }
            step.StepType = s.StepType;
            step.Label = label;
            step.Orden = orden++;
            step.ParamsJson = paramsJson;

            var schedules = s.Schedules ?? Array.Empty<SaveContactWorkflowScheduleRequest>();
            var keptSchedIds = schedules.Where(h => h.Id is Guid).Select(h => h.Id!.Value).ToHashSet();
            foreach (var oldSched in step.Schedules.Where(x => !keptSchedIds.Contains(x.Id)).ToList())
            {
                _db.ContactWorkflowSchedules.Remove(oldSched);
                step.Schedules.Remove(oldSched);
            }
            foreach (var h in schedules)
            {
                var sched = h.Id is Guid hid ? step.Schedules.FirstOrDefault(x => x.Id == hid) : null;
                if (sched is null)
                {
                    sched = new ContactWorkflowSchedule { TenantId = tenantId, ContactWorkflowStepId = step.Id };
                    _db.ContactWorkflowSchedules.Add(sched);
                    step.Schedules.Add(sched);
                }
                sched.StartDate = h.StartDate;
                sched.EndDate = h.EndDate;
                sched.StartTime = h.StartTime;
                sched.EndTime = h.EndTime;
                sched.ActiveDays = string.IsNullOrWhiteSpace(h.ActiveDays) ? "1,2,3,4,5" : h.ActiveDays.Trim();
                sched.TemplateId = string.IsNullOrWhiteSpace(h.TemplateId) ? null : h.TemplateId.Trim();
                sched.AccountId = h.AccountId;
                sched.RepeatEvery = h.RepeatEvery;
                sched.PackageSize = h.PackageSize;
            }
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

    public async Task<IReadOnlyList<ContactWorkflowRunDto>> ListRecentRunsAsync(
        Guid filtroId, int take = 50, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return Array.Empty<ContactWorkflowRunDto>(); }
        take = Math.Clamp(take, 1, 200);

        // Filtro global por tenant en ambas tablas; el contacto se resuelve por Guid plano (sin navegacion).
        var runs = await _db.ContactWorkflowRuns.AsNoTracking()
            .Where(r => r.ContactWorkflowStep!.ContactWorkflow!.TerceroFiltroId == filtroId)
            .OrderByDescending(r => r.DispatchedAtUtc)
            .Take(take)
            .Select(r => new
            {
                r.DispatchedAtUtc,
                r.TerceroId,
                Paso = r.ContactWorkflowStep!.Label,
                r.Channel,
                r.Status,
                r.Attempts,
                r.Error,
                r.DeliveryStatus,
                r.DeliveryDetail,
                r.OpenedAtUtc,
                r.ClickedAtUtc
            })
            .ToListAsync(cancellationToken);
        if (runs.Count == 0) { return Array.Empty<ContactWorkflowRunDto>(); }

        var ids = runs.Select(r => r.TerceroId).Distinct().ToList();
        var nombres = await _db.Terceros.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Nombre })
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, cancellationToken);

        var tz = Scheduling.ScheduledJobRecurrence.ResolveTimeZone(await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken));

        return runs.Select(r => new ContactWorkflowRunDto(
            TimeZoneInfo.ConvertTime(r.DispatchedAtUtc, tz).DateTime,
            nombres.TryGetValue(r.TerceroId, out var n) ? n : "(contacto eliminado)",
            r.Paso,
            r.Channel,
            r.Status,
            r.Attempts,
            r.Error,
            r.DeliveryStatus,
            r.DeliveryDetail,
            r.OpenedAtUtc is DateTimeOffset o ? TimeZoneInfo.ConvertTime(o, tz).DateTime : null,
            r.ClickedAtUtc is DateTimeOffset k ? TimeZoneInfo.ConvertTime(k, tz).DateTime : null)).ToList();
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

    private static string? SerializeWhatsApp(ContactWorkflowWhatsAppParams? wa)
    {
        if (wa is null) { return null; }
        var texto = string.IsNullOrWhiteSpace(wa.Texto) ? null : wa.Texto.Trim();
        var vars = wa.Variables?
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());
        if (wa.LineaId is null && texto is null && wa.PlantillaId is null && (vars is null || vars.Count == 0))
        {
            return null;
        }
        return JsonSerializer.Serialize(
            new ContactWorkflowWhatsAppParams(wa.LineaId, texto, wa.PlantillaId, vars is { Count: > 0 } ? vars : null), JsonOpts);
    }

    /// <summary>Remitente del paso E-mail (ADR-0185). Null si no se configuro nada (usa el del servidor).</summary>
    internal static string? SerializeEmail(ContactWorkflowEmailParams? email)
    {
        if (email is null) { return null; }
        static string? Clean(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : SurrogateText.SafeTruncate(v.Trim(), max);
        var p = new ContactWorkflowEmailParams(Clean(email.RemitenteNombre, 120), Clean(email.RemitenteCorreo, 200), Clean(email.ResponderA, 200));
        return p.RemitenteNombre is null && p.RemitenteCorreo is null && p.ResponderA is null
            ? null
            : JsonSerializer.Serialize(p, JsonOpts);
    }

    internal static ContactWorkflowEmailParams? DeserializeEmail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try { return JsonSerializer.Deserialize<ContactWorkflowEmailParams>(json, JsonOpts); }
        catch (JsonException) { return null; }
    }

    private static ContactWorkflowWhatsAppParams? DeserializeWhatsApp(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            return JsonSerializer.Deserialize<ContactWorkflowWhatsAppParams>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
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
