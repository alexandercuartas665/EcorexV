using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Reconciliacion PURA del estado de aprobacion de una plantilla HSM contra lo que lista YCloud/Meta
/// (ADR-0184). Solo toca el ESTADO (status, motivo de rechazo, id del proveedor, fecha de revision): nunca el
/// cuerpo, las variables ni los botones (eso es del import manual). Sin dependencias para poder probarla.
/// </summary>
public static class WhatsAppTemplateStatusSync
{
    /// <summary>Estado de Meta -> enum interno. Devuelve null si el texto no es un estado conocido (en ese caso
    /// la sincronizacion NO cambia nada: mejor conservar el estado local que degradarlo a borrador).</summary>
    public static WhatsAppTemplateStatus? MapStatus(string? providerStatus) => (providerStatus ?? "").Trim().ToUpperInvariant() switch
    {
        "APPROVED" => WhatsAppTemplateStatus.Approved,
        "REJECTED" => WhatsAppTemplateStatus.Rejected,
        "PAUSED" => WhatsAppTemplateStatus.Paused,
        "DISABLED" or "DELETED" => WhatsAppTemplateStatus.Disabled,
        "PENDING" or "IN_APPEAL" or "PENDING_DELETION" => WhatsAppTemplateStatus.Submitted,
        _ => null
    };

    /// <summary>Busca la plantilla remota que corresponde a la local: primero por id del proveedor (estable aunque
    /// cambie el nombre), luego por (nombre, idioma) sin distinguir mayusculas.</summary>
    public static YCloudTemplateStatus? FindRemote(WhatsAppTemplate local, IReadOnlyList<YCloudTemplateStatus> remote)
    {
        if (!string.IsNullOrWhiteSpace(local.ProviderTemplateId))
        {
            var byId = remote.FirstOrDefault(r => string.Equals(r.Id, local.ProviderTemplateId, StringComparison.Ordinal));
            if (byId is not null) { return byId; }
        }
        var name = local.Name.Trim();
        var lang = local.Language.Trim();
        return remote.FirstOrDefault(r =>
            string.Equals(WhatsAppTemplateCalculations.NormalizeName(r.Name), name, StringComparison.OrdinalIgnoreCase)
            && string.Equals((r.Language ?? "").Trim(), lang, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Aplica el estado remoto a la plantilla local. Devuelve el cambio si el ESTADO o el MOTIVO de rechazo
    /// cambiaron (null si no hubo cambio visible). Completa en silencio el id del proveedor si faltaba.
    /// </summary>
    public static WhatsAppStatusChange? Apply(WhatsAppTemplate local, YCloudTemplateStatus remote, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(local.ProviderTemplateId) && !string.IsNullOrWhiteSpace(remote.Id))
        {
            local.ProviderTemplateId = remote.Id;
        }
        if (MapStatus(remote.Status) is not WhatsAppTemplateStatus next)
        {
            return null;
        }
        var reason = next == WhatsAppTemplateStatus.Rejected && !string.IsNullOrWhiteSpace(remote.RejectedReason)
            ? remote.RejectedReason!.Trim()
            : null;
        if (next == local.Status && string.Equals(reason, local.RejectionReason, StringComparison.Ordinal))
        {
            return null;
        }

        var from = local.Status;
        local.Status = next;
        local.RejectionReason = reason;
        if ((next is WhatsAppTemplateStatus.Approved or WhatsAppTemplateStatus.Rejected) && from != next)
        {
            local.ReviewedAt = nowUtc;
        }
        return new WhatsAppStatusChange(local.Id, local.Name, local.Language, from, next, reason);
    }

    /// <summary>Texto corto en espanol del estado (para mensajes del agente y la bitacora).</summary>
    public static string Label(WhatsAppTemplateStatus status) => status switch
    {
        WhatsAppTemplateStatus.Draft => "borrador",
        WhatsAppTemplateStatus.Submitted => "en revision de Meta",
        WhatsAppTemplateStatus.Approved => "aprobada",
        WhatsAppTemplateStatus.Rejected => "rechazada",
        WhatsAppTemplateStatus.Paused => "pausada por Meta",
        WhatsAppTemplateStatus.Disabled => "deshabilitada",
        _ => status.ToString()
    };
}
