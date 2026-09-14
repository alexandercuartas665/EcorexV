using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de CONTENEDOR DE DATOS (ADR-0103): permite a un agente de IA
/// ESCRIBIR filas en un contenedor del tenant (modelo EAV data_container_rows/cells). Pensada para agentes
/// que EXTRAEN datos estructurados (ej. "Clasificador de productos y precios") y los cargan. Tenant-scoped
/// por el filtro global; resuelve el contenedor por NOMBRE (no por id). Solo INSERTA (no borra ni pisa).
/// </summary>
public interface IContenedorDatosToolset : IAgentToolset { }

public sealed class ContenedorDatosToolset : IContenedorDatosToolset
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ContenedorDatosToolset(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public string GroupKey => "contenedor-datos";
    public string GroupLabel => "Contenedor de datos";

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            "listar_contenedores",
            "Lista los contenedores de datos del tenant con sus columnas. Usala si no sabes el nombre exacto " +
            "del contenedor destino o el nombre de sus columnas antes de cargar filas.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new AiToolSpec(
            "agregar_filas_a_contenedor",
            "Inserta filas en un contenedor de datos del tenant. 'contenedor' es el nombre EXACTO del contenedor " +
            "(ver listar_contenedores); 'filas' es una lista de objetos: cada objeto es una fila donde cada clave " +
            "es el nombre de una COLUMNA del contenedor y su valor el dato. Las claves que no correspondan a una " +
            "columna se ignoran. Devuelve cuantas filas se cargaron.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"Nombre exacto del contenedor destino"},"filas":{"type":"array","items":{"type":"object"},"description":"Lista de filas; cada fila es un objeto columna->valor"}},"required":["contenedor","filas"],"additionalProperties":false}"""),
        new AiToolSpec(
            "cargar_productos",
            "Carga los productos extraidos en el contenedor de datos 'Productos' del tenant. 'productos' es una " +
            "lista de objetos; cada objeto es un producto con sus campos (por ejemplo categoria, marca, nombre, " +
            "referencia, especificaciones, precio, moneda, proveedor, vigencia, pagina, archivo). Cada campo se " +
            "mapea a la columna del mismo nombre; los campos que no sean columnas se ignoran. Incluye 'archivo' " +
            "con el nombre del archivo de origen cuando lo tengas. Devuelve cuantos productos se cargaron.",
            """{"type":"object","properties":{"productos":{"type":"array","items":{"type":"object"},"description":"Lista de productos (objeto campo->valor) a cargar en el contenedor 'Productos'"}},"required":["productos"],"additionalProperties":false}"""),
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
                "listar_contenedores" => await ListContainersAsync(cancellationToken),
                "agregar_filas_a_contenedor" => await AddRowsAsync(Str(args, "contenedor"), GetProp(args, "filas"), cancellationToken),
                // Atajo pedido por el prompt del agente Clasificador: filas -> contenedor fijo "Productos".
                "cargar_productos" => await AddRowsAsync("Productos", GetProp(args, "productos"), cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    private async Task<AgentToolResult> ListContainersAsync(CancellationToken ct)
    {
        var containers = await _db.DataContainers.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        if (containers.Count == 0) { return Ok(new { ok = true, contenedores = Array.Empty<object>() }); }

        var ids = containers.Select(c => c.Id).ToList();
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => ids.Contains(c.ContainerId)
                && c.Type != DataContainerColumnType.Submodel && c.Type != DataContainerColumnType.RelationMany)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.ContainerId, c.Name })
            .ToListAsync(ct);
        var byContainer = cols.GroupBy(c => c.ContainerId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        return Ok(new
        {
            ok = true,
            contenedores = containers.Select(c => new
            {
                nombre = c.Name,
                columnas = byContainer.TryGetValue(c.Id, out var cc) ? cc : new List<string>()
            })
        });
    }

    /// <summary>
    /// Inserta N filas en el contenedor <paramref name="containerName"/> (por nombre, tenant-scoped). Cada item
    /// de <paramref name="filas"/> es un objeto columna->valor; las claves que no sean columnas se ignoran.
    /// Todo se guarda como texto (EAV). Solo inserta; no borra ni actualiza.
    /// </summary>
    private async Task<AgentToolResult> AddRowsAsync(string? containerName, JsonElement? filas, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tenantId) { return Err("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(containerName)) { return Err("Falta el nombre del contenedor (contenedor)."); }
        if (filas is not { } filasEl || filasEl.ValueKind != JsonValueKind.Array || filasEl.GetArrayLength() == 0)
        {
            return Err("No hay filas para cargar: envia una lista de objetos (filas/productos).");
        }

        var name = containerName.Trim();
        var container = await _db.DataContainers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower(), ct);
        if (container is null)
        {
            var nombres = await _db.DataContainers.AsNoTracking().OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(ct);
            return Err($"No existe un contenedor de datos llamado '{name}'. Contenedores disponibles: {string.Join(", ", nombres)}.");
        }

        // Columnas escalares del contenedor (se excluyen Submodel y RelationMany: no tienen celda simple).
        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == container.Id
                && c.Type != DataContainerColumnType.Submodel && c.Type != DataContainerColumnType.RelationMany)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var colByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in columns)
        {
            var key = c.Name.Trim();
            if (!colByName.ContainsKey(key)) { colByName[key] = c.Id; }
        }

        var cargados = 0;
        var celdas = 0;
        foreach (var item in filasEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { continue; }
            var row = new DataContainerRow { TenantId = tenantId, ContainerId = container.Id };
            _db.DataContainerRows.Add(row);
            foreach (var prop in item.EnumerateObject())
            {
                if (!colByName.TryGetValue(prop.Name.Trim(), out var colId)) { continue; } // no es columna: se ignora
                _db.DataContainerCells.Add(new DataContainerCell
                {
                    TenantId = tenantId,
                    RowId = row.Id,
                    ColumnId = colId,
                    Value = CellValue(prop.Value)
                });
                celdas++;
            }
            cargados++;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, cargados, celdas, contenedor = container.Name });
    }

    // El contenedor guarda TODO como texto (EAV): numeros/booleanos se serializan a su texto tal cual.
    private static string? CellValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => v.GetRawText()
    };

    private static JsonElement? GetProp(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) ? v : (JsonElement?)null;

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);
}
