using System.Text.Json;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Mcp;

/// <summary>
/// CLASIFICACION POR LOTES en el servidor MCP personal (ADR-0197). Capa delgada sobre
/// <see cref="IContenedorClasificacionService"/>: toma hasta N filas de una tabla cuya columna marca esta vacia, las
/// clasifica con un agente de IA y escribe las columnas de salida + la marca por la columna clave. El usuario (o su IA)
/// la repite lote a lote mirando 'restantes'. PERMISO: editar contenedores de datos (misma matriz que la pantalla).
/// Consume tokens del proveedor del agente (queda en AiUsageLog con fuente "clasificar-lote").
/// </summary>
public sealed partial class TableroMcpToolset
{
    private static readonly HashSet<string> ClassifyToolNames = new(StringComparer.Ordinal) { "clasificar_lote" };

    /// <summary>Se expone solo si estan el runner y el modulo de contenedores (resuelve contenedor/tabla por id o nombre).</summary>
    private bool HasClassify => _clasificador is not null && _dataModel is not null;

    private static readonly AiToolSpec[] ClassifySpecs =
    {
        new("clasificar_lote",
            "Clasifica un LOTE de filas de una tabla con un agente de IA y escribe el resultado en la misma fila. Toma " +
            "hasta 'limite' filas (max 50) cuya 'columna_marca' este vacia, le envia al agente las 'columnas_entrada' " +
            "(por defecto todas menos la marca y las de salida), y por cada fila que el agente devuelva (identificada " +
            "por 'columna_clave') escribe SOLO las 'columnas_salida' y pone la marca con la fecha UTC. Devuelve " +
            "procesados, actualizados, sin_respuesta y 'restantes': repitela hasta que restantes sea 0. El agente debe " +
            "estar ENCENDIDO. Consume tokens de IA. 'contenedor' y 'tabla' = id o nombre; 'agente' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"id o nombre del contenedor"},"tabla":{"type":"string","description":"id o nombre de la tabla"},"agente":{"type":"string","description":"id o nombre del agente de IA que clasifica"},"columna_clave":{"type":"string","description":"columna que identifica la fila (ej. id_proceso)"},"columna_marca":{"type":"string","description":"columna que marca la fila como clasificada (ej. clasificado_en); vacia = pendiente"},"columnas_salida":{"type":"array","items":{"type":"string"},"description":"columnas que el agente puede escribir (ej. [\"sector\"])"},"columnas_entrada":{"type":"array","items":{"type":"string"},"description":"opcional: columnas que se le envian al agente"},"limite":{"type":"integer","minimum":1,"maximum":50}},"required":["contenedor","tabla","agente","columna_clave","columna_marca","columnas_salida"],"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> ExecuteClassifyAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = NeedContainer(perms, PermissionAction.Edit, "clasificar filas de contenedores de datos");
        if (denied is not null) { return Err(denied); }

        var (_, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }

        var agente = Str(args, "agente");
        var clave = Str(args, "columna_clave");
        var marca = Str(args, "columna_marca");
        if (string.IsNullOrWhiteSpace(agente) || string.IsNullOrWhiteSpace(clave) || string.IsNullOrWhiteSpace(marca))
        {
            return Err("Indica agente, columna_clave y columna_marca.");
        }
        var salida = StrList(args, "columnas_salida");
        if (salida.Count == 0) { return Err("Indica las columnas_salida (ej. [\"sector\"])."); }
        var entrada = StrList(args, "columnas_entrada");
        var limite = args.TryGetProperty("limite", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var n) ? n : 25;

        var r = await _clasificador!.ClasificarLoteAsync(new ClasificarLoteRequest(
            table!.Id, agente, clave, marca, salida, entrada.Count > 0 ? entrada : null, limite), ct);
        if (!r.Ok) { return Err(r.Error ?? "No se pudo clasificar el lote."); }
        return Ok(new
        {
            tabla = table.Name,
            agente = r.Agente,
            modelo = r.Modelo,
            procesados = r.Procesados,
            actualizados = r.Actualizados,
            sin_respuesta = r.SinRespuesta,
            ignorados = r.Ignorados,
            restantes = r.Restantes,
            tokens = new { entrada = r.InputTokens, salida = r.OutputTokens },
            aviso = r.Procesados == 0 ? "No hay filas pendientes (todas tienen la marca o no tienen clave)." : null
        });
    }

    private static List<string> StrList(JsonElement args, string prop)
    {
        if (!args.TryGetProperty(prop, out var v)) { return new List<string>(); }
        if (v.ValueKind == JsonValueKind.String)
        {
            return (v.GetString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        if (v.ValueKind != JsonValueKind.Array) { return new List<string>(); }
        return v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0).ToList();
    }
}
