using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de CIERRE comercial: el agente registra al cliente cuando logra los
/// datos clave. El toolset NO depende del dominio Lead/CRM; delega el cierre en <see cref="IAgentLeadSink"/>
/// (costura), cuyo adaptador vivo (PipelineLeadSink) mapea el canal a la unidad de negocio y crea el lead en
/// el embudo. Con el sink No-Op el runtime opera sin CRM y no crea nada.
/// </summary>
public interface IPipelineToolset : IAgentToolset
{
}

public sealed class PipelineToolset : IPipelineToolset
{
    private readonly IAgentLeadSink _leadSink;
    private readonly IApplicationDbContext _db;

    public PipelineToolset(IAgentLeadSink leadSink, IApplicationDbContext db)
    {
        _leadSink = leadSink;
        _db = db;
    }

    public string GroupKey => "pipeline";
    public string GroupLabel => "Pipeline comercial (cierre)";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "crear_lead",
            "Registra (CIERRA) al cliente como un LEAD en el pipeline comercial. Usala UNA SOLA VEZ al final del " +
            "guion de cierre, cuando ya tengas el nombre y el canal del cliente. Indica 'tipo_cliente' con el canal o " +
            "unidad de negocio del cliente (ej. 'b2b', 'productos', 'cursos' u otro texto descriptivo). El sistema " +
            "asigna el lead a la unidad de negocio correcta y al inicio del embudo para que un asesor lo contacte.",
            """{"type":"object","properties":{"cliente_nombre":{"type":"string","description":"Nombre del cliente"},"cliente_telefono":{"type":"string","description":"Telefono del cliente (opcional)"},"tipo_cliente":{"type":"string","description":"Canal del cliente (texto descriptivo, ej. b2b, productos, cursos)"},"valor_estimado":{"type":"number","description":"Valor estimado de la venta en pesos (opcional)"},"resumen":{"type":"string","description":"Resumen breve de lo que quiere el cliente: producto, cantidad, curso, servicio, etc."}},"required":["cliente_nombre","tipo_cliente"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch
        {
            return Err("Los argumentos no son un JSON valido.");
        }

        try
        {
            return toolName switch
            {
                "crear_lead" => await CreateLeadAsync(args, actorUserId, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgentToolResult> CreateLeadAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        var nombre = Str(args, "cliente_nombre");
        if (string.IsNullOrWhiteSpace(nombre)) { return Err("Falta el nombre del cliente (cliente_nombre)."); }
        var telefono = Str(args, "cliente_telefono");
        var tipo = Str(args, "tipo_cliente") ?? "";

        // Si el agente no capturo el telefono, usamos por defecto el numero de WhatsApp DESDE EL QUE escribe
        // el cliente (el contacto de la conversacion en curso). Es contexto de la CONVERSACION del agente, no del
        // CRM, por eso se resuelve aqui y se pasa ya resuelto al sink de cierre.
        if (string.IsNullOrWhiteSpace(telefono) && AiToolRunContext.ConversationId is Guid convId)
        {
            telefono = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == convId)
                .Select(c => c.ContactPhone)
                .FirstOrDefaultAsync(ct);
        }
        var resumen = Str(args, "resumen");
        var valor = Dec(args, "valor_estimado");

        var result = await _leadSink.CreateLeadAsync(
            new AgentLeadRequest(nombre!.Trim(), string.IsNullOrWhiteSpace(telefono) ? null : telefono!.Trim(),
                tipo, resumen, valor, "COP"),
            actor, ct);

        if (!result.Ok)
        {
            return Err(result.Error ?? "No se pudo registrar el cierre.");
        }

        return Ok(new
        {
            ok = true,
            lead_id = result.LeadId,
            unidad_negocio = result.BusinessUnitName ?? "(sin unidad)",
            etapa = "LEAD",
            mensaje = result.Message ?? "Lead registrado en el pipeline. Un asesor lo contactara para continuar."
        });
    }

    // ===== Helpers =====

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : (v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null))
            : null;

    private static decimal? Dec(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) { return null; }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) { return d; }
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) { return ds; }
        return null;
    }
}
