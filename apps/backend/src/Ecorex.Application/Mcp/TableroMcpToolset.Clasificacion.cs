using System.Text.Json;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// CLASIFICACION POR LOTES en el servidor MCP personal (ADR-0197). Capa delgada sobre
/// <see cref="IContenedorClasificacionService"/>: toma hasta N filas de una tabla cuya columna marca esta vacia, las
/// clasifica con un agente de IA y escribe las columnas de salida + la marca por la columna clave. El usuario (o su IA)
/// la repite lote a lote mirando 'restantes'. PERMISO: editar contenedores de datos (misma matriz que la pantalla).
/// Consume tokens del proveedor del agente (queda en AiUsageLog con fuente "clasificar-lote").
///
/// ADR-0199: ademas, la clasificacion como PASO DE LA IMPORTACION (configurar_paso_clasificacion /
/// quitar_paso_clasificacion / ejecutar_clasificacion_ahora): despues de cada corrida ok de la programacion, el
/// worker clasifica solo las filas con la marca vacia. Y limpiar_marca vacia la marca (y las salidas) para
/// reclasificar cuando cambia la taxonomia.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private static readonly HashSet<string> ClassifyToolNames = new(StringComparer.Ordinal) { "clasificar_lote", "limpiar_marca" };

    private static readonly HashSet<string> ClassifyDestructive = new(StringComparer.Ordinal) { "limpiar_marca" };

    private static readonly HashSet<string> ClassifyStepToolNames = new(StringComparer.Ordinal)
    {
        "configurar_paso_clasificacion", "quitar_paso_clasificacion", "ejecutar_clasificacion_ahora"
    };

    /// <summary>El paso de clasificacion vive en la programacion de importacion: requiere su servicio + contenedores.</summary>
    private bool HasClassifyStep => _importClassify is not null && _dataModel is not null && _dataImport is not null;

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
        new("limpiar_marca",
            "VACIA la columna marca (y opcionalmente las columnas de salida) de las filas de una tabla para que se " +
            "vuelvan a clasificar (ej. al cambiar la taxonomia). Sin 'filtro' afecta TODAS las filas; con 'filtro' " +
            "({columna: valor}, igualdad exacta, todas las condiciones) solo las que coinciden. No borra filas ni " +
            "columnas. Confirma con el usuario antes de usarla: reclasificar consume tokens de IA.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"id o nombre del contenedor"},"tabla":{"type":"string","description":"id o nombre de la tabla"},"columna_marca":{"type":"string","description":"columna marca a vaciar (ej. clasificado_en)"},"columnas_salida":{"type":"array","items":{"type":"string"},"description":"opcional: columnas de salida a vaciar tambien (ej. [\"sector\"])"},"filtro":{"type":"object","additionalProperties":{"type":"string"},"description":"opcional: {columna: valor} para limpiar solo esas filas"}},"required":["contenedor","tabla","columna_marca"],"additionalProperties":false}"""),
    };

    private static readonly AiToolSpec[] ClassifyStepSpecs =
    {
        new("configurar_paso_clasificacion",
            "Agrega (o reemplaza) un PASO DE CLASIFICACION con IA a una programacion de importacion: despues de cada " +
            "corrida exitosa, el sistema clasifica automaticamente las filas NUEVAS (las que tienen la 'columna_marca' " +
            "vacia) lote a lote con el agente, hasta no quedar pendientes o llegar a 'max_lotes' (default 20) o al cupo " +
            "de IA del plan; lo que falte queda para la siguiente corrida. 'tabla' por defecto = tabla destino del " +
            "conector (obligatoria si la programacion es de un flujo de extraccion). El agente debe estar ENCENDIDO " +
            "para que clasifique. 'contenedor' = id o nombre; 'programacion' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"id o nombre del contenedor"},"programacion":{"type":"string","description":"id o nombre de la programacion de importacion (o id de la programacion de un flujo)"},"tabla":{"type":"string","description":"opcional: id o nombre de la tabla a clasificar"},"agente":{"type":"string","description":"id o nombre del agente de IA"},"columna_clave":{"type":"string"},"columna_marca":{"type":"string"},"columnas_salida":{"type":"array","items":{"type":"string"}},"columnas_entrada":{"type":"array","items":{"type":"string"}},"limite":{"type":"integer","minimum":1,"maximum":50,"description":"filas por lote (default 50)"},"max_lotes":{"type":"integer","minimum":1,"maximum":200,"description":"tope de lotes por corrida (default 20)"}},"required":["contenedor","programacion","agente","columna_clave","columna_marca","columnas_salida"],"additionalProperties":false}"""),
        new("quitar_paso_clasificacion",
            "Quita el paso de clasificacion con IA de una programacion de importacion (la importacion sigue igual). " +
            "'contenedor' = id o nombre; 'programacion' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"programacion":{"type":"string"}},"required":["contenedor","programacion"],"additionalProperties":false}"""),
        new("ejecutar_clasificacion_ahora",
            "Pide correr YA el paso de clasificacion de una programacion (sin esperar a la siguiente importacion): el " +
            "motor lo toma en menos de un minuto y deja el resultado (lotes, clasificadas, pendientes y por que paro) en " +
            "el detalle de la ultima corrida (listar_corridas_importacion). Consume tokens de IA. 'contenedor' = id o " +
            "nombre; 'programacion' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"programacion":{"type":"string"}},"required":["contenedor","programacion"],"additionalProperties":false}"""),
    };


    private async Task<McpToolResult> ExecuteClassifyAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = NeedContainer(perms, PermissionAction.Edit, "clasificar filas de contenedores de datos");
        if (denied is not null) { return Err(denied); }
        if (tool == "limpiar_marca") { return await ClearMarkAsync(args, ct); }

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

    private async Task<McpToolResult> ClearMarkAsync(JsonElement args, CancellationToken ct)
    {
        var (_, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var marca = Str(args, "columna_marca");
        if (string.IsNullOrWhiteSpace(marca)) { return Err("Indica la columna_marca."); }
        Dictionary<string, string>? filtro = null;
        if (args.TryGetProperty("filtro", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            filtro = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in f.EnumerateObject())
            {
                filtro[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
            }
        }
        var salida = StrList(args, "columnas_salida");
        var r = await _clasificador!.LimpiarMarcaAsync(new LimpiarMarcaRequest(table!.Id, marca, salida, filtro), ct);
        if (!r.Ok) { return Err(r.Error ?? "No se pudo limpiar la marca."); }
        return Ok(new
        {
            ok = true,
            tabla = table.Name,
            filas = r.Filas,
            celdas_vaciadas = r.Celdas,
            aviso = r.Filas == 0 ? "Ninguna fila coincide con el filtro." : "Esas filas vuelven a estar pendientes de clasificar."
        });
    }

    // ===================== Paso de clasificacion en la importacion (ADR-0199) =====================

    private async Task<McpToolResult> ExecuteClassifyStepAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = NeedContainer(perms, PermissionAction.Edit, "configurar la clasificacion de importaciones");
        if (denied is not null) { return Err(denied); }

        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var procKey = Str(args, "programacion");
        var (processId, processName) = await ResolveStepProcessAsync(detail!.Id, procKey, ct);
        if (processId is not Guid pid) { return Err($"No encontre la programacion '{procKey}' en el contenedor '{detail.Name}'."); }

        if (tool == "quitar_paso_clasificacion")
        {
            return await _importClassify!.QuitarAsync(pid, ct)
                ? Ok(new { ok = true, programacion = processName, paso_clasificacion = (object?)null })
                : Err("No se pudo quitar el paso.");
        }
        if (tool == "ejecutar_clasificacion_ahora")
        {
            var e = await _importClassify!.SolicitarAsync(pid, ct);
            return e is not null ? Err(e) : Ok(new
            {
                ok = true,
                programacion = processName,
                aviso = "Clasificacion encolada: el motor la toma en menos de un minuto. Mira el resultado en " +
                        "listar_corridas_importacion (detalle de la ultima corrida)."
            });
        }

        // configurar_paso_clasificacion
        Guid? tableId = null;
        var tablaArg = Str(args, "tabla");
        if (!string.IsNullOrWhiteSpace(tablaArg))
        {
            var t = FindTable(detail, tablaArg);
            if (t is null) { return Err($"No encontre la tabla '{tablaArg}' en el contenedor '{detail.Name}'."); }
            tableId = t.Id;
        }
        var entrada = StrList(args, "columnas_entrada");
        var res = await _importClassify!.ConfigurarAsync(new ConfigurarPasoClasificacionRequest(
            pid, tableId, Str(args, "agente") ?? "", Str(args, "columna_clave") ?? "", Str(args, "columna_marca") ?? "",
            StrList(args, "columnas_salida"), entrada.Count > 0 ? entrada : null,
            Int(args, "limite"), Int(args, "max_lotes")), ct);
        if (!res.Ok) { return Err(res.Error ?? "No se pudo configurar el paso."); }
        return Ok(new
        {
            ok = true,
            programacion = processName,
            tabla = res.Tabla,
            agente = res.AgenteNombre,
            paso_clasificacion = StepView(res.Paso),
            aviso = res.Aviso
        });
    }

    /// <summary>Programacion por id o nombre dentro del contenedor; si no esta y es un id, acepta la programacion de
    /// un FLUJO de extraccion (no cuelga de ningun contenedor).</summary>
    private async Task<(Guid? id, string? name)> ResolveStepProcessAsync(Guid modelId, string? key, CancellationToken ct)
    {
        var p = await ResolveProcessAsync(modelId, key, ct);
        if (p is not null) { return (p.Id, p.Name); }
        if (Guid.TryParse(key, out var g))
        {
            var flow = await _db.ImportProcesses.AsNoTracking()
                .Where(x => x.Id == g && x.FlowId != null).Select(x => new { x.Id, x.Name }).FirstOrDefaultAsync(ct);
            if (flow is not null) { return (flow.Id, flow.Name); }
        }
        return (null, null);
    }

    /// <summary>Vista del paso para el MCP (null si no hay).</summary>
    private static object? StepView(ImportClassifyStep? s) => s is null ? null : new
    {
        tabla_id = s.TableId,
        agente_id = s.Agente,
        columna_clave = s.ColumnaClave,
        columna_marca = s.ColumnaMarca,
        columnas_salida = s.ColumnasSalida,
        columnas_entrada = s.ColumnasEntrada,
        limite = s.Limite,
        max_lotes = s.MaxLotes
    };

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
