using System.Text.Json;
using Ecorex.Application.Scraping;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// EXTRACCION DE DATOS ("drones") en el servidor MCP personal (ADR-0159, addendum extraccion). Capa delgada
/// sobre <see cref="IScrapeFlowService"/> (el CRUD de configuracion del modulo /extraccion-datos, 000730): la IA
/// crea un dron (cabecera + URL de arranque), le agrega pasos (los 24 tipos: navegar, extraer, IA, procesar
/// archivos, etc.), define variables (con secretos cifrados por ISecretProtector) y lo programa (intervalo / cron
/// / barrido por tramos, ADR-0171). Corre COMO el usuario del token. Solo CONFIGURACION: el runtime (el sub-agente
/// Navegador) es diferido, igual que en la pantalla.
///
/// PERMISO: la pantalla gobierna este modulo con la policy "ExtraccionDatos.Editar", que en ECOREX solo exige ser
/// usuario del tenant (no esta en la matriz de roles, a diferencia de "flujos"/"directorio"). Por coherencia, estas
/// herramientas quedan disponibles para CUALQUIER portador de un token valido, sin un gate de rol extra; el
/// aislamiento multi-tenant lo garantiza el filtro global (como en todo el MCP).
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const int MaxDrones = 100;
    private const int MaxRuns = 50;

    private static readonly HashSet<string> ExtractionReadOnly = new(StringComparer.Ordinal)
    {
        "listar_drones", "ver_dron", "listar_destinos_extraccion", "listar_reglas_extraccion",
        "listar_corridas", "listar_programaciones"
    };

    private static readonly HashSet<string> ExtractionDestructive = new(StringComparer.Ordinal)
    {
        "eliminar_paso", "eliminar_variable", "eliminar_programacion", "eliminar_dron"
    };

    private static readonly HashSet<string> ExtractionToolNames = new(StringComparer.Ordinal)
    {
        "listar_drones", "ver_dron", "listar_destinos_extraccion", "listar_reglas_extraccion",
        "listar_corridas", "listar_programaciones",
        "crear_dron", "editar_dron", "agregar_paso", "editar_paso", "reordenar_pasos", "definir_variable",
        "guardar_programacion",
        "eliminar_paso", "eliminar_variable", "eliminar_programacion", "eliminar_dron"
    };

    private const string TiposPasoDoc =
        "navegar (ir a una URL, admite {{VAR}}), esperar, clic, inyectar_script, extraer (JS que devuelve filas + " +
        "mapeo), captura, ia (un agente maneja el navegador segun 'instruccion', acotado por topes), " +
        "leer_correo_otp, procesar_archivos, ingesta_newton, cerrar_navegador, crear_actividad, notificar, " +
        "ensamblado (invoca una Regla), ejecutar_sql, api, tabla, variable. La config fina de cada tipo va en " +
        "'mapeo_json' (ver la pantalla /extraccion-datos).";

    private static readonly AiToolSpec[] ExtractionSpecs =
    {
        new("listar_drones",
            "Lista los drones (flujos) de extraccion de datos del tenant: id, nombre, estado (activo/inactivo/error), " +
            "numero de pasos, contenedor destino y ultima corrida. Usala primero para conocer ids y nombres exactos.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_dron",
            "Muestra un dron completo: cabecera (nombre, URL de arranque, estado, contenedor), sus PASOS en orden " +
            "(id, tipo, nombre y campos), sus VARIABLES (sin revelar secretos) y sus PROGRAMACIONES. 'dron' = id o " +
            "nombre (ver listar_drones).",
            """{"type":"object","properties":{"dron":{"type":"string","description":"id o nombre del dron"}},"required":["dron"],"additionalProperties":false}"""),
        new("listar_destinos_extraccion",
            "Lista las tablas que un dron puede usar como destino de los datos extraidos (etiquetadas 'Modelo / Tabla'). " +
            "Usa el id devuelto en 'contenedor' de crear_dron/editar_dron o 'contenedor_destino' de un paso.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("listar_reglas_extraccion",
            "Lista las reglas de negocio del tenant disponibles para un paso tipo 'ensamblado' (etiquetadas " +
            "'Documento / Regla (verbo)'). Usa el id devuelto en 'regla' de agregar_paso/editar_paso.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("listar_corridas",
            "Bitacora de ejecuciones de un dron, mas recientes primero: cuando corrio, resultado (ok/error/omitida/" +
            "agente_desconectado), disparador (manual/programada), filas insertadas/actualizadas/borradas y detalle. " +
            "'dron' = id o nombre.",
            """{"type":"object","properties":{"dron":{"type":"string"},"limite":{"type":"integer","minimum":1,"maximum":50}},"required":["dron"],"additionalProperties":false}"""),
        new("listar_programaciones",
            "Lista las programaciones de un dron (un dron puede tener varias, ADR-0171): id, nombre, tipo " +
            "(manual/intervalo/cron), cada cuanto, activa y proxima corrida. 'dron' = id o nombre.",
            """{"type":"object","properties":{"dron":{"type":"string"}},"required":["dron"],"additionalProperties":false}"""),
        new("crear_dron",
            "Crea un dron de extraccion NUEVO (cabecera). 'url_inicio' es la pagina de arranque (obligatoria). " +
            "'estado': activo (por defecto) o inactivo. 'contenedor' = id o nombre de la tabla destino " +
            "(ver listar_destinos_extraccion). Despues agrega pasos con agregar_paso y variables con definir_variable. " +
            "Devuelve 'dron_id'.",
            """{"type":"object","properties":{"nombre":{"type":"string"},"descripcion":{"type":"string"},"url_inicio":{"type":"string"},"estado":{"type":"string","enum":["activo","inactivo"]},"contenedor":{"type":"string","description":"id o nombre de la tabla destino"},"pagina_var":{"type":"string","description":"variable de paginacion (opcional)"},"pagina_desde":{"type":"integer"},"pagina_hasta":{"type":"integer"}},"required":["nombre","url_inicio"],"additionalProperties":false}"""),
        new("editar_dron",
            "Edita la CABECERA de un dron: cambia solo los campos que envies (nombre, descripcion, url_inicio, estado, " +
            "contenedor, paginacion). 'dron' = id o nombre.",
            """{"type":"object","properties":{"dron":{"type":"string"},"nombre":{"type":"string"},"descripcion":{"type":"string"},"url_inicio":{"type":"string"},"estado":{"type":"string","enum":["activo","inactivo","error"]},"contenedor":{"type":"string"},"pagina_var":{"type":"string"},"pagina_desde":{"type":"integer"},"pagina_hasta":{"type":"integer"}},"required":["dron"],"additionalProperties":false}"""),
        new("agregar_paso",
            "Agrega un paso al final (o en 'orden') de un dron. 'tipo' define que hace el paso: " + TiposPasoDoc + " " +
            "Pasa solo los campos que apliquen al tipo. Devuelve 'paso_id'. 'dron' = id o nombre.",
            """{"type":"object","properties":{"dron":{"type":"string"},"tipo":{"type":"string","enum":["navegar","esperar","clic","inyectar_script","extraer","captura","ia","leer_correo_otp","procesar_archivos","ingesta_newton","cerrar_navegador","crear_actividad","notificar","ensamblado","ejecutar_sql","api","tabla","variable"]},"nombre":{"type":"string"},"orden":{"type":"integer","description":"posicion (si falta, al final)"},"espera_ms":{"type":"integer"},"url":{"type":"string"},"script":{"type":"string"},"selector":{"type":"string"},"instruccion":{"type":"string","description":"para el paso tipo ia"},"contenedor_destino":{"type":"string","description":"id o nombre de la tabla destino de ESTE paso"},"mapeo_json":{"type":"string","description":"config fina del paso en JSON (segun el tipo)"},"out_var":{"type":"string"},"recorre_var":{"type":"string","description":"variable de lista que recorre el bucle"},"fin_bucle":{"type":"boolean"},"pagina_var":{"type":"string"},"pagina_siguiente":{"type":"boolean"},"ingesta_ruta":{"type":"string"},"ingesta_llave":{"type":"string"},"regla":{"type":"string","description":"id o etiqueta de la regla (paso ensamblado)"},"regla_input_var":{"type":"string"},"ia_proveedor_id":{"type":"string"},"ia_modelo":{"type":"string"},"tope_pasos":{"type":"integer"},"tope_segundos":{"type":"integer","description":"Segundos que el servidor espera a este paso antes de cancelar la corrida (navegar 30, extraer 20, esperar = su espera; manda el mayor). Subelo en portales lentos (login o consulta pesada). En el paso de IA es su tope total."},"aviso_etiqueta":{"type":"string"},"aviso_accion":{"type":"string","enum":["ninguna","notificar","detener"]},"tool_allowlist_json":{"type":"string"}},"required":["dron","tipo","nombre"],"additionalProperties":false}"""),
        new("editar_paso",
            "Edita un paso existente: cambia solo los campos que envies (los demas se conservan). 'paso_id' de ver_dron. " +
            "Puedes cambiar el 'tipo'. Para los campos que admite ver agregar_paso.",
            """{"type":"object","properties":{"paso_id":{"type":"string"},"tipo":{"type":"string"},"nombre":{"type":"string"},"orden":{"type":"integer"},"espera_ms":{"type":"integer"},"url":{"type":"string"},"script":{"type":"string"},"selector":{"type":"string"},"instruccion":{"type":"string"},"contenedor_destino":{"type":"string"},"mapeo_json":{"type":"string"},"out_var":{"type":"string"},"recorre_var":{"type":"string"},"fin_bucle":{"type":"boolean"},"pagina_var":{"type":"string"},"pagina_siguiente":{"type":"boolean"},"ingesta_ruta":{"type":"string"},"ingesta_llave":{"type":"string"},"regla":{"type":"string"},"regla_input_var":{"type":"string"},"ia_proveedor_id":{"type":"string"},"ia_modelo":{"type":"string"},"tope_pasos":{"type":"integer"},"tope_segundos":{"type":"integer","description":"Segundos que el servidor espera a este paso antes de cancelar la corrida (navegar 30, extraer 20, esperar = su espera; manda el mayor). Subelo en portales lentos (login o consulta pesada). En el paso de IA es su tope total."},"aviso_etiqueta":{"type":"string"},"aviso_accion":{"type":"string","enum":["ninguna","notificar","detener"]},"tool_allowlist_json":{"type":"string"}},"required":["paso_id"],"additionalProperties":false}"""),
        new("reordenar_pasos",
            "Reordena los pasos de un dron segun la lista de ids que envies (de primero a ultimo). Deben ser TODOS los " +
            "pasos del dron. 'dron' = id o nombre; 'pasos' = lista de paso_id (ver ver_dron).",
            """{"type":"object","properties":{"dron":{"type":"string"},"pasos":{"type":"array","items":{"type":"string"}}},"required":["dron","pasos"],"additionalProperties":false}"""),
        new("definir_variable",
            "Crea o actualiza (por nombre) una variable del dron. 'secreta'=true la cifra y no se podra volver a leer en " +
            "claro. 'pedir_al_correr'=true hace que se pregunte su valor en cada corrida (con 'prompt'). Si editas una " +
            "secreta sin 'valor', conserva el valor actual. 'dron' = id o nombre.",
            """{"type":"object","properties":{"dron":{"type":"string"},"nombre":{"type":"string"},"valor":{"type":"string"},"secreta":{"type":"boolean"},"pedir_al_correr":{"type":"boolean"},"prompt":{"type":"string"}},"required":["dron","nombre"],"additionalProperties":false}"""),
        new("guardar_programacion",
            "Crea (sin 'id') o actualiza una programacion del dron. 'tipo': manual (solo bajo demanda), intervalo (cada " +
            "'cada_minutos') o cron ('cron' de 5 campos en hora del tenant). 'activa' la enciende. Opcional 'tramos' = " +
            "barrido por tramos (ADR-0171): corre el dron varias veces seguidas moviendo dos variables (p.ej. DIAS_DESDE/" +
            "DIAS_ATRAS). Devuelve id y proxima corrida. 'dron' = id o nombre.",
            """{"type":"object","properties":{"dron":{"type":"string"},"id":{"type":"string","description":"para editar una existente"},"nombre":{"type":"string"},"tipo":{"type":"string","enum":["manual","intervalo","cron"]},"cada_minutos":{"type":"integer","minimum":1},"cron":{"type":"string"},"activa":{"type":"boolean"},"tramos":{"type":"object","properties":{"var_desde":{"type":"string"},"var_largo":{"type":"string"},"desde":{"type":"integer"},"hasta":{"type":"integer"},"paso":{"type":"integer"},"pausa_min":{"type":"integer"},"reintento_min":{"type":"integer"},"reintentos":{"type":"integer"},"max_horas":{"type":"integer"}},"additionalProperties":false}},"required":["dron","tipo","activa"],"additionalProperties":false}"""),
        new("eliminar_paso",
            "Borra un paso de un dron. 'paso_id' de ver_dron. Confirma con el usuario antes de usarla.",
            """{"type":"object","properties":{"paso_id":{"type":"string"}},"required":["paso_id"],"additionalProperties":false}"""),
        new("eliminar_variable",
            "Borra una variable de un dron. 'variable_id' de ver_dron. Confirma con el usuario antes de usarla.",
            """{"type":"object","properties":{"variable_id":{"type":"string"}},"required":["variable_id"],"additionalProperties":false}"""),
        new("eliminar_programacion",
            "Borra una programacion de un dron. 'programacion_id' de ver_dron o listar_programaciones. Confirma con el " +
            "usuario antes de usarla.",
            """{"type":"object","properties":{"programacion_id":{"type":"string"}},"required":["programacion_id"],"additionalProperties":false}"""),
        new("eliminar_dron",
            "Borra un dron completo (cabecera, pasos, variables y programaciones). 'dron' = id o nombre. Es destructiva: " +
            "confirma con el usuario antes de usarla.",
            """{"type":"object","properties":{"dron":{"type":"string"}},"required":["dron"],"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> ExecuteExtractionAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_scrape is null) { return Err("La extraccion de datos no esta disponible."); }

        try
        {
            return tool switch
            {
                "listar_drones" => await ListDronesAsync(ct),
                "ver_dron" => await ViewDronAsync(args, ct),
                "listar_destinos_extraccion" => await ListExtractionTargetsAsync(ct),
                "listar_reglas_extraccion" => await ListExtractionRulesAsync(ct),
                "listar_corridas" => await ListDronRunsAsync(args, ct),
                "listar_programaciones" => await ListDronSchedulesAsync(args, ct),
                "crear_dron" => await CreateDronAsync(args, actor, ct),
                "editar_dron" => await EditDronAsync(args, actor, ct),
                "agregar_paso" => await AddDronStepAsync(args, actor, ct),
                "editar_paso" => await EditDronStepAsync(args, actor, ct),
                "reordenar_pasos" => await ReorderDronStepsAsync(args, actor, ct),
                "definir_variable" => await DefineDronVariableAsync(args, actor, ct),
                "guardar_programacion" => await SaveDronScheduleAsync(args, actor, ct),
                "eliminar_paso" => await DeleteDronStepAsync(args, actor, ct),
                "eliminar_variable" => await DeleteDronVariableAsync(args, actor, ct),
                "eliminar_programacion" => await DeleteDronScheduleAsync(args, actor, ct),
                "eliminar_dron" => await DeleteDronAsync(args, actor, ct),
                _ => Err($"Herramienta desconocida: {tool}")
            };
        }
        catch (InvalidOperationException ex) { return Err(ex.Message); }
    }

    // ===================== Lectura =====================

    private async Task<McpToolResult> ListDronesAsync(CancellationToken ct)
    {
        var list = await _scrape!.ListAsync(ct);
        return Ok(new
        {
            drones = list.Select(f => new
            {
                dron_id = f.Id,
                nombre = f.Name,
                estado = StatusEs(f.Status),
                pasos = f.StepCount,
                cliente = f.ClientName,
                contenedor = f.ContainerName,
                ultima_corrida = f.LastRunAt,
                ultimo_resultado = f.LastResultSummary
            })
        });
    }

    private async Task<McpToolResult> ViewDronAsync(JsonElement args, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        var schedules = await _scrape!.ListSchedulesAsync(flow!.Id, ct);
        return Ok(new
        {
            dron_id = flow.Id,
            nombre = flow.Name,
            descripcion = flow.Description,
            url_inicio = flow.StartUrl,
            estado = StatusEs(flow.Status),
            contenedor_id = flow.ContainerId,
            contenedor = flow.ContainerName,
            cliente = flow.ClientName,
            paginacion = flow.PageVar is null ? null : new { variable = flow.PageVar, desde = flow.PageFrom, hasta = flow.PageTo },
            ultima_corrida = flow.LastRunAt,
            pasos = flow.Steps.OrderBy(s => s.Order).Select(s => new
            {
                paso_id = s.Id,
                orden = s.Order,
                tipo = StepKindEs(s.Kind),
                nombre = s.Name,
                espera_ms = s.WaitMs,
                tope_segundos = s.MaxSeconds,
                url = s.Url,
                selector = s.Selector,
                instruccion = s.Instruction,
                contenedor_destino = s.TargetContainerId,
                out_var = s.OutputVar,
                recorre_var = s.LoopOverVar,
                fin_bucle = s.IsLoopEnd,
                regla_id = s.RuleId,
                tiene_script = !string.IsNullOrWhiteSpace(s.Script),
                tiene_mapeo = !string.IsNullOrWhiteSpace(s.MappingJson)
            }),
            variables = flow.Variables.Select(v => new
            {
                variable_id = v.Id,
                nombre = v.Name,
                secreta = v.IsSecret,
                tiene_valor = v.HasValue,
                valor = v.IsSecret ? null : v.Value,
                pedir_al_correr = v.AskOnRun
            }),
            programaciones = schedules.Select(ScheduleView)
        });
    }

    private async Task<McpToolResult> ListExtractionTargetsAsync(CancellationToken ct)
    {
        var list = await _scrape!.ListContainersAsync(ct);
        return Ok(new { destinos = list.Select(c => new { id = c.Id, etiqueta = c.Label }) });
    }

    private async Task<McpToolResult> ListExtractionRulesAsync(CancellationToken ct)
    {
        var list = await _scrape!.ListRulesAsync(ct);
        return Ok(new { reglas = list.Select(r => new { id = r.Id, etiqueta = r.Label }) });
    }

    private async Task<McpToolResult> ListDronRunsAsync(JsonElement args, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        var take = Math.Clamp(Int(args, "limite") ?? 10, 1, MaxRuns);
        var runs = await _scrape!.ListRunsAsync(flow!.Id, take, ct);
        return Ok(new
        {
            dron_id = flow.Id,
            corridas = runs.Select(r => new
            {
                id = r.Id,
                disparada = r.FiredAt,
                termino = r.FinishedAt,
                disparador = r.Trigger == ImportRunTrigger.Scheduled ? "programada" : "manual",
                resultado = RunResultEs(r.Result),
                pasos = r.StepCount,
                insertados = r.Inserted,
                actualizados = r.Updated,
                borrados = r.Deleted,
                detalle = r.Detail
            })
        });
    }

    private async Task<McpToolResult> ListDronSchedulesAsync(JsonElement args, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        var schedules = await _scrape!.ListSchedulesAsync(flow!.Id, ct);
        return Ok(new { dron_id = flow.Id, programaciones = schedules.Select(ScheduleView) });
    }

    // ===================== Escritura =====================

    private async Task<McpToolResult> CreateDronAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var nombre = Str(args, "nombre");
        var url = Str(args, "url_inicio");
        if (nombre is null) { return Err("Indica el 'nombre' del dron."); }
        if (url is null) { return Err("Indica la 'url_inicio' del dron."); }

        var count = (await _scrape!.ListAsync(ct)).Count;
        if (count >= MaxDrones) { return Err($"Se alcanzo el tope de {MaxDrones} drones; borra alguno antes de crear otro."); }

        var (containerId, cErr) = await ResolveContainerAsync(Str(args, "contenedor"), ct);
        if (cErr is not null) { return Err(cErr); }
        var status = ParseStatus(Str(args, "estado")) ?? ScrapeSourceStatus.Active;

        var dto = await _scrape.SaveFlowAsync(new SaveScrapeFlowRequest(
            Id: null, Name: nombre, Description: Str(args, "descripcion"), StartUrl: url, Status: status,
            ClientId: null, ContainerId: containerId,
            PageVar: Str(args, "pagina_var"), PageFrom: Int(args, "pagina_desde"), PageTo: Int(args, "pagina_hasta")),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo crear el dron."); }
        return Ok(new { ok = true, dron_id = dto.Id, nombre = dto.Name, estado = StatusEs(dto.Status) });
    }

    private async Task<McpToolResult> EditDronAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }

        Guid? containerId = flow!.ContainerId;
        if (args.TryGetProperty("contenedor", out _))
        {
            var (cid, cErr) = await ResolveContainerAsync(Str(args, "contenedor"), ct);
            if (cErr is not null) { return Err(cErr); }
            containerId = cid;
        }

        var dto = await _scrape!.SaveFlowAsync(new SaveScrapeFlowRequest(
            Id: flow.Id,
            Name: Str(args, "nombre") ?? flow.Name,
            Description: args.TryGetProperty("descripcion", out _) ? Str(args, "descripcion") : flow.Description,
            StartUrl: Str(args, "url_inicio") ?? flow.StartUrl,
            Status: ParseStatus(Str(args, "estado")) ?? flow.Status,
            ClientId: flow.ClientId,
            ContainerId: containerId,
            PageVar: args.TryGetProperty("pagina_var", out _) ? Str(args, "pagina_var") : flow.PageVar,
            PageFrom: args.TryGetProperty("pagina_desde", out _) ? Int(args, "pagina_desde") : flow.PageFrom,
            PageTo: args.TryGetProperty("pagina_hasta", out _) ? Int(args, "pagina_hasta") : flow.PageTo),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo editar el dron."); }
        return Ok(new { ok = true, dron_id = dto.Id, nombre = dto.Name, estado = StatusEs(dto.Status) });
    }

    private async Task<McpToolResult> AddDronStepAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' del paso."); }
        var kind = ParseStepKind(Str(args, "tipo"));
        if (kind is null) { return Err($"Tipo de paso desconocido. Tipos validos: {TiposPasoDoc}"); }

        var (containerId, cErr) = await ResolveContainerAsync(Str(args, "contenedor_destino"), ct);
        if (cErr is not null) { return Err(cErr); }
        var (ruleId, rErr) = await ResolveRuleAsync(Str(args, "regla"), ct);
        if (rErr is not null) { return Err(rErr); }

        int order = Int(args, "orden") ?? await NextStepOrderAsync(flow!.Id, ct);

        var dto = await _scrape!.SaveStepAsync(new SaveScrapeStepRequest(
            Id: null, FlowId: flow!.Id, Order: order, Kind: kind.Value, Name: nombre,
            WaitMs: Int(args, "espera_ms"), Url: Str(args, "url"), Script: Str(args, "script"),
            Selector: Str(args, "selector"), MappingJson: Str(args, "mapeo_json"), Instruction: Str(args, "instruccion"),
            TargetContainerId: containerId, ToolAllowListJson: Str(args, "tool_allowlist_json"),
            MaxSteps: Int(args, "tope_pasos"), MaxSeconds: Int(args, "tope_segundos"),
            AiProviderId: Guid2(args, "ia_proveedor_id"), AiModel: Str(args, "ia_modelo"),
            WarningLabel: Str(args, "aviso_etiqueta"), WarningAction: ParseWarningAction(Str(args, "aviso_accion")),
            OutputVar: Str(args, "out_var"), LoopOverVar: Str(args, "recorre_var"), IsLoopEnd: Bool(args, "fin_bucle") ?? false,
            IngestPath: Str(args, "ingesta_ruta"), IngestKeyColumn: Str(args, "ingesta_llave"),
            PageCountVar: Str(args, "pagina_var"), IsPageNext: Bool(args, "pagina_siguiente") ?? false,
            RuleId: ruleId, RuleInputVar: Str(args, "regla_input_var")),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo agregar el paso (el dron no existe)."); }
        return Ok(new { ok = true, paso_id = dto.Id, orden = dto.Order, tipo = StepKindEs(dto.Kind), nombre = dto.Name });
    }

    private async Task<McpToolResult> EditDronStepAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var stepId = Guid2(args, "paso_id");
        if (stepId is null) { return Err("Indica el 'paso_id' (ver ver_dron)."); }
        var step = await _db.ScrapeSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step is null) { return Err("No encontre ese paso."); }

        var kind = step.Kind;
        if (args.TryGetProperty("tipo", out _))
        {
            var parsed = ParseStepKind(Str(args, "tipo"));
            if (parsed is null) { return Err($"Tipo de paso desconocido. Tipos validos: {TiposPasoDoc}"); }
            kind = parsed.Value;
        }

        Guid? containerId = step.TargetContainerId;
        if (args.TryGetProperty("contenedor_destino", out _))
        {
            var (cid, cErr) = await ResolveContainerAsync(Str(args, "contenedor_destino"), ct);
            if (cErr is not null) { return Err(cErr); }
            containerId = cid;
        }
        Guid? ruleId = step.RuleId;
        if (args.TryGetProperty("regla", out _))
        {
            var (rid, rErr) = await ResolveRuleAsync(Str(args, "regla"), ct);
            if (rErr is not null) { return Err(rErr); }
            ruleId = rid;
        }

        var dto = await _scrape!.SaveStepAsync(new SaveScrapeStepRequest(
            Id: step.Id, FlowId: step.FlowId, Order: Int(args, "orden") ?? step.Order, Kind: kind,
            Name: Str(args, "nombre") ?? step.Name,
            WaitMs: args.TryGetProperty("espera_ms", out _) ? Int(args, "espera_ms") : step.WaitMs,
            Url: args.TryGetProperty("url", out _) ? Str(args, "url") : step.Url,
            Script: args.TryGetProperty("script", out _) ? Str(args, "script") : step.Script,
            Selector: args.TryGetProperty("selector", out _) ? Str(args, "selector") : step.Selector,
            MappingJson: args.TryGetProperty("mapeo_json", out _) ? Str(args, "mapeo_json") : step.MappingJson,
            Instruction: args.TryGetProperty("instruccion", out _) ? Str(args, "instruccion") : step.Instruction,
            TargetContainerId: containerId,
            ToolAllowListJson: args.TryGetProperty("tool_allowlist_json", out _) ? Str(args, "tool_allowlist_json") : step.ToolAllowListJson,
            MaxSteps: args.TryGetProperty("tope_pasos", out _) ? Int(args, "tope_pasos") : step.MaxSteps,
            MaxSeconds: args.TryGetProperty("tope_segundos", out _) ? Int(args, "tope_segundos") : step.MaxSeconds,
            AiProviderId: args.TryGetProperty("ia_proveedor_id", out _) ? Guid2(args, "ia_proveedor_id") : step.AiProviderId,
            AiModel: args.TryGetProperty("ia_modelo", out _) ? Str(args, "ia_modelo") : step.AiModel,
            WarningLabel: args.TryGetProperty("aviso_etiqueta", out _) ? Str(args, "aviso_etiqueta") : step.WarningLabel,
            WarningAction: args.TryGetProperty("aviso_accion", out _) ? ParseWarningAction(Str(args, "aviso_accion")) : step.WarningAction,
            OutputVar: args.TryGetProperty("out_var", out _) ? Str(args, "out_var") : step.OutputVar,
            LoopOverVar: args.TryGetProperty("recorre_var", out _) ? Str(args, "recorre_var") : step.LoopOverVar,
            IsLoopEnd: Bool(args, "fin_bucle") ?? step.IsLoopEnd,
            IngestPath: args.TryGetProperty("ingesta_ruta", out _) ? Str(args, "ingesta_ruta") : step.IngestPath,
            IngestKeyColumn: args.TryGetProperty("ingesta_llave", out _) ? Str(args, "ingesta_llave") : step.IngestKeyColumn,
            PageCountVar: args.TryGetProperty("pagina_var", out _) ? Str(args, "pagina_var") : step.PageCountVar,
            IsPageNext: Bool(args, "pagina_siguiente") ?? step.IsPageNext,
            RuleId: ruleId,
            RuleInputVar: args.TryGetProperty("regla_input_var", out _) ? Str(args, "regla_input_var") : step.RuleInputVar),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo editar el paso."); }
        return Ok(new { ok = true, paso_id = dto.Id, orden = dto.Order, tipo = StepKindEs(dto.Kind), nombre = dto.Name });
    }

    private async Task<McpToolResult> ReorderDronStepsAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        if (!args.TryGetProperty("pasos", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Err("Envia 'pasos' como lista de paso_id en el orden deseado.");
        }
        var ids = new List<Guid>();
        foreach (var e in arr.EnumerateArray())
        {
            if (Guid.TryParse(e.GetString(), out var g)) { ids.Add(g); }
            else { return Err($"paso_id invalido en la lista: '{e.GetString()}'."); }
        }
        if (ids.Count == 0) { return Err("La lista 'pasos' esta vacia."); }
        var done = await _scrape!.ReorderStepsAsync(flow!.Id, ids, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, dron_id = flow.Id, pasos = ids.Count }) : Err("No se pudo reordenar (revisa que los ids sean de este dron).");
    }

    private async Task<McpToolResult> DefineDronVariableAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' de la variable."); }

        // Upsert por nombre: si ya existe una variable con ese nombre, la actualiza.
        var existing = flow!.Variables.FirstOrDefault(v => string.Equals(v.Name, nombre, StringComparison.OrdinalIgnoreCase));
        var dto = await _scrape!.SaveVariableAsync(new SaveScrapeVariableRequest(
            Id: existing?.Id, FlowId: flow.Id, Name: nombre, Value: Str(args, "valor"),
            IsSecret: Bool(args, "secreta") ?? existing?.IsSecret ?? false,
            AskOnRun: Bool(args, "pedir_al_correr") ?? existing?.AskOnRun ?? false,
            Prompt: Str(args, "prompt")),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo guardar la variable."); }
        return Ok(new { ok = true, variable_id = dto.Id, nombre = dto.Name, secreta = dto.IsSecret, tiene_valor = dto.HasValue });
    }

    private async Task<McpToolResult> SaveDronScheduleAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        var kind = ParseScheduleKind(Str(args, "tipo"));
        if (kind is null) { return Err("'tipo' debe ser manual, intervalo o cron."); }

        FlowSequenceSpec? seq = null;
        if (args.TryGetProperty("tramos", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            seq = new FlowSequenceSpec
            {
                VarDesde = Str(t, "var_desde") ?? "DIAS_DESDE",
                VarLargo = Str(t, "var_largo") ?? "DIAS_ATRAS",
                Desde = Int(t, "desde") ?? 0,
                Hasta = Int(t, "hasta") ?? 6,
                Paso = Int(t, "paso") ?? 3,
                PausaMin = Int(t, "pausa_min") ?? 5,
                ReintentoMin = Int(t, "reintento_min") ?? 20,
                Reintentos = Int(t, "reintentos") ?? 1,
                MaxHoras = Int(t, "max_horas") ?? 0
            };
        }

        var dto = await _scrape!.SaveScheduleAsync(new SaveScrapeScheduleRequest(
            FlowId: flow!.Id, Kind: kind.Value,
            IntervalMinutes: Int(args, "cada_minutos"), CronExpression: Str(args, "cron"),
            IsActive: Bool(args, "activa") ?? true, Id: Guid2(args, "id"), Name: Str(args, "nombre"), Sequence: seq),
            actor.PlatformUserId, ct);
        return Ok(new { ok = true, programacion_id = dto.Id, nombre = dto.Name, tipo = ScheduleKindEs(dto.Kind), activa = dto.IsActive, proxima_corrida = dto.NextRunAt, aviso = dto.DisabledReason });
    }

    // ===================== Destructivas =====================

    private async Task<McpToolResult> DeleteDronStepAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var stepId = Guid2(args, "paso_id");
        if (stepId is null) { return Err("Indica el 'paso_id' (ver ver_dron)."); }
        var done = await _scrape!.DeleteStepAsync(stepId.Value, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, paso_id = stepId.Value, borrado = true }) : Err("No encontre ese paso.");
    }

    private async Task<McpToolResult> DeleteDronVariableAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var varId = Guid2(args, "variable_id");
        if (varId is null) { return Err("Indica el 'variable_id' (ver ver_dron)."); }
        var done = await _scrape!.DeleteVariableAsync(varId.Value, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, variable_id = varId.Value, borrado = true }) : Err("No encontre esa variable.");
    }

    private async Task<McpToolResult> DeleteDronScheduleAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var schedId = Guid2(args, "programacion_id");
        if (schedId is null) { return Err("Indica el 'programacion_id' (ver listar_programaciones)."); }
        var done = await _scrape!.DeleteScheduleAsync(schedId.Value, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, programacion_id = schedId.Value, borrado = true }) : Err("No encontre esa programacion.");
    }

    private async Task<McpToolResult> DeleteDronAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (flow, error) = await ResolveDronAsync(Str(args, "dron"), ct);
        if (error is not null) { return Err(error); }
        var done = await _scrape!.DeleteFlowAsync(flow!.Id, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, dron_id = flow.Id, borrado = true }) : Err("No se pudo borrar el dron.");
    }

    // ===================== Helpers =====================

    private async Task<(ScrapeFlowDto? flow, string? error)> ResolveDronAsync(string? dron, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dron)) { return (null, "Indica el dron (id o nombre)."); }
        if (Guid.TryParse(dron, out var g))
        {
            var one = await _scrape!.GetAsync(g, ct);
            return one is null ? (null, $"No encontre un dron con id '{dron}'.") : (one, null);
        }
        var list = await _scrape!.ListAsync(ct);
        var exact = list.Where(f => string.Equals(f.Name, dron, StringComparison.OrdinalIgnoreCase)).ToList();
        var pick = exact.Count > 0
            ? exact
            : list.Where(f => f.Name.Contains(dron, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pick.Count == 0) { return (null, $"No encontre ningun dron que coincida con '{dron}'."); }
        if (pick.Count > 1) { return (null, $"Hay varios drones que coinciden con '{dron}': {string.Join(", ", pick.Take(5).Select(f => f.Name))}. Usa el id."); }
        var full = await _scrape!.GetAsync(pick[0].Id, ct);
        return full is null ? (null, $"No pude abrir el dron '{dron}'.") : (full, null);
    }

    private async Task<(Guid? id, string? error)> ResolveContainerAsync(string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) { return (null, null); }
        if (Guid.TryParse(value, out var g)) { return (g, null); }
        var list = await _scrape!.ListContainersAsync(ct);
        var match = list.Where(c => string.Equals(c.Label, value, StringComparison.OrdinalIgnoreCase)
            || c.Label.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match.Count == 1) { return (match[0].Id, null); }
        if (match.Count == 0) { return (null, $"No encontre una tabla destino que coincida con '{value}' (ver listar_destinos_extraccion)."); }
        return (null, $"Hay varias tablas destino que coinciden con '{value}'. Usa el id (ver listar_destinos_extraccion).");
    }

    private async Task<(Guid? id, string? error)> ResolveRuleAsync(string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) { return (null, null); }
        if (Guid.TryParse(value, out var g)) { return (g, null); }
        var list = await _scrape!.ListRulesAsync(ct);
        var match = list.Where(r => string.Equals(r.Label, value, StringComparison.OrdinalIgnoreCase)
            || r.Label.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        if (match.Count == 1) { return (match[0].Id, null); }
        if (match.Count == 0) { return (null, $"No encontre una regla que coincida con '{value}' (ver listar_reglas_extraccion)."); }
        return (null, $"Hay varias reglas que coinciden con '{value}'. Usa el id (ver listar_reglas_extraccion).");
    }

    private async Task<int> NextStepOrderAsync(Guid flowId, CancellationToken ct)
    {
        var max = await _db.ScrapeSteps.Where(s => s.FlowId == flowId).Select(s => (int?)s.Order).MaxAsync(ct);
        return (max ?? -1) + 1;
    }

    private static object ScheduleView(ScrapeScheduleDto s) => new
    {
        programacion_id = s.Id,
        nombre = s.Name,
        tipo = ScheduleKindEs(s.Kind),
        cada_minutos = s.IntervalMinutes,
        cron = s.CronExpression,
        activa = s.IsActive,
        proxima_corrida = s.NextRunAt,
        ultima_corrida = s.LastRunAt,
        aviso = s.DisabledReason,
        tramos = s.Sequence is null ? null : new
        {
            var_desde = s.Sequence.VarDesde,
            var_largo = s.Sequence.VarLargo,
            desde = s.Sequence.Desde,
            hasta = s.Sequence.Hasta,
            paso = s.Sequence.Paso
        }
    };

    private static ScrapeStepKind? ParseStepKind(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) { return null; }
        var v = t.Trim();
        if (Enum.TryParse<ScrapeStepKind>(v, ignoreCase: true, out var direct)) { return direct; }
        return v.ToLowerInvariant() switch
        {
            "navegar" => ScrapeStepKind.Navigate,
            "esperar" => ScrapeStepKind.Wait,
            "clic" => ScrapeStepKind.Click,
            "inyectar_script" or "inyectar" => ScrapeStepKind.InjectScript,
            "extraer" => ScrapeStepKind.Extract,
            "captura" => ScrapeStepKind.Screenshot,
            "ia" => ScrapeStepKind.Ai,
            "leer_correo_otp" or "otp" => ScrapeStepKind.LeerCorreoOtp,
            "procesar_archivos" or "procesar" => ScrapeStepKind.ProcesarArchivos,
            "ingesta_newton" or "newton" => ScrapeStepKind.IngestaNewton,
            "cerrar_navegador" => ScrapeStepKind.CerrarNavegador,
            "crear_actividad" => ScrapeStepKind.CrearActividad,
            "notificar" => ScrapeStepKind.Notificar,
            "ensamblado" => ScrapeStepKind.Ensamblado,
            "ejecutar_sql" or "sql" => ScrapeStepKind.EjecutarSql,
            "api" => ScrapeStepKind.Api,
            "tabla" => ScrapeStepKind.Tabla,
            "variable" => ScrapeStepKind.Variable,
            _ => null
        };
    }

    private static string StepKindEs(ScrapeStepKind k) => k switch
    {
        ScrapeStepKind.Navigate => "navegar",
        ScrapeStepKind.Wait => "esperar",
        ScrapeStepKind.Click => "clic",
        ScrapeStepKind.InjectScript => "inyectar_script",
        ScrapeStepKind.Extract => "extraer",
        ScrapeStepKind.Screenshot => "captura",
        ScrapeStepKind.Ai => "ia",
        ScrapeStepKind.LeerCorreoOtp => "leer_correo_otp",
        ScrapeStepKind.ProcesarArchivos => "procesar_archivos",
        ScrapeStepKind.IngestaNewton => "ingesta_newton",
        ScrapeStepKind.CerrarNavegador => "cerrar_navegador",
        ScrapeStepKind.CrearActividad => "crear_actividad",
        ScrapeStepKind.Notificar => "notificar",
        ScrapeStepKind.Ensamblado => "ensamblado",
        ScrapeStepKind.EjecutarSql => "ejecutar_sql",
        ScrapeStepKind.Api => "api",
        _ => k.ToString().ToLowerInvariant()
    };

    private static ScrapeSourceStatus? ParseStatus(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "activo" or "active" => ScrapeSourceStatus.Active,
        "inactivo" or "inactive" => ScrapeSourceStatus.Inactive,
        "error" => ScrapeSourceStatus.Error,
        _ => null
    };

    private static string StatusEs(ScrapeSourceStatus s) => s switch
    {
        ScrapeSourceStatus.Active => "activo",
        ScrapeSourceStatus.Inactive => "inactivo",
        ScrapeSourceStatus.Error => "error",
        _ => s.ToString().ToLowerInvariant()
    };

    private static ImportScheduleKind? ParseScheduleKind(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "manual" => ImportScheduleKind.Manual,
        "intervalo" or "interval" => ImportScheduleKind.Interval,
        "cron" => ImportScheduleKind.Cron,
        _ => null
    };

    private static string ScheduleKindEs(ImportScheduleKind k) => k switch
    {
        ImportScheduleKind.Manual => "manual",
        ImportScheduleKind.Interval => "intervalo",
        ImportScheduleKind.Cron => "cron",
        _ => k.ToString().ToLowerInvariant()
    };

    private static ScrapeWarningAction ParseWarningAction(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "notificar" or "notify" => ScrapeWarningAction.Notify,
        "detener" or "stop" => ScrapeWarningAction.Stop,
        _ => ScrapeWarningAction.None
    };

    private static string RunResultEs(ImportRunResult r) => r switch
    {
        ImportRunResult.Running => "corriendo",
        ImportRunResult.Ok => "ok",
        ImportRunResult.Error => "error",
        ImportRunResult.Skipped => "omitida",
        ImportRunResult.PendingOffline => "agente_desconectado",
        _ => r.ToString().ToLowerInvariant()
    };
}
