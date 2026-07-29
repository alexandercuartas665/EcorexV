using System.Text;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Authoring;

/// <summary>
/// Implementacion real de <see cref="IReportSpecGenerator"/>: resuelve el agente de IA del tenant y
/// la cuenta del proveedor (config global del Super Admin), llama al modelo con un prompt que exige un
/// JSON-spec estricto y registra el consumo (AiUsageLog, source "report-authoring"). Espeja el patron
/// de <c>WorkflowAgentInvoker</c> (una pregunta cerrada, una vuelta, sin usar el motor conversacional).
/// El agente y el proveedor viven bajo el filtro global de tenant, asi que no hay fuga cross-tenant.
/// </summary>
public sealed class AiReportSpecGenerator : IReportSpecGenerator
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;

    public AiReportSpecGenerator(
        IApplicationDbContext db,
        ISecretProtector secretProtector,
        IAiProviderClient client,
        IAiUsageService usage)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _usage = usage;
    }

    public async Task<ReportGenerationResult> GenerateAsync(string instruction, string catalogText, CancellationToken ct = default)
    {
        var agent = await _db.AiAgents.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.SortOrder)
            .FirstOrDefaultAsync(ct);
        if (agent is null)
        {
            return new ReportGenerationResult(false, null, "No hay un agente de IA activo en el tenant para generar el reporte.");
        }

        var providerCfg = await _db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == agent.Provider, ct);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return new ReportGenerationResult(false, null, $"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try
        {
            apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted);
        }
        catch
        {
            return new ReportGenerationResult(false, null, $"La API key del proveedor {agent.Provider} no se pudo descifrar.");
        }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(instruction, catalogText);

        AiChatResult response;
        try
        {
            response = await _client.CompleteAsync(
                agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt,
                [new AiChatTurn("user", userPrompt)], ct);
        }
        catch (Exception ex)
        {
            await SafeRecord(agent.Id, agent.Provider, model, 0, 0, success: false, ct);
            return new ReportGenerationResult(false, null, $"Error llamando al proveedor {agent.Provider}: {ex.Message}");
        }

        await SafeRecord(agent.Id, agent.Provider, model, response.InputTokens, response.OutputTokens, response.Ok, ct);

        if (!response.Ok || string.IsNullOrWhiteSpace(response.Text))
        {
            return new ReportGenerationResult(false, null, response.Error ?? "El proveedor de IA no devolvio respuesta.");
        }

        return new ReportGenerationResult(true, response.Text, null);
    }

    private async Task SafeRecord(Guid agentId, Ecorex.Domain.Enums.AiProvider provider, string model, int input, int output, bool success, CancellationToken ct)
    {
        try
        {
            await _usage.RecordAsync(agentId, provider, model, input, output, "report-authoring", success, ct);
        }
        catch
        {
            // El registro de consumo no debe tumbar la autoria; se ignora un fallo del contador.
        }
    }

    private static string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Eres un generador de reportes para un sistema multi-tenant.");
        sb.AppendLine("Tu tarea: convertir la instruccion del usuario en un JSON-spec de reporte.");
        sb.AppendLine("REGLAS ESTRICTAS:");
        sb.AppendLine("- Responde UNICAMENTE con un objeto JSON, sin texto ni bloques de codigo alrededor.");
        sb.AppendLine("- Usa EXACTAMENTE las claves de fuente (sourceKey) y de campo que aparecen en el catalogo.");
        sb.AppendLine("- NUNCA inventes campos, tablas, columnas ni SQL. Si algo no esta en el catalogo, no lo uses.");
        sb.AppendLine("- Para un grafico de barras/torta/linea agrupa por UN campo y agrega (Count/Sum/Avg/Min/Max).");
        sb.AppendLine();
        sb.AppendLine("Esquema del JSON de salida:");
        sb.AppendLine("{");
        sb.AppendLine("  \"title\": string,");
        sb.AppendLine("  \"sourceKey\": string,                     // una clave del catalogo");
        sb.AppendLine("  \"chart\": \"Table\"|\"Bar\"|\"Pie\"|\"Line\",");
        sb.AppendLine("  \"fields\": [string],                       // para chart=Table");
        sb.AppendLine("  \"groupBy\": [string],                      // 1 campo para agregados");
        sb.AppendLine("  \"aggregates\": [{\"field\": string, \"function\": \"Count\"|\"Sum\"|\"Avg\"|\"Min\"|\"Max\"}],");
        sb.AppendLine("  \"filters\": [{\"field\": string, \"op\": \"Equals\"|\"NotEquals\"|\"Contains\"|\"GreaterThan\"|\"GreaterThanOrEqual\"|\"LessThan\"|\"LessThanOrEqual\"|\"Between\"|\"In\", \"values\": [string]}],");
        sb.AppendLine("  \"sort\": [{\"field\": string, \"desc\": bool}],");
        sb.AppendLine("  \"top\": number|null");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildUserPrompt(string instruction, string catalogText)
    {
        var sb = new StringBuilder();
        sb.AppendLine(catalogText);
        sb.AppendLine();
        sb.AppendLine("INSTRUCCION DEL USUARIO:");
        sb.AppendLine(instruction.Trim());
        sb.AppendLine();
        sb.AppendLine("Devuelve solo el JSON-spec.");
        return sb.ToString();
    }
}
