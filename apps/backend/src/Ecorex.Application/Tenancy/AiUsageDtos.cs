using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

public sealed record AgentUsageDto(Guid? AgentId, int Calls, long InputTokens, long OutputTokens, long TotalTokens, decimal EstimatedCostUsd);

public sealed record AiUsageSummaryDto(
    int TotalCalls,
    long TotalTokens,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd,
    IReadOnlyList<AgentUsageDto> ByAgent);

/// <summary>Cupo mensual de tokens de IA del plan del tenant y su consumo del mes en curso.</summary>
public sealed record AiQuotaDto(long MonthlyLimitTokens, long MonthlyUsedTokens, bool Hard)
{
    public bool HasLimit => MonthlyLimitTokens > 0;
    public long Remaining => HasLimit ? Math.Max(0, MonthlyLimitTokens - MonthlyUsedTokens) : 0;
    public bool Exceeded => HasLimit && MonthlyUsedTokens >= MonthlyLimitTokens;
    public int UsedPct => HasLimit ? (int)Math.Min(100, Math.Round(100.0 * MonthlyUsedTokens / MonthlyLimitTokens)) : 0;
}

/// <summary>
/// Modulo de consumo de tokens (capa 3). Punto unico por el que pasa TODO uso de IA del tenant:
/// registra proveedor, modelo, tokens y costo estimado. Provee indicadores globales y por agente.
/// </summary>
public interface IAiUsageService
{
    /// <summary>Registra un consumo de IA. Lo invoca el motor de inferencia tras cada llamada al proveedor.</summary>
    Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default);

    /// <summary>Totales de consumo del tenant: global y desglosado por agente.</summary>
    Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Cupo mensual de tokens (segun el plan) y consumo del mes en curso.</summary>
    Task<AiQuotaDto> GetQuotaAsync(CancellationToken cancellationToken = default);

    /// <summary>Clave del limite de plan para tokens de IA mensuales.</summary>
    public const string MonthlyTokenLimitKey = "max_ai_tokens_monthly";
}

/// <summary>Tarifas aproximadas (USD por 1M tokens) para estimar costo. Resuelve POR MODELO cuando lo conoce
/// (asi Gemini flash no se cobra como pro), y cae a la tarifa por proveedor si el modelo es desconocido.
/// Nota: las tarifas de Claude vienen del catalogo oficial; las de Gemini/OpenAI/DeepSeek son publicas
/// aproximadas y conviene revisarlas si el precio cambia.</summary>
public static class AiCostEstimator
{
    // Fallback por proveedor (gama alta) cuando no se conoce el modelo exacto.
    private static readonly Dictionary<AiProvider, (decimal In, decimal Out)> RatesPerMillion = new()
    {
        [AiProvider.Claude] = (3m, 15m),
        [AiProvider.Gemini] = (1.25m, 10m),
        [AiProvider.ChatGpt] = (2.5m, 10m),
        [AiProvider.DeepSeek] = (0.27m, 1.10m)
    };

    // Tarifa (In, Out) por 1M tokens segun el MODELO. Se evalua de lo mas especifico a lo mas general.
    private static (decimal In, decimal Out) RatesFor(AiProvider provider, string? model)
    {
        var m = (model ?? string.Empty).Trim().ToLowerInvariant();
        if (m.Length > 0)
        {
            // Google Gemini
            if (m.Contains("gemini-2.0-flash") || m.Contains("2.0-flash")) { return (0.10m, 0.40m); }
            if (m.Contains("gemini") && m.Contains("flash")) { return (0.30m, 2.50m); }   // 2.5-flash
            if (m.Contains("gemini")) { return (1.25m, 10m); }                            // 2.5-pro / otros
            // OpenAI
            if (m.Contains("gpt-6-luna")) { return (0.10m, 0.50m); }
            if (m.Contains("4o-mini") || m.Contains("gpt-4o-mini")) { return (0.15m, 0.60m); }
            if (m.Contains("gpt-4o")) { return (2.50m, 10m); }
            // Anthropic Claude (tarifas del catalogo oficial)
            if (m.Contains("haiku")) { return (1m, 5m); }
            if (m.Contains("sonnet")) { return (2m, 10m); }
            if (m.Contains("opus-5-5")) { return (4m, 20m); }
            if (m.Contains("opus")) { return (5m, 25m); }
            if (m.Contains("fable") || m.Contains("mythos")) { return (10m, 50m); }
            // DeepSeek
            if (m.Contains("deepseek-reasoner")) { return (0.55m, 2.19m); }
            if (m.Contains("deepseek")) { return (0.27m, 1.10m); }
        }
        return RatesPerMillion.TryGetValue(provider, out var r) ? r : (0m, 0m);
    }

    /// <summary>Costo estimado usando la tarifa del MODELO (si se conoce) o la del proveedor.</summary>
    public static decimal Estimate(AiProvider provider, string? model, int inputTokens, int outputTokens)
    {
        var r = RatesFor(provider, model);
        return Math.Round((inputTokens * r.In + outputTokens * r.Out) / 1_000_000m, 6);
    }

    /// <summary>Compat: costo por proveedor (sin modelo) -> usa la tarifa de gama alta del proveedor.</summary>
    public static decimal Estimate(AiProvider provider, int inputTokens, int outputTokens)
        => Estimate(provider, null, inputTokens, outputTokens);
}
