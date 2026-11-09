using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Directorio;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// Herramientas del Directorio General (modulo 000232) en el servidor MCP (ADR-0159, addendum
/// "gestion de contactos"). Capa delgada sobre ITerceroService / ITerceroVinculoService /
/// ITerceroFieldService: no reimplementa reglas. Lo que SI agrega:
/// - Respeta los permisos del usuario en el Directorio (misma matriz que la pantalla).
/// - No duplica: una edicion que deja el NIT o el telefono de OTRO contacto se rechaza.
/// - Deja rastro: cada cambio va a la auditoria (antes/despues) y como nota en la ficha.
/// - Nada se borra: inactivar es una baja logica reversible y exige motivo.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const int MaxContactResults = 25;

    private static readonly HashSet<string> DirectoryToolNames = new(StringComparer.Ordinal)
    {
        "buscar_contacto", "ver_contacto", "crear_contacto", "editar_contacto", "nota_contacto",
        "vincular_persona_empresa", "contacto_interno", "tareas_del_contacto", "listar_campos_contacto",
        "inactivar_contacto", "reactivar_contacto", "vincular_contacto"
    };

    private static readonly AiToolSpec[] DirectorySpecs =
    {
        new("buscar_contacto",
            "Busca contactos (empresas o personas) del Directorio General. 'texto' busca en nombre, identificacion, " +
            "email y telefono. Filtros opcionales: tipo, ciudad, sector, estado (por defecto solo activos y prospectos).",
            """{"type":"object","properties":{"texto":{"type":"string"},"tipo":{"type":"string","enum":["empresa","persona"]},"ciudad":{"type":"string"},"sector":{"type":"string"},"estado":{"type":"string","enum":["activos","inactivos","todos"]}},"additionalProperties":false}"""),
        new("ver_contacto",
            "Ficha completa de un contacto: datos, campos configurables, personas/empresas vinculadas, ultimas notas " +
            "y resumen de tareas. 'contacto' = id, identificacion o nombre exacto (ver buscar_contacto).",
            """{"type":"object","properties":{"contacto":{"type":"string"}},"required":["contacto"],"additionalProperties":false}"""),
        new("crear_contacto",
            "Registra un contacto (empresa o persona) en el Directorio General. No duplica: si ya existe con la misma " +
            "identificacion o telefono, devuelve el existente. Devuelve 'contacto_id'.",
            """{"type":"object","properties":{"nombre":{"type":"string","description":"Persona o razon social"},"tipo":{"type":"string","enum":["empresa","persona"]},"identificacion":{"type":"string","description":"NIT o cedula"},"tipo_identificacion":{"type":"string","enum":["nit","cedula","correo","telefono"]},"ciudad":{"type":"string"},"email":{"type":"string"},"telefono":{"type":"string"},"sector":{"type":"string"},"cargo":{"type":"string"}},"required":["nombre"],"additionalProperties":false}"""),
        new("editar_contacto",
            "Cambia SOLO los datos indicados de un contacto (lo que no envies queda igual). 'campos' = campos " +
            "configurables de la ficha como {clave_o_etiqueta: valor} (ver listar_campos_contacto; si la clave esta en varias " +
            "fichas usa ficha.clave, ej. base.direccion); valor vacio lo limpia. " +
            "Se niega si el NIT o el telefono nuevo ya pertenece a otro contacto. Queda nota y auditoria del cambio.",
            """{"type":"object","properties":{"contacto":{"type":"string"},"nombre":{"type":"string"},"identificacion":{"type":"string"},"tipo_identificacion":{"type":"string","enum":["nit","cedula","correo","telefono","ninguno"]},"ciudad":{"type":"string"},"email":{"type":"string"},"telefono":{"type":"string"},"sector":{"type":"string"},"cargo":{"type":"string"},"estado":{"type":"string","enum":["activo","prospecto"]},"campos":{"type":"object","additionalProperties":{"type":["string","number","boolean","null"]}}},"required":["contacto"],"additionalProperties":false}"""),
        new("nota_contacto",
            "Agrega una nota a la ficha del contacto (accion='agregar', por defecto) o lista sus notas (accion='listar').",
            """{"type":"object","properties":{"contacto":{"type":"string"},"accion":{"type":"string","enum":["agregar","listar"]},"texto":{"type":"string"},"tipo":{"type":"string","description":"Nota, Llamada, Reunion, Correo... (por defecto Nota)"}},"required":["contacto"],"additionalProperties":false}"""),
        new("vincular_persona_empresa",
            "Liga una persona EXISTENTE a una empresa con su cargo (accion='vincular', por defecto; si ya estaba ligada " +
            "actualiza el cargo) o la desliga (accion='desvincular'). Para una persona nueva usa contacto_interno.",
            """{"type":"object","properties":{"persona":{"type":"string"},"empresa":{"type":"string"},"cargo":{"type":"string"},"accion":{"type":"string","enum":["vincular","desvincular"]}},"required":["persona","empresa"],"additionalProperties":false}"""),
        new("contacto_interno",
            "Agrega una persona dentro de la ficha de una empresa (ej. su contador o gerente de compras) con su cargo. " +
            "Si la persona ya existe (misma identificacion, email o telefono) la liga en vez de duplicarla. Para " +
            "editarla despues usa editar_contacto.",
            """{"type":"object","properties":{"empresa":{"type":"string"},"nombre":{"type":"string"},"cargo":{"type":"string"},"email":{"type":"string"},"telefono":{"type":"string"},"identificacion":{"type":"string","description":"Cedula (opcional)"}},"required":["empresa","nombre"],"additionalProperties":false}"""),
        new("tareas_del_contacto",
            "Tareas ligadas a un contacto (como cliente/solicitante), abiertas y/o terminadas. Solo las que el usuario puede ver.",
            """{"type":"object","properties":{"contacto":{"type":"string"},"estado":{"type":"string","enum":["abiertas","terminadas","todas"]}},"required":["contacto"],"additionalProperties":false}"""),
        new("listar_campos_contacto",
            "Campos configurables de la ficha del contacto (clave, etiqueta, tipo y opciones). Consultalo antes de " +
            "llenar 'campos' en editar_contacto: no inventes claves.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("inactivar_contacto",
            "Da de baja un contacto (estado Inactivo; NO se borra y se puede reactivar). Exige 'motivo'. Confirma con " +
            "el usuario antes de usarla.",
            """{"type":"object","properties":{"contacto":{"type":"string"},"motivo":{"type":"string"}},"required":["contacto","motivo"],"additionalProperties":false}"""),
        new("reactivar_contacto",
            "Reactiva un contacto inactivo (vuelve a estado Activo). Exige 'motivo'.",
            """{"type":"object","properties":{"contacto":{"type":"string"},"motivo":{"type":"string"}},"required":["contacto","motivo"],"additionalProperties":false}"""),
        new("vincular_contacto",
            "Liga un contacto del Directorio a una tarea (queda como su cliente/solicitante). 'contacto' = id, " +
            "identificacion o nombre exacto (ver buscar_contacto).",
            """{"type":"object","properties":{"numero":{"type":"string"},"contacto":{"type":"string"}},"required":["numero","contacto"],"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> ExecuteDirectoryAsync(string tool, JsonElement args, string argumentsJson, McpActor actor, CancellationToken ct)
    {
        if (_terceros is null) { return Err("El directorio no esta disponible."); }

        // Permisos del Directorio: la misma matriz que usa la pantalla (Owner/Admin y usuarios sin rol = sin restriccion).
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = tool switch
        {
            "buscar_contacto" or "ver_contacto" or "tareas_del_contacto" or "listar_campos_contacto"
                => Need(perms, PermissionAction.View, "consultar el Directorio"),
            "crear_contacto" => CheckCreate(perms, args),
            "contacto_interno" => Need(perms, PermissionAction.Edit, "editar contactos del Directorio")
                ?? (CanCreatePerson(perms) ? null : "Tu usuario no tiene permiso para crear personas en el Directorio."),
            "editar_contacto" or "nota_contacto" or "vincular_persona_empresa" or "reactivar_contacto"
                => tool == "nota_contacto" && Str(args, "accion") == "listar"
                    ? Need(perms, PermissionAction.View, "consultar el Directorio")
                    : Need(perms, PermissionAction.Edit, "editar contactos del Directorio"),
            "inactivar_contacto" => Need(perms, PermissionAction.Delete, "dar de baja contactos del Directorio"),
            _ => null // vincular_contacto cambia la TAREA, no el Directorio: la regla es la de la tarea.
        };
        if (denied is not null) { return Err(denied); }

        return tool switch
        {
            "buscar_contacto" => await SearchContactsAsync(args, ct),
            "ver_contacto" => await ViewContactAsync(args, actor, ct),
            "crear_contacto" => await CreateContactAsync(argumentsJson, actor, ct),
            "editar_contacto" => await EditContactAsync(args, actor, ct),
            "nota_contacto" => await ContactNoteAsync(args, actor, ct),
            "vincular_persona_empresa" => await LinkPersonCompanyAsync(args, actor, ct),
            "contacto_interno" => await AddInternalContactAsync(args, actor, ct),
            "tareas_del_contacto" => await ContactTasksAsync(args, actor, ct),
            "listar_campos_contacto" => await ListContactFieldsAsync(ct),
            "inactivar_contacto" => await SetContactStateAsync(args, actor, active: false, ct),
            "reactivar_contacto" => await SetContactStateAsync(args, actor, active: true, ct),
            "vincular_contacto" => await LinkContactAsync(args, actor, ct),
            _ => Err($"Herramienta desconocida: {tool}")
        };
    }

    private static string? Need(EffectivePermissions perms, PermissionAction action, string what)
        => perms.Can(DirectorioSubPermisos.ModuleRoute, action) ? null : $"Tu usuario no tiene permiso para {what}.";

    private static bool CanCreatePerson(EffectivePermissions perms)
        => perms.Can(DirectorioSubPermisos.CrearCliente, PermissionAction.Create)
           || perms.Can(DirectorioSubPermisos.CrearSospechoso, PermissionAction.Create);

    private static string? CheckCreate(EffectivePermissions perms, JsonElement args)
    {
        var persona = string.Equals(Str(args, "tipo"), "persona", StringComparison.OrdinalIgnoreCase);
        if (persona) { return CanCreatePerson(perms) ? null : "Tu usuario no tiene permiso para crear personas en el Directorio."; }
        return perms.Can(DirectorioSubPermisos.CrearEmpresa, PermissionAction.Create)
            ? null : "Tu usuario no tiene permiso para crear empresas en el Directorio.";
    }

    // ===================== Consulta =====================

    private async Task<McpToolResult> SearchContactsAsync(JsonElement args, CancellationToken ct)
    {
        var text = Str(args, "texto");
        var tipo = Str(args, "tipo");
        var ciudad = Str(args, "ciudad");
        var sector = Str(args, "sector");
        var estado = Str(args, "estado") ?? "activos";
        if (text is null && tipo is null && ciudad is null && sector is null)
        {
            return Err("Indica al menos 'texto' o un filtro (tipo, ciudad, sector).");
        }

        // El filtro global por tenant acota; aqui NUNCA se filtra a mano por TenantId.
        var q = _db.Terceros.AsNoTracking();
        q = estado switch
        {
            "inactivos" => q.Where(t => t.Estado == TerceroEstado.Inactivo),
            "todos" => q,
            _ => q.Where(t => t.Estado != TerceroEstado.Inactivo)
        };
        if (tipo == "empresa") { q = q.Where(t => t.Tipo == TerceroTipo.Empresa); }
        else if (tipo == "persona") { q = q.Where(t => t.Tipo == TerceroTipo.Persona); }
        if (ciudad is not null) { var c = ciudad.ToLower(); q = q.Where(t => t.Ciudad != null && t.Ciudad.ToLower().Contains(c)); }
        if (sector is not null) { var s = sector.ToLower(); q = q.Where(t => t.Sector != null && t.Sector.ToLower().Contains(s)); }
        if (text is not null)
        {
            var low = text.ToLower();
            var digits = Digits(text);
            q = q.Where(t => t.Nombre.ToLower().Contains(low)
                || (t.IdValor != null && t.IdValor.ToLower().Contains(low))
                || (t.Email != null && t.Email.ToLower().Contains(low))
                || (digits != null && digits.Length >= 5 && t.Telefono != null && t.Telefono.Contains(digits)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(t => t.Nombre).Take(MaxContactResults)
            .Select(t => new { t.Id, t.Nombre, t.Tipo, t.IdValor, t.Ciudad, t.Sector, t.Estado, t.Email, t.Telefono, t.EmpresaId })
            .ToListAsync(ct);
        var empresas = await NamesOfAsync(rows.Select(r => r.EmpresaId), ct);
        return Ok(new
        {
            total,
            mostrando = rows.Count,
            contactos = rows.Select(t => new
            {
                id = t.Id,
                nombre = t.Nombre,
                tipo = TipoEs(t.Tipo),
                identificacion = t.IdValor,
                ciudad = t.Ciudad,
                sector = t.Sector,
                estado = EstadoEs(t.Estado),
                email = t.Email,
                telefono = t.Telefono,
                empresa = t.EmpresaId is Guid e && empresas.TryGetValue(e, out var en) ? en : null
            })
        });
    }

    private async Task<McpToolResult> ViewContactAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (id, error) = await ResolveContactAsync(Str(args, "contacto"), includeInactive: true, ct);
        if (id is null) { return Err(error!); }
        var d = await _terceros!.GetAsync(id.Value, ct);
        if (d is null) { return Err("El contacto no existe."); }

        var vinculos = _vinculos is null ? Array.Empty<TerceroVinculoDto>() : await _vinculos.ListDeAsync(d.Id, ct);
        var notas = await _terceros.ListNotasAsync(d.Id, ct);
        var tareas = await VisibleContactTasksAsync(d.Id, actor, ct);
        var labels = await FieldLabelsAsync(ct);

        return Ok(new
        {
            id = d.Id,
            nombre = d.Nombre,
            tipo = TipoEs(d.Tipo),
            estado = EstadoEs(d.Estado),
            identificacion = d.Identificacion,
            ciudad = d.Ciudad,
            sector = d.Sector,
            cargo = d.Cargo,
            email = d.Email,
            telefono = d.Telefono,
            vendedor = d.VendedorAsesorNombre ?? d.Vendedor,
            empresa = d.EmpresaNombre,
            campos = d.Fichas.SelectMany(f => f.Value
                    .Where(v => !string.IsNullOrWhiteSpace(v.Value))
                    .Select(v => new { clave = v.Key, etiqueta = labels.TryGetValue(v.Key, out var l) ? l : v.Key, valor = v.Value }))
                .ToList(),
            vinculados = vinculos.Select(v => new { id = v.OtroId, nombre = v.OtroNombre, tipo = v.OtroEsEmpresa ? "empresa" : "persona", cargo = v.Cargo }),
            contactos_ligeros = d.Contactos.Where(c => !c.EsTercero).Select(c => new { c.Nombre, c.Cargo, c.Email, c.Telefono }),
            notas = notas.OrderByDescending(n => n.CreatedAt).Take(10)
                .Select(n => new { tipo = n.Accion, texto = Clip(n.Texto, 500), autor = n.Autor, fecha = n.CreatedAt }),
            total_notas = notas.Count,
            tareas = new
            {
                abiertas = tareas.Count(t => !IsClosed(t.Status)),
                terminadas = tareas.Count(t => IsClosed(t.Status)),
                ultimas = tareas.Take(5).Select(t => new { numero = t.Number, titulo = t.Title, estado = StatusEs(t.Status) })
            }
        });
    }

    private async Task<McpToolResult> ContactTasksAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (id, error) = await ResolveContactAsync(Str(args, "contacto"), includeInactive: true, ct);
        if (id is null) { return Err(error!); }
        var estado = Str(args, "estado") ?? "todas";
        var rows = (await VisibleContactTasksAsync(id.Value, actor, ct))
            .Where(t => estado == "todas" || (estado == "terminadas") == IsClosed(t.Status))
            .Take(MaxSearch).ToList();
        var boards = await BoardNamesAsync(rows.Select(r => r.BoardId), ct);
        var users = await UserNamesAsync(rows.Select(r => r.AssigneeTenantUserId), ct);
        return Ok(new
        {
            total = rows.Count,
            tareas = rows.Select(t => new
            {
                numero = t.Number,
                titulo = t.Title,
                estado = StatusEs(t.Status),
                tablero = t.BoardId is Guid b && boards.TryGetValue(b, out var bn) ? bn : null,
                encargado = t.AssigneeTenantUserId is Guid u && users.TryGetValue(u, out var un) ? un : null,
                vence = t.DueDate,
                cerrada = t.ClosedAt
            })
        });
    }

    private async Task<McpToolResult> ListContactFieldsAsync(CancellationToken ct)
    {
        if (_fields is null) { return Err("Los campos configurables no estan disponibles."); }
        var defs = (await _fields.ListFieldsAsync(ct)).Where(IsWritableField).ToList();
        return Ok(new
        {
            total = defs.Count,
            campos = defs.OrderBy(f => f.FichaKey).ThenBy(f => f.SortOrder).Select(f => new
            {
                clave = f.FieldKey,
                clave_completa = $"{f.FichaKey}.{f.FieldKey}",
                etiqueta = f.Label,
                ficha = f.FichaKey,
                tipo = f.FieldType.ToString(),
                opciones = string.IsNullOrWhiteSpace(f.Options) ? null : Clip(f.Options, 1000),
                descripcion = f.Description
            })
        });
    }

    // ===================== Cambios =====================

    /// <summary>Reusa crear_contacto del agente interno (misma deduplicacion por identificacion/telefono).</summary>
    private async Task<McpToolResult> CreateContactAsync(string argumentsJson, McpActor actor, CancellationToken ct)
    {
        if (_directorio is null) { return Err("El directorio no esta disponible."); }
        var res = await _directorio.ExecuteAsync("crear_contacto", argumentsJson, actor.PlatformUserId, autonomous: false, ct);
        bool isError;
        try
        {
            using var doc = JsonDocument.Parse(res.Json);
            isError = doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False;
        }
        catch (JsonException) { isError = true; }
        return new McpToolResult(res.Json, isError);
    }

    private async Task<McpToolResult> EditContactAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (id, error) = await ResolveContactAsync(Str(args, "contacto"), includeInactive: true, ct);
        if (id is null) { return Err(error!); }
        var d = await _terceros!.GetAsync(id.Value, ct);
        if (d is null) { return Err("El contacto no existe."); }

        var changes = new List<(string Campo, string? Antes, string? Despues)>();
        string? Pick(string arg, string label, string? current)
        {
            if (!args.TryGetProperty(arg, out _)) { return current; }
            var v = Str(args, arg);
            if (!Same(current, v)) { changes.Add((label, current, v)); }
            return v;
        }

        var nombre = Pick("nombre", "nombre", d.Nombre);
        if (string.IsNullOrWhiteSpace(nombre)) { return Err("El nombre no puede quedar vacio."); }
        var idTipo = d.IdTipo;
        if (Str(args, "tipo_identificacion") is string tiArg)
        {
            var parsed = ParseIdTipoArg(tiArg);
            if (parsed is null) { return Err($"Tipo de identificacion '{tiArg}' no valido."); }
            if (parsed != d.IdTipo) { changes.Add(("tipo_identificacion", d.IdTipo.ToString(), parsed.ToString())); }
            idTipo = parsed.Value;
        }
        var idValor = Pick("identificacion", "identificacion", d.IdValor);
        if (idValor is not null && idTipo == TerceroIdTipo.Ninguno) { idTipo = d.Tipo == TerceroTipo.Empresa ? TerceroIdTipo.Nit : TerceroIdTipo.Identificacion; }
        var ciudad = Pick("ciudad", "ciudad", d.Ciudad);
        var email = Pick("email", "email", d.Email);
        var telefono = Pick("telefono", "telefono", d.Telefono);
        if (telefono is not null && !Same(telefono, d.Telefono)) { telefono = Digits(telefono) ?? telefono; }
        var sector = Pick("sector", "sector", d.Sector);
        var cargo = Pick("cargo", "cargo", d.Cargo);
        var estado = d.Estado;
        if (Str(args, "estado") is string est)
        {
            var nuevo = est == "prospecto" ? TerceroEstado.Prospecto : TerceroEstado.Activo;
            if (d.Estado == TerceroEstado.Inactivo) { return Err("El contacto esta inactivo: usa reactivar_contacto."); }
            if (nuevo != d.Estado) { changes.Add(("estado", EstadoEs(d.Estado), EstadoEs(nuevo))); estado = nuevo; }
        }

        // Campos configurables (FichasJson = { ficha: { clave: valor } }).
        var fichasJson = d.FichasJson;
        if (args.TryGetProperty("campos", out var campos) && campos.ValueKind == JsonValueKind.Object && campos.EnumerateObject().Any())
        {
            if (_fields is null) { return Err("Los campos configurables no estan disponibles."); }
            var defs = (await _fields.ListFieldsAsync(ct)).Where(IsWritableField).ToList();
            JsonObject root;
            try { root = (string.IsNullOrWhiteSpace(fichasJson) ? null : JsonNode.Parse(fichasJson) as JsonObject) ?? new JsonObject(); }
            catch (JsonException) { root = new JsonObject(); }
            foreach (var p in campos.EnumerateObject())
            {
                var matches = MatchField(defs, p.Name);
                if (matches.Count == 0) { return Err($"No existe el campo '{p.Name}'. Usa listar_campos_contacto."); }
                if (matches.Count > 1)
                {
                    return Err($"El campo '{p.Name}' esta en varias fichas; indica cual con ficha.clave: " +
                        string.Join(", ", matches.Select(m => $"{m.FichaKey}.{m.FieldKey}")) + ".");
                }
                var f = matches[0];
                var value = p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => string.IsNullOrWhiteSpace(p.Value.GetString()) ? null : p.Value.GetString()!.Trim(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => p.Value.GetRawText()
                };
                if (root[f.FichaKey] is not JsonObject ficha) { ficha = new JsonObject(); root[f.FichaKey] = ficha; }
                var before = ficha[f.FieldKey] is JsonValue jv && jv.TryGetValue<string>(out var sv) ? sv : ficha[f.FieldKey]?.ToJsonString();
                if (Same(before, value)) { continue; }
                ficha[f.FieldKey] = value is null ? null : JsonValue.Create(value);
                changes.Add((f.Label, before, value));
            }
            fichasJson = root.ToJsonString();
        }

        if (changes.Count == 0) { return Err("No hay cambios: los valores enviados son iguales a los actuales."); }

        // No duplicar: el documento o el telefono nuevos no pueden ser de OTRO contacto activo.
        if (idValor is not null && !Same(idValor, d.IdValor))
        {
            var dup = await _db.Terceros.AsNoTracking()
                .Where(t => t.Id != d.Id && t.IdValor == idValor && t.Estado != TerceroEstado.Inactivo)
                .Select(t => t.Nombre).FirstOrDefaultAsync(ct);
            if (dup is not null) { return Err($"La identificacion {idValor} ya es del contacto '{dup}'. No se cambio para no duplicar."); }
        }
        if (telefono is not null && !Same(telefono, d.Telefono) && await PhoneOwnerAsync(telefono, d.Id, ct) is string phoneOwner)
        {
            return Err($"El telefono {telefono} ya es del contacto '{phoneOwner}'. No se cambio para no duplicar.");
        }

        var req = new SaveTerceroRequest(nombre!, d.Tipo, d.Perfiles, estado, d.Vendedor, ciudad, idTipo, idValor, sector, cargo,
            email, telefono, d.EmpresaId, fichasJson, d.VendedorAsesorId, d.ImagenUrl);
        var res = await _terceros.UpdateAsync(d.Id, req, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo guardar el contacto."); }

        await TrailNoteAsync(d.Id, "Cambios via IA: " + string.Join("; ", changes.Select(c => $"{c.Campo}: {c.Antes ?? "(vacio)"} -> {c.Despues ?? "(vacio)"}")), actor, ct);
        var cambios = changes.Select(c => new { campo = c.Campo, antes = c.Antes, despues = c.Despues }).ToList();
        return Ok(new { ok = true, contacto = nombre, cambios }) with
        {
            AuditBefore = new { contacto_id = d.Id, valores = changes.ToDictionary(c => c.Campo, c => c.Antes) },
            AuditAfter = new { contacto_id = d.Id, valores = changes.ToDictionary(c => c.Campo, c => c.Despues) }
        };
    }

    private async Task<McpToolResult> ContactNoteAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (id, error) = await ResolveContactAsync(Str(args, "contacto"), includeInactive: true, ct);
        if (id is null) { return Err(error!); }
        if (Str(args, "accion") == "listar")
        {
            var notas = await _terceros!.ListNotasAsync(id.Value, ct);
            return Ok(new
            {
                total = notas.Count,
                notas = notas.OrderByDescending(n => n.CreatedAt).Take(50)
                    .Select(n => new { tipo = n.Accion, texto = n.Texto, autor = n.Autor, fecha = n.CreatedAt, concepto = n.ConceptoNombre })
            });
        }
        var texto = Str(args, "texto");
        if (texto is null) { return Err("'texto' es obligatorio para agregar una nota."); }
        var res = await _terceros!.AddNotaAsync(id.Value, new SaveNotaRequest(Clip(texto, 4000), Str(args, "tipo") ?? "Nota", Autor: ActorName(actor)), ct);
        return res.IsOk ? Ok(new { ok = true, nota_id = res.Value!.Id }) : Err(res.Error ?? "No se pudo guardar la nota.");
    }

    private async Task<McpToolResult> LinkPersonCompanyAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_vinculos is null) { return Err("Los vinculos del directorio no estan disponibles."); }
        var (personaId, e1) = await ResolveContactAsync(Str(args, "persona"), includeInactive: false, ct, TerceroTipo.Persona);
        if (personaId is null) { return Err(e1!); }
        var (empresaId, e2) = await ResolveContactAsync(Str(args, "empresa"), includeInactive: false, ct, TerceroTipo.Empresa);
        if (empresaId is null) { return Err(e2!); }

        var names = await NamesOfAsync(new Guid?[] { personaId, empresaId }, ct);
        if (Str(args, "accion") == "desvincular")
        {
            var err = await _vinculos.QuitarAsync(personaId.Value, empresaId.Value, ct);
            if (err is not null) { return Err(err); }
            await TrailNoteAsync(empresaId.Value, $"Via IA: se desvinculo a {names[personaId.Value]}.", actor, ct);
            return Ok(new { ok = true, persona = names[personaId.Value], empresa = names[empresaId.Value], vinculada = false });
        }
        var cargo = Str(args, "cargo");
        var addErr = await LinkAsync(personaId.Value, empresaId.Value, cargo, ct);
        if (addErr is not null) { return Err(addErr); }
        await TrailNoteAsync(empresaId.Value, $"Via IA: se vinculo a {names[personaId.Value]}{(cargo is null ? "" : $" ({cargo})")}.", actor, ct);
        return Ok(new { ok = true, persona = names[personaId.Value], empresa = names[empresaId.Value], cargo, vinculada = true });
    }

    private async Task<McpToolResult> AddInternalContactAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_vinculos is null) { return Err("Los vinculos del directorio no estan disponibles."); }
        var (empresaId, e1) = await ResolveContactAsync(Str(args, "empresa"), includeInactive: false, ct, TerceroTipo.Empresa);
        if (empresaId is null) { return Err(e1!); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("'nombre' es obligatorio."); }
        var cargo = Str(args, "cargo");
        var email = Str(args, "email");
        var telefono = Digits(Str(args, "telefono"));
        var cedula = Str(args, "identificacion");

        // No duplicar: misma cedula, email o telefono = la persona ya existe; se liga en vez de crearla.
        Guid? existing = null;
        if (cedula is not null)
        {
            existing = await _db.Terceros.AsNoTracking()
                .Where(t => t.Tipo == TerceroTipo.Persona && t.IdValor == cedula && t.Estado != TerceroEstado.Inactivo)
                .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        }
        if (existing is null && email is not null)
        {
            var low = email.ToLower();
            existing = await _db.Terceros.AsNoTracking()
                .Where(t => t.Tipo == TerceroTipo.Persona && t.Email != null && t.Email.ToLower() == low && t.Estado != TerceroEstado.Inactivo)
                .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        }
        if (existing is null && telefono is not null)
        {
            existing = await PhoneOwnerIdAsync(telefono, null, ct, TerceroTipo.Persona);
        }

        var yaExistia = existing is not null;
        Guid personaId;
        if (existing is Guid ex) { personaId = ex; }
        else
        {
            var req = new SaveTerceroRequest(nombre, TerceroTipo.Persona, TerceroPerfil.Ninguno, TerceroEstado.Activo,
                IdTipo: cedula is null ? TerceroIdTipo.Ninguno : TerceroIdTipo.Identificacion, IdValor: cedula, Cargo: cargo,
                Email: email, Telefono: telefono, EmpresaId: empresaId);
            var created = await _terceros!.CreateAsync(req, ct);
            if (!created.IsOk) { return Err(created.Error ?? "No se pudo crear la persona."); }
            personaId = created.Value!.Id;
        }

        var alreadyLinked = yaExistia && (await _vinculos.ListDeAsync(empresaId.Value, ct)).Any(v => v.OtroId == personaId);
        var err = await LinkAsync(personaId, empresaId.Value, cargo, ct);
        if (err is not null) { return Err(err); }
        var names = await NamesOfAsync(new Guid?[] { personaId, empresaId }, ct);
        if (!alreadyLinked)
        {
            await TrailNoteAsync(empresaId.Value, $"Via IA: se agrego a {names[personaId]}{(cargo is null ? "" : $" ({cargo})")} como contacto de la empresa.", actor, ct);
        }
        return Ok(new
        {
            ok = true,
            contacto_id = personaId,
            persona = names[personaId],
            empresa = names[empresaId.Value],
            cargo,
            ya_existia = yaExistia,
            mensaje = alreadyLinked ? "La persona ya era contacto de esta empresa; no se duplico."
                : yaExistia ? "La persona ya existia en el Directorio: se ligo a la empresa sin duplicarla." : null
        });
    }

    private async Task<McpToolResult> SetContactStateAsync(JsonElement args, McpActor actor, bool active, CancellationToken ct)
    {
        var motivo = Str(args, "motivo");
        if (motivo is null) { return Err("'motivo' es obligatorio."); }
        var (id, error) = await ResolveContactAsync(Str(args, "contacto"), includeInactive: true, ct);
        if (id is null) { return Err(error!); }
        var d = await _terceros!.GetAsync(id.Value, ct);
        if (d is null) { return Err("El contacto no existe."); }

        if (!active)
        {
            if (d.Estado == TerceroEstado.Inactivo) { return Err($"'{d.Nombre}' ya esta inactivo."); }
            var res = await _terceros.DeleteAsync(d.Id, ct); // baja logica (Estado = Inactivo)
            if (!res.IsOk) { return Err(res.Error ?? "No se pudo inactivar el contacto."); }
        }
        else
        {
            if (d.Estado != TerceroEstado.Inactivo) { return Err($"'{d.Nombre}' ya esta activo."); }
            var req = new SaveTerceroRequest(d.Nombre, d.Tipo, d.Perfiles, TerceroEstado.Activo, d.Vendedor, d.Ciudad, d.IdTipo, d.IdValor,
                d.Sector, d.Cargo, d.Email, d.Telefono, d.EmpresaId, d.FichasJson, d.VendedorAsesorId, d.ImagenUrl);
            var res = await _terceros.UpdateAsync(d.Id, req, ct);
            if (!res.IsOk) { return Err(res.Error ?? "No se pudo reactivar el contacto."); }
        }

        var nuevo = active ? "Activo" : "Inactivo";
        await TrailNoteAsync(d.Id, $"Via IA: contacto {(active ? "reactivado" : "inactivado")}. Motivo: {Clip(motivo, 1000)}", actor, ct);
        return Ok(new { ok = true, contacto = d.Nombre, estado = nuevo }) with
        {
            AuditBefore = new { contacto_id = d.Id, estado = EstadoEs(d.Estado) },
            AuditAfter = new { contacto_id = d.Id, estado = nuevo, motivo }
        };
    }

    private async Task<McpToolResult> LinkContactAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (task, error) = await ResolveVisibleTaskAsync(Str(args, "numero"), actor, ct);
        if (task is null) { return Err(error!); }
        var (id, e2) = await ResolveContactAsync(Str(args, "contacto"), includeInactive: false, ct);
        if (id is null) { return Err(e2!); }
        var names = await NamesOfAsync(new Guid?[] { id }, ct);
        var res = await _tasks.SetContactTerceroAsync(task.Id, id.Value, actor.PlatformUserId, ActorName(actor), ct);
        return res.IsOk ? Ok(new { ok = true, numero = task.Number, contacto = names[id.Value] }) : Err(res.Error ?? "No se pudo vincular el contacto.");
    }

    // ===================== Helpers del directorio =====================

    /// <summary>
    /// Liga persona -> empresa de forma visible en AMBOS directorios: si la persona no tiene empresa, queda como
    /// su contacto principal (EmpresaId, lo unico que muestra el Directorio Clasico); si ya tiene otra, se agrega
    /// un vinculo M:N (Modular). AgregarAsync fija el cargo en ambos casos.
    /// </summary>
    private async Task<string?> LinkAsync(Guid personaId, Guid empresaId, string? cargo, CancellationToken ct)
    {
        var current = await _db.Terceros.AsNoTracking().Where(t => t.Id == personaId).Select(t => t.EmpresaId).FirstOrDefaultAsync(ct);
        if (current is null)
        {
            // Si ya existia un vinculo M:N con esta empresa, el enlace principal lo reemplaza (no duplicar).
            if ((await _vinculos!.ListDeAsync(personaId, ct)).Any(v => v.OtroId == empresaId && v.VinculoId is not null))
            {
                await _vinculos.QuitarAsync(personaId, empresaId, ct);
            }
            var assign = await _terceros!.AssignToEmpresaAsync(personaId, empresaId, ct);
            if (!assign.IsOk) { return assign.Error ?? "No se pudo ligar la persona a la empresa."; }
        }
        return await _vinculos!.AgregarAsync(personaId, empresaId, cargo, ct);
    }

    /// <summary>Resuelve un contacto por id, identificacion o nombre exacto (sin distinguir mayusculas).</summary>
    private async Task<(Guid? Id, string? Error)> ResolveContactAsync(string? key, bool includeInactive, CancellationToken ct, TerceroTipo? tipo = null)
    {
        if (key is null) { return (null, "Indica el contacto (id, identificacion o nombre exacto)."); }
        var q = _db.Terceros.AsNoTracking();
        if (!includeInactive) { q = q.Where(t => t.Estado != TerceroEstado.Inactivo); }
        if (tipo is TerceroTipo tt) { q = q.Where(t => t.Tipo == tt); }
        var what = tipo switch { TerceroTipo.Empresa => "la empresa", TerceroTipo.Persona => "la persona", _ => "el contacto" };

        if (Guid.TryParse(key, out var gid))
        {
            return await q.AnyAsync(t => t.Id == gid, ct) ? (gid, null) : (null, $"No encontre {what} con id {gid}.");
        }
        var low = key.ToLower();
        var hits = await q.Where(t => t.IdValor == key || t.Nombre.ToLower() == low)
            .Select(t => new { t.Id, t.Nombre, t.Estado }).Take(5).ToListAsync(ct);
        // Si hay varios, se prefiere el unico activo.
        if (hits.Count > 1 && hits.Count(h => h.Estado != TerceroEstado.Inactivo) == 1) { hits = hits.Where(h => h.Estado != TerceroEstado.Inactivo).ToList(); }
        if (hits.Count == 1) { return (hits[0].Id, null); }
        if (hits.Count > 1) { return (null, $"Hay varios '{key}'; indica el id (ver buscar_contacto)."); }

        var similares = await q.Where(t => t.Nombre.ToLower().Contains(low)).OrderBy(t => t.Nombre)
            .Select(t => t.Nombre).Take(5).ToListAsync(ct);
        return (null, similares.Count == 0
            ? $"No encontre {what} '{key}'. Usa buscar_contacto o crear_contacto."
            : $"No encontre {what} '{key}' exacto. Parecidos: {string.Join(", ", similares)}.");
    }

    /// <summary>Tareas del contacto que el actor puede ver (misma regla de visibilidad del tablero).</summary>
    private async Task<List<TaskItem>> VisibleContactTasksAsync(Guid terceroId, McpActor actor, CancellationToken ct)
    {
        var rows = await _db.TaskItems.AsNoTracking().Where(t => t.TerceroId == terceroId)
            .OrderByDescending(t => t.CreatedAt).Take(300).ToListAsync(ct);
        if (rows.Count == 0) { return rows; }
        var vis = await VisibilityContext.LoadAsync(_db, _inbox, actor, ct);
        var result = new List<TaskItem>();
        foreach (var t in rows)
        {
            if (await vis.CanSeeAsync(t.BoardId, ct) || await vis.IsMineAsync(t.Id, t.AssigneeTenantUserId, ct)) { result.Add(t); }
        }
        return result;
    }

    private async Task<Dictionary<string, string>> FieldLabelsAsync(CancellationToken ct)
    {
        if (_fields is null) { return new Dictionary<string, string>(StringComparer.Ordinal); }
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in await _fields.ListFieldsAsync(ct)) { map.TryAdd(f.FieldKey, f.Label); }
        return map;
    }

    private async Task<Dictionary<Guid, string>> NamesOfAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (set.Count == 0) { return new Dictionary<Guid, string>(); }
        return await _db.Terceros.AsNoTracking().Where(t => set.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);
    }

    private async Task<string?> PhoneOwnerAsync(string phone, Guid exclude, CancellationToken ct)
    {
        var id = await PhoneOwnerIdAsync(phone, exclude, ct);
        return id is Guid g ? (await NamesOfAsync(new Guid?[] { g }, ct))[g] : null;
    }

    /// <summary>Dueno de un telefono comparando los ULTIMOS 10 digitos (absorbe el prefijo de pais). Excluye inactivos.</summary>
    private async Task<Guid?> PhoneOwnerIdAsync(string phone, Guid? exclude, CancellationToken ct, TerceroTipo? tipo = null)
    {
        var digits = Digits(phone);
        if (digits is null || digits.Length < 7) { return null; }
        var last10 = digits.Length > 10 ? digits[^10..] : digits;
        var q = _db.Terceros.AsNoTracking().Where(t => t.Estado != TerceroEstado.Inactivo && t.Telefono != null
            && EF.Functions.Like(t.Telefono, "%" + last10));
        if (exclude is Guid ex) { q = q.Where(t => t.Id != ex); }
        if (tipo is TerceroTipo tt) { q = q.Where(t => t.Tipo == tt); }
        var cands = await q.Select(t => new { t.Id, t.Telefono }).Take(20).ToListAsync(ct);
        return cands.FirstOrDefault(c => { var d = Digits(c.Telefono); return d is not null && (d.Length > 10 ? d[^10..] : d) == last10; })?.Id;
    }

    /// <summary>Rastro visible en la ficha (timeline del contacto). Si falla, no tumba el cambio ya guardado.</summary>
    private async Task TrailNoteAsync(Guid terceroId, string text, McpActor actor, CancellationToken ct)
    {
        try { await _terceros!.AddNotaAsync(terceroId, new SaveNotaRequest(Clip(text, 4000), "Nota", Autor: ActorName(actor)), ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { /* la auditoria del endpoint ya registra el cambio */ }
    }

    /// <summary>Campo por "ficha.clave", por clave o por etiqueta (sin distinguir mayusculas). Varios = ambiguo.</summary>
    private static List<TerceroFieldDto> MatchField(IReadOnlyList<TerceroFieldDto> defs, string name)
    {
        var dot = name.IndexOf('.');
        if (dot > 0)
        {
            var ficha = name[..dot];
            var key = name[(dot + 1)..];
            var exact = defs.Where(f => string.Equals(f.FichaKey, ficha, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(f.FieldKey, key, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Label, key, StringComparison.OrdinalIgnoreCase))).ToList();
            if (exact.Count > 0) { return exact; }
        }
        var byKey = defs.Where(f => string.Equals(f.FieldKey, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return byKey.Count > 0 ? byKey : defs.Where(f => string.Equals(f.Label, name, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool IsWritableField(TerceroFieldDto f)
        => f.FieldType is not (TerceroFieldType.Separator or TerceroFieldType.Calculated or TerceroFieldType.Table);

    private static bool IsClosed(TaskItemStatus s) => s is TaskItemStatus.Done or TaskItemStatus.Closed;

    private static TerceroIdTipo? ParseIdTipoArg(string s) => s.ToLowerInvariant() switch
    {
        "nit" => TerceroIdTipo.Nit,
        "cedula" or "cc" => TerceroIdTipo.Identificacion,
        "correo" or "email" => TerceroIdTipo.Correo,
        "telefono" => TerceroIdTipo.Telefono,
        "ninguno" => TerceroIdTipo.Ninguno,
        _ => null
    };

    private static string TipoEs(TerceroTipo t) => t == TerceroTipo.Empresa ? "empresa" : "persona";

    private static string EstadoEs(TerceroEstado e) => e switch
    {
        TerceroEstado.Prospecto => "Prospecto",
        TerceroEstado.Inactivo => "Inactivo",
        _ => "Activo"
    };

    private static string? Digits(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var d = new string(s.Where(char.IsDigit).ToArray());
        return d.Length == 0 ? null : d;
    }
}
