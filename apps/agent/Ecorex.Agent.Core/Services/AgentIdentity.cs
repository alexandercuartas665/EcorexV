using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Reglas puras (sin DPAPI ni IO) para RE-fijar la identidad del agente. Se extrajo del verbo
/// <c>--save-config</c> (App.xaml.cs) para poder probarla: tanto el MSI reconfigurable (ADR-0063)
/// como la auto-elevacion de la colmena (ADR-0050) reescriben el vault, y ambos deben respetar el
/// mismo criterio del secreto.
/// </summary>
public static class AgentIdentity
{
    /// <summary>
    /// Centinela de "conserva el secreto actual" que emite el MSI cuando NO se paso SECRET (ADR-0063).
    /// Existe porque un argumento vacio final ("") en el comando formateado del MSI se corrompe: MSI lo
    /// reemplaza por basura (p.ej. CURRENTDIRECTORY=...), que se escribiria como secreto y romperia el
    /// handshake. Pasar SIEMPRE un token no vacio evita esa corrupcion; aqui se interpreta como "no lo
    /// cambies". Riesgo despreciable: un secreto real igual a este literal se conservaria en vez de
    /// rotarse (no se pierde, solo no cambia).
    /// </summary>
    public const string KeepSecretSentinel = "__KEEP__";

    /// <summary>
    /// Combina la identidad ENTRANTE (clientId/hub y, opcionalmente, un secreto NUEVO) con la que ya
    /// hay en el vault. Criterio del secreto (clave para re-fijar sin exponerlo): si el secreto
    /// entrante viene vacio, omitido o es el centinela <see cref="KeepSecretSentinel"/>, se CONSERVA
    /// el del vault; solo un secreto no vacio y distinto del centinela lo rota. clientId y hub siempre
    /// se aplican (recortados). Idempotente.
    /// </summary>
    /// <param name="clientId">ClientId entrante (obligatorio).</param>
    /// <param name="hubUrl">URL del hub entrante (obligatorio).</param>
    /// <param name="incomingSecret">Secreto nuevo, o null/vacio/centinela para conservar el actual.</param>
    /// <param name="existing">Identidad actual del vault (usar <see cref="AgentConfig.Empty"/> si no hay).</param>
    public static AgentConfig Merge(string clientId, string hubUrl, string? incomingSecret, AgentConfig existing)
    {
        var trimmedIncoming = incomingSecret?.Trim() ?? string.Empty;
        var keep = trimmedIncoming.Length == 0
                || string.Equals(trimmedIncoming, KeepSecretSentinel, StringComparison.Ordinal);
        var secret = keep ? existing.Secret : trimmedIncoming;
        return new AgentConfig((clientId ?? string.Empty).Trim(), (hubUrl ?? string.Empty).Trim(), secret);
    }
}
