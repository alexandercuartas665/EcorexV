using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Enlaces publicos de decision del cliente para las salidas de una compuerta exclusiva ATENDIDA (feature
/// generica). Emite un token por salida marcada como publica cuando el paso llega; el cliente abre /d/{token}
/// (anonimo), captura lo configurado (firma / observacion / nada) y con eso se resuelve la compuerta por esa
/// ruta. La validacion anonima es el UNICO punto cross-tenant (por Token exacto, IgnoreQueryFilters).
/// </summary>
public interface IWorkflowDecisionLinkService
{
    /// <summary>Emite (idempotente) el enlace de decision de UNA salida de la compuerta de este paso y
    /// devuelve su URL absoluta. Lo llama la REGLA DE NOTIFICACION al dispararse. Reusa el token vivo si ya
    /// existe para (paso, salida); si no, lo crea. Devuelve null si no aplica (nodo no compuerta, sin URL
    /// base, etc.). Corre con el tenant ya fijado (contexto del motor).</summary>
    Task<string?> EnsureLinkAsync(Guid stepId, Guid targetNodeId, WorkflowDecisionCapture capture,
        bool observationRequired, string? buttonLabel, int? expiryHours, CancellationToken cancellationToken = default);

    /// <summary>Valida un token en claro (existe, no usado, no revocado, no expirado y el paso sigue vigente).
    /// NO requiere tenant. Resultado neutro si invalido.</summary>
    Task<DecisionTokenValidation> ValidateAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Aplica la decision: guarda evidencia (firma) / observacion, resuelve la compuerta por la ruta,
    /// marca el token usado e invalida los hermanos. Debe correr con el tenant del token fijado como ambient.</summary>
    Task<DecisionApplyResult> ApplyAsync(string token, DecisionSubmit submit, CancellationToken cancellationToken = default);
}

/// <summary>Resultado neutro de validar un token de decision.</summary>
public sealed record DecisionTokenValidation(
    bool IsValid,
    Guid? TenantId = null,
    Guid? TokenId = null,
    WorkflowDecisionCapture Capture = WorkflowDecisionCapture.None,
    bool ObservationRequired = false,
    string? ButtonLabel = null,
    string? ActivityTitle = null,
    string? ActivityNumber = null,
    string? ContactName = null);

/// <summary>Datos que aporta el cliente al enviar. La firma ya viene guardada como archivo (SignatureUrl+Size);
/// la observacion es texto libre.</summary>
public sealed record DecisionSubmit(
    string? SignatureUrl = null,
    long SignatureSize = 0,
    string? Observation = null);

public sealed record DecisionApplyResult(bool Ok, string? Error = null);
