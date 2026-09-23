using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Enlace publico de decision del cliente para UNA salida de una compuerta exclusiva ATENDIDA (feature
/// generica, no atada a un dominio). Se emite cuando el paso de la compuerta se vuelve actual: por cada
/// salida marcada como "enlace publico" se crea un token. El cliente abre /d/{Token} (anonimo), hace la
/// captura configurada (firma / observacion / nada) y con eso se RESUELVE la compuerta por esa ruta
/// (ChooseGatewayRouteAsync hacia <see cref="TargetNodeId"/>).
///
/// Seguridad: <see cref="Token"/> es un secreto aleatorio (capability URL) de un-solo-uso y con caducidad.
/// A diferencia de <see cref="FormToken"/> (que guarda solo el hash), aqui se guarda el secreto porque la
/// notificacion del nodo debe poder EMITIR el enlace por su token de plantilla ({enlace.&lt;clave&gt;}) sin
/// volver a generarlo; el riesgo es bajo (decision unica, caduca, no da acceso a datos). La validacion es
/// el UNICO punto cross-tenant permitido (busca por Token con IgnoreQueryFilters) y luego fija el tenant
/// como ambient. TENANT-SCOPED.
/// </summary>
public class WorkflowDecisionToken : TenantEntity
{
    /// <summary>Secreto opaco del enlace (URL-safe). Unico. Va en /d/{Token}.</summary>
    public string Token { get; set; } = null!;

    /// <summary>Instancia del flujo a la que pertenece la compuerta.</summary>
    public Guid InstanceId { get; set; }

    /// <summary>Paso (historial) de la compuerta que este enlace resuelve. Debe seguir current y Pending.</summary>
    public Guid StepId { get; set; }

    /// <summary>Nodo compuerta origen (para invalidar los enlaces hermanos al decidir).</summary>
    public Guid GatewayNodeId { get; set; }

    /// <summary>Nodo DESTINO de la salida: la rama que se toma al usar el enlace (ChooseGatewayRouteAsync).</summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>Actividad ligada a la instancia (para guardar la firma como adjunto y escribir la bitacora).</summary>
    public Guid? TaskItemId { get; set; }

    /// <summary>Que se le pide al cliente antes de resolver la salida.</summary>
    public WorkflowDecisionCapture Capture { get; set; } = WorkflowDecisionCapture.None;

    /// <summary>Si la observacion es obligatoria (solo aplica a Capture = Observation).</summary>
    public bool ObservationRequired { get; set; }

    /// <summary>Texto del boton/accion que ve el cliente (ej. "Firmar aprobacion"). Null = generico.</summary>
    public string? ButtonLabel { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Marcado al resolver (un-solo-uso). Los hermanos se marcan Revoked al mismo tiempo.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Invalidado sin usar (ej. otra salida decidio la compuerta, o el paso avanzo por consola).</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
