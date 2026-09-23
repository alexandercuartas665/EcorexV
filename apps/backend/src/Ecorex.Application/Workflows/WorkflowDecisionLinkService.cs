using System.Security.Cryptography;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <inheritdoc />
public sealed class WorkflowDecisionLinkService : IWorkflowDecisionLinkService
{
    private const int DefaultExpiryHours = 72;

    private readonly IApplicationDbContext _db;
    private readonly IWorkflowEngine _engine;
    private readonly INotifyLinkBuilder _link;

    public WorkflowDecisionLinkService(IApplicationDbContext db, IWorkflowEngine engine, INotifyLinkBuilder link)
    {
        _db = db;
        _engine = engine;
        _link = link;
    }

    public async Task<string?> EnsureLinkAsync(Guid stepId, Guid targetNodeId, WorkflowDecisionCapture capture,
        bool observationRequired, string? buttonLabel, int? expiryHours, CancellationToken cancellationToken = default)
    {
        var step = await _db.WorkflowStepHistories.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null || !step.IsCurrent || step.Status != WorkflowStepStatus.Pending) { return null; }

        var node = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.Id == step.NodeId).Select(n => n.NodeType).FirstOrDefaultAsync(cancellationToken);
        if (node != WorkflowNodeType.ExclusiveGateway) { return null; }

        // Reusa el token vivo si ya existe para (paso, salida): asi reenviar la notificacion no crea duplicados.
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.WorkflowDecisionTokens
            .Where(t => t.StepId == stepId && t.TargetNodeId == targetNodeId
                && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .Select(t => t.Token)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) { return _link.BuildDecisionLink(existing); }

        var taskItemId = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == step.InstanceId).Select(i => (Guid?)i.TaskItemId).FirstOrDefaultAsync(cancellationToken);

        var secret = NewSecret();
        _db.WorkflowDecisionTokens.Add(new WorkflowDecisionToken
        {
            TenantId = step.TenantId,
            Token = secret,
            InstanceId = step.InstanceId,
            StepId = stepId,
            GatewayNodeId = step.NodeId,
            TargetNodeId = targetNodeId,
            TaskItemId = taskItemId,
            Capture = capture,
            ObservationRequired = observationRequired,
            ButtonLabel = string.IsNullOrWhiteSpace(buttonLabel) ? null : buttonLabel!.Trim(),
            ExpiresAt = now.AddHours(expiryHours is int h && h > 0 ? h : DefaultExpiryHours)
        });
        await _db.SaveChangesAsync(cancellationToken);
        return _link.BuildDecisionLink(secret);
    }

    public async Task<DecisionTokenValidation> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) { return new DecisionTokenValidation(false); }
        // UNICO punto cross-tenant: por Token exacto, sin tenant en contexto.
        var t = await _db.WorkflowDecisionTokens.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Token == token, cancellationToken);
        if (t is null) { return new DecisionTokenValidation(false); }

        var now = DateTimeOffset.UtcNow;
        if (t.UsedAt != null || t.RevokedAt != null || t.ExpiresAt <= now) { return new DecisionTokenValidation(false); }

        var step = await _db.WorkflowStepHistories.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == t.StepId).Select(s => new { s.IsCurrent, s.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (step is null || !step.IsCurrent || step.Status != WorkflowStepStatus.Pending) { return new DecisionTokenValidation(false); }

        string? title = null, number = null, contact = null;
        if (t.TaskItemId is Guid tid)
        {
            var task = await _db.TaskItems.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == tid).Select(x => new { x.Title, x.Number, x.RequesterName })
                .FirstOrDefaultAsync(cancellationToken);
            title = task?.Title; number = task?.Number; contact = task?.RequesterName;
        }
        return new DecisionTokenValidation(true, t.TenantId, t.Id, t.Capture, t.ObservationRequired,
            t.ButtonLabel, title, number, contact);
    }

    public async Task<DecisionApplyResult> ApplyAsync(string token, DecisionSubmit submit, CancellationToken cancellationToken = default)
    {
        // Corre con el tenant del token fijado como ambient por el llamador (pagina /d). Por eso las
        // consultas de aqui son tenant-scoped normales.
        var t = await _db.WorkflowDecisionTokens.FirstOrDefaultAsync(x => x.Token == token, cancellationToken);
        if (t is null) { return new DecisionApplyResult(false, "Enlace no valido."); }

        var now = DateTimeOffset.UtcNow;
        if (t.UsedAt != null || t.RevokedAt != null || t.ExpiresAt <= now)
        { return new DecisionApplyResult(false, "Este enlace ya no esta disponible."); }

        var step = await _db.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.Id == t.StepId).Select(s => new { s.IsCurrent, s.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (step is null || !step.IsCurrent || step.Status != WorkflowStepStatus.Pending)
        { return new DecisionApplyResult(false, "La decision ya fue registrada."); }

        // Requisitos de captura.
        if (t.Capture == WorkflowDecisionCapture.Signature && string.IsNullOrWhiteSpace(submit.SignatureUrl))
        { return new DecisionApplyResult(false, "Falta la firma."); }
        if (t.Capture == WorkflowDecisionCapture.Observation && t.ObservationRequired && string.IsNullOrWhiteSpace(submit.Observation))
        { return new DecisionApplyResult(false, "La observacion es obligatoria."); }

        var contact = t.TaskItemId is Guid ctid
            ? await _db.TaskItems.AsNoTracking().Where(x => x.Id == ctid).Select(x => x.RequesterName).FirstOrDefaultAsync(cancellationToken)
            : null;
        var actorName = string.IsNullOrWhiteSpace(contact) ? "Cliente (enlace)" : contact!.Trim();

        // Nota que hereda la ruta (queda como ApprovalComment del paso): la observacion, o una marca de firma.
        var note = t.Capture switch
        {
            WorkflowDecisionCapture.Observation => submit.Observation?.Trim(),
            WorkflowDecisionCapture.Signature => "Firmado por el cliente (enlace publico).",
            _ => null
        };

        // 1) RESOLVER la compuerta por la ruta (accion esencial). Cliente = sin usuario ni agente.
        var res = await _engine.ChooseGatewayRouteAsync(t.InstanceId, t.StepId, t.TargetNodeId, null, note, null, cancellationToken);
        if (!res.IsOk) { return new DecisionApplyResult(false, res.Error ?? "No se pudo registrar la decision."); }

        // 2) Evidencia y trazabilidad (best-effort respecto a la decision ya tomada).
        if (t.TaskItemId is Guid tid)
        {
            if (t.Capture == WorkflowDecisionCapture.Signature && !string.IsNullOrWhiteSpace(submit.SignatureUrl))
            {
                _db.TaskItemAttachments.Add(new TaskItemAttachment
                {
                    TenantId = t.TenantId,
                    TaskItemId = tid,
                    FileName = $"Firma cliente {DateTimeOffset.UtcNow:yyyyMMdd-HHmm}.png",
                    Url = submit.SignatureUrl!,
                    MimeType = "image/png",
                    SizeBytes = submit.SignatureSize,
                    UploadedByName = actorName
                });
            }

            var label = t.ButtonLabel ?? "salida";
            var text = t.Capture == WorkflowDecisionCapture.Observation && !string.IsNullOrWhiteSpace(submit.Observation)
                ? $"Decision del cliente por enlace: {label}. Observacion: {submit.Observation!.Trim()}"
                : $"Decision del cliente por enlace: {label}." + (t.Capture == WorkflowDecisionCapture.Signature ? " Firma adjunta." : "");
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = t.TenantId,
                TaskItemId = tid,
                Type = TaskActivityType.Action,
                ActorName = actorName,
                Text = text
            });
        }

        // 3) Marcar usado e invalidar los enlaces hermanos de esta misma compuerta/paso.
        var self = await _db.WorkflowDecisionTokens.FirstOrDefaultAsync(x => x.Id == t.Id, cancellationToken);
        if (self is not null) { self.UsedAt = now; }
        var siblings = await _db.WorkflowDecisionTokens
            .Where(x => x.StepId == t.StepId && x.Id != t.Id && x.UsedAt == null && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var s in siblings) { s.RevokedAt = now; }

        await _db.SaveChangesAsync(cancellationToken);
        return new DecisionApplyResult(true);
    }

    // Secreto URL-safe (~43 chars) para el enlace publico. Aleatorio de 32 bytes en base64url.
    private static string NewSecret()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
