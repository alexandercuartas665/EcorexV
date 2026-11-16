using System.Collections.Concurrent;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>Pausa vigente de un proveedor de IA sin cupo/saldo (para mostrarla en Servidores de IA).</summary>
public sealed record AiProviderPause(AiProvider Provider, DateTimeOffset Since, DateTimeOffset Until, string Detail);

/// <summary>
/// Freno por proveedor de IA cuando se queda SIN CUPO o SIN SALDO (2026-11-16). Caso real: la llave de Gemini
/// de plataforma llego a su tope mensual de gasto (HTTP 429 "exceeded its monthly spending cap") y el barrido
/// del despachador del agente re-disparaba cada conversacion cada 2 min durante 17 h: 3.634 errores en la
/// bitacora. Un tope de gasto NO se resuelve reintentando: al detectarlo se PAUSAN todas las llamadas a ese
/// proveedor por <see cref="AiProviderQuotaGuard.PauseFor"/>; vencida la pausa, la siguiente llamada sondea
/// (si sigue sin cupo, se pausa otra vez). Estado en memoria del proceso (singleton): un reinicio solo cuesta
/// una llamada de sondeo. La llave de cada proveedor es unica de plataforma, asi que la clave es el proveedor.
/// </summary>
public interface IAiProviderQuotaGuard
{
    /// <summary>True si el proveedor esta pausado; <paramref name="until"/> = fin de la pausa.</summary>
    bool IsPaused(AiProvider provider, out DateTimeOffset until);

    /// <summary>Pausa el proveedor. Devuelve true solo cuando ARRANCA una caida (estaba sano), para avisar una vez.</summary>
    bool Trip(AiProvider provider, string detail);

    /// <summary>El proveedor respondio bien: termina la caida (la proxima vez que falle se vuelve a avisar).</summary>
    void MarkHealthy(AiProvider provider);

    /// <summary>Pausas vigentes (para el aviso en pantalla).</summary>
    IReadOnlyList<AiProviderPause> ActivePauses();
}

/// <inheritdoc />
public sealed class AiProviderQuotaGuard : IAiProviderQuotaGuard
{
    /// <summary>Duracion de cada pausa. Corta para notar pronto una recarga de saldo; larga frente a un barrido de 2 min.</summary>
    public static readonly TimeSpan PauseFor = TimeSpan.FromMinutes(30);

    private sealed record State(DateTimeOffset Since, DateTimeOffset Until, string Detail);

    private readonly ConcurrentDictionary<AiProvider, State> _states = new();
    private readonly TimeProvider _clock;

    public AiProviderQuotaGuard(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public bool IsPaused(AiProvider provider, out DateTimeOffset until)
    {
        if (_states.TryGetValue(provider, out var s) && s.Until > _clock.GetUtcNow())
        {
            until = s.Until;
            return true;
        }
        until = default;
        return false;
    }

    public bool Trip(AiProvider provider, string detail)
    {
        var now = _clock.GetUtcNow();
        var isNew = true;
        _states.AddOrUpdate(provider,
            _ => new State(now, now + PauseFor, detail),
            (_, old) => { isNew = false; return old with { Until = now + PauseFor, Detail = detail }; });
        return isNew;
    }

    public void MarkHealthy(AiProvider provider) => _states.TryRemove(provider, out _);

    public IReadOnlyList<AiProviderPause> ActivePauses()
    {
        var now = _clock.GetUtcNow();
        return _states.Where(kv => kv.Value.Until > now)
            .Select(kv => new AiProviderPause(kv.Key, kv.Value.Since, kv.Value.Until, kv.Value.Detail))
            .OrderBy(p => p.Provider.ToString())
            .ToList();
    }
}

/// <summary>Clasifica los errores del proveedor que significan "sin cupo/saldo" (no se arreglan reintentando).</summary>
public static class AiQuotaErrors
{
    // Marcas de los proveedores: Gemini (tope de gasto del proyecto), OpenAI ("insufficient_quota", "exceeded your
    // current quota ... billing"), Claude ("credit balance is too low"). NO incluye el limite POR MINUTO de Gemini
    // (tambien RESOURCE_EXHAUSTED): ese si es transitorio y se reintenta.
    private static readonly string[] Markers =
    {
        "spending cap", "insufficient_quota", "exceeded your current quota", "credit balance", "billing"
    };

    /// <summary>True si la respuesta (codigo + cuerpo o texto del error) es de cupo/saldo agotado.</summary>
    public static bool IsQuotaExhausted(int status, string? body) =>
        status is 429 or 402 or 400 or 403 && ContainsMarker(body);

    /// <summary>Igual, a partir del texto de error del cliente ("El proveedor respondio HTTP 429. {cuerpo}").</summary>
    public static bool IsQuotaExhaustedError(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && (error.Contains("HTTP 429", StringComparison.Ordinal) || error.Contains("HTTP 402", StringComparison.Ordinal)
            || error.Contains("HTTP 400", StringComparison.Ordinal) || error.Contains("HTTP 403", StringComparison.Ordinal))
        && ContainsMarker(error);

    private static bool ContainsMarker(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}
