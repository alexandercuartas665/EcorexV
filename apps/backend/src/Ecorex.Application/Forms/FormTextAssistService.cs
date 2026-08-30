using System.Text;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>Resultado de "Mejorar con IA": Ok + texto reescrito, o Ok=false + Error legible.</summary>
public sealed record FormTextAssistResult(bool Ok, string? Text, string? Error);

/// <summary>
/// Reescribe/estructura el texto de un campo de formulario con el AI Gateway del tenant (P2#7 SOLDARCO).
/// Generico: cualquier campo de texto puede pedir "mejorar" su contenido. Respeta el proveedor/cuenta del
/// Super Admin y registra el consumo (AiUsageLog, source "form-assist"), igual que la autoria de reportes.
/// </summary>
public interface IFormTextAssistService
{
    Task<FormTextAssistResult> ImproveAsync(string text, string? hint = null, CancellationToken cancellationToken = default);
}

public sealed class FormTextAssistService : IFormTextAssistService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;

    public FormTextAssistService(
        IApplicationDbContext db, ISecretProtector secretProtector,
        IAiProviderClient client, IAiUsageService usage)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _usage = usage;
    }

    public async Task<FormTextAssistResult> ImproveAsync(string text, string? hint = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new FormTextAssistResult(false, null, "Escribe algo primero para poder mejorarlo.");
        }

        var agent = await _db.AiAgents.AsNoTracking()
            .Where(a => a.IsActive).OrderBy(a => a.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (agent is null)
        {
            return new FormTextAssistResult(false, null, "No hay un agente de IA activo en el tenant.");
        }

        var providerCfg = await _db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == agent.Provider, cancellationToken);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return new FormTextAssistResult(false, null, $"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return new FormTextAssistResult(false, null, $"La API key del proveedor {agent.Provider} no se pudo descifrar."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model! : meta.DefaultModel;

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(text, hint);

        AiChatResult response;
        try
        {
            response = await _client.CompleteAsync(
                agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt,
                [new AiChatTurn("user", userPrompt)], cancellationToken);
        }
        catch (Exception ex)
        {
            await SafeRecord(agent.Id, agent.Provider, model, 0, 0, false, cancellationToken);
            return new FormTextAssistResult(false, null, $"Error llamando al proveedor {agent.Provider}: {ex.Message}");
        }

        await SafeRecord(agent.Id, agent.Provider, model, response.InputTokens, response.OutputTokens, response.Ok, cancellationToken);

        if (!response.Ok || string.IsNullOrWhiteSpace(response.Text))
        {
            return new FormTextAssistResult(false, null, response.Error ?? "El proveedor de IA no devolvio respuesta.");
        }
        return new FormTextAssistResult(true, response.Text!.Trim(), null);
    }

    private async Task SafeRecord(Guid agentId, Ecorex.Domain.Enums.AiProvider provider, string model, int input, int output, bool success, CancellationToken ct)
    {
        try { await _usage.RecordAsync(agentId, provider, model, input, output, "form-assist", success, ct); }
        catch { /* el conteo de consumo no debe tumbar la mejora */ }
    }

    private static string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Eres un asistente de redaccion para formularios de negocio.");
        sb.AppendLine("Tu tarea: reescribir y ESTRUCTURAR el texto del usuario para que sea claro, profesional y conciso.");
        sb.AppendLine("REGLAS ESTRICTAS:");
        sb.AppendLine("- Conserva el idioma original y TODOS los datos/hechos; no inventes informacion.");
        sb.AppendLine("- Devuelve UNICAMENTE el texto mejorado, sin encabezados, comillas ni explicaciones.");
        sb.AppendLine("- Manten un tono profesional y directo. Usa vinetas solo si el contenido las pide.");
        return sb.ToString();
    }

    private static string BuildUserPrompt(string text, string? hint)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(hint)) { sb.AppendLine("Contexto del campo: " + hint!.Trim()); sb.AppendLine(); }
        sb.AppendLine("TEXTO A MEJORAR:");
        sb.AppendLine(text.Trim());
        return sb.ToString();
    }
}
