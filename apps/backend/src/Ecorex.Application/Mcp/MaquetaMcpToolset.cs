using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// Servidor MCP de MAQUETAS (ADR-0182, ola 2 de ADR-0179/0180): la IA del PC local (Claude Code junto a la carpeta de
/// la maqueta) lee las solicitudes de ajuste que se anotaron en el editor del sistema, ve la captura y el elemento,
/// aplica el cambio en los archivos locales, comenta/resuelve la tarjeta y publica una version nueva por la API de
/// subida. Capa DELGADA: lee con <see cref="IMockupService"/> y escribe en la tarjeta via el toolset de tableros
/// (misma visibilidad, bitacora "via IA" y reglas de flujo). Solo opera tarjetas que nacieron de una maqueta.
/// </summary>
public interface IMaquetaMcpToolset : IMcpToolset
{
    /// <summary>URL base publica de ECOREX (la pone el endpoint) para armar enlaces y comandos de publicacion.</summary>
    string? PublicBaseUrl { get; set; }
}

public sealed class MaquetaMcpToolset : IMaquetaMcpToolset
{
    /// <summary>Tope de la captura que se devuelve como imagen (bytes ya decodificados).</summary>
    public const int MaxCaptureBytes = 4 * 1024 * 1024;

    private const int MaxList = 200;

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal)
    {
        "listar_proyectos", "listar_solicitudes", "ver_solicitud", "listar_versiones", "como_publicar_version",
        "listar_notas", "buscar_notas", "ver_nota", "grafo_cerebro", "listar_adjuntos", "ver_adjunto", "listar_cerebros"
    };

    private static readonly HashSet<string> Destructive = new(StringComparer.Ordinal);

    private static readonly AiToolSpec[] Specs =
    {
        new("listar_proyectos",
            "Lista los proyectos de maqueta de la empresa: identificador (slug), nombre, version actual, archivos, " +
            "tablero de las solicitudes y cuantas solicitudes hay abiertas. Usala primero.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("listar_solicitudes",
            "Lista las solicitudes de ajuste de un proyecto (tarjetas creadas desde el editor de maquetas): numero, " +
            "titulo, prioridad, estado/columna, pantalla, ARCHIVO de la maqueta y selector CSS del elemento. Por defecto " +
            "solo las abiertas, de la mas reciente a la mas vieja.",
            """{"type":"object","properties":{"proyecto":{"type":"string","description":"Slug del proyecto (ver listar_proyectos). Si la empresa tiene uno solo, se puede omitir."},"estado":{"type":"string","enum":["abiertas","terminadas","todas"]},"archivo":{"type":"string","description":"Solo las de esta pantalla (ruta relativa, ej. rq01-dependencias.html)"}},"additionalProperties":false}"""),
        new("ver_solicitud",
            "Detalle de una solicitud por numero (ej. T00046): descripcion completa, contexto tecnico (archivo, selector, " +
            "texto y clases del elemento, ruta del menu, origen en el app real, handoff, tema, viewport), comentarios y " +
            "adjuntos de la tarjeta. Devuelve ademas la CAPTURA de pantalla como imagen (el elemento va resaltado en rojo).",
            """{"type":"object","properties":{"numero":{"type":"string","description":"Numero de la tarjeta, ej. T00046"},"sin_captura":{"type":"boolean","description":"true = no devolver la imagen"}},"required":["numero"],"additionalProperties":false}"""),
        new("listar_versiones",
            "Versiones publicadas de un proyecto (numero, fecha, autor, archivos, notas) y cual es la actual.",
            """{"type":"object","properties":{"proyecto":{"type":"string"}},"additionalProperties":false}"""),
        new("como_publicar_version",
            "Devuelve el comando exacto (PowerShell y curl) para publicar la carpeta local de la maqueta como version " +
            "NUEVA del proyecto con el MISMO token de este servidor: comprimir en ZIP (HTML puro, con index.html) y " +
            "subirlo. Publicar exige ser Owner/Admin de la empresa. Pide confirmacion al usuario antes de publicar.",
            """{"type":"object","properties":{"proyecto":{"type":"string"},"notas":{"type":"string","description":"Notas de la version (ej. 'Ajustes T00046, T00047')"}},"additionalProperties":false}"""),
        new("comentar_solicitud",
            "Agrega un comentario a la bitacora de la solicitud (queda a nombre del usuario 'via IA'). Usala para contar " +
            "que se cambio y en que archivo.",
            """{"type":"object","properties":{"numero":{"type":"string"},"texto":{"type":"string"}},"required":["numero","texto"],"additionalProperties":false}"""),
        new("mover_solicitud",
            "Mueve la solicitud a otra columna de su tablero (ej. 'En progreso' al empezar). Si la tarjeta la gobierna un " +
            "flujo en curso, no se mueve a mano.",
            """{"type":"object","properties":{"numero":{"type":"string"},"columna":{"type":"string","description":"Nombre de la columna destino"},"nota":{"type":"string"}},"required":["numero","columna"],"additionalProperties":false}"""),
        new("resolver_solicitud",
            "Marca la solicitud como RESUELTA (tarjeta terminada) y deja una nota con lo que se hizo. Hazlo despues de " +
            "aplicar el cambio en la maqueta (idealmente despues de publicar la version, indicandola en la nota).",
            """{"type":"object","properties":{"numero":{"type":"string"},"nota":{"type":"string","description":"Que se cambio (archivo, version publicada)"},"motivo_cierre":{"type":"string","description":"Concepto de cierre si el tablero lo exige"}},"required":["numero","nota"],"additionalProperties":false}"""),

        // Cerebros (ADR-0183; modulo propio desde ADR-0200): notas markdown enlazadas tipo Obsidian. Las tools reciben
        // 'cerebro' (slug, ola 4) o, por compatibilidad, 'proyecto' (usa el cerebro PRINCIPAL de esa maqueta).
        new("listar_cerebros",
            "Lista los CEREBROS de la empresa (bases de notas tipo Obsidian): slug, nombre, categoria, cuantas notas tiene, " +
            "si es de SOLO LECTURA (sembrado por la plataforma) y a que maquetas esta vinculado (y si es su principal). " +
            "Pasa su slug como 'cerebro' en las demas herramientas de notas.",
            """{"type":"object","properties":{"texto":{"type":"string","description":"Filtra por nombre, descripcion o categoria"}},"additionalProperties":false}"""),
        new("listar_notas",
            "Lista las notas de un CEREBRO (notas tipo Obsidian: decisiones, conocimiento, lo que se construye en una " +
            "maqueta): titulo, resumen, enlaces salientes y cuantas notas la enlazan. Mas recientes primero.",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"}},"additionalProperties":false}"""),
        new("buscar_notas",
            "Busca notas del cerebro digital por texto en el titulo o en el contenido.",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"},"texto":{"type":"string"}},"required":["texto"],"additionalProperties":false}"""),
        new("ver_nota",
            "Contenido completo de una nota del cerebro (markdown), su version, sus enlaces salientes ya resueltos (notas, " +
            "pantallas .html de la maqueta, tarjetas T00000, etiquetas) y las notas que la enlazan (backlinks).",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"},"titulo":{"type":"string"}},"required":["titulo"],"additionalProperties":false}"""),
        new("crear_nota",
            "Crea una nota en el cerebro (no se puede en uno de solo lectura). Sintaxis de enlaces: [[Titulo de nota]], [[Titulo|texto]], " +
            "[[pantalla.html]] (pantalla de la maqueta), [[T00046]] (solicitud) y #etiqueta. Un enlace a una nota que no " +
            "existe queda como nodo pendiente. Usala para guardar conclusiones, decisiones y preguntas abiertas.",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"},"titulo":{"type":"string","description":"Max 200, sin [ ] | # ^"},"contenido":{"type":"string","description":"Markdown"}},"required":["titulo","contenido"],"additionalProperties":false}"""),
        new("editar_nota",
            "Edita una nota del cerebro: modo 'agregar' (por defecto) anade el contenido al final; 'reemplazar' sustituye " +
            "todo el cuerpo (pasa la 'version' que te dio ver_nota para no pisar cambios ajenos). 'nuevo_titulo' la " +
            "renombra y actualiza los [[enlaces]] de las demas notas.",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"},"titulo":{"type":"string"},"contenido":{"type":"string"},"modo":{"type":"string","enum":["agregar","reemplazar"]},"nuevo_titulo":{"type":"string"},"version":{"type":"integer"}},"required":["titulo"],"additionalProperties":false}"""),
        new("grafo_cerebro",
            "Resumen del grafo del cerebro digital: nodos (notas, pantallas, tarjetas, etiquetas y notas pendientes por " +
            "crear) con su grado de conexion, y las aristas. Sirve para ver de que trata el proyecto y que esta suelto.",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"}},"additionalProperties":false}"""),
        new("listar_adjuntos",
            "Lista los adjuntos del cerebro digital (imagenes y PDF: diagramas, bocetos, capturas, documentos): nombre, " +
            "tipo, tamano y en que notas se embeben (![[nombre]]). Usa ver_adjunto para mirar una imagen.",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"}},"additionalProperties":false}"""),
        new("ver_adjunto",
            "Devuelve un adjunto del cerebro como IMAGEN para que la mires (diagramas, bocetos, capturas). Los PDF no se " +
            "devuelven: solo su informacion. Pasa el nombre exacto de listar_adjuntos o el que aparece en ![[...]].",
            """{"type":"object","properties":{"cerebro":{"type":"string","description":"Slug del cerebro (ver listar_cerebros). Tiene prioridad sobre proyecto."},"proyecto":{"type":"string"},"nombre":{"type":"string","description":"Nombre del adjunto, ej. boceto.png"}},"required":["nombre"],"additionalProperties":false}"""),
    };

    private readonly IApplicationDbContext _db;
    private readonly IMockupService _mockups;
    private readonly ITableroMcpToolset _tablero;
    private readonly IFileStorageService _files;
    private readonly IMockupBrainService _brain;

    public MaquetaMcpToolset(IApplicationDbContext db, IMockupService mockups, ITableroMcpToolset tablero, IFileStorageService files,
        IMockupBrainService brain)
    {
        _db = db;
        _mockups = mockups;
        _tablero = tablero;
        _files = files;
        _brain = brain;
    }

    public IReadOnlySet<string> ReadOnlyTools => ReadOnly;

    public IReadOnlySet<string> DestructiveTools => Destructive;

    public IReadOnlyList<AiToolSpec> GetSpecs() => Specs;

    /// <summary>URL base publica (la pone el endpoint para armar los comandos de publicacion).</summary>
    public string? PublicBaseUrl { get; set; }

    public async Task<McpToolResult> ExecuteAsync(string toolName, string argumentsJson, McpActor actor, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException) { return Err("Los argumentos no son un JSON valido."); }
        if (args.ValueKind != JsonValueKind.Object) { return Err("Los argumentos deben ser un objeto JSON."); }

        return toolName switch
        {
            "listar_proyectos" => await ListProjectsAsync(cancellationToken),
            "listar_solicitudes" => await ListRequestsAsync(args, cancellationToken),
            "ver_solicitud" => await ViewRequestAsync(args, actor, cancellationToken),
            "listar_versiones" => await ListVersionsAsync(args, cancellationToken),
            "como_publicar_version" => await HowToPublishAsync(args, actor, cancellationToken),
            "comentar_solicitud" => await DelegateAsync(args, actor, "comentar", "texto", cancellationToken),
            "mover_solicitud" => await DelegateAsync(args, actor, "mover_tarea", null, cancellationToken),
            "resolver_solicitud" => await ResolveAsync(args, actor, cancellationToken),
            "listar_cerebros" => await ListBrainsAsync(args, cancellationToken),
            "listar_notas" => await ListNotesAsync(args, null, cancellationToken),
            "buscar_notas" => Str(args, "texto") is { } texto
                ? await ListNotesAsync(args, texto, cancellationToken)
                : Err("'texto' es obligatorio."),
            "ver_nota" => await ViewNoteAsync(args, cancellationToken),
            "crear_nota" => await CreateNoteAsync(args, actor, cancellationToken),
            "editar_nota" => await EditNoteAsync(args, actor, cancellationToken),
            "grafo_cerebro" => await GraphAsync(args, cancellationToken),
            "listar_adjuntos" => await ListAssetsAsync(args, cancellationToken),
            "ver_adjunto" => await ViewAssetAsync(args, actor, cancellationToken),
            _ => Err($"Herramienta desconocida: {toolName}")
        };
    }

    // ------------------------------------------------------------------ lectura

    private async Task<McpToolResult> ListProjectsAsync(CancellationToken ct)
    {
        var projects = await _mockups.ListProjectsAsync(ct);
        return Ok(new
        {
            ok = true,
            proyectos = projects.Select(p => new
            {
                proyecto = p.Slug,
                nombre = p.Name,
                descripcion = p.Description,
                version_actual = p.CurrentVersionNumber,
                archivos = p.CurrentFileCount,
                tablero = p.BoardName,
                solicitudes_abiertas = p.OpenRequests,
                solicitudes_total = p.TotalRequests,
                editor = Url($"/maqueta/{p.Slug}")
            }),
            nota = projects.Count == 0 ? "La empresa no tiene proyectos de maqueta (se crean en /maqueta)." : null
        });
    }

    private async Task<McpToolResult> ListRequestsAsync(JsonElement args, CancellationToken ct)
    {
        var (slug, error) = await ResolveProjectAsync(Str(args, "proyecto"), ct);
        if (error is not null) { return Err(error); }
        var estado = (Str(args, "estado") ?? "abiertas").ToLowerInvariant();
        var archivo = Str(args, "archivo");

        var all = await _mockups.ListSolicitudesAsync(slug!, ct);
        IEnumerable<MockupSolicitudDto> list = estado switch
        {
            "todas" => all,
            "terminadas" => all.Where(s => !s.IsOpen),
            _ => all.Where(s => s.IsOpen)
        };
        if (archivo is not null) { list = list.Where(s => string.Equals(s.Archivo, archivo, StringComparison.OrdinalIgnoreCase)); }
        var rows = list.Take(MaxList).ToList();
        return Ok(new
        {
            ok = true,
            proyecto = slug,
            estado,
            total = rows.Count,
            solicitudes = rows.Select(s => new
            {
                numero = s.Number,
                titulo = s.Title,
                prioridad = Prioridad(s.Priority),
                estado = Estado(s.Status),
                columna = s.ColumnName,
                pantalla = s.Pantalla,
                archivo = s.Archivo,
                selector = s.Selector,
                creada = s.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'")
            })
        });
    }

    private async Task<McpToolResult> ViewRequestAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var numero = Str(args, "numero");
        if (numero is null) { return Err("'numero' es obligatorio (ej. T00046)."); }
        var link = await FindLinkAsync(numero, ct);
        if (link is null) { return Err($"La tarjeta {numero} no existe o no es una solicitud de maqueta."); }

        // Detalle de la tarjeta via el toolset de tableros (visibilidad del usuario, comentarios, adjuntos).
        var detail = await _tablero.ExecuteAsync("ver_tarea", JsonSerializer.Serialize(new { numero = link.Number }), actor, ct);
        if (detail.IsError) { return detail; }

        JsonNode? meta = null;
        if (!string.IsNullOrWhiteSpace(link.MetadataJson))
        {
            try { meta = JsonNode.Parse(link.MetadataJson); } catch (JsonException) { meta = null; }
        }

        McpMedia? media = null;
        string? capturaNota = null;
        if (args.TryGetProperty("sin_captura", out var sc) && sc.ValueKind == JsonValueKind.True)
        {
            capturaNota = "omitida a pedido";
        }
        else
        {
            (media, capturaNota) = await LoadCaptureAsync(link.TaskId, actor.TenantId, ct);
        }

        var result = new JsonObject
        {
            ["ok"] = true,
            ["origen"] = link.Source,
            ["proyecto"] = link.Source.StartsWith(MockupService.SourcePrefix, StringComparison.Ordinal) ? link.Source[MockupService.SourcePrefix.Length..] : null,
            ["contexto_maqueta"] = meta,
            ["captura"] = media is not null ? "adjunta como imagen (el elemento va resaltado en rojo)" : capturaNota ?? "sin captura",
            ["tarjeta"] = JsonNode.Parse(detail.Json),
            ["siguiente_paso"] = "Abre el archivo de la maqueta en tu carpeta local, ubica el elemento por el selector, aplica " +
                                 "el ajuste, comenta lo que cambiaste (comentar_solicitud) y, al publicar la version, resuelvela."
        };
        return new McpToolResult(result.ToJsonString(JsonOut), false) { Media = media };
    }

    private async Task<McpToolResult> ListVersionsAsync(JsonElement args, CancellationToken ct)
    {
        var (slug, error) = await ResolveProjectAsync(Str(args, "proyecto"), ct);
        if (error is not null) { return Err(error); }
        var project = await _mockups.GetProjectAsync(slug!, ct);
        if (project is null) { return Err($"No existe el proyecto de maqueta '{slug}'."); }
        var versions = await _mockups.ListVersionsAsync(project.Id, ct);
        return Ok(new
        {
            ok = true,
            proyecto = slug,
            versiones = versions.Select(v => new
            {
                version = v.Number,
                actual = v.IsCurrent,
                fecha = v.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                por = v.UploadedByName,
                archivos = v.FileCount,
                entrada = v.EntryPath,
                notas = v.Notes,
                url = Url($"/maqueta/{slug}~{v.Number}/{v.EntryPath}")
            })
        });
    }

    private async Task<McpToolResult> HowToPublishAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (slug, error) = await ResolveProjectAsync(Str(args, "proyecto"), ct);
        if (error is not null) { return Err(error); }
        var project = await _mockups.GetProjectAsync(slug!, ct);
        if (project is null) { return Err($"No existe el proyecto de maqueta '{slug}'."); }
        var notas = Str(args, "notas") ?? "Ajustes desde el PC local";
        var uploadUrl = Url($"/api/maqueta/proyectos/{slug}/versiones") + "?notas=" + Uri.EscapeDataString(notas);

        var powershell = string.Join("\n", new[]
        {
            "# Desde la carpeta RAIZ de la maqueta (la que tiene index.html). $env:ECOREX_MAQUETA_TOKEN = el token de este MCP.",
            "$zip = Join-Path $env:TEMP \"" + slug + "-maqueta.zip\"",
            "if (Test-Path $zip) { Remove-Item $zip }",
            "Get-ChildItem -Force | Where-Object { $_.Name -notlike '.*' } | Compress-Archive -DestinationPath $zip",
            "Invoke-RestMethod -Method Post -Uri \"" + uploadUrl + "\" -Headers @{ Authorization = \"Bearer $env:ECOREX_MAQUETA_TOKEN\" } -ContentType 'application/zip' -InFile $zip"
        });
        var curl = string.Join("\n", new[]
        {
            "# Desde la carpeta RAIZ de la maqueta. ECOREX_MAQUETA_TOKEN = el token de este MCP.",
            "zip -r -q /tmp/" + slug + "-maqueta.zip . -x '.*' -x '*/.*'",
            "curl -sS -X POST \"" + uploadUrl + "\" -H \"Authorization: Bearer $ECOREX_MAQUETA_TOKEN\" -H \"Content-Type: application/zip\" --data-binary @/tmp/" + slug + "-maqueta.zip"
        });
        return Ok(new
        {
            ok = true,
            proyecto = slug,
            version_actual = project.CurrentVersionNumber,
            siguiente_version = await NextNumberAsync(project.Id, ct),
            puede_publicar = actor.IsOwnerOrAdmin,
            aviso = actor.IsOwnerOrAdmin ? null : "Tu usuario no es Owner/Admin: la subida respondera 403. Pide a un administrador que publique.",
            requisitos = "ZIP de la carpeta de la maqueta: HTML puro (index.html + pantallas, css, js, imagenes), max 100 MB. " +
                         "No hace falta el kit anotador/: el editor es del sistema. Se ignoran .git, .env y ocultos.",
            token = "Usa el MISMO token con el que configuraste este servidor MCP (variable de entorno ECOREX_MAQUETA_TOKEN). " +
                    "Nunca lo escribas en archivos de la maqueta ni en el chat.",
            powershell,
            bash = curl,
            respuesta = "201 {proyecto, version, archivos, entrada, url, editor}; 400 ZIP invalido; 403 no es Owner/Admin; 413 > 100 MB.",
            confirmar = "Pide confirmacion al usuario antes de publicar: la version nueva queda como ACTUAL para toda la empresa."
        });
    }

    private async Task<int> NextNumberAsync(Guid projectId, CancellationToken ct)
        => await _db.MockupProjects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.LastVersionNumber).FirstOrDefaultAsync(ct) + 1;

    // ------------------------------------------------------------------ escritura (via toolset de tableros)

    private async Task<McpToolResult> DelegateAsync(JsonElement args, McpActor actor, string tableroTool, string? requiredText, CancellationToken ct)
    {
        var numero = Str(args, "numero");
        if (numero is null) { return Err("'numero' es obligatorio (ej. T00046)."); }
        if (requiredText is not null && Str(args, requiredText) is null) { return Err($"'{requiredText}' es obligatorio."); }
        var link = await FindLinkAsync(numero, ct);
        if (link is null) { return Err($"La tarjeta {numero} no existe o no es una solicitud de maqueta."); }

        var forwarded = JsonNode.Parse(args.GetRawText())!.AsObject();
        forwarded["numero"] = link.Number;
        return await _tablero.ExecuteAsync(tableroTool, forwarded.ToJsonString(), actor, ct);
    }

    private async Task<McpToolResult> ResolveAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var numero = Str(args, "numero");
        var nota = Str(args, "nota");
        if (numero is null || nota is null) { return Err("'numero' y 'nota' son obligatorios."); }
        var link = await FindLinkAsync(numero, ct);
        if (link is null) { return Err($"La tarjeta {numero} no existe o no es una solicitud de maqueta."); }

        var comment = await _tablero.ExecuteAsync("comentar",
            JsonSerializer.Serialize(new { numero = link.Number, texto = "Resuelto desde el PC local: " + nota }), actor, ct);
        if (comment.IsError) { return comment; }

        var close = new JsonObject { ["numero"] = link.Number, ["nota"] = nota };
        if (Str(args, "motivo_cierre") is { } motivo) { close["motivo_cierre"] = motivo; }
        return await _tablero.ExecuteAsync("terminar_tarea", close.ToJsonString(), actor, ct);
    }

    // ------------------------------------------------------------------ cerebro digital (ADR-0183)

    private async Task<McpToolResult> ListNotesAsync(JsonElement args, string? texto, CancellationToken ct)
    {
        var (slug, error) = await ResolveBrainAsync(args, ct);
        if (error is not null) { return Err(error); }
        var notes = await _brain.ListNotesAsync(slug!, texto, ct);
        return Ok(new
        {
            ok = true,
            cerebro = slug,
            buscar = texto,
            total = notes.Count,
            notas = notes.Take(MaxList).Select(n => new
            {
                titulo = n.Title,
                resumen = n.Snippet,
                enlaces = n.OutLinks,
                la_enlazan = n.BackLinks,
                actualizada = n.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                por = n.UpdatedByName
            }),
            nota = notes.Count == 0
                ? (texto is null ? "El cerebro esta vacio: crea la primera nota con crear_nota." : "Ninguna nota coincide.")
                : null,
            enlace = Url($"/cerebros/{slug}")
        });
    }

    private async Task<McpToolResult> ViewNoteAsync(JsonElement args, CancellationToken ct)
    {
        var titulo = Str(args, "titulo");
        if (titulo is null) { return Err("'titulo' es obligatorio."); }
        var (slug, error) = await ResolveBrainAsync(args, ct);
        if (error is not null) { return Err(error); }
        var note = await _brain.GetNoteAsync(slug!, null, titulo, ct);
        if (note is null) { return Err($"No existe la nota '{titulo}' en el cerebro de '{slug}'. Usa listar_notas o buscar_notas."); }
        var embeds = MockupBrainAssets.Embeds(note.Body);
        var assets = embeds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _brain.ListAssetsAsync(slug!, false, ct)).Select(a => MockupBrainAssets.Key(a.Name)).ToHashSet(StringComparer.Ordinal);
        return Ok(NotePayload(slug!, note, embeds.Select(k => (object)new { nombre = k, existe = assets.Contains(k) }).ToList()));
    }

    // ------------------------------------------------------------------ adjuntos del cerebro (ADR-0183, ola 3b)

    private async Task<McpToolResult> ListAssetsAsync(JsonElement args, CancellationToken ct)
    {
        var (slug, error) = await ResolveBrainAsync(args, ct);
        if (error is not null) { return Err(error); }
        var assets = await _brain.ListAssetsAsync(slug!, true, ct);
        return Ok(new
        {
            ok = true,
            cerebro = slug,
            total = assets.Count,
            adjuntos = assets.Take(MaxList).Select(a => new
            {
                nombre = a.Name,
                tipo = a.IsImage ? "imagen" : "pdf",
                kb = Math.Max(1, a.SizeBytes / 1024),
                en_notas = a.UsedIn
            }),
            nota = assets.Count == 0 ? "El cerebro no tiene adjuntos (se suben desde la pestana Cerebro o importando un vault)." : null
        });
    }

    private async Task<McpToolResult> ViewAssetAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("'nombre' es obligatorio."); }
        var (slug, error) = await ResolveBrainAsync(args, ct);
        if (error is not null) { return Err(error); }
        var key = MockupBrainAssets.Key(nombre);
        var asset = (await _brain.ListAssetsAsync(slug!, true, ct)).FirstOrDefault(a => MockupBrainAssets.Key(a.Name) == key);
        if (asset is null) { return Err($"No existe el adjunto '{nombre}' en el cerebro de '{slug}'. Usa listar_adjuntos."); }

        // Los clientes MCP aceptan PNG/JPEG/GIF/WEBP como bloque de imagen; BMP y PDF solo se describen.
        var returnable = asset.IsImage && asset.ContentType != "image/bmp" && asset.SizeBytes <= MaxCaptureBytes;
        var info = new
        {
            ok = true,
            cerebro = slug,
            nombre = asset.Name,
            tipo = asset.IsImage ? "imagen" : "pdf",
            kb = Math.Max(1, asset.SizeBytes / 1024),
            en_notas = asset.UsedIn,
            imagen = returnable ? "adjunta"
                : !asset.IsImage ? "los PDF no se devuelven por este servidor; abrelo en el cerebro"
                : asset.ContentType == "image/bmp" ? "no se devuelve: formato BMP no soportado por los clientes MCP"
                : $"no se devuelve: pesa mas de {MaxCaptureBytes / (1024 * 1024)} MB"
        };
        if (!returnable) { return Ok(info); }
        var content = await _files.ReadForTenantAsync(asset.Id, actor.TenantId, ct);
        if (content is null || content.Content.Length == 0) { return Err($"No se pudo leer el adjunto '{asset.Name}'."); }
        return new McpToolResult(JsonSerializer.Serialize(info, JsonOut), false)
        {
            Media = new McpMedia(Convert.ToBase64String(content.Content), asset.ContentType, asset.Name)
        };
    }

    private async Task<McpToolResult> CreateNoteAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var titulo = Str(args, "titulo");
        if (titulo is null) { return Err("'titulo' es obligatorio."); }
        var contenido = RawStr(args, "contenido") ?? string.Empty;
        var (slug, error) = await ResolveBrainAsync(args, ct);
        if (error is not null) { return Err(error); }
        var res = await _brain.SaveNoteAsync(slug!, null, titulo, contenido, null, ViaIa(actor), ct);
        return res.IsOk ? Ok(NotePayload(slug!, res.Value!)) : Err(res.Error ?? "No se pudo crear la nota.");
    }

    private async Task<McpToolResult> EditNoteAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var titulo = Str(args, "titulo");
        if (titulo is null) { return Err("'titulo' es obligatorio."); }
        var (slug, error) = await ResolveBrainAsync(args, ct);
        if (error is not null) { return Err(error); }
        var note = await _brain.GetNoteAsync(slug!, null, titulo, ct);
        if (note is null) { return Err($"No existe la nota '{titulo}'. Usa crear_nota."); }

        var contenido = RawStr(args, "contenido");
        var modo = (Str(args, "modo") ?? "agregar").ToLowerInvariant();
        long? version = args.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
        if (modo == "reemplazar" && contenido is not null && version is null)
        {
            return Err("Para reemplazar el cuerpo pasa 'version' (la que devolvio ver_nota), asi no pisas cambios ajenos.");
        }
        var body = contenido is null ? note.Body
            : modo == "reemplazar" ? contenido
            : note.Body.TrimEnd() + (note.Body.Trim().Length > 0 ? "\n\n" : string.Empty) + contenido;
        var res = await _brain.SaveNoteAsync(slug!, note.Id, Str(args, "nuevo_titulo") ?? note.Title, body, version ?? note.Version, ViaIa(actor), ct);
        return res.IsOk ? Ok(NotePayload(slug!, res.Value!)) : Err(res.Error ?? "No se pudo editar la nota.");
    }

    private async Task<McpToolResult> GraphAsync(JsonElement args, CancellationToken ct)
    {
        var (slug, error) = await ResolveBrainAsync(args, ct);
        if (error is not null) { return Err(error); }
        var graph = await _brain.GetGraphAsync(slug!, ct);
        if (graph is null) { return Err($"No existe el proyecto de maqueta '{slug}'."); }
        var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
        return Ok(new
        {
            ok = true,
            cerebro = slug,
            nodos = graph.Nodes.Count,
            aristas = graph.Edges.Count,
            por_tipo = graph.Nodes.GroupBy(n => n.Kind).ToDictionary(g => TipoNodo(g.Key), g => g.Count()),
            mas_conectados = graph.Nodes.OrderByDescending(n => n.Degree).Take(15)
                .Select(n => new { tipo = TipoNodo(n.Kind), nombre = n.Label, conexiones = n.Degree }),
            notas_sueltas = graph.Nodes.Where(n => n.Kind == "note" && n.Degree == 0).Select(n => n.Label).Take(50),
            notas_pendientes = graph.Nodes.Where(n => n.Kind == "missing").Select(n => n.Label).Take(50),
            enlaces = graph.Edges.Take(400).Select(e => new { de = labels.GetValueOrDefault(e.Source), a = labels.GetValueOrDefault(e.Target) })
        });
    }

    /// <param name="adjuntos">Adjuntos que embebe la nota (solo en ver_nota); null = se omite.</param>
    private object NotePayload(string slug, MockupNoteDto note, IReadOnlyList<object>? adjuntos = null) => new
    {
        ok = true,
        cerebro = slug,
        titulo = note.Title,
        version = note.Version,
        contenido = note.Body,
        autor = note.AuthorName,
        actualizada = note.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'"),
        por = note.UpdatedByName,
        enlaces = note.Links.Select(l => new
        {
            tipo = TipoNodo(l.Kind == MockupBrainLinkKinds.Note && !l.Exists ? "missing" : l.Kind),
            destino = l.Label,
            existe = l.Exists
        }),
        la_enlazan = note.Backlinks.Select(b => new { titulo = b.Title, resumen = b.Snippet }),
        adjuntos,
        abrir = Url($"/cerebros/{slug}?nota={Uri.EscapeDataString(note.Title)}")
    };

    private static McpActor ViaIa(McpActor actor)
        => actor.DisplayName.EndsWith("(via IA)", StringComparison.Ordinal) ? actor : actor with { DisplayName = actor.DisplayName + " (via IA)" };

    private static string TipoNodo(string kind) => kind switch
    {
        "note" => "nota",
        "page" => "pantalla",
        "task" => "tarjeta",
        "tag" => "etiqueta",
        "missing" => "nota pendiente",
        _ => kind
    };

    /// <summary>Texto tal cual (sin recortar espacios ni saltos), o null si no viene.</summary>
    private static string? RawStr(JsonElement args, string name)
        => args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ------------------------------------------------------------------ helpers

    private sealed record LinkInfo(Guid TaskId, string Number, string Source, string? MetadataJson);

    /// <summary>Tarjeta por numero, SOLO si nacio de una maqueta (origen maqueta-*). El join aplica el filtro de tenant.</summary>
    private async Task<LinkInfo?> FindLinkAsync(string numero, CancellationToken ct)
    {
        var n = numero.Trim().ToUpperInvariant();
        return await (from t in _db.TaskItems.AsNoTracking()
                      where t.Number == n
                      join l in _db.ExternalTaskLinks.AsNoTracking() on t.Id equals l.TaskItemId
                      where l.Source.StartsWith(MockupService.SourcePrefix)
                      select new LinkInfo(t.Id, t.Number, l.Source, l.MetadataJson))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Cerebro de las herramientas de notas (ADR-0200 ola 4): 'cerebro' (slug) tiene prioridad; si no viene, 'proyecto'
    /// usa el cerebro PRINCIPAL de esa maqueta (compatibilidad; un slug de cerebro tambien sirve). Sin ninguno: si la
    /// empresa tiene un solo cerebro, ese; si no, el de la unica maqueta.
    /// </summary>
    private async Task<(string? Slug, string? Error)> ResolveBrainAsync(JsonElement args, CancellationToken ct)
    {
        if (Str(args, "cerebro") is { } cerebro)
        {
            var clean = cerebro.Trim().ToLowerInvariant();
            if (!MockupService.IsValidSlug(clean)) { return (null, $"Cerebro invalido: '{cerebro}'. Usa el slug de listar_cerebros."); }
            return await _db.Brains.AsNoTracking().AnyAsync(b => b.Slug == clean, ct)
                ? (clean, null)
                : (null, $"No existe el cerebro '{clean}'. Usa listar_cerebros.");
        }
        var proyecto = Str(args, "proyecto");
        if (proyecto is null)
        {
            var only = await _db.Brains.AsNoTracking().OrderBy(b => b.Name).Select(b => b.Slug).Take(2).ToListAsync(ct);
            if (only.Count == 1) { return (only[0], null); }
            if (only.Count > 1 && (await _mockups.ListProjectsAsync(ct)).Count != 1)
            {
                return (null, "Indica 'cerebro' (slug de listar_cerebros) o 'proyecto' (maqueta).");
            }
        }
        var (slug, error) = await ResolveProjectAsync(proyecto, ct);
        if (error is not null) { return (null, error + " O indica 'cerebro' (ver listar_cerebros)."); }
        var primary = await (from l in _db.MockupProjectBrains.AsNoTracking()
                             join p in _db.MockupProjects.AsNoTracking() on l.MockupProjectId equals p.Id
                             join b in _db.Brains.AsNoTracking() on l.BrainId equals b.Id
                             where p.Slug == slug && p.DeletedAt == null
                             orderby l.SortOrder, l.CreatedAt
                             select b.Slug)
            .FirstOrDefaultAsync(ct);
        if (primary is not null) { return (primary, null); }
        if (await _db.Brains.AsNoTracking().AnyAsync(x => x.Slug == slug, ct)) { return (slug, null); }
        return (null, $"La maqueta '{slug}' no tiene cerebro vinculado. Crealo desde la pestana Cerebros de la maqueta o usa 'cerebro' (listar_cerebros).");
    }

    /// <summary>listar_cerebros (ADR-0200 ola 4): cerebros activos del tenant con su categoria, notas, candado y maquetas.</summary>
    private async Task<McpToolResult> ListBrainsAsync(JsonElement args, CancellationToken ct)
    {
        var term = (Str(args, "texto") ?? string.Empty).Trim().ToLowerInvariant();
        var query = _db.Brains.AsNoTracking();
        if (term.Length > 0)
        {
            query = query.Where(b => b.Name.ToLower().Contains(term)
                || (b.Description != null && b.Description.ToLower().Contains(term))
                || (b.Category != null && b.Category.Name.ToLower().Contains(term)));
        }
        var brains = await query
            .OrderBy(b => b.Name)
            .Select(b => new
            {
                b.Id,
                b.Slug,
                b.Name,
                b.Description,
                b.IsLocked,
                b.SourceTemplateId,
                Categoria = b.Category != null ? b.Category.Name : null
            })
            .Take(200)
            .ToListAsync(ct);
        var ids = brains.Select(b => b.Id).ToList();
        var notes = await _db.MockupNotes.AsNoTracking()
            .Where(n => ids.Contains(n.BrainId))
            .GroupBy(n => n.BrainId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var links = await (from l in _db.MockupProjectBrains.AsNoTracking()
                           join p in _db.MockupProjects.AsNoTracking() on l.MockupProjectId equals p.Id
                           where p.DeletedAt == null && ids.Contains(l.BrainId)
                           select new { l.BrainId, l.MockupProjectId, l.SortOrder, l.CreatedAt, p.Slug })
            .ToListAsync(ct);
        // Principal de cada maqueta = menor orden entre sus cerebros ACTIVOS (los de esta lista).
        var allLinks = await (from l in _db.MockupProjectBrains.AsNoTracking()
                              join b in _db.Brains.AsNoTracking() on l.BrainId equals b.Id
                              select new { l.BrainId, l.MockupProjectId, l.SortOrder, l.CreatedAt })
            .ToListAsync(ct);
        var primaryByProject = allLinks.GroupBy(l => l.MockupProjectId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAt).First().BrainId);
        return Ok(new
        {
            ok = true,
            total = brains.Count,
            cerebros = brains.Select(b => new
            {
                cerebro = b.Slug,
                nombre = b.Name,
                descripcion = b.Description,
                categoria = b.Categoria,
                notas = notes.GetValueOrDefault(b.Id),
                solo_lectura = b.IsLocked,
                sembrado_por_plataforma = b.SourceTemplateId is not null,
                maquetas = links.Where(l => l.BrainId == b.Id).OrderBy(l => l.Slug)
                    .Select(l => new { proyecto = l.Slug, principal = primaryByProject.GetValueOrDefault(l.MockupProjectId) == b.Id })
                    .ToList()
            }).ToList(),
            sugerencia = "Pasa 'cerebro' (slug) a listar_notas, ver_nota, crear_nota, editar_nota, grafo_cerebro y adjuntos."
        });
    }

    /// <summary>Proyecto por slug; si no viene y la empresa tiene UNO solo, ese. Un slug sin proyecto (origen del kit
    /// heredado, ej. "tronox") tambien vale para listar sus solicitudes.</summary>
    private async Task<(string? Slug, string? Error)> ResolveProjectAsync(string? proyecto, CancellationToken ct)
    {
        if (proyecto is not null)
        {
            var slug = proyecto.Trim().ToLowerInvariant();
            return MockupService.IsValidSlug(slug) ? (slug, null) : (null, $"Proyecto invalido: '{proyecto}'. Usa el slug de listar_proyectos.");
        }
        var projects = await _mockups.ListProjectsAsync(ct);
        return projects.Count switch
        {
            1 => (projects[0].Slug, null),
            0 => (null, "La empresa no tiene proyectos de maqueta."),
            _ => (null, "Indica 'proyecto' (slug). Proyectos: " + string.Join(", ", projects.Select(p => p.Slug)))
        };
    }

    /// <summary>Ultima captura (imagen) adjunta a la tarjeta, leida del gestor de archivos del tenant.</summary>
    private async Task<(McpMedia? Media, string? Note)> LoadCaptureAsync(Guid taskId, Guid tenantId, CancellationToken ct)
    {
        var attachments = await _db.TaskItemAttachments.AsNoTracking()
            .Where(a => a.TaskItemId == taskId && a.MimeType != null && a.MimeType.StartsWith("image/"))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Url, a.MimeType, a.FileName, a.SizeBytes })
            .Take(5)
            .ToListAsync(ct);
        foreach (var a in attachments)
        {
            if (a.SizeBytes > MaxCaptureBytes) { return (null, $"la captura pesa {a.SizeBytes / 1024} KB (tope {MaxCaptureBytes / 1024} KB)"); }
            if (!TryFileId(a.Url, out var fileId)) { continue; }
            var content = await _files.ReadForTenantAsync(fileId, tenantId, ct);
            if (content is null || content.Content.Length == 0) { continue; }
            if (content.Content.Length > MaxCaptureBytes) { return (null, "la captura supera el tope para devolverla como imagen"); }
            return (new McpMedia(Convert.ToBase64String(content.Content), a.MimeType ?? "image/png", a.FileName), null);
        }
        return (null, attachments.Count == 0 ? "la tarjeta no tiene captura" : "no se pudo leer la captura");
    }

    /// <summary>Id del archivo desde la Url del gestor ("/archivo/{id}").</summary>
    public static bool TryFileId(string? url, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(url)) { return false; }
        var path = url.Split('?', '#')[0].TrimEnd('/');
        var last = path[(path.LastIndexOf('/') + 1)..];
        return path.Contains("/archivo/", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(last, out id);
    }

    private string Url(string path) => (PublicBaseUrl ?? string.Empty).TrimEnd('/') + path;

    private static string? Str(JsonElement args, string name)
        => args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static string Prioridad(TaskPriority p) => p switch
    {
        TaskPriority.High => "alta",
        TaskPriority.Low => "baja",
        _ => "media"
    };

    private static string Estado(TaskItemStatus s) => s switch
    {
        TaskItemStatus.Pending => "pendiente",
        TaskItemStatus.Active => "activa",
        TaskItemStatus.InProgress => "en progreso",
        TaskItemStatus.Done => "terminada",
        TaskItemStatus.Suspended => "suspendida",
        TaskItemStatus.Closed => "cerrada",
        _ => s.ToString()
    };

    private static McpToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), false);

    private static McpToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), true);
}
