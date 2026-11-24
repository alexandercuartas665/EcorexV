using System.Text.Json;
using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Authoring;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// REPORTES e INDICADORES en el servidor MCP personal (ADR-0198). Capa delgada sobre el motor que ya existe:
/// catalogo (<see cref="IReportCatalog"/>), ejecucion tenant-safe (<see cref="IReportDataSource"/>), reportes
/// guardados (<see cref="IReportDefinitionService"/>, ADR-0051) y libros de indicadores
/// (<see cref="IAnalisisService"/> + <see cref="IAnalisisWidgetRunner"/>, ADR-0158).
///
/// FUENTE: cada TABLA raiz de un contenedor de datos ya es su propia fuente del catalogo ("container:{id_tabla}"),
/// asi que un contenedor con dos tablas publica dos fuentes. La herramienta acepta la clave del catalogo, el
/// par contenedor + tabla (id o nombre) o el nombre de la fuente; tambien sirven fuentes nativas y de formulario.
///
/// CONSULTA: la IA habla en NOMBRES de campo; aqui se traducen a las claves del catalogo (en un contenedor la
/// clave es el id de la columna). Antes de guardar se EJECUTA la consulta (reporte) o el widget (indicador): un
/// campo inexistente, una operacion no admitida o un widget que no corre se rechazan sin guardar nada.
///
/// PERMISOS (misma matriz que las pantallas): fuentes = ver reportes o ver analisis; reportes = "reportes/galeria"
/// (ver; "reportes/admin" ve todos) y crear = "reportes/ia" Crear o "reportes/admin" Ver; indicadores = "analisis"
/// (ver; crear/agregar = Crear o Editar). Dueno/Admin del libro edita; el resto lo valida el servicio.
/// v1: solo paneles (Dashboard). Los imprimibles (RDL) se disenian en pantalla.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const string ReportGalleryModule = "reportes/galeria";
    private const string ReportAuthorModule = "reportes/ia";
    private const string ReportAdminModule = "reportes/admin";
    private const string AnalisisModule = "analisis";
    private const int PreviewRows = 10;

    private bool HasReportSources => _reportCatalog is not null;

    private bool HasReports => _reportCatalog is not null && _reportData is not null && _reportDefs is not null;

    private bool HasIndicators => _reportCatalog is not null && _analisis is not null && _widgetRunner is not null;

    private static readonly HashSet<string> ReportSourceToolNames = new(StringComparer.Ordinal)
    {
        "listar_fuentes_reporte", "ver_fuente_reporte"
    };

    private static readonly HashSet<string> ReportToolNames = new(StringComparer.Ordinal)
    {
        "listar_reportes", "ver_reporte", "crear_reporte", "editar_reporte", "archivar_reporte"
    };

    private static readonly HashSet<string> IndicatorToolNames = new(StringComparer.Ordinal)
    {
        "listar_libros_indicadores", "ver_libro_indicadores", "crear_libro_indicadores", "agregar_indicador",
        "eliminar_indicador", "eliminar_libro_indicadores"
    };

    private static readonly HashSet<string> ReportingReadOnly = new(StringComparer.Ordinal)
    {
        "listar_fuentes_reporte", "ver_fuente_reporte", "listar_reportes", "ver_reporte",
        "listar_libros_indicadores", "ver_libro_indicadores"
    };

    /// <summary>Quitan algo (ADR-0206): archivar un reporte (recuperable desde la galeria) y borrar indicadores/libros.</summary>
    private static readonly HashSet<string> ReportingDestructive = new(StringComparer.Ordinal)
    {
        "archivar_reporte", "eliminar_indicador", "eliminar_libro_indicadores"
    };

    private const string FuenteDoc =
        "La fuente se indica con 'fuente' (clave del catalogo, p.ej. container:{id}, o su nombre) o con " +
        "'contenedor' + 'tabla' (id o nombre): cada tabla de un contenedor es una fuente.";

    private const string ConsultaSchema =
        """{"type":"object","description":"consulta sobre la fuente, con NOMBRES de campo (ver_fuente_reporte)","properties":{"campos":{"type":"array","items":{"type":"string"},"description":"modo tabla: campos a mostrar (vacio = todos)"},"filtros":{"type":"array","items":{"type":"object","properties":{"campo":{"type":"string"},"operador":{"type":"string","enum":["igual","distinto","contiene","mayor","mayor_igual","menor","menor_igual","entre","en"]},"valor":{"type":"string"},"valores":{"type":"array","items":{"type":"string"}}},"required":["campo"]}},"agrupar_por":{"type":"string","description":"campo por el que se agrupa (un contenedor admite uno)"},"periodo":{"type":"string","enum":["dia","semana","mes","trimestre","anio"],"description":"si agrupar_por es un campo FECHA, agrupa por ese periodo (ventas por mes) en orden cronologico"},"agregaciones":{"type":"array","items":{"type":"object","properties":{"funcion":{"type":"string","enum":["contar","sumar","promedio","minimo","maximo"]},"campo":{"type":"string","description":"campo numerico (no hace falta para contar)"}},"required":["funcion"]}},"orden":{"type":"array","items":{"type":"object","properties":{"campo":{"type":"string","description":"un campo, o conteo/suma/promedio/minimo/maximo para ordenar por la agregacion"},"desc":{"type":"boolean"}},"required":["campo"]}},"limite":{"type":"integer","minimum":1}},"additionalProperties":false}""";

    private static readonly AiToolSpec[] ReportSourceSpecs =
    {
        new("listar_fuentes_reporte",
            "Lista las FUENTES de datos sobre las que se pueden crear reportes e indicadores: tablas de contenedores " +
            "de datos (una fuente por tabla, con su contenedor), entidades del sistema (tareas, terceros...) y " +
            "modulos de formulario. Devuelve la clave 'fuente' y el numero de campos. 'tipo' opcional filtra: " +
            "contenedor, nativa, formulario, externa.",
            """{"type":"object","properties":{"tipo":{"type":"string","enum":["contenedor","nativa","formulario","externa"]}},"additionalProperties":false}"""),
        new("ver_fuente_reporte",
            "Muestra los CAMPOS de una fuente: nombre, tipo (texto/numero/decimal/fecha/booleano) y si admite filtro, " +
            "agrupacion o suma/promedio (solo numericos). Usala antes de armar una consulta. " + FuenteDoc,
            """{"type":"object","properties":{"fuente":{"type":"string"},"contenedor":{"type":"string"},"tabla":{"type":"string"}},"additionalProperties":false}"""),
    };

    private static readonly AiToolSpec[] ReportSpecs =
    {
        new("listar_reportes",
            "Lista los reportes (paneles e imprimibles) que el usuario puede ver: id, nombre, tipo, fuente y categoria.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_reporte",
            "Ejecuta un reporte guardado y muestra sus columnas, el total de filas y una muestra. 'reporte' = id o nombre.",
            """{"type":"object","properties":{"reporte":{"type":"string"}},"required":["reporte"],"additionalProperties":false}"""),
        new("crear_reporte",
            "Crea un reporte tipo PANEL (galeria de reportes) sobre una fuente. 'grafico': tabla (por defecto), barras, " +
            "torta o linea; barras/torta/linea necesitan 'agrupar_por' en la consulta (si no indicas agregaciones se " +
            "cuenta). Antes de guardar EJECUTA la consulta: si un campo no existe o no admite la operacion, no guarda " +
            "nada y explica por que. Devuelve 'reporte_id', el total de filas y una muestra. " + FuenteDoc +
            " Los imprimibles se disenian en pantalla.",
            $$"""{"type":"object","properties":{"nombre":{"type":"string"},"descripcion":{"type":"string"},"categoria":{"type":"string"},"grafico":{"type":"string","enum":["tabla","barras","torta","linea"]},"fuente":{"type":"string"},"contenedor":{"type":"string"},"tabla":{"type":"string"},"consulta":{{ConsultaSchema}}},"required":["nombre","consulta"],"additionalProperties":false}"""),
        new("editar_reporte",
            "Edita un reporte tipo PANEL creado con crear_reporte. Cambio PARCIAL: solo lo que mandes (nombre, " +
            "categoria, descripcion, grafico y/o consulta; la fuente solo si mandas consulta). Si cambias grafico o " +
            "consulta se EJECUTA antes de guardar, igual que al crear. 'reporte' = id o nombre. Los imprimibles y los " +
            "paneles disenados en pantalla se editan alli.",
            $$"""{"type":"object","properties":{"reporte":{"type":"string"},"nombre":{"type":"string"},"descripcion":{"type":"string"},"categoria":{"type":"string"},"grafico":{"type":"string","enum":["tabla","barras","torta","linea"]},"fuente":{"type":"string"},"contenedor":{"type":"string"},"tabla":{"type":"string"},"consulta":{{ConsultaSchema}}},"required":["reporte"],"additionalProperties":false}"""),
        new("archivar_reporte",
            "ARCHIVA un reporte (sale de la galeria; un administrador lo recupera desde 'Archivados'). Usala para " +
            "quitar reportes de prueba o que quedaron sin fuente. 'reporte' = id o nombre.",
            """{"type":"object","properties":{"reporte":{"type":"string"}},"required":["reporte"],"additionalProperties":false}"""),
    };

    private static readonly AiToolSpec[] IndicatorSpecs =
    {
        new("listar_libros_indicadores",
            "Lista los libros de indicadores que el usuario puede ver: id, nombre, visibilidad, si es dueno, hojas y widgets.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_libro_indicadores",
            "Muestra un libro de indicadores: sus hojas y los widgets de cada una (id, tipo, titulo, fuente, ancho). " +
            "'libro' = id o nombre.",
            """{"type":"object","properties":{"libro":{"type":"string"}},"required":["libro"],"additionalProperties":false}"""),
        new("crear_libro_indicadores",
            "Crea un libro de indicadores NUEVO (privado del usuario, nace con la 'Hoja 1'). Devuelve 'libro_id' y 'hoja_id'.",
            """{"type":"object","properties":{"nombre":{"type":"string"},"descripcion":{"type":"string"}},"required":["nombre"],"additionalProperties":false}"""),
        new("agregar_indicador",
            "Agrega un indicador (widget) a una hoja de un libro. 'tipo': kpi (un numero: una agregacion sin agrupar), " +
            "barras, barras_horizontales, dona, embudo, treemap (agrupan por un campo), linea, area o tabla. 'hoja' = id " +
            "o nombre (por defecto la primera; si el nombre no existe se crea). 'ancho' = 1..4 columnas. Antes de " +
            "guardar EJECUTA el widget: si no corre, no guarda nada y explica por que. Devuelve 'widget_id' y una " +
            "muestra del resultado. " + FuenteDoc,
            $$"""{"type":"object","properties":{"libro":{"type":"string"},"hoja":{"type":"string"},"titulo":{"type":"string"},"tipo":{"type":"string","enum":["kpi","barras","barras_horizontales","dona","embudo","treemap","linea","area","tabla"]},"ancho":{"type":"integer","minimum":1,"maximum":4},"fuente":{"type":"string"},"contenedor":{"type":"string"},"tabla":{"type":"string"},"consulta":{{ConsultaSchema}}},"required":["libro","titulo","tipo","consulta"],"additionalProperties":false}"""),
        new("eliminar_indicador",
            "Quita un indicador (widget) de su libro. 'indicador' = widget_id (ver_libro_indicadores). Solo el dueno del " +
            "libro o un administrador.",
            """{"type":"object","properties":{"indicador":{"type":"string"}},"required":["indicador"],"additionalProperties":false}"""),
        new("eliminar_libro_indicadores",
            "Elimina un libro de indicadores completo (con sus hojas). 'libro' = id o nombre. Solo el dueno o un administrador.",
            """{"type":"object","properties":{"libro":{"type":"string"}},"required":["libro"],"additionalProperties":false}"""),
    };

    private IEnumerable<string> ReportingReadOnlyExposed() =>
        ReportingReadOnly.Where(t => (HasReportSources && ReportSourceToolNames.Contains(t))
            || (HasReports && ReportToolNames.Contains(t))
            || (HasIndicators && IndicatorToolNames.Contains(t)));

    private IEnumerable<string> ReportingDestructiveExposed() =>
        ReportingDestructive.Where(t => (HasReports && ReportToolNames.Contains(t)) || (HasIndicators && IndicatorToolNames.Contains(t)));

    private IEnumerable<AiToolSpec> ReportingSpecsExposed()
    {
        var list = new List<AiToolSpec>();
        if (HasReportSources) { list.AddRange(ReportSourceSpecs); }
        if (HasReports) { list.AddRange(ReportSpecs); }
        if (HasIndicators) { list.AddRange(IndicatorSpecs); }
        return list;
    }

    private bool IsReportingTool(string tool) =>
        (HasReportSources && ReportSourceToolNames.Contains(tool))
        || (HasReports && ReportToolNames.Contains(tool))
        || (HasIndicators && IndicatorToolNames.Contains(tool));

    private async Task<McpToolResult> ExecuteReportingAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var canAuthorIndicators = perms.Can(AnalisisModule, PermissionAction.Create) || perms.Can(AnalisisModule, PermissionAction.Edit);
        var denied = tool switch
        {
            "listar_fuentes_reporte" or "ver_fuente_reporte"
                => perms.Can(ReportGalleryModule, PermissionAction.View) || perms.Can(AnalisisModule, PermissionAction.View)
                    ? null : "Tu usuario no tiene permiso para consultar reportes ni indicadores.",
            "listar_reportes" or "ver_reporte"
                => perms.Can(ReportGalleryModule, PermissionAction.View) ? null : "Tu usuario no tiene permiso para ver reportes.",
            "crear_reporte" or "editar_reporte" or "archivar_reporte"
                => perms.Can(ReportAuthorModule, PermissionAction.Create) || perms.Can(ReportAdminModule, PermissionAction.View)
                    ? null : "Tu usuario no tiene permiso para crear ni cambiar reportes.",
            "listar_libros_indicadores" or "ver_libro_indicadores"
                => perms.Can(AnalisisModule, PermissionAction.View) ? null : "Tu usuario no tiene permiso para ver libros de indicadores.",
            _ => canAuthorIndicators ? null : "Tu usuario no tiene permiso para crear indicadores."
        };
        if (denied is not null) { return Err(denied); }

        var analisisActor = new AnalisisActor(actor.PlatformUserId, perms.Unrestricted);
        try
        {
            return tool switch
            {
                "listar_fuentes_reporte" => await ListReportSourcesAsync(args, ct),
                "ver_fuente_reporte" => await ViewReportSourceAsync(args, ct),
                "listar_reportes" => Ok(new { reportes = (await VisibleReportsAsync(perms, ct)).Select(ReportCard) }),
                "ver_reporte" => await ViewReportAsync(args, perms, ct),
                "crear_reporte" => await CreateReportAsync(args, actor, ct),
                "listar_libros_indicadores" => await ListBooksAsync(analisisActor, ct),
                "ver_libro_indicadores" => await ViewBookAsync(args, analisisActor, ct),
                "crear_libro_indicadores" => await CreateBookAsync(args, analisisActor, ct),
                "agregar_indicador" => await AddIndicatorAsync(args, actor, analisisActor, ct),
                "editar_reporte" => await EditReportAsync(args, actor, perms, ct),
                "archivar_reporte" => await ArchiveReportAsync(args, perms, ct),
                "eliminar_indicador" => await DeleteIndicatorAsync(args, analisisActor, ct),
                "eliminar_libro_indicadores" => await DeleteBookAsync(args, analisisActor, ct),
                _ => Err($"Herramienta desconocida: {tool}")
            };
        }
        catch (ReportValidationException ex) { return Err(ex.Message); }
    }

    // ===================== Fuentes =====================

    private async Task<McpToolResult> ListReportSourcesAsync(JsonElement args, CancellationToken ct)
    {
        var tipo = Str(args, "tipo")?.ToLowerInvariant();
        var sources = await _reportCatalog!.GetSourcesAsync(ct);
        var tables = await ContainerTableNamesAsync(ct);
        var rows = sources
            .Select(s => new { s, kind = SourceKindEs(s) })
            .Where(x => tipo is null || x.kind == tipo)
            .Select(x =>
            {
                var tableId = x.s.Kind == ReportSourceKind.Container ? ContainerReportReader.ParseId(x.s.Key) : null;
                var names = tableId is Guid id && tables.TryGetValue(id, out var n) ? n : default;
                return new
                {
                    fuente = x.s.Key,
                    nombre = x.s.DisplayName,
                    tipo = x.kind,
                    contenedor = names.Model,
                    tabla = tableId is null ? null : x.s.DisplayName,
                    campos = x.s.Fields.Count
                };
            })
            .ToList();
        return Ok(new { total = rows.Count, fuentes = rows });
    }

    private async Task<McpToolResult> ViewReportSourceAsync(JsonElement args, CancellationToken ct)
    {
        var (source, error) = await ResolveReportSourceAsync(args, ct);
        if (error is not null) { return Err(error); }
        return Ok(new
        {
            fuente = source!.Key,
            nombre = source.DisplayName,
            tipo = SourceKindEs(source),
            campos = source.Fields.Select(f => new
            {
                campo = f.DisplayName,
                tipo = FieldTypeEs(f.Type),
                filtrable = f.CanFilter,
                agrupable = f.CanGroup,
                sumable = f.CanAggregate
            }),
            aviso = source.Fields.Any(f => f.CanAggregate) ? null
                : "Ningun campo es numerico: solo se puede contar. Para sumar, el campo debe ser de tipo numero o decimal."
        });
    }

    // ===================== Reportes =====================

    private async Task<IReadOnlyList<ReportDefinitionSummary>> VisibleReportsAsync(EffectivePermissions perms, CancellationToken ct)
    {
        var seeAll = perms.Unrestricted || perms.Can(ReportAdminModule, PermissionAction.View);
        return await _reportDefs!.ListAsync(seeAll, perms.RolId, ct);
    }

    private static object ReportCard(ReportDefinitionSummary r) => new
    {
        reporte_id = r.Id,
        nombre = r.Name,
        tipo = r.Kind == ReportDefinitionKind.Printable ? "imprimible" : "panel",
        fuente = r.SourceKey,
        categoria = r.Category,
        actualizado = r.UpdatedAt
    };

    private async Task<McpToolResult> ViewReportAsync(JsonElement args, EffectivePermissions perms, CancellationToken ct)
    {
        var (hit, findError) = await FindReportAsync(Str(args, "reporte"), perms, ct);
        if (hit is null) { return Err(findError!); }

        var run = Reporting.Panels.PanelSpecSource.Is(hit.SourceKey) ? null : await _reportDefs!.RunAsync(hit.Id, ct);
        if (run is null)
        {
            return Ok(new { reporte = ReportCard(hit), aviso = "Este reporte no tiene una consulta ejecutable aqui (p.ej. un panel por spec o un imprimible): abrelo en la galeria." });
        }
        return Ok(new { reporte = ReportCard(hit), grafico = run.Spec.Chart.ToString().ToLowerInvariant(), resultado = Preview(run.DataSet) });
    }

    private async Task<McpToolResult> CreateReportAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el nombre del reporte."); }
        var chart = (Str(args, "grafico") ?? "tabla").ToLowerInvariant() switch
        {
            "tabla" => (ReportChartKind?)ReportChartKind.Table,
            "barras" => ReportChartKind.Bar,
            "torta" or "dona" => ReportChartKind.Pie,
            "linea" => ReportChartKind.Line,
            _ => null
        };
        if (chart is null) { return Err("'grafico' debe ser tabla, barras, torta o linea."); }

        var (source, srcError) = await ResolveReportSourceAsync(args, ct);
        if (srcError is not null) { return Err(srcError); }
        var (query, qError) = BuildReportQuery(source!, args);
        if (qError is not null) { return Err(qError); }
        if (chart != ReportChartKind.Table && query!.GroupBy.Count == 0)
        {
            return Err("Un grafico de barras, torta o linea necesita 'agrupar_por' en la consulta.");
        }

        // Valida ejecutando (mismo camino tenant-safe que la galeria) ANTES de guardar.
        var ds = await _reportData!.QueryAsync(query!, new ReportContext(actor.TenantId, actor.PlatformUserId), ct);

        var spec = new ReportSpec
        {
            Title = nombre,
            SourceKey = query!.SourceKey,
            Chart = chart.Value,
            Fields = query.Fields.ToList(),
            Filters = query.Filters.Select(f => new ReportFilterSpec { Field = f.FieldKey, Op = f.Operator, Values = f.Values.ToList() }).ToList(),
            GroupBy = query.GroupBy.ToList(),
            Aggregates = query.Aggregates.Select(a => new ReportAggregateSpec { Field = a.FieldKey, Function = a.Function }).ToList(),
            Sort = query.Sort.Select(s => new ReportSortSpec { Field = s.FieldKey, Desc = s.Descending }).ToList(),
            Top = query.Top,
            GroupPeriod = query.GroupPeriod
        };
        var id = await _reportDefs!.SaveAsync(spec, Str(args, "descripcion"), ct);
        if (Str(args, "categoria") is string categoria)
        {
            await _reportDefs.UpdateCardAsync(id, nombre, categoria, Str(args, "descripcion"), ct);
        }

        return Ok(new
        {
            ok = true,
            reporte_id = id,
            nombre,
            fuente = source!.DisplayName,
            grafico = Str(args, "grafico") ?? "tabla",
            resultado = Preview(ds),
            aviso = "Queda en la galeria de reportes (pestana de su categoria)."
        });
    }

    /// <summary>Reporte visible por id, nombre exacto o nombre parcial unico.</summary>
    private async Task<(ReportDefinitionSummary? report, string? error)> FindReportAsync(string? key, EffectivePermissions perms, CancellationToken ct)
    {
        if (key is null) { return (null, "Indica el reporte (id o nombre)."); }
        var visible = await VisibleReportsAsync(perms, ct);
        var hit = Guid.TryParse(key, out var g)
            ? visible.FirstOrDefault(r => r.Id == g)
            : visible.FirstOrDefault(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase))
              ?? SingleOrNull(visible.Where(r => r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)));
        return hit is null ? (null, $"No encontre un reporte visible llamado '{key}' (usa listar_reportes).") : (hit, null);
    }

    private static ReportChartKind? ParseChart(string? s) => (s ?? "tabla").ToLowerInvariant() switch
    {
        "tabla" => ReportChartKind.Table,
        "barras" => ReportChartKind.Bar,
        "torta" or "dona" => ReportChartKind.Pie,
        "linea" => ReportChartKind.Line,
        _ => null
    };

    private static string ChartEs(ReportChartKind c) => c switch
    {
        ReportChartKind.Bar => "barras",
        ReportChartKind.Pie => "torta",
        ReportChartKind.Line => "linea",
        _ => "tabla"
    };

    private static ReportSpec ToReportSpec(string title, ReportChartKind chart, ReportQuerySpec query) => new()
    {
        Title = title,
        SourceKey = query.SourceKey,
        Chart = chart,
        Fields = query.Fields.ToList(),
        Filters = query.Filters.Select(f => new ReportFilterSpec { Field = f.FieldKey, Op = f.Operator, Values = f.Values.ToList() }).ToList(),
        GroupBy = query.GroupBy.ToList(),
        Aggregates = query.Aggregates.Select(a => new ReportAggregateSpec { Field = a.FieldKey, Function = a.Function }).ToList(),
        Sort = query.Sort.Select(s => new ReportSortSpec { Field = s.FieldKey, Desc = s.Descending }).ToList(),
        Top = query.Top,
        GroupPeriod = query.GroupPeriod
    };

    /// <summary>
    /// editar_reporte (ADR-0206): cambio PARCIAL de un panel creado con crear_reporte. Tarjeta (nombre, categoria,
    /// descripcion) por UpdateCardAsync; grafico/consulta se validan EJECUTANDO antes de guardar (como al crear).
    /// </summary>
    private async Task<McpToolResult> EditReportAsync(JsonElement args, McpActor actor, EffectivePermissions perms, CancellationToken ct)
    {
        var (hit, findError) = await FindReportAsync(Str(args, "reporte"), perms, ct);
        if (hit is null) { return Err(findError!); }

        var hasQuery = args.TryGetProperty("consulta", out var cq) && cq.ValueKind == JsonValueKind.Object;
        var hasChart = Str(args, "grafico") is not null;
        var hasCard = ArgPresent(args, "nombre") || ArgPresent(args, "categoria") || ArgPresent(args, "descripcion");
        if (!hasQuery && !hasChart && !hasCard)
        {
            return Err("No indicaste cambios: manda nombre, categoria, descripcion, grafico o consulta.");
        }
        if (!hasQuery && (Str(args, "fuente") is not null || Str(args, "contenedor") is not null))
        {
            return Err("Para cambiar la fuente manda tambien la 'consulta' completa (los campos cambian con la fuente).");
        }

        var nombre = Str(args, "nombre") ?? hit.Name;
        var cambios = new List<string>();
        ReportDataSet? ds = null;

        if (hasQuery || hasChart)
        {
            var detail = hit.Kind == ReportDefinitionKind.Dashboard && !Reporting.Panels.PanelSpecSource.Is(hit.SourceKey)
                ? await _reportDefs!.GetAsync(hit.Id, ct) : null;
            if (detail is null)
            {
                return Err("Este reporte no es un panel de consulta simple (es un imprimible o un panel disenado en pantalla): editalo en la galeria.");
            }
            var current = detail.Spec;
            if ((hasChart ? ParseChart(Str(args, "grafico")) : current.Chart) is not ReportChartKind chart)
            {
                return Err("'grafico' debe ser tabla, barras, torta o linea.");
            }

            ReportQuerySpec query;
            ReportSourceDescriptor? source;
            if (hasQuery)
            {
                if (Str(args, "fuente") is null && Str(args, "contenedor") is null)
                {
                    source = await _reportCatalog!.GetSourceAsync(current.SourceKey, ct);
                    if (source is null) { return Err("La fuente actual del reporte ya no existe: indica 'fuente' o 'contenedor' + 'tabla' junto con la consulta."); }
                }
                else
                {
                    var (resolved, srcError) = await ResolveReportSourceAsync(args, ct);
                    if (srcError is not null) { return Err(srcError); }
                    source = resolved;
                }
                var (built, qError) = BuildReportQuery(source!, args);
                if (qError is not null) { return Err(qError); }
                query = built!;
                cambios.Add("consulta");
            }
            else
            {
                query = current.ToQuerySpec();
                source = await _reportCatalog!.GetSourceAsync(current.SourceKey, ct);
                if (source is null) { return Err("La fuente del reporte ya no existe: no se puede cambiar el grafico. Archivalo o manda una consulta nueva."); }
            }
            if (chart != ReportChartKind.Table && query.GroupBy.Count == 0)
            {
                return Err("Un grafico de barras, torta o linea necesita 'agrupar_por' en la consulta.");
            }
            if (hasChart) { cambios.Add("grafico"); }

            ds = await _reportData!.QueryAsync(query, new ReportContext(actor.TenantId, actor.PlatformUserId), ct);
            await _reportDefs!.UpdateSpecAsync(hit.Id, ToReportSpec(nombre, chart, query), ct);
        }

        if (hasCard)
        {
            var categoria = ArgPresent(args, "categoria") ? Str(args, "categoria") : hit.Category;
            var descripcion = ArgPresent(args, "descripcion") ? Str(args, "descripcion") : hit.Description;
            await _reportDefs!.UpdateCardAsync(hit.Id, nombre, categoria, descripcion, ct);
            if (ArgPresent(args, "nombre")) { cambios.Add("nombre"); }
            if (ArgPresent(args, "categoria")) { cambios.Add("categoria"); }
            if (ArgPresent(args, "descripcion")) { cambios.Add("descripcion"); }
        }

        return Ok(new
        {
            ok = true,
            reporte_id = hit.Id,
            nombre,
            cambios,
            resultado = ds is null ? null : Preview(ds)
        });
    }

    private async Task<McpToolResult> ArchiveReportAsync(JsonElement args, EffectivePermissions perms, CancellationToken ct)
    {
        var (hit, findError) = await FindReportAsync(Str(args, "reporte"), perms, ct);
        if (hit is null) { return Err(findError!); }
        await _reportDefs!.ArchiveAsync(hit.Id, ct);
        return Ok(new
        {
            ok = true,
            reporte_id = hit.Id,
            nombre = hit.Name,
            aviso = "Archivado: ya no sale en la galeria. Un administrador lo recupera desde 'Archivados'."
        });
    }

    // ===================== Indicadores =====================

    private async Task<McpToolResult> DeleteIndicatorAsync(JsonElement args, AnalisisActor actor, CancellationToken ct)
    {
        if (!Guid.TryParse(Str(args, "indicador"), out var widgetId))
        {
            return Err("Indica 'indicador' = widget_id (lo ves con ver_libro_indicadores).");
        }
        var r = await _analisis!.EliminarWidgetAsync(widgetId, actor, ct);
        return r.Ok ? Ok(new { ok = true, indicador = widgetId }) : Err(r.Error ?? "No se pudo quitar el indicador.");
    }

    private async Task<McpToolResult> DeleteBookAsync(JsonElement args, AnalisisActor actor, CancellationToken ct)
    {
        var (libro, error) = await ResolveBookAsync(Str(args, "libro"), actor, ct);
        if (error is not null) { return Err(error); }
        var r = await _analisis!.EliminarLibroAsync(libro!.Id, actor, ct);
        return r.Ok ? Ok(new { ok = true, libro_id = libro.Id, nombre = libro.Nombre }) : Err(r.Error ?? "No se pudo eliminar el libro.");
    }

    private async Task<McpToolResult> ListBooksAsync(AnalisisActor actor, CancellationToken ct)
    {
        var libros = await _analisis!.ListarLibrosAsync(actor, ct);
        return Ok(new
        {
            libros = libros.Select(l => new
            {
                libro_id = l.Id,
                nombre = l.Nombre,
                descripcion = l.Descripcion,
                visibilidad = l.Visibilidad.ToString().ToLowerInvariant(),
                es_dueno = l.EsDueno,
                hojas = l.Hojas,
                widgets = l.Widgets
            })
        });
    }

    private async Task<McpToolResult> ViewBookAsync(JsonElement args, AnalisisActor actor, CancellationToken ct)
    {
        var (libro, error) = await ResolveBookAsync(Str(args, "libro"), actor, ct);
        if (error is not null) { return Err(error); }
        return Ok(new
        {
            libro_id = libro!.Id,
            nombre = libro.Nombre,
            descripcion = libro.Descripcion,
            puede_editar = libro.PuedeEditar,
            hojas = libro.Hojas.OrderBy(h => h.Orden).Select(h => new
            {
                hoja_id = h.Id,
                nombre = h.Nombre,
                widgets = h.Widgets.OrderBy(w => w.Orden).Select(w => new
                {
                    widget_id = w.Id,
                    tipo = WidgetTypeEs(w.Tipo),
                    titulo = w.Titulo,
                    fuente = AnalisisJson.DeserializeSpec(w.SpecJson)?.SourceKey ?? w.IndicadorKey,
                    ancho = w.ColSpan
                })
            })
        });
    }

    private async Task<McpToolResult> CreateBookAsync(JsonElement args, AnalisisActor actor, CancellationToken ct)
    {
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el nombre del libro."); }
        var r = await _analisis!.CrearLibroAsync(nombre, Str(args, "descripcion"), actor, ct);
        if (!r.Ok || r.Id is not Guid libroId) { return Err(r.Error ?? "No se pudo crear el libro."); }
        var detalle = await _analisis.ObtenerLibroAsync(libroId, actor, ct);
        var hoja = detalle?.Hojas.OrderBy(h => h.Orden).FirstOrDefault();
        return Ok(new { ok = true, libro_id = libroId, nombre, hoja_id = hoja?.Id, hoja = hoja?.Nombre });
    }

    private async Task<McpToolResult> AddIndicatorAsync(JsonElement args, McpActor mcpActor, AnalisisActor actor, CancellationToken ct)
    {
        var titulo = Str(args, "titulo");
        if (titulo is null) { return Err("Indica el titulo del indicador."); }
        if (ParseWidgetType(Str(args, "tipo")) is not AnalisisTipoWidget tipo)
        {
            return Err("'tipo' debe ser kpi, barras, barras_horizontales, dona, embudo, treemap, linea, area o tabla.");
        }

        var (libro, bookError) = await ResolveBookAsync(Str(args, "libro"), actor, ct);
        if (bookError is not null) { return Err(bookError); }
        if (!libro!.PuedeEditar) { return Err("Solo el dueno del libro o un administrador pueden agregarle indicadores."); }

        var (source, srcError) = await ResolveReportSourceAsync(args, ct);
        if (srcError is not null) { return Err(srcError); }
        var (query, qError) = BuildReportQuery(source!, args);
        if (qError is not null) { return Err(qError); }
        if (tipo == AnalisisTipoWidget.Kpi && (query!.GroupBy.Count > 0 || query.Aggregates.Count != 1))
        {
            return Err("Un kpi es UN numero: usa exactamente una agregacion y sin 'agrupar_por'.");
        }
        if (tipo is not (AnalisisTipoWidget.Kpi or AnalisisTipoWidget.Tabla) && query!.GroupBy.Count == 0)
        {
            return Err($"Un indicador '{Str(args, "tipo")}' necesita 'agrupar_por' en la consulta.");
        }

        var ancho = Math.Clamp(Int(args, "ancho") ?? (tipo == AnalisisTipoWidget.Kpi ? 1 : 2), 1, AnalisisService.MaxColumnas);
        var sheets = libro.Hojas.OrderBy(h => h.Orden).ToList();
        var hojaKey = Str(args, "hoja");

        // Ensayo del widget ANTES de guardarlo: mismo runner que pinta la hoja.
        var specJson = AnalisisJson.Serialize(query!);
        var ensayo = new AnalisisWidgetDto(Guid.Empty, Guid.Empty, tipo, titulo, specJson, null, null, 0, ancho, 1);
        var prueba = await _widgetRunner!.RunAsync(ensayo, new ReportContext(mcpActor.TenantId, mcpActor.PlatformUserId), ct);
        if (!prueba.Ok) { return Err($"El indicador no corre: {prueba.Error}"); }

        Guid hojaId;
        var hojaCreada = false;
        var hit = hojaKey is null ? sheets.FirstOrDefault()
            : Guid.TryParse(hojaKey, out var hg) ? sheets.FirstOrDefault(h => h.Id == hg)
            : sheets.FirstOrDefault(h => string.Equals(h.Nombre, hojaKey, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            hojaId = hit.Id;
        }
        else
        {
            if (hojaKey is not null && Guid.TryParse(hojaKey, out _)) { return Err($"La hoja '{hojaKey}' no esta en el libro '{libro.Nombre}'."); }
            var nueva = await _analisis!.CrearHojaAsync(libro.Id, hojaKey ?? "Hoja 1", actor, ct);
            if (!nueva.Ok || nueva.Id is not Guid nuevaId) { return Err(nueva.Error ?? "No se pudo crear la hoja."); }
            hojaId = nuevaId;
            hojaCreada = true;
        }

        var r = await _analisis!.AgregarWidgetAsync(hojaId, new AnalisisNuevoWidget(tipo, titulo, query, null, ancho), actor, ct);
        if (!r.Ok) { return Err(r.Error ?? "No se pudo agregar el indicador."); }
        return Ok(new
        {
            ok = true,
            widget_id = r.Id,
            libro_id = libro.Id,
            hoja_id = hojaId,
            hoja_creada = hojaCreada,
            tipo = WidgetTypeEs(tipo),
            fuente = source!.DisplayName,
            resultado = ShapePreview(prueba.Forma!),
            avisos = prueba.Avisos.Count > 0 ? prueba.Avisos : null
        });
    }

    private async Task<(AnalisisLibroDetalle? libro, string? error)> ResolveBookAsync(string? key, AnalisisActor actor, CancellationToken ct)
    {
        if (key is null) { return (null, "Indica el libro (id o nombre)."); }
        var libros = await _analisis!.ListarLibrosAsync(actor, ct);
        var hit = Guid.TryParse(key, out var g)
            ? libros.FirstOrDefault(l => l.Id == g)
            : libros.FirstOrDefault(l => string.Equals(l.Nombre, key, StringComparison.OrdinalIgnoreCase))
              ?? SingleOrNull(libros.Where(l => l.Nombre.Contains(key, StringComparison.OrdinalIgnoreCase)));
        if (hit is null) { return (null, $"No encontre un libro de indicadores llamado '{key}' (usa listar_libros_indicadores)."); }
        var detalle = await _analisis.ObtenerLibroAsync(hit.Id, actor, ct);
        return detalle is null ? (null, $"No pude abrir el libro '{hit.Nombre}'.") : (detalle, null);
    }

    // ===================== Fuente + consulta =====================

    /// <summary>Tablas raiz de contenedores (id -> contenedor, tabla), para decir de que contenedor es cada fuente.</summary>
    private async Task<Dictionary<Guid, (string? Model, string Table)>> ContainerTableNamesAsync(CancellationToken ct)
    {
        var rows = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ParentContainerId == null)
            .Select(c => new { c.Id, c.Name, Model = c.Model != null ? c.Model.Name : null })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.Model, r.Name));
    }

    /// <summary>
    /// Resuelve la fuente: 'contenedor' (+ 'tabla') -> la tabla raiz -> "container:{id}"; o 'fuente' como clave del
    /// catalogo o como nombre (si el nombre se repite en dos contenedores pide contenedor + tabla).
    /// </summary>
    private async Task<(ReportSourceDescriptor? source, string? error)> ResolveReportSourceAsync(JsonElement args, CancellationToken ct)
    {
        var fuente = Str(args, "fuente");
        var contenedor = Str(args, "contenedor");
        var tabla = Str(args, "tabla");

        if (contenedor is not null)
        {
            var models = await _db.DataModels.AsNoTracking().Select(m => new { m.Id, m.Name }).ToListAsync(ct);
            var model = Guid.TryParse(contenedor, out var mg) ? models.FirstOrDefault(m => m.Id == mg)
                : models.FirstOrDefault(m => string.Equals(m.Name, contenedor, StringComparison.OrdinalIgnoreCase))
                  ?? SingleOrNull(models.Where(m => m.Name.Contains(contenedor, StringComparison.OrdinalIgnoreCase)));
            if (model is null) { return (null, $"No encontre el contenedor '{contenedor}'."); }
            var tables = await _db.DataContainers.AsNoTracking()
                .Where(c => c.ModelId == model.Id && c.ParentContainerId == null)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct);
            var key = tabla ?? fuente;
            var table = key is null ? (tables.Count == 1 ? tables[0] : null)
                : Guid.TryParse(key, out var tg) ? tables.FirstOrDefault(t => t.Id == tg)
                : tables.FirstOrDefault(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase))
                  ?? SingleOrNull(tables.Where(t => t.Name.Contains(key, StringComparison.OrdinalIgnoreCase)));
            if (table is null)
            {
                return (null, key is null
                    ? $"El contenedor '{model.Name}' tiene {tables.Count} tablas: indica 'tabla' ({string.Join(", ", tables.Select(t => t.Name))})."
                    : $"No encontre la tabla '{key}' en el contenedor '{model.Name}' ({string.Join(", ", tables.Select(t => t.Name))}).");
            }
            var described = await _reportCatalog!.GetSourceAsync(ContainerReportReader.KeyFor(table.Id), ct);
            return described is null ? (null, $"La tabla '{table.Name}' no es reportable.") : (described, null);
        }

        if (fuente is null) { return (null, "Indica la fuente: 'fuente' (clave o nombre) o 'contenedor' + 'tabla'."); }

        var direct = await _reportCatalog!.GetSourceAsync(fuente, ct);
        if (direct is not null) { return (direct, null); }

        var all = await _reportCatalog.GetSourcesAsync(ct);
        var byName = all.Where(s => string.Equals(s.DisplayName, fuente, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 0)
        {
            byName = all.Where(s => s.DisplayName.Contains(fuente, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return byName.Count switch
        {
            1 => (byName[0], null),
            0 => (null, $"No encontre la fuente '{fuente}' (usa listar_fuentes_reporte)."),
            _ => (null, $"Hay {byName.Count} fuentes llamadas '{fuente}': indica la clave ({string.Join(", ", byName.Take(5).Select(s => s.Key))}) o 'contenedor' + 'tabla'.")
        };
    }

    /// <summary>La consulta en nombres de campo -> <see cref="ReportQuerySpec"/> con las claves del catalogo.</summary>
    private static (ReportQuerySpec? query, string? error) BuildReportQuery(ReportSourceDescriptor source, JsonElement args)
    {
        if (!args.TryGetProperty("consulta", out var c) || c.ValueKind != JsonValueKind.Object)
        {
            return (null, "Indica la 'consulta' (objeto con campos, filtros, agrupar_por, agregaciones, orden, limite).");
        }

        string? Missing(string name) => $"El campo '{name}' no existe en la fuente '{source.DisplayName}'. Campos: " +
            string.Join(", ", source.Fields.Take(40).Select(f => f.DisplayName)) + (source.Fields.Count > 40 ? "..." : ".");

        ReportField? Field(string name) =>
            source.FindField(name)
            ?? source.Fields.FirstOrDefault(f => string.Equals(f.DisplayName, name, StringComparison.OrdinalIgnoreCase))
            ?? source.Fields.FirstOrDefault(f => string.Equals(f.DisplayName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        var fields = new List<string>();
        foreach (var name in StrList(c, "campos"))
        {
            if (Field(name) is not ReportField f) { return (null, Missing(name)); }
            fields.Add(f.Key);
        }

        var filters = new List<ReportFilter>();
        if (c.TryGetProperty("filtros", out var fs) && fs.ValueKind == JsonValueKind.Array)
        {
            foreach (var fe in fs.EnumerateArray())
            {
                if (fe.ValueKind != JsonValueKind.Object || Str(fe, "campo") is not string name) { return (null, "Cada filtro necesita 'campo'."); }
                if (Field(name) is not ReportField f) { return (null, Missing(name)); }
                if (ParseOperator(Str(fe, "operador")) is not ReportFilterOperator op)
                {
                    return (null, $"Operador desconocido en el filtro de '{name}': usa igual, distinto, contiene, mayor, mayor_igual, menor, menor_igual, entre o en.");
                }
                var values = new List<string?>();
                if (fe.TryGetProperty("valores", out var vs) && vs.ValueKind == JsonValueKind.Array)
                {
                    values.AddRange(vs.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? null : v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()));
                }
                else if (fe.TryGetProperty("valor", out var v1))
                {
                    values.Add(v1.ValueKind == JsonValueKind.Null ? null : v1.ValueKind == JsonValueKind.String ? v1.GetString() : v1.GetRawText());
                }
                if (op == ReportFilterOperator.Between && values.Count != 2) { return (null, $"El filtro 'entre' de '{name}' necesita 'valores' con desde y hasta."); }
                filters.Add(new ReportFilter(f.Key, op, values));
            }
        }

        var groupBy = new List<string>();
        foreach (var name in StrList(c, "agrupar_por"))
        {
            if (Field(name) is not ReportField f) { return (null, Missing(name)); }
            groupBy.Add(f.Key);
        }

        var aggregates = new List<ReportAggregate>();
        if (c.TryGetProperty("agregaciones", out var ag) && ag.ValueKind == JsonValueKind.Array)
        {
            foreach (var ae in ag.EnumerateArray())
            {
                if (ae.ValueKind != JsonValueKind.Object || ParseAggregate(Str(ae, "funcion")) is not ReportAggregateFunction fn)
                {
                    return (null, "Cada agregacion necesita 'funcion': contar, sumar, promedio, minimo o maximo.");
                }
                var campo = Str(ae, "campo");
                if (fn == ReportAggregateFunction.Count)
                {
                    var key = campo is null ? groupBy.FirstOrDefault() ?? string.Empty
                        : Field(campo)?.Key ?? (string?)null;
                    if (key is null) { return (null, Missing(campo!)); }
                    aggregates.Add(new ReportAggregate(key, fn));
                    continue;
                }
                if (campo is null) { return (null, $"La agregacion '{Str(ae, "funcion")}' necesita 'campo' (numerico)."); }
                if (Field(campo) is not ReportField af) { return (null, Missing(campo)); }
                if (!af.CanAggregate)
                {
                    return (null, $"El campo '{af.DisplayName}' es de tipo {FieldTypeEs(af.Type)}: no se puede {Str(ae, "funcion")}. Cambia la columna a numero o decimal, o usa contar.");
                }
                aggregates.Add(new ReportAggregate(af.Key, fn));
            }
        }
        if (groupBy.Count > 0 && aggregates.Count == 0)
        {
            aggregates.Add(new ReportAggregate(groupBy[0], ReportAggregateFunction.Count));
        }

        var sort = new List<ReportSort>();
        if (c.TryGetProperty("orden", out var os) && os.ValueKind == JsonValueKind.Array)
        {
            foreach (var oe in os.EnumerateArray())
            {
                if (oe.ValueKind != JsonValueKind.Object || Str(oe, "campo") is not string name) { return (null, "Cada orden necesita 'campo'."); }
                var desc = Bool(oe, "desc") ?? false;
                if (ParseAggregate(name) is ReportAggregateFunction byFn
                    && aggregates.FirstOrDefault(a => a.Function == byFn) is ReportAggregate target)
                {
                    sort.Add(new ReportSort($"{target.Function}_{target.FieldKey}", desc));
                    continue;
                }
                if (Field(name) is not ReportField f) { return (null, Missing(name)); }
                sort.Add(new ReportSort(f.Key, desc));
            }
        }

        // Periodo (ADR-0206): agrupa el campo FECHA de agrupar_por por dia/semana/mes/trimestre/anio.
        ReportDatePeriod? periodo = null;
        if (Str(c, "periodo") is string ps)
        {
            periodo = ParsePeriod(ps);
            if (periodo is null) { return (null, "'periodo' debe ser dia, semana, mes, trimestre o anio."); }
            if (groupBy.Count != 1) { return (null, "'periodo' necesita 'agrupar_por' con el campo fecha."); }
            var gf = source.FindField(groupBy[0])!;
            if (gf.Type != ReportFieldType.Date)
            {
                return (null, $"'periodo' solo aplica a un campo fecha: '{gf.DisplayName}' es de tipo {FieldTypeEs(gf.Type)}.");
            }
        }

        var limite = Int(c, "limite");
        return (new ReportQuerySpec
        {
            SourceKey = source.Key,
            Fields = fields,
            Filters = filters,
            GroupBy = groupBy,
            Aggregates = aggregates,
            Sort = sort,
            Top = limite is > 0 ? limite : null,
            GroupPeriod = periodo
        }, null);
    }

    private static ReportDatePeriod? ParsePeriod(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "dia" or "day" => ReportDatePeriod.Day,
        "semana" or "week" => ReportDatePeriod.Week,
        "mes" or "month" => ReportDatePeriod.Month,
        "trimestre" or "quarter" => ReportDatePeriod.Quarter,
        "anio" or "ano" or "year" => ReportDatePeriod.Year,
        _ => null
    };

    private static ReportFilterOperator? ParseOperator(string? s) => (s ?? "igual").ToLowerInvariant() switch
    {
        "igual" or "=" or "equals" => ReportFilterOperator.Equals,
        "distinto" or "!=" or "notequals" => ReportFilterOperator.NotEquals,
        "contiene" or "contains" => ReportFilterOperator.Contains,
        "mayor" or ">" or "greaterthan" => ReportFilterOperator.GreaterThan,
        "mayor_igual" or ">=" or "greaterthanorequal" => ReportFilterOperator.GreaterThanOrEqual,
        "menor" or "<" or "lessthan" => ReportFilterOperator.LessThan,
        "menor_igual" or "<=" or "lessthanorequal" => ReportFilterOperator.LessThanOrEqual,
        "entre" or "between" => ReportFilterOperator.Between,
        "en" or "in" => ReportFilterOperator.In,
        _ => null
    };

    private static ReportAggregateFunction? ParseAggregate(string? s) => (s ?? string.Empty).ToLowerInvariant() switch
    {
        "contar" or "conteo" or "count" => ReportAggregateFunction.Count,
        "sumar" or "suma" or "sum" => ReportAggregateFunction.Sum,
        "promedio" or "avg" => ReportAggregateFunction.Avg,
        "minimo" or "min" => ReportAggregateFunction.Min,
        "maximo" or "max" => ReportAggregateFunction.Max,
        _ => null
    };

    private static AnalisisTipoWidget? ParseWidgetType(string? s) => (s ?? string.Empty).ToLowerInvariant() switch
    {
        "kpi" => AnalisisTipoWidget.Kpi,
        "barras" => AnalisisTipoWidget.Barras,
        "barras_horizontales" => AnalisisTipoWidget.BarrasHorizontales,
        "dona" or "torta" => AnalisisTipoWidget.Dona,
        "embudo" => AnalisisTipoWidget.Embudo,
        "treemap" => AnalisisTipoWidget.Treemap,
        "linea" => AnalisisTipoWidget.Linea,
        "area" => AnalisisTipoWidget.Area,
        "tabla" => AnalisisTipoWidget.Tabla,
        _ => null
    };

    private static string WidgetTypeEs(AnalisisTipoWidget t) => t switch
    {
        AnalisisTipoWidget.BarrasHorizontales => "barras_horizontales",
        AnalisisTipoWidget.BarrasApiladas => "barras_apiladas",
        AnalisisTipoWidget.BarrasLinea => "barras_linea",
        _ => t.ToString().ToLowerInvariant()
    };

    private static string SourceKindEs(ReportSourceDescriptor s) =>
        s.Kind == ReportSourceKind.Container ? "contenedor"
        : s.Kind == ReportSourceKind.External ? "externa"
        : FormResponseReportReader.Handles(s.Key) ? "formulario"
        : "nativa";

    private static string FieldTypeEs(ReportFieldType t) => t switch
    {
        ReportFieldType.Text => "texto",
        ReportFieldType.Number => "numero",
        ReportFieldType.Decimal => "decimal",
        ReportFieldType.Date => "fecha",
        ReportFieldType.Boolean => "booleano",
        _ => t.ToString().ToLowerInvariant()
    };

    private static object Preview(ReportDataSet ds) => new
    {
        columnas = ds.Columns.Select(col => col.DisplayName),
        filas = ds.RowCount,
        muestra = ds.Rows.Take(PreviewRows)
    };

    private static object ShapePreview(WidgetShape shape) => shape switch
    {
        EscalarShape e => new { forma = "kpi", valor = e.Valor },
        CategoriasShape cat => new { forma = "categorias", total = cat.Items.Count, muestra = cat.Items.Take(PreviewRows).Select(i => new { etiqueta = i.Etiqueta, valor = i.Valor }) },
        SerieShape s => new { forma = "serie", ejes = s.Ejes.Count, series = s.Series.Select(x => x.Nombre) },
        TablaShape t => new { forma = "tabla", columnas = t.Columnas.Select(col => col.DisplayName), filas = t.Filas.Count },
        _ => new { forma = shape.GetType().Name }
    };
}
