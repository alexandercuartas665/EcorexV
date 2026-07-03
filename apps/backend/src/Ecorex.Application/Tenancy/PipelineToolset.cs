using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de CIERRE comercial: el agente registra al cliente como un LEAD
/// en el pipeline cuando logra los datos clave. Mapea el canal (tipo de cliente) a la unidad de negocio
/// correcta (por nombre) y lo coloca al inicio del embudo.
/// </summary>
public interface IPipelineToolset : IAgentToolset
{
}

public sealed class PipelineToolset : IPipelineToolset
{
    private readonly ILeadService _leads;
    private readonly IBusinessUnitService _units;
    private readonly IApplicationDbContext _db;

    public PipelineToolset(ILeadService leads, IBusinessUnitService units, IApplicationDbContext db)
    {
        _leads = leads;
        _units = units;
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
        // el cliente (el contacto de la conversacion en curso). Asi un lead nunca queda sin telefono. Igual
        // que el proyecto hermano (default phone = conversacion.ContactPhone).
        if (string.IsNullOrWhiteSpace(telefono) && AiToolRunContext.ConversationId is Guid convId)
        {
            telefono = await _db.Conversations.AsNoTracking()
                .Where(c => c.Id == convId)
                .Select(c => c.ContactPhone)
                .FirstOrDefaultAsync(ct);
        }
        var resumen = Str(args, "resumen");
        var valor = Dec(args, "valor_estimado");

        var unit = await ResolveUnitAsync(tipo, ct);
        var req = new CreateLeadRequest(nombre!.Trim(), string.IsNullOrWhiteSpace(telefono) ? null : telefono!.Trim(),
            string.IsNullOrWhiteSpace(resumen) ? null : resumen!.Trim(), valor, "COP", StageId: null, BusinessUnitId: unit?.Id);
        var lead = await _leads.CreateAsync(req, actor, ct);
        if (lead is null) { return Err("No se pudo crear el lead. Verifica que el pipeline tenga al menos una etapa."); }

        // Nota con el detalle capturado, para el asesor que retome el lead.
        if (!string.IsNullOrWhiteSpace(resumen))
        {
            try { await _leads.AddNoteAsync(lead.Id, $"[Agente IA] {resumen!.Trim()}", "yellow", actor, ct); }
            catch { /* la nota es complementaria: si falla, el lead ya quedo creado */ }
        }

        return Ok(new
        {
            ok = true,
            lead_id = lead.Id,
            unidad_negocio = unit?.Name ?? "(sin unidad)",
            etapa = "LEAD",
            mensaje = "Lead registrado en el pipeline. Un asesor lo contactara para continuar."
        });
    }

    // Resuelve la unidad de negocio a partir del canal/tipo de cliente que indico el agente.
    private async Task<BusinessUnitDto?> ResolveUnitAsync(string tipo, CancellationToken ct)
    {
        var units = await _units.ListAsync(includeInactive: false, ct);
        if (units.Count == 0) { return null; }
        var t = Normalize(tipo);
        bool NameHas(BusinessUnitDto u, params string[] keys) => keys.Any(k => Normalize(u.Name).Contains(k));

        if (t.Contains("b2b") || t.Contains("empresa") || t.Contains("mayor") || t.Contains("suministro") || t.Contains("negocio"))
        {
            return units.FirstOrDefault(u => NameHas(u, "b2b", "empresa"));
        }
        if (t.Contains("curso") || t.Contains("formacion") || t.Contains("capacit"))
        {
            return units.FirstOrDefault(u => NameHas(u, "curso"));
        }
        // Producto al detal (uso personal): por defecto para cualquier mencion de producto.
        if (t.Contains("producto") || t.Contains("detal") || t.Contains("retail") || t.Contains("personal"))
        {
            return units.FirstOrDefault(u => NameHas(u, "detal")) ?? units.FirstOrDefault(u => NameHas(u, "producto"));
        }
        return null;
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

    private static string Normalize(string s)
    {
        var n = (s ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }
}
