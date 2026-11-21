using System.Text.Json;
using Ecorex.Application.DataContainers;
using Ecorex.Application.MenuConfig;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Mcp;

/// <summary>
/// CONTENEDOR DE DATOS en el servidor MCP personal (ADR-0159, addendum contenedores / ADR-0194). Capa delgada
/// sobre los servicios REALES del modulo (000730 / "dc"): <see cref="IDataModelService"/> (contenedor = modelo ER
/// que agrupa TABLAS + relaciones), <see cref="IDataImportConfigService"/> (conectores + procesos con horario +
/// bitacora) y <see cref="IDataContainerModuleService"/> + <see cref="IMenuConfigService"/> (publicar una tabla
/// como modulo del menu = privacidad por la matriz de roles). Corre COMO el usuario del token.
///
/// ABSTRACCION: "contenedor" = DataModel (agrupa tablas); "tabla" = DataContainer (columnas/filas, unidad que se
/// publica). Por eso las herramientas van contenedor -> tablas -> columnas. Los conectores/horarios cuelgan del
/// ModelId (contenedor); publicar es por tabla raiz.
///
/// PERMISO: el modulo "contenedor-datos" SI esta en la matriz de roles (la pantalla usa Perm:contenedor-datos:View),
/// a diferencia de extraccion. Por eso estas herramientas exigen el permiso efectivo del actor (View/Create/Edit/
/// Delete), igual que las de flujos. Owner/Admin y sin-rol = acceso total. Aislamiento multi-tenant: filtro global.
///
/// SECRETOS: guardar_conector NUNCA recibe credenciales por el MCP (Credentials = null); el secreto se pone en la
/// pantalla. El servicio las cifra (ISecretProtector) y jamas las devuelve en claro.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const string ContainerModuleKey = "contenedor-datos";

    private static readonly HashSet<string> ContainerReadOnly = new(StringComparer.Ordinal)
    {
        "listar_contenedores_datos", "ver_contenedor_datos", "listar_conectores",
        "listar_programaciones_importacion", "listar_corridas_importacion", "ver_publicacion_contenedor"
    };

    private static readonly HashSet<string> ContainerDestructive = new(StringComparer.Ordinal)
    {
        "eliminar_contenedor_datos", "eliminar_tabla", "eliminar_columna", "eliminar_relacion",
        "eliminar_conector", "eliminar_programacion_importacion", "despublicar_contenedor"
    };

    private static readonly HashSet<string> ContainerToolNames = new(StringComparer.Ordinal)
    {
        "listar_contenedores_datos", "ver_contenedor_datos", "crear_contenedor_datos", "editar_contenedor_datos",
        "eliminar_contenedor_datos",
        "agregar_tabla", "editar_tabla", "eliminar_tabla",
        "agregar_columna", "editar_columna", "eliminar_columna",
        "agregar_relacion", "eliminar_relacion",
        "listar_conectores", "guardar_conector", "eliminar_conector",
        "listar_programaciones_importacion", "guardar_programacion_importacion", "eliminar_programacion_importacion",
        "listar_corridas_importacion",
        "ver_publicacion_contenedor", "publicar_contenedor", "despublicar_contenedor"
    };

    private const string TiposColumnaDoc =
        "texto, numero, decimal, fecha, booleano, submodelo (sub-tabla anidada; requiere 'hijo' = id de otra tabla).";

    private static readonly AiToolSpec[] ContainerSpecs =
    {
        // ---- Contenedor (DataModel) ----
        new("listar_contenedores_datos",
            "Lista los contenedores de datos del tenant (modelos que agrupan tablas): id, nombre, descripcion, " +
            "numero de tablas y de relaciones. Usala primero para conocer ids y nombres exactos.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_contenedor_datos",
            "Muestra un contenedor completo: sus TABLAS (id, nombre, columnas con tipo/requerido y si es derivada) y " +
            "las RELACIONES entre tablas. 'contenedor' = id o nombre (ver listar_contenedores_datos).",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"id o nombre del contenedor"}},"required":["contenedor"],"additionalProperties":false}"""),
        new("crear_contenedor_datos",
            "Crea un contenedor de datos NUEVO (cabecera: nombre + descripcion). Despues agregale tablas con " +
            "agregar_tabla. Devuelve 'contenedor_id'.",
            """{"type":"object","properties":{"nombre":{"type":"string"},"descripcion":{"type":"string"}},"required":["nombre"],"additionalProperties":false}"""),
        new("editar_contenedor_datos",
            "Edita la cabecera de un contenedor (nombre y/o descripcion). 'contenedor' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"nombre":{"type":"string"},"descripcion":{"type":"string"}},"required":["contenedor"],"additionalProperties":false}"""),
        new("eliminar_contenedor_datos",
            "Borra un contenedor COMPLETO con sus tablas, columnas, filas, relaciones y configuracion de importacion " +
            "(cascada). Confirma con el usuario antes de usarla. 'contenedor' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"}},"required":["contenedor"],"additionalProperties":false}"""),

        // ---- Tablas ----
        new("agregar_tabla",
            "Agrega una TABLA a un contenedor. 'columnas' (opcional) es una lista de objetos {nombre, tipo, requerido}. " +
            "Tipos: " + TiposColumnaDoc + " Devuelve 'tabla_id'.",
            """{"type":"object","properties":{"contenedor":{"type":"string","description":"id o nombre del contenedor"},"nombre":{"type":"string"},"descripcion":{"type":"string"},"columnas":{"type":"array","items":{"type":"object","properties":{"nombre":{"type":"string"},"tipo":{"type":"string"},"requerido":{"type":"boolean"}},"required":["nombre"],"additionalProperties":false}}},"required":["contenedor","nombre"],"additionalProperties":false}"""),
        new("editar_tabla",
            "Edita el nombre y/o descripcion de una tabla (conserva sus columnas). 'contenedor' y 'tabla' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"},"nombre":{"type":"string"},"descripcion":{"type":"string"}},"required":["contenedor","tabla"],"additionalProperties":false}"""),
        new("eliminar_tabla",
            "Borra una tabla de un contenedor (con sus columnas y filas). Confirma con el usuario. 'contenedor' y " +
            "'tabla' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"}},"required":["contenedor","tabla"],"additionalProperties":false}"""),

        // ---- Columnas ----
        new("agregar_columna",
            "Agrega una columna a una tabla. 'tipo': " + TiposColumnaDoc + " 'requerido' marca el campo obligatorio. " +
            "'contenedor' y 'tabla' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"},"nombre":{"type":"string"},"tipo":{"type":"string"},"requerido":{"type":"boolean"},"hijo":{"type":"string","description":"solo tipo submodelo: id de la tabla hija"}},"required":["contenedor","tabla","nombre","tipo"],"additionalProperties":false}"""),
        new("editar_columna",
            "Edita una columna existente (por nombre o posicion 1..n). Cambia solo lo que envies (nombre nuevo, tipo, " +
            "requerido). 'contenedor' y 'tabla' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"},"columna":{"type":"string","description":"nombre actual o posicion 1..n"},"nombre":{"type":"string","description":"nombre nuevo (opcional)"},"tipo":{"type":"string"},"requerido":{"type":"boolean"}},"required":["contenedor","tabla","columna"],"additionalProperties":false}"""),
        new("eliminar_columna",
            "Borra una columna de una tabla (por nombre o posicion). No se puede borrar una columna que ya tiene datos. " +
            "'contenedor' y 'tabla' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"},"columna":{"type":"string","description":"nombre o posicion 1..n"}},"required":["contenedor","tabla","columna"],"additionalProperties":false}"""),

        // ---- Relaciones ----
        new("agregar_relacion",
            "Crea una relacion entre dos tablas del MISMO contenedor. 'cardinalidad': n1 (muchos a uno / lookup) o " +
            "nn (muchos a muchos). 'contenedor', 'origen' y 'destino' = id o nombre de tabla.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"origen":{"type":"string","description":"tabla origen (id o nombre)"},"destino":{"type":"string","description":"tabla destino (id o nombre)"},"cardinalidad":{"type":"string","enum":["n1","nn"]},"nombre":{"type":"string"}},"required":["contenedor","origen","destino","cardinalidad"],"additionalProperties":false}"""),
        new("eliminar_relacion",
            "Borra una relacion por su id (ver ver_contenedor_datos).",
            """{"type":"object","properties":{"relacion_id":{"type":"string"}},"required":["relacion_id"],"additionalProperties":false}"""),

        // ---- Conectores (fuente) ----
        new("listar_conectores",
            "Lista los conectores (fuentes) de un contenedor: id, nombre, tipo (excel/api/bd/tabla), si ya tiene " +
            "credenciales, tabla destino y si esta activo. 'contenedor' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"}},"required":["contenedor"],"additionalProperties":false}"""),
        new("guardar_conector",
            "Crea o edita un conector (fuente de datos) de un contenedor. 'tipo': excel, api (REST), bd (base de datos) " +
            "o tabla (desde otra tabla del mismo contenedor). IMPORTANTE: el MCP NO recibe credenciales; el secreto " +
            "(clave de API / contrasena) se pone en la pantalla. 'destino' = tabla donde aterrizan los datos. Para " +
            "editar, envia 'conector' (id o nombre).",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"conector":{"type":"string","description":"id o nombre si editas; omitelo para crear"},"nombre":{"type":"string"},"tipo":{"type":"string","enum":["excel","api","bd","tabla"]},"destino":{"type":"string","description":"tabla destino (id o nombre)"},"endpoint":{"type":"string","description":"API: URL"},"metodo":{"type":"string","description":"API: GET/POST"},"auth":{"type":"string","enum":["ninguna","apikey","bearer","basic","tokenexchange"]},"motor":{"type":"string","enum":["postgresql","mysql","sqlserver","oracle","mariadb","sqlite"]},"host":{"type":"string"},"puerto":{"type":"integer"},"base_datos":{"type":"string"},"usuario":{"type":"string"},"query":{"type":"string","description":"BD: SELECT"},"mapeo_json":{"type":"string"},"activo":{"type":"boolean"}},"required":["contenedor","nombre","tipo"],"additionalProperties":false}"""),
        new("eliminar_conector",
            "Borra un conector de un contenedor. 'contenedor' = id o nombre; 'conector' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"conector":{"type":"string"}},"required":["contenedor","conector"],"additionalProperties":false}"""),

        // ---- Programaciones (horarios) ----
        new("listar_programaciones_importacion",
            "Lista los procesos de importacion (horarios) de un contenedor: id, nombre, tipo (manual/intervalo/cron), " +
            "cada cuanto, modo, activo y proxima/ultima corrida. 'contenedor' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"}},"required":["contenedor"],"additionalProperties":false}"""),
        new("guardar_programacion_importacion",
            "Crea o edita un proceso de importacion (horario) de un contenedor. 'tipo': manual, intervalo (cada N " +
            "minutos en 'cada_minutos') o cron ('cron' de 5 campos, hora del tenant). 'modo': append, replace o " +
            "upsert (con 'columna_clave'). 'conector' (opcional) = fuente a disparar. Para editar envia 'proceso'.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"proceso":{"type":"string","description":"id o nombre si editas; omitelo para crear"},"nombre":{"type":"string"},"tipo":{"type":"string","enum":["manual","intervalo","cron"]},"cada_minutos":{"type":"integer"},"cron":{"type":"string"},"modo":{"type":"string","enum":["append","replace","upsert"]},"columna_clave":{"type":"string"},"conector":{"type":"string","description":"id o nombre del conector"},"activa":{"type":"boolean"}},"required":["contenedor","nombre","tipo"],"additionalProperties":false}"""),
        new("eliminar_programacion_importacion",
            "Borra un proceso de importacion (horario). 'contenedor' = id o nombre; 'proceso' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"proceso":{"type":"string"}},"required":["contenedor","proceso"],"additionalProperties":false}"""),
        new("listar_corridas_importacion",
            "Bitacora de un proceso de importacion, mas recientes primero: cuando corrio, disparador (manual/programada), " +
            "resultado (ok/error/omitida/corriendo/agente_desconectado), filas insertadas/actualizadas/borradas y detalle. " +
            "'contenedor' = id o nombre; 'proceso' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"proceso":{"type":"string"},"limite":{"type":"integer","minimum":1,"maximum":50}},"required":["contenedor","proceso"],"additionalProperties":false}"""),

        // ---- Publicar / privacidad ----
        new("ver_publicacion_contenedor",
            "Dice si una tabla esta publicada como modulo del menu: ruta, icono y columnas de grilla/filtro. " +
            "'contenedor' y 'tabla' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"}},"required":["contenedor","tabla"],"additionalProperties":false}"""),
        new("publicar_contenedor",
            "Publica una tabla RAIZ como modulo del menu (aparece en el menu y en la matriz de Roles y permisos, donde " +
            "defines quien la ve). 'vista' (opcional) = vista de menu destino por nombre; sin ella, la vista por defecto " +
            "del tenant. 'padre' = nombre de la SECCION del menu donde colgar el modulo (un elemento no puede colgar de la " +
            "raiz: si la omites y la vista no tiene donde, el menu lo rechaza). 'icono' opcional. 'contenedor' y 'tabla' = " +
            "id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"},"vista":{"type":"string"},"padre":{"type":"string"},"icono":{"type":"string"}},"required":["contenedor","tabla"],"additionalProperties":false}"""),
        new("despublicar_contenedor",
            "Retira una tabla del menu (deja de ser modulo). No borra la tabla ni sus datos y conserva la ruta, asi que " +
            "al republicar los permisos siguen valiendo. 'contenedor' y 'tabla' = id o nombre.",
            """{"type":"object","properties":{"contenedor":{"type":"string"},"tabla":{"type":"string"}},"required":["contenedor","tabla"],"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> ExecuteContainerAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_dataModel is null) { return Err("El modulo Contenedor de datos no esta disponible."); }

        // Permiso del modulo "contenedor-datos" (misma matriz que la pantalla; Owner/Admin y sin-rol = sin restriccion).
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = tool switch
        {
            "listar_contenedores_datos" or "ver_contenedor_datos" or "listar_conectores"
                or "listar_programaciones_importacion" or "listar_corridas_importacion" or "ver_publicacion_contenedor"
                => NeedContainer(perms, PermissionAction.View, "consultar contenedores de datos"),
            "crear_contenedor_datos" => NeedContainer(perms, PermissionAction.Create, "crear contenedores de datos"),
            "eliminar_contenedor_datos" or "eliminar_tabla" or "eliminar_columna" or "eliminar_relacion"
                or "eliminar_conector" or "eliminar_programacion_importacion" or "despublicar_contenedor"
                => NeedContainer(perms, PermissionAction.Delete, "borrar en contenedores de datos"),
            _ => NeedContainer(perms, PermissionAction.Edit, "editar contenedores de datos")
        };
        if (denied is not null) { return Err(denied); }

        try
        {
            return tool switch
            {
                "listar_contenedores_datos" => await ListModelsAsync(ct),
                "ver_contenedor_datos" => await ViewModelAsync(args, ct),
                "crear_contenedor_datos" => await CreateModelAsync(args, actor, ct),
                "editar_contenedor_datos" => await EditModelAsync(args, actor, ct),
                "eliminar_contenedor_datos" => await DeleteModelAsync(args, actor, ct),
                "agregar_tabla" => await AddTableAsync(args, actor, ct),
                "editar_tabla" => await EditTableAsync(args, actor, ct),
                "eliminar_tabla" => await DeleteTableAsync(args, actor, ct),
                "agregar_columna" => await AddColumnAsync(args, actor, ct),
                "editar_columna" => await EditColumnAsync(args, actor, ct),
                "eliminar_columna" => await DeleteColumnAsync(args, actor, ct),
                "agregar_relacion" => await AddRelationAsync(args, actor, ct),
                "eliminar_relacion" => await DeleteRelationAsync(args, actor, ct),
                "listar_conectores" => await ListConnectorsAsync(args, ct),
                "guardar_conector" => await SaveConnectorAsync(args, actor, ct),
                "eliminar_conector" => await DeleteConnectorAsync(args, actor, ct),
                "listar_programaciones_importacion" => await ListProcessesAsync(args, ct),
                "guardar_programacion_importacion" => await SaveProcessAsync(args, actor, ct),
                "eliminar_programacion_importacion" => await DeleteProcessAsync(args, actor, ct),
                "listar_corridas_importacion" => await ListRunsAsync(args, ct),
                "ver_publicacion_contenedor" => await ViewPublicationAsync(args, ct),
                "publicar_contenedor" => await PublishAsync(args, ct),
                "despublicar_contenedor" => await UnpublishAsync(args, ct),
                _ => Err($"Herramienta desconocida: {tool}")
            };
        }
        catch (InvalidOperationException ex) { return Err(ex.Message); }
    }

    private static string? NeedContainer(EffectivePermissions perms, PermissionAction action, string what)
        => perms.Can(ContainerModuleKey, action) ? null : $"Tu usuario no tiene permiso para {what}.";

    // ===================== Contenedor (DataModel) =====================

    private async Task<McpToolResult> ListModelsAsync(CancellationToken ct)
    {
        var list = await _dataModel!.ListAsync(ct);
        return Ok(new
        {
            contenedores = list.Select(m => new
            {
                contenedor_id = m.Id,
                nombre = m.Name,
                descripcion = m.Description,
                tablas = m.TableCount,
                relaciones = m.RelationCount,
                actualizado = m.UpdatedAt
            })
        });
    }

    private async Task<McpToolResult> ViewModelAsync(JsonElement args, CancellationToken ct)
    {
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var tableName = detail!.Tables.ToDictionary(t => t.Id, t => t.Name);
        return Ok(new
        {
            contenedor_id = detail.Id,
            nombre = detail.Name,
            descripcion = detail.Description,
            tablas = detail.Tables.Select(t => new
            {
                tabla_id = t.Id,
                nombre = t.Name,
                descripcion = t.Description,
                derivada_de = t.DerivedFromName,
                columnas = t.Columns.Select((c, i) => new
                {
                    n = i + 1,
                    columna_id = c.Id,
                    nombre = c.Name,
                    tipo = ColumnTypeEs(c.Type),
                    requerido = c.IsRequired,
                    hijo = c.ChildContainerName
                })
            }),
            relaciones = detail.Relations.Select(r => new
            {
                relacion_id = r.Id,
                origen = tableName.TryGetValue(r.FromTableId, out var fn) ? fn : r.FromTableId.ToString(),
                destino = tableName.TryGetValue(r.ToTableId, out var tn) ? tn : r.ToTableId.ToString(),
                cardinalidad = RelationKindEs(r.Kind),
                nombre = r.Name
            })
        });
    }

    private async Task<McpToolResult> CreateModelAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' del contenedor."); }
        var dto = await _dataModel!.SaveModelAsync(new SaveDataModelRequest(null, nombre, Str(args, "descripcion")), actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo crear el contenedor."); }
        return Ok(new { ok = true, contenedor_id = dto.Id, nombre = dto.Name });
    }

    private async Task<McpToolResult> EditModelAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var dto = await _dataModel!.SaveModelAsync(new SaveDataModelRequest(
            detail!.Id,
            Str(args, "nombre") ?? detail.Name,
            args.TryGetProperty("descripcion", out _) ? Str(args, "descripcion") : detail.Description),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo editar el contenedor."); }
        return Ok(new { ok = true, contenedor_id = dto.Id, nombre = dto.Name });
    }

    private async Task<McpToolResult> DeleteModelAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var done = await _dataModel!.DeleteModelAsync(detail!.Id, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, contenedor_id = detail.Id, borrado = true }) : Err("No se pudo borrar el contenedor.");
    }

    // ===================== Tablas =====================

    private async Task<McpToolResult> AddTableAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' de la tabla."); }

        var (columns, colErr) = BuildNewColumns(args, "columnas");
        if (colErr is not null) { return Err(colErr); }

        // Posicion en el lienzo: escalonada segun cuantas tablas ya hay.
        var n = detail!.Tables.Count;
        var dto = await _dataModel!.SaveTableAsync(new SaveModelTableRequest(
            detail.Id, null, nombre, Str(args, "descripcion"), 60 + (n % 4) * 240, 60 + (n / 4) * 200, columns),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo agregar la tabla."); }
        return Ok(new { ok = true, tabla_id = dto.Id, nombre = dto.Name, columnas = dto.Columns.Count });
    }

    private async Task<McpToolResult> EditTableAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var dto = await _dataModel!.SaveTableAsync(new SaveModelTableRequest(
            detail!.Id, table!.Id,
            Str(args, "nombre") ?? table.Name,
            args.TryGetProperty("descripcion", out _) ? Str(args, "descripcion") : table.Description,
            table.CanvasX, table.CanvasY, KeepColumns(table.Columns)),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo editar la tabla."); }
        return Ok(new { ok = true, tabla_id = dto.Id, nombre = dto.Name });
    }

    private async Task<McpToolResult> DeleteTableAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (_, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var done = await _dataModel!.DeleteTableAsync(table!.Id, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, tabla_id = table.Id, borrado = true }) : Err("No se pudo borrar la tabla.");
    }

    // ===================== Columnas (replace-all via SaveTableAsync) =====================

    private async Task<McpToolResult> AddColumnAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' de la columna."); }
        var tipo = ParseColumnType(Str(args, "tipo"));
        if (tipo is null) { return Err($"Tipo de columna desconocido. Tipos validos: {TiposColumnaDoc}"); }
        if (table!.Columns.Any(c => string.Equals(c.Name.Trim(), nombre, StringComparison.OrdinalIgnoreCase)))
        {
            return Err($"La tabla '{table.Name}' ya tiene una columna llamada '{nombre}'.");
        }

        var cols = KeepColumns(table.Columns).ToList();
        cols.Add(new SaveDataColumnInput(
            null, nombre, null, tipo.Value, cols.Count, Bool(args, "requerido") ?? false,
            tipo.Value == DataContainerColumnType.Submodel ? Guid2(args, "hijo") : null));

        var dto = await _dataModel!.SaveTableAsync(new SaveModelTableRequest(
            detail!.Id, table.Id, table.Name, table.Description, table.CanvasX, table.CanvasY, cols),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo agregar la columna."); }
        return Ok(new { ok = true, tabla_id = dto.Id, columna = nombre, tipo = ColumnTypeEs(tipo.Value) });
    }

    private async Task<McpToolResult> EditColumnAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var cols = KeepColumns(table!.Columns).ToList();
        var idx = FindColumnIndex(cols, Str(args, "columna"));
        if (idx < 0) { return Err($"No encontre la columna '{Str(args, "columna")}' en la tabla '{table.Name}'."); }

        var cur = cols[idx];
        var newType = cur.Type;
        if (args.TryGetProperty("tipo", out _))
        {
            var parsed = ParseColumnType(Str(args, "tipo"));
            if (parsed is null) { return Err($"Tipo de columna desconocido. Tipos validos: {TiposColumnaDoc}"); }
            newType = parsed.Value;
        }
        cols[idx] = cur with
        {
            Name = Str(args, "nombre") ?? cur.Name,
            Type = newType,
            IsRequired = Bool(args, "requerido") ?? cur.IsRequired
        };

        var dto = await _dataModel!.SaveTableAsync(new SaveModelTableRequest(
            detail!.Id, table.Id, table.Name, table.Description, table.CanvasX, table.CanvasY, cols),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo editar la columna."); }
        return Ok(new { ok = true, tabla_id = dto.Id, columna = cols[idx].Name, tipo = ColumnTypeEs(cols[idx].Type) });
    }

    private async Task<McpToolResult> DeleteColumnAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var cols = KeepColumns(table!.Columns).ToList();
        var idx = FindColumnIndex(cols, Str(args, "columna"));
        if (idx < 0) { return Err($"No encontre la columna '{Str(args, "columna")}' en la tabla '{table.Name}'."); }
        var removed = cols[idx].Name;
        cols.RemoveAt(idx);
        // Re-numera el orden para que quede compacto.
        for (var i = 0; i < cols.Count; i++) { cols[i] = cols[i] with { SortOrder = i }; }

        var dto = await _dataModel!.SaveTableAsync(new SaveModelTableRequest(
            detail!.Id, table.Id, table.Name, table.Description, table.CanvasX, table.CanvasY, cols),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo borrar la columna."); }
        return Ok(new { ok = true, tabla_id = dto.Id, columna = removed, borrado = true });
    }

    // ===================== Relaciones =====================

    private async Task<McpToolResult> AddRelationAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var origen = FindTable(detail!, Str(args, "origen"));
        if (origen is null) { return Err($"No encontre la tabla origen '{Str(args, "origen")}'."); }
        var destino = FindTable(detail, Str(args, "destino"));
        if (destino is null) { return Err($"No encontre la tabla destino '{Str(args, "destino")}'."); }
        var kind = ParseRelationKind(Str(args, "cardinalidad"));
        if (kind is null) { return Err("'cardinalidad' debe ser n1 (muchos a uno) o nn (muchos a muchos)."); }

        var dto = await _dataModel!.AddRelationAsync(new SaveModelRelationRequest(
            detail.Id, origen.Id, destino.Id, kind.Value, Str(args, "nombre")), actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo crear la relacion (revisa que ambas tablas sean del mismo contenedor y distintas)."); }
        return Ok(new { ok = true, relacion_id = dto.Id, origen = origen.Name, destino = destino.Name, cardinalidad = RelationKindEs(dto.Kind) });
    }

    private async Task<McpToolResult> DeleteRelationAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var relId = Guid2(args, "relacion_id");
        if (relId is null) { return Err("Indica el 'relacion_id' (ver ver_contenedor_datos)."); }
        var done = await _dataModel!.DeleteRelationAsync(relId.Value, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, relacion_id = relId.Value, borrado = true }) : Err("No encontre esa relacion.");
    }

    // ===================== Conectores =====================

    private async Task<McpToolResult> ListConnectorsAsync(JsonElement args, CancellationToken ct)
    {
        if (_dataImport is null) { return Err("La configuracion de importacion no esta disponible."); }
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var list = await _dataImport.ListConnectorsAsync(detail!.Id, ct);
        return Ok(new
        {
            conectores = list.Select(c => new
            {
                conector_id = c.Id,
                nombre = c.Name,
                tipo = ConnectorKindEs(c.Kind),
                tiene_credenciales = c.HasCredentials,
                destino = c.ContainerName,
                activo = c.IsActive
            })
        });
    }

    private async Task<McpToolResult> SaveConnectorAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_dataImport is null) { return Err("La configuracion de importacion no esta disponible."); }
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' del conector."); }
        var kind = ParseConnectorKind(Str(args, "tipo"));
        if (kind is null) { return Err("'tipo' debe ser excel, api, bd o tabla."); }

        // Edicion: resuelve el conector existente por id o nombre (dentro del contenedor).
        Guid? id = null;
        var conectorArg = Str(args, "conector");
        if (conectorArg is not null)
        {
            var existing = await ResolveConnectorAsync(detail!.Id, conectorArg, ct);
            if (existing is null) { return Err($"No encontre el conector '{conectorArg}' en este contenedor."); }
            id = existing.Id;
        }

        // Tabla destino (opcional): por id o nombre dentro del contenedor.
        Guid? destino = null;
        var destinoArg = Str(args, "destino");
        if (destinoArg is not null)
        {
            var t = FindTable(detail!, destinoArg);
            if (t is null) { return Err($"No encontre la tabla destino '{destinoArg}' en este contenedor."); }
            destino = t.Id;
        }

        var dto = await _dataImport.SaveConnectorAsync(new SaveDataConnectorRequest(
            Id: id, ModelId: detail!.Id, Name: nombre, Kind: kind.Value,
            EndpointUrl: Str(args, "endpoint"), HttpMethod: Str(args, "metodo"),
            AuthKind: ParseAuthKind(Str(args, "auth")),
            DbEngine: ParseDbEngine(Str(args, "motor")), Host: Str(args, "host"), Port: Int(args, "puerto"),
            DatabaseName: Str(args, "base_datos"), Username: Str(args, "usuario"),
            Credentials: null, // El MCP nunca recibe el secreto; se pone en la pantalla.
            Query: Str(args, "query"), MappingJson: Str(args, "mapeo_json"),
            IsActive: Bool(args, "activo") ?? true, ContainerId: destino),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo guardar el conector."); }
        return Ok(new
        {
            ok = true,
            conector_id = dto.Id,
            nombre = dto.Name,
            tipo = ConnectorKindEs(dto.Kind),
            tiene_credenciales = dto.HasCredentials,
            aviso = dto.HasCredentials ? null : "Sin credenciales: ponlas en la pantalla de Contenedor de datos (el MCP no recibe secretos)."
        });
    }

    private async Task<McpToolResult> DeleteConnectorAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_dataImport is null) { return Err("La configuracion de importacion no esta disponible."); }
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var existing = await ResolveConnectorAsync(detail!.Id, Str(args, "conector"), ct);
        if (existing is null) { return Err($"No encontre el conector '{Str(args, "conector")}' en este contenedor."); }
        var done = await _dataImport.DeleteConnectorAsync(existing.Id, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, conector_id = existing.Id, borrado = true }) : Err("No se pudo borrar el conector.");
    }

    // ===================== Programaciones (horarios) =====================

    private async Task<McpToolResult> ListProcessesAsync(JsonElement args, CancellationToken ct)
    {
        if (_dataImport is null) { return Err("La configuracion de importacion no esta disponible."); }
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var list = await _dataImport.ListProcessesAsync(detail!.Id, ct);
        return Ok(new { programaciones = list.Select(ProcessView) });
    }

    private async Task<McpToolResult> SaveProcessAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_dataImport is null) { return Err("La configuracion de importacion no esta disponible."); }
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' de la programacion."); }
        var kind = ParseScheduleKind(Str(args, "tipo"));
        if (kind is null) { return Err("'tipo' debe ser manual, intervalo o cron."); }

        Guid? id = null;
        var procArg = Str(args, "proceso");
        if (procArg is not null)
        {
            var existing = await ResolveProcessAsync(detail!.Id, procArg, ct);
            if (existing is null) { return Err($"No encontre la programacion '{procArg}' en este contenedor."); }
            id = existing.Id;
        }

        Guid? connectorId = null;
        var conectorArg = Str(args, "conector");
        if (conectorArg is not null)
        {
            var conn = await ResolveConnectorAsync(detail!.Id, conectorArg, ct);
            if (conn is null) { return Err($"No encontre el conector '{conectorArg}' en este contenedor."); }
            connectorId = conn.Id;
        }

        var dto = await _dataImport.SaveProcessAsync(new SaveImportProcessRequest(
            Id: id, ModelId: detail!.Id, ConnectorId: connectorId, ClientId: null, Name: nombre,
            ScheduleKind: kind.Value, IntervalMinutes: Int(args, "cada_minutos"), CronExpression: Str(args, "cron"),
            IsActive: Bool(args, "activa") ?? true,
            Mode: ParseImportMode(Str(args, "modo")), KeyColumn: Str(args, "columna_clave")),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo guardar la programacion."); }
        return Ok(new { ok = true, programacion = ProcessView(dto) });
    }

    private async Task<McpToolResult> DeleteProcessAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_dataImport is null) { return Err("La configuracion de importacion no esta disponible."); }
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var existing = await ResolveProcessAsync(detail!.Id, Str(args, "proceso"), ct);
        if (existing is null) { return Err($"No encontre la programacion '{Str(args, "proceso")}' en este contenedor."); }
        var done = await _dataImport.DeleteProcessAsync(existing.Id, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, programacion_id = existing.Id, borrado = true }) : Err("No se pudo borrar la programacion.");
    }

    private async Task<McpToolResult> ListRunsAsync(JsonElement args, CancellationToken ct)
    {
        if (_dataImport is null) { return Err("La configuracion de importacion no esta disponible."); }
        var (detail, error) = await ResolveModelDetailAsync(Str(args, "contenedor"), ct);
        if (error is not null) { return Err(error); }
        var proc = await ResolveProcessAsync(detail!.Id, Str(args, "proceso"), ct);
        if (proc is null) { return Err($"No encontre la programacion '{Str(args, "proceso")}' en este contenedor."); }
        var take = Math.Clamp(Int(args, "limite") ?? 10, 1, 50);
        var runs = await _dataImport.ListRunsAsync(proc.Id, take, ct);
        return Ok(new
        {
            programacion_id = proc.Id,
            corridas = runs.Select(r => new
            {
                corrida_id = r.Id,
                cuando = r.FiredAt,
                disparador = r.Trigger == ImportRunTrigger.Scheduled ? "programada" : "manual",
                resultado = RunResultEs(r.Result),
                insertadas = r.Inserted,
                actualizadas = r.Updated,
                borradas = r.Deleted,
                detalle = r.Detail,
                termino = r.FinishedAt
            })
        });
    }

    // ===================== Publicar / privacidad =====================

    private async Task<McpToolResult> ViewPublicationAsync(JsonElement args, CancellationToken ct)
    {
        if (_dataModule is null) { return Err("La publicacion de modulos no esta disponible."); }
        var (_, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var dto = await _dataModule.GetAsync(table!.Id, ct);
        if (dto is null) { return Err("No encontre esa tabla."); }
        return Ok(new
        {
            tabla_id = dto.ContainerId,
            nombre = dto.ContainerName,
            publicada = dto.IsPublished,
            ruta = dto.ModuleRoute,
            icono = dto.ModuleIcon,
            columnas_grilla = dto.ListColumnIds,
            columnas_filtro = dto.FilterColumnIds
        });
    }

    private async Task<McpToolResult> PublishAsync(JsonElement args, CancellationToken ct)
    {
        if (_dataModule is null || _menu is null) { return Err("La publicacion de modulos no esta disponible."); }
        var (_, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }

        var (viewId, viewErr) = await ResolveMenuViewAsync(Str(args, "vista"), ct);
        if (viewErr is not null) { return Err(viewErr); }
        var (parentId, parentErr) = await ResolveMenuNodeAsync(viewId!.Value, Str(args, "padre"), ct);
        if (parentErr is not null) { return Err(parentErr); }

        // Por defecto todas las columnas escalares van a la grilla (el usuario afina luego en la pantalla).
        var listCols = table!.Columns
            .Where(c => c.Type != DataContainerColumnType.Submodel)
            .Select(c => c.Id).ToList();

        var res = await _dataModule.PublishAsync(new PublishContainerRequest(
            ContainerId: table.Id, MenuViewId: viewId.Value, ParentNodeId: parentId,
            Icon: Str(args, "icono"), ListColumnIds: listCols, FilterColumnIds: null), ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo publicar la tabla."); }
        return Ok(new { ok = true, tabla_id = res.Value.ContainerId, nombre = res.Value.ContainerName, ruta = res.Value.ModuleRoute, publicada = res.Value.IsPublished });
    }

    private async Task<McpToolResult> UnpublishAsync(JsonElement args, CancellationToken ct)
    {
        if (_dataModule is null) { return Err("La publicacion de modulos no esta disponible."); }
        var (_, table, error) = await ResolveTableAsync(Str(args, "contenedor"), Str(args, "tabla"), ct);
        if (error is not null) { return Err(error); }
        var res = await _dataModule.UnpublishAsync(table!.Id, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo despublicar la tabla."); }
        return Ok(new { ok = true, tabla_id = table.Id, publicada = false });
    }

    // ===================== Resolucion =====================

    private async Task<(DataModelDetailDto? detail, string? error)> ResolveModelDetailAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) { return (null, "Indica el contenedor (id o nombre)."); }
        if (Guid.TryParse(key, out var g))
        {
            var one = await _dataModel!.GetAsync(g, ct);
            return one is null ? (null, $"No encontre un contenedor con id '{key}'.") : (one, null);
        }
        var list = await _dataModel!.ListAsync(ct);
        var exact = list.Where(m => string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        var pick = exact.Count > 0 ? exact : list.Where(m => m.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pick.Count == 0) { return (null, $"No encontre ningun contenedor que coincida con '{key}'."); }
        if (pick.Count > 1) { return (null, $"Hay varios contenedores que coinciden con '{key}': {string.Join(", ", pick.Take(5).Select(m => m.Name))}. Usa el id."); }
        var full = await _dataModel!.GetAsync(pick[0].Id, ct);
        return full is null ? (null, $"No pude abrir el contenedor '{key}'.") : (full, null);
    }

    private async Task<(DataModelDetailDto? detail, ModelTableDto? table, string? error)> ResolveTableAsync(
        string? modelKey, string? tableKey, CancellationToken ct)
    {
        var (detail, error) = await ResolveModelDetailAsync(modelKey, ct);
        if (error is not null) { return (null, null, error); }
        if (string.IsNullOrWhiteSpace(tableKey)) { return (null, null, "Indica la tabla (id o nombre)."); }
        var table = FindTable(detail!, tableKey);
        return table is null
            ? (null, null, $"No encontre la tabla '{tableKey}' en el contenedor '{detail!.Name}'.")
            : (detail, table, null);
    }

    private static ModelTableDto? FindTable(DataModelDetailDto detail, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) { return null; }
        if (Guid.TryParse(key, out var g)) { return detail.Tables.FirstOrDefault(t => t.Id == g); }
        var exact = detail.Tables.FirstOrDefault(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) { return exact; }
        var partial = detail.Tables.Where(t => t.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private async Task<DataConnectorDto?> ResolveConnectorAsync(Guid modelId, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || _dataImport is null) { return null; }
        var list = await _dataImport.ListConnectorsAsync(modelId, ct);
        if (Guid.TryParse(key, out var g)) { return list.FirstOrDefault(c => c.Id == g); }
        var exact = list.FirstOrDefault(c => string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) { return exact; }
        var partial = list.Where(c => c.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private async Task<ImportProcessDto?> ResolveProcessAsync(Guid modelId, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || _dataImport is null) { return null; }
        var list = await _dataImport.ListProcessesAsync(modelId, ct);
        if (Guid.TryParse(key, out var g)) { return list.FirstOrDefault(p => p.Id == g); }
        var exact = list.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) { return exact; }
        var partial = list.Where(p => p.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private async Task<(Guid? viewId, string? error)> ResolveMenuViewAsync(string? viewName, CancellationToken ct)
    {
        var views = await _menu!.ListViewsAsync(ct);
        if (views.Count == 0) { return (null, "El tenant no tiene vistas de menu."); }
        if (!string.IsNullOrWhiteSpace(viewName))
        {
            var hit = views.FirstOrDefault(v => string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase))
                      ?? SingleOrNull(views.Where(v => v.Name.Contains(viewName, StringComparison.OrdinalIgnoreCase)));
            return hit is null ? (null, $"No encontre una vista de menu llamada '{viewName}'.") : (hit.Id, null);
        }
        var def = views.FirstOrDefault(v => v.IsDefault) ?? (views.Count == 1 ? views[0] : null);
        return def is null ? (null, "Indica 'vista': hay varias vistas de menu y ninguna por defecto.") : (def.Id, null);
    }

    private async Task<(Guid? parentId, string? error)> ResolveMenuNodeAsync(Guid viewId, string? nodeName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeName)) { return (null, null); } // raiz
        var tree = await _menu!.GetViewTreeAsync(viewId, ct);
        if (!tree.IsOk || tree.Value is null) { return (null, "No pude leer el arbol de la vista de menu."); }
        var matches = new List<MenuEditorNodeDto>();
        CollectNodes(tree.Value.Roots, nodeName, matches);
        if (matches.Count == 0) { return (null, $"No encontre un nodo de menu llamado '{nodeName}' en esa vista."); }
        if (matches.Count > 1) { return (null, $"Hay varios nodos de menu que coinciden con '{nodeName}'. Usa un nombre mas preciso."); }
        return (matches[0].Id, null);
    }

    private static void CollectNodes(IReadOnlyList<MenuEditorNodeDto> nodes, string name, List<MenuEditorNodeDto> acc)
    {
        foreach (var n in nodes)
        {
            if (string.Equals(n.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) { acc.Add(n); }
            if (n.Children.Count > 0) { CollectNodes(n.Children, name, acc); }
        }
    }

    // ===================== Columnas: helpers =====================

    /// <summary>Convierte las columnas existentes a input CONSERVANDO su Id (para no recrearlas; replace-all por Id).</summary>
    private static List<SaveDataColumnInput> KeepColumns(IReadOnlyList<DataContainerColumnDto> cols)
        => cols.Select(c => new SaveDataColumnInput(c.Id, c.Name, c.Description, c.Type, c.SortOrder, c.IsRequired, c.ChildContainerId)).ToList();

    /// <summary>Construye columnas NUEVAS (Id null) desde el arg 'columnas' (lista de {nombre, tipo, requerido}).</summary>
    private (IReadOnlyList<SaveDataColumnInput> cols, string? error) BuildNewColumns(JsonElement args, string prop)
    {
        var result = new List<SaveDataColumnInput>();
        if (!args.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) { return (result, null); }
        var i = 0;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) { continue; }
            var nombre = Str(e, "nombre");
            if (string.IsNullOrWhiteSpace(nombre)) { return (result, "Cada columna necesita un 'nombre'."); }
            var tipo = ParseColumnType(Str(e, "tipo")) ?? DataContainerColumnType.Text;
            result.Add(new SaveDataColumnInput(null, nombre, null, tipo, i++, Bool(e, "requerido") ?? false, null));
        }
        return (result, null);
    }

    private static int FindColumnIndex(List<SaveDataColumnInput> cols, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) { return -1; }
        if (int.TryParse(key, out var pos) && pos >= 1 && pos <= cols.Count) { return pos - 1; }
        var exact = cols.FindIndex(c => string.Equals(c.Name.Trim(), key, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) { return exact; }
        var partial = Enumerable.Range(0, cols.Count).Where(i => cols[i].Name.Contains(key, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return partial.Count == 1 ? partial[0] : -1;
    }

    private static object ProcessView(ImportProcessDto p) => new
    {
        programacion_id = p.Id,
        nombre = p.Name,
        tipo = ScheduleKindEs(p.ScheduleKind),
        cada_minutos = p.IntervalMinutes,
        cron = p.CronExpression,
        modo = ImportModeEs(p.Mode),
        columna_clave = p.KeyColumn,
        conector = p.ConnectorName,
        activa = p.IsActive,
        proxima_corrida = p.NextRunAt,
        ultima_corrida = p.LastRunAt,
        aviso = p.DisabledReason
    };

    // ===================== Parsers de enum (alias en espanol) =====================

    private static DataContainerColumnType? ParseColumnType(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) { return null; }
        var v = t.Trim();
        if (Enum.TryParse<DataContainerColumnType>(v, ignoreCase: true, out var direct)
            && direct != DataContainerColumnType.Reference && direct != DataContainerColumnType.RelationMany)
        {
            return direct;
        }
        return v.ToLowerInvariant() switch
        {
            "texto" or "text" => DataContainerColumnType.Text,
            "numero" or "entero" or "number" => DataContainerColumnType.Number,
            "decimal" or "moneda" => DataContainerColumnType.Decimal,
            "fecha" or "date" => DataContainerColumnType.Date,
            "booleano" or "bool" or "si_no" => DataContainerColumnType.Boolean,
            "submodelo" or "submodel" or "sub_tabla" => DataContainerColumnType.Submodel,
            _ => null
        };
    }

    private static string ColumnTypeEs(DataContainerColumnType t) => t switch
    {
        DataContainerColumnType.Text => "texto",
        DataContainerColumnType.Number => "numero",
        DataContainerColumnType.Decimal => "decimal",
        DataContainerColumnType.Date => "fecha",
        DataContainerColumnType.Boolean => "booleano",
        DataContainerColumnType.Submodel => "submodelo",
        _ => t.ToString().ToLowerInvariant()
    };

    private static DataModelRelationKind? ParseRelationKind(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "n1" or "manytoone" or "muchos_a_uno" or "lookup" => DataModelRelationKind.ManyToOne,
        "nn" or "manytomany" or "muchos_a_muchos" => DataModelRelationKind.ManyToMany,
        _ => null
    };

    private static string RelationKindEs(DataModelRelationKind k) => k switch
    {
        DataModelRelationKind.ManyToOne => "n1",
        DataModelRelationKind.ManyToMany => "nn",
        _ => k.ToString().ToLowerInvariant()
    };

    private static ConnectorKind? ParseConnectorKind(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "excel" => ConnectorKind.Excel,
        "api" or "rest" or "restapi" or "api_rest" => ConnectorKind.RestApi,
        "bd" or "database" or "base_datos" or "basedatos" => ConnectorKind.Database,
        "tabla" or "fromtable" or "desde_tabla" or "desde_otra_tabla" => ConnectorKind.FromTable,
        _ => null
    };

    private static string ConnectorKindEs(ConnectorKind k) => k switch
    {
        ConnectorKind.Excel => "excel",
        ConnectorKind.RestApi => "api",
        ConnectorKind.Database => "bd",
        ConnectorKind.FromTable => "tabla",
        _ => k.ToString().ToLowerInvariant()
    };

    private static ConnectorAuthKind ParseAuthKind(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "apikey" or "api_key" => ConnectorAuthKind.ApiKey,
        "bearer" or "token" => ConnectorAuthKind.Bearer,
        "basic" => ConnectorAuthKind.Basic,
        "tokenexchange" or "intercambio" or "intercambio_token" => ConnectorAuthKind.TokenExchange,
        _ => ConnectorAuthKind.None
    };

    private static DbEngine? ParseDbEngine(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "postgresql" or "postgres" => DbEngine.PostgreSql,
        "mysql" => DbEngine.MySql,
        "sqlserver" or "mssql" => DbEngine.SqlServer,
        "oracle" => DbEngine.Oracle,
        "mariadb" => DbEngine.MariaDb,
        "sqlite" => DbEngine.SqLite,
        _ => null
    };

    private static ApiImportMode ParseImportMode(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "append" or "agregar" => ApiImportMode.Append,
        "upsert" or "reconciliar" => ApiImportMode.Upsert,
        _ => ApiImportMode.Replace
    };

    private static string ImportModeEs(ApiImportMode m) => m switch
    {
        ApiImportMode.Append => "append",
        ApiImportMode.Upsert => "upsert",
        _ => "replace"
    };
}
