using System.Text.Json;
using Ecorex.Application.Inventory;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de INVENTARIO: permite al agente de IA CONSULTAR (solo
/// lectura) los items del catalogo de inventario del tenant. Reusa <see cref="IItemService"/> y su
/// aislamiento por tenant (query filter). No crea ni modifica nada: es informacion para responder
/// preguntas sobre productos, precios y existencias.
/// </summary>
public interface IInventarioToolset : IAgentToolset { }

public sealed class InventarioToolset : IInventarioToolset
{
    private readonly IItemService _items;

    public InventarioToolset(IItemService items) => _items = items;

    public string GroupKey => "inventario";
    public string GroupLabel => "Inventario";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "consultar_inventario",
            "Consulta (solo lectura) los items del inventario del tenant. Usala para responder preguntas " +
            "sobre productos: existencia, precio, marca, grupo o stock por bodega. Pasa 'busqueda' (nombre, " +
            "SKU o palabra clave); si no la pasas, devuelve los primeros items. Devuelve una lista con " +
            "nombre, SKU, precio, marca, grupo, si esta activo y el stock total y por bodega.",
            """{"type":"object","properties":{"busqueda":{"type":"string","description":"Texto a buscar en nombre o SKU del item (opcional)"},"incluir_inactivos":{"type":"boolean","description":"Incluir items inactivos (por defecto false)"},"pagina":{"type":"integer","minimum":1,"description":"Pagina de resultados (20 por pagina, por defecto 1)"}},"additionalProperties":false}"""),
        new AiToolSpec(
            "ver_item",
            "Devuelve el DETALLE de un item del inventario por su id (el 'item_id' que entrega " +
            "'consultar_inventario'): descripcion, especificaciones, precio, stock por bodega y disponibilidad.",
            """{"type":"object","properties":{"item_id":{"type":"string","description":"Id (GUID) del item, tal como lo devuelve consultar_inventario"}},"required":["item_id"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch { return Err("Los argumentos no son un JSON valido."); }

        try
        {
            return toolName switch
            {
                "consultar_inventario" => await ConsultarAsync(args, cancellationToken),
                "ver_item" => await VerItemAsync(args, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgentToolResult> ConsultarAsync(JsonElement args, CancellationToken ct)
    {
        var busqueda = Str(args, "busqueda");
        var incluirInactivos = Bool(args, "incluir_inactivos") ?? false;
        var pagina = Int(args, "pagina") ?? 1;
        if (pagina < 1) { pagina = 1; }

        var page = await _items.ListAsync(new ItemQuery(
            Search: string.IsNullOrWhiteSpace(busqueda) ? null : busqueda!.Trim(),
            IncludeInactive: incluirInactivos,
            Page: pagina,
            PageSize: 20), ct);

        var items = page.Items.Select(i => new
        {
            item_id = i.Id,
            sku = i.Sku,
            nombre = i.Name,
            precio = i.Price,
            marca = i.BrandName,
            grupo = i.GroupName,
            subgrupo = i.SubgroupName,
            tipo = i.ItemTypeName,
            activo = i.IsActive,
            stock_total = i.TotalStock,
            stock_por_bodega = i.StockByWarehouse.Select(s => new { bodega = s.WarehouseName, cantidad = s.Stock })
        }).ToList();

        return Ok(new
        {
            ok = true,
            total = page.Total,
            pagina = page.Page,
            por_pagina = page.PageSize,
            devueltos = items.Count,
            items
        });
    }

    private async Task<AgentToolResult> VerItemAsync(JsonElement args, CancellationToken ct)
    {
        var raw = Str(args, "item_id");
        if (!Guid.TryParse(raw, out var id)) { return Err("Falta un 'item_id' valido (GUID)."); }

        var d = await _items.GetDetailAsync(id, ct);
        if (d is null) { return Err("No se encontro un item con ese id."); }

        return Ok(new
        {
            ok = true,
            item_id = d.Id,
            sku = d.Sku,
            nombre = d.Name,
            descripcion = d.Description,
            especificaciones = d.Specifications,
            precio = d.Price,
            activo = d.IsActive,
            stock_total = d.TotalStock,
            stock_por_bodega = d.StockByWarehouse.Select(s => new { bodega = s.WarehouseName, cantidad = s.Stock }),
            disponible_en = d.AvailableAt
        });
    }

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool? Bool(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : null;

    private static int? Int(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : null;
}
