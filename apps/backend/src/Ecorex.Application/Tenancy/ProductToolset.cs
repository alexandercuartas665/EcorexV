using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Catalogo de herramientas (function calling / "MCP") de PRODUCTOS y SEDES que el agente de IA puede
/// invocar para responder al cliente: consultar productos (precio, categoria, stock por sede) y listar
/// las sedes del salon. Son herramientas de SOLO LECTURA: el agente no modifica inventario.
/// </summary>
public interface IProductToolset : IAgentToolset
{
}

public sealed class ProductToolset : IProductToolset
{
    private readonly IProductService _products;
    private readonly ISedeService _sedes;

    public ProductToolset(IProductService products, ISedeService sedes)
    {
        _products = products;
        _sedes = sedes;
    }

    public string GroupKey => "productos";
    public string GroupLabel => "Productos y sedes";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "consultar_productos",
            "Consulta los productos a la venta del salon con su precio, categoria y existencias por sede. " +
            "Filtros opcionales: 'busqueda' (texto en nombre/categoria/descripcion), 'sede' (nombre de la sede; " +
            "solo devuelve productos con stock en esa sede) y 'categoria'. NO inventes productos ni precios: " +
            "usa solo lo que devuelve esta herramienta. Indica en que sedes hay disponibilidad.",
            """{"type":"object","properties":{"busqueda":{"type":"string","description":"Texto a buscar en nombre, categoria o descripcion (opcional)"},"sede":{"type":"string","description":"Nombre de la sede para ver solo lo disponible alli (opcional)"},"categoria":{"type":"string","description":"Categoria exacta a filtrar (opcional)"}},"additionalProperties":false}"""),

        new AiToolSpec(
            "consultar_sedes",
            "Lista las sedes (locales) activas del salon con su ciudad, direccion y telefono. Usala para decirle " +
            "al cliente donde queda el salon o en que ciudades hay sede.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
    };

    public async Task<AgendaToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
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
                "consultar_productos" => await ProductsAsync(args, cancellationToken),
                "consultar_sedes" => await SedesAsync(cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgendaToolResult> ProductsAsync(JsonElement args, CancellationToken ct)
    {
        var busqueda = Str(args, "busqueda");
        var sedeName = Str(args, "sede");
        var categoria = Str(args, "categoria");

        // Si pidieron una sede concreta, resolvemos su id para traer solo lo disponible alli (stock > 0).
        Guid? sedeId = null;
        if (!string.IsNullOrWhiteSpace(sedeName))
        {
            var sedes = await _sedes.ListAsync(includeInactive: false, ct);
            var match = sedes.FirstOrDefault(s => Normalize(s.Name) == Normalize(sedeName!))
                ?? sedes.FirstOrDefault(s => Normalize(s.Name).Contains(Normalize(sedeName!)) || Normalize(s.City) == Normalize(sedeName!));
            if (match is null)
            {
                return Err($"No encontre la sede '{sedeName}'. Usa consultar_sedes para ver los nombres exactos.");
            }
            sedeId = match.Id;
        }

        var list = (await _products.ListAsync(sedeId, includeInactive: false, ct)).ToList();

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            var cat = Normalize(categoria!);
            var byCat = list.Where(p => !string.IsNullOrWhiteSpace(p.Category) && Normalize(p.Category!).Contains(cat)).ToList();
            // El agente suele ADIVINAR el nombre de la categoria (p.ej. "Cuidado Capilar" vs "Cuidado del
            // cabello"). Si la coincidencia exacta no encuentra nada pero si hay catalogo, caemos a una
            // busqueda laxa por palabras significativas del termino sobre nombre/categoria/descripcion,
            // para no devolver vacio cuando claramente hay productos afines.
            if (byCat.Count == 0)
            {
                var stop = new HashSet<string> { "para", "cuidado", "producto", "productos", "linea" };
                var words = cat.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 4 && !stop.Contains(w))
                    .ToArray();
                list = words.Length == 0
                    ? byCat
                    : list.Where(p => words.Any(w => HaystackContains(p, w))).ToList();
            }
            else
            {
                list = byCat;
            }
        }
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var q = Normalize(busqueda!);
            // Coincidir por PALABRAS (no por la frase completa): el cliente/agente escribe "shampoo con
            // keratina" pero el nombre es "Shampoo Maclaw Professional" y la keratina esta en la
            // descripcion. Exigimos que cada palabra significativa aparezca en nombre/sku/categoria/desc.
            var stop = new HashSet<string> { "con", "sin", "para", "los", "las", "del", "una", "uno", "unos", "unas", "que", "por", "the" };
            var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !stop.Contains(w))
                .ToArray();
            list = words.Length == 0
                ? list.Where(p => HaystackContains(p, q)).ToList()
                : list.Where(p => words.All(w => HaystackContains(p, w))).ToList();
        }

        var data = list.Take(30).Select(p => new
        {
            id = p.Id,
            nombre = p.Name,
            sku = p.Sku,
            precio = p.Price,
            categoria = p.Category,
            descripcion = p.Description,
            especificaciones_de_uso = p.Specifications,
            stock_total = p.TotalStock,
            disponible_en = p.AvailableAt.Select(s => new { sede = s.SedeName, ciudad = s.City, stock = s.Stock }).ToArray()
        }).ToArray();

        return Ok(new { productos = data, total = data.Length });
    }

    private async Task<AgendaToolResult> SedesAsync(CancellationToken ct)
    {
        var sedes = await _sedes.ListAsync(includeInactive: false, ct);
        var data = sedes.Select(s => new
        {
            id = s.Id,
            nombre = s.Name,
            ciudad = s.City,
            direccion = s.Address,
            telefono = s.Phone
        }).ToArray();
        return Ok(new { sedes = data, total = data.Length });
    }

    // ===== Helpers =====

    private static AgendaToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), BookingCreated: false);
    private static AgendaToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), BookingCreated: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // Una palabra (ya normalizada) aparece en nombre/sku/categoria/descripcion del producto.
    private static bool HaystackContains(ProductDto p, string word) =>
        Normalize(p.Name).Contains(word)
        || (!string.IsNullOrWhiteSpace(p.Sku) && Normalize(p.Sku!).Contains(word))
        || (!string.IsNullOrWhiteSpace(p.Category) && Normalize(p.Category!).Contains(word))
        || (!string.IsNullOrWhiteSpace(p.Description) && Normalize(p.Description!).Contains(word));

    // Minusculas + sin acentos para comparar de forma laxa.
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
