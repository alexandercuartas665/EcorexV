using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>
/// Herramientas del agente para PLANTILLAS DE WHATSAPP (HSM) (ADR-0175, fase 2):
///  - get_whatsapp_template / list_whatsapp_context / verify_whatsapp: solo lectura (sin gate);
///  - check_meta_status: consulta en YCloud si Meta ya aprobo/rechazo la plantilla (ADR-0184). Sin gate: solo
///    reconcilia el ESTADO con el del proveedor, no cambia el contenido;
///  - save_whatsapp_draft: crea o actualiza el BORRADOR via IWhatsAppTemplateService (validacion, unicidad,
///    auditoria) -> gate + version previa;
///  - submit_to_meta: somete el borrador guardado (YCloud lo crea en Meta). IRREVERSIBLE: ademas del gate exige
///    la confirmacion explicita aparte, y se niega si la verificacion tiene errores.
/// </summary>
public sealed class WhatsAppTemplateAgentToolset : ITemplateAgentToolset
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal) { "get_whatsapp_template", "list_whatsapp_context", "verify_whatsapp", "check_meta_status", "list_images" };
    private static readonly HashSet<string> Mutating = new(StringComparer.Ordinal) { "save_whatsapp_draft" };
    private static readonly HashSet<string> Irreversible = new(StringComparer.Ordinal) { "submit_to_meta" };

    // Meta solo acepta JPG/PNG en el encabezado de imagen (no GIF/WebP/SVG).
    private static readonly HashSet<string> WaImageMimes = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg" };

    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppTemplateService _templates;
    private readonly INotifyLinkBuilder _links;

    public WhatsAppTemplateAgentToolset(IApplicationDbContext db, IWhatsAppTemplateService templates, INotifyLinkBuilder links)
    {
        _db = db;
        _templates = templates;
        _links = links;
    }

    public TemplateAgentKind Kind => TemplateAgentKind.WhatsApp;
    public IReadOnlySet<string> ReadOnlyTools => ReadOnly;
    public IReadOnlySet<string> IrreversibleTools => Irreversible;
    public IReadOnlySet<string> TemplateMutatingTools => Mutating;

    private const string DraftSchema = """
        {"type":"object","properties":{
          "nombre":{"type":"string","description":"Nombre tecnico snake_case en minusculas"},
          "idioma":{"type":"string","description":"es, es_CO, en_US..."},
          "categoria":{"type":"string","enum":["Utility","Marketing"]},
          "encabezado":{"type":"string","description":"Encabezado de TEXTO opcional (max 60, sin variables)"},
          "cuerpo":{"type":"string","description":"Cuerpo con variables {{nombre_variable}} (max 1024)"},
          "pie":{"type":"string","description":"Pie opcional (max 60, sin variables)"},
          "imagen_url":{"type":"string","description":"Encabezado de IMAGEN: URL https de una imagen JPG/PNG de la galeria (list_images). Excluye el encabezado de texto. Cadena vacia = quitar la imagen."},
          "botones":{"type":"array","description":"Botones (max 10). Omitelo para conservar los actuales; lista vacia los quita.","items":{"type":"object","properties":{
            "tipo":{"type":"string","enum":["respuesta_rapida","sitio_web","llamar"]},
            "texto":{"type":"string","description":"Max 25 caracteres, sin variables"},
            "url":{"type":"string","description":"Solo sitio_web: https://... fija"},
            "telefono":{"type":"string","description":"Solo llamar: formato internacional +57..."}},"required":["tipo","texto"]}},
          "variables":{"type":"array","items":{"type":"object","properties":{"token":{"type":"string"},"ejemplo":{"type":"string"}},"required":["token","ejemplo"]}},
          "linea_id":{"type":"string","description":"Id de la linea (de list_whatsapp_context); obligatorio si hay varias"}
        },"required":["nombre","categoria","cuerpo","variables"]}
        """;

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec("get_whatsapp_template", "Lee la plantilla de WhatsApp abierta (campos, variables, estado, linea).", """{"type":"object","properties":{}}"""),
        new AiToolSpec("list_whatsapp_context", "Lista las lineas de WhatsApp del tenant (id, proveedor, estado) y los nombres de variable que el motor de acciones llena solo.", """{"type":"object","properties":{}}"""),
        new AiToolSpec("verify_whatsapp", "Verifica un borrador (o la plantilla guardada si no se pasan argumentos) contra las reglas de Meta. Usala antes de guardar.", DraftSchema.Replace("\"required\":[\"nombre\",\"categoria\",\"cuerpo\",\"variables\"]", "\"required\":[]")),
        new AiToolSpec("save_whatsapp_draft", "Guarda el BORRADOR completo de la plantilla (crea si es nueva). No la envia a Meta. Requiere confirmacion.", DraftSchema),
        new AiToolSpec("list_images", "Lista las imagenes JPG/PNG de la GALERIA del tenant (url publica, descripcion, ancho x alto) para usarlas como encabezado de imagen. Usa SOLO estas URLs o las que el usuario escriba.", """{"type":"object","properties":{}}"""),
        new AiToolSpec("check_meta_status", "Consulta en YCloud/Meta el estado de aprobacion de la plantilla abierta (en revision, aprobada, rechazada con motivo, pausada) y lo actualiza en el sistema.", """{"type":"object","properties":{}}"""),
        new AiToolSpec("submit_to_meta", "Somete el borrador GUARDADO a Meta para aprobacion (via YCloud). IRREVERSIBLE: el usuario lo confirma aparte. Solo cuando el usuario lo pida explicitamente.", """{"type":"object","properties":{}}"""),
    };

    public async Task<string> ExecuteAsync(TemplateAgentToolContext context, string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var args = ParseArgs(argumentsJson);
        var root = args.RootElement;
        switch (toolName)
        {
            case "get_whatsapp_template":
                {
                    if (context.TemplateId is not Guid id) { return Ok(new { exists = false, note = "No hay plantilla abierta: es NUEVA. Usa save_whatsapp_draft para crearla." }); }
                    var t = await _templates.GetAsync(id, cancellationToken);
                    if (t is null) { return Fail("La plantilla ya no existe."); }
                    return Ok(new
                    {
                        exists = true,
                        template_id = t.Id,
                        nombre = t.Name,
                        idioma = t.Language,
                        categoria = t.Category.ToString(),
                        tipo_encabezado = t.HeaderType?.ToString(),
                        encabezado = t.HeaderText,
                        encabezado_media_url = t.HeaderMediaUrl,
                        cuerpo = t.BodyText,
                        pie = t.FooterText,
                        variables = t.Variables.Select(v => new { token = v.Token, ejemplo = v.Example }),
                        botones = t.Buttons.Select(b => new { tipo = ButtonKind(b.Type), texto = b.Text, url = b.Url, telefono = b.PhoneNumber }),
                        estado = t.Status.ToString(),
                        editable = WhatsAppTemplateCalculations.CanEdit(t.Status) || t.Provider == WhatsAppProvider.Evolution,
                        linea_id = t.WhatsAppLineId,
                        linea = t.WhatsAppLineName,
                        motivo_rechazo = t.RejectionReason,
                    });
                }
            case "list_whatsapp_context":
                {
                    var lines = await _db.WhatsAppLines.AsNoTracking()
                        .Select(l => new { l.Id, l.InstanceName, l.PhoneNumber, l.Provider, l.Status, YCloud = l.YCloudWabaId != null && l.YCloudApiKeyEncrypted != null })
                        .ToListAsync(cancellationToken);
                    return Ok(new
                    {
                        lineas = lines.Select(l => new
                        {
                            id = l.Id,
                            nombre = l.InstanceName,
                            telefono = l.PhoneNumber,
                            proveedor = l.Provider.ToString(),
                            estado = l.Status.ToString(),
                            somete_a_meta = l.Provider == WhatsAppProvider.YCloud && l.YCloud,
                        }),
                        variables_motor = new[] { "nombre (o cliente)", "empresa", "cargo", "ciudad", "correo", "telefono" },
                        nota = "Las variables con esos nombres se llenan solas desde el contacto en el motor de acciones; otras se mapean a mano en el paso.",
                    });
                }
            case "list_images":
                {
                    var imgs = await _db.TemplateAssets.AsNoTracking()
                        .OrderByDescending(a => a.CreatedAt)
                        .Select(a => new { a.FileName, a.Url, a.Alt, a.Width, a.Height, a.MimeType })
                        .Take(60)
                        .ToListAsync(cancellationToken);
                    var list = imgs
                        .Where(a => a.MimeType is not null && WaImageMimes.Contains(a.MimeType))
                        .Select(a => new { nombre = a.FileName, url = _links.ToAbsoluteUrl(a.Url) ?? a.Url, descripcion = a.Alt ?? a.FileName, ancho = a.Width, alto = a.Height })
                        .ToList();
                    return Ok(new
                    {
                        count = list.Count,
                        images = list,
                        note = list.Count == 0
                            ? "La galeria no tiene imagenes JPG/PNG: pide al usuario que suba una en el boton Galeria del panel."
                            : "Para WhatsApp la imagen ideal es horizontal 1,91:1 (p.ej. 1200 x 628) y pesa menos de 5 MB.",
                    });
                }
            case "verify_whatsapp":
                {
                    WhatsAppDraft? draft;
                    if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any())
                    {
                        draft = DraftFromArgs(root);
                    }
                    else
                    {
                        if (context.TemplateId is not Guid id) { return Fail("No hay plantilla guardada que verificar; pasa el borrador."); }
                        draft = await DraftFromDbAsync(id, cancellationToken);
                        if (draft is null) { return Fail("La plantilla ya no existe."); }
                    }
                    var r = WhatsAppTemplateVerifier.Verify(draft);
                    return Ok(new { errors = r.Errors, warnings = r.Warnings, issues = r.Issues, vista_previa = Preview(draft) });
                }
            case "save_whatsapp_draft":
                return await SaveAsync(context, root, cancellationToken);
            case "submit_to_meta":
                return await SubmitAsync(context, cancellationToken);
            case "check_meta_status":
                return await CheckStatusAsync(context, cancellationToken);
            default:
                return Fail($"Herramienta desconocida: {toolName}.");
        }
    }

    private async Task<string> SaveAsync(TemplateAgentToolContext context, JsonElement root, CancellationToken ct)
    {
        var draft = DraftFromArgs(root);
        var lineId = Guid.TryParse(Str(root, "linea_id"), out var lid) ? lid : Guid.Empty;
        WhatsAppTemplateDto? current = null;
        if (context.TemplateId is Guid id)
        {
            current = await _templates.GetAsync(id, ct);
            if (current is null) { return Fail("La plantilla ya no existe."); }
            if (lineId == Guid.Empty) { lineId = current.WhatsAppLineId; }
        }
        if (lineId == Guid.Empty)
        {
            var lines = await _db.WhatsAppLines.AsNoTracking().Select(l => l.Id).ToListAsync(ct);
            if (lines.Count == 1) { lineId = lines[0]; }
            else { return Fail(lines.Count == 0 ? "El tenant no tiene lineas de WhatsApp." : "Hay varias lineas: pasa linea_id (ver list_whatsapp_context)."); }
        }

        // Encabezado: imagen de la galeria (imagen_url) > texto > el de media que ya tenia la plantilla (lo subio
        // el usuario en el editor). imagen_url = "" quita la imagen.
        var imageArg = Str(root, "imagen_url");
        var keepMedia = imageArg is null && string.IsNullOrWhiteSpace(draft.Encabezado)
            && current?.HeaderType is WhatsAppTemplateHeaderType.Image or WhatsAppTemplateHeaderType.Document or WhatsAppTemplateHeaderType.Video;
        WhatsAppTemplateHeaderType? headerType;
        string? mediaUrl = null;
        if (!string.IsNullOrWhiteSpace(imageArg)) { headerType = WhatsAppTemplateHeaderType.Image; mediaUrl = imageArg.Trim(); }
        else if (!string.IsNullOrWhiteSpace(draft.Encabezado)) { headerType = WhatsAppTemplateHeaderType.Text; }
        else if (keepMedia) { headerType = current!.HeaderType; mediaUrl = current.HeaderMediaUrl; }
        else { headerType = null; }
        var req = new SaveWhatsAppTemplateRequest(
            draft.Nombre ?? string.Empty, string.IsNullOrWhiteSpace(draft.Idioma) ? "es" : draft.Idioma!.Trim(), draft.Categoria,
            draft.Cuerpo ?? string.Empty, lineId,
            HeaderType: headerType,
            HeaderText: headerType == WhatsAppTemplateHeaderType.Text ? draft.Encabezado : null,
            HeaderMediaUrl: mediaUrl,
            FooterText: string.IsNullOrWhiteSpace(draft.Pie) ? null : draft.Pie,
            Variables: draft.Variables,
            // Botones: solo si el agente mando la lista (omitida = conservar los actuales).
            Buttons: HasProp(root, "botones") ? draft.Botones ?? Array.Empty<WhatsAppTemplateButtonDto>() : null);

        var result = current is null ? await _templates.CreateAsync(req, ct) : await _templates.UpdateAsync(current.Id, req, ct);
        if (!result.IsOk || result.Value is null) { return Fail(result.Error ?? "No se pudo guardar la plantilla."); }

        context.TemplateId = result.Value.Id;
        context.TemplateChanged = true;
        var saved = await DraftFromDbAsync(result.Value.Id, ct);
        var v = WhatsAppTemplateVerifier.Verify(saved ?? draft with { Nombre = result.Value.Name });
        return Ok(new { template_id = result.Value.Id, nombre = result.Value.Name, estado = result.Value.Status.ToString(), saved = true, verify = new { errors = v.Errors, warnings = v.Warnings, issues = v.Issues } });
    }

    private async Task<string> SubmitAsync(TemplateAgentToolContext context, CancellationToken ct)
    {
        if (context.TemplateId is not Guid id) { return Fail("Primero guarda el borrador (save_whatsapp_draft)."); }
        var draft = await DraftFromDbAsync(id, ct);
        if (draft is null) { return Fail("La plantilla ya no existe."); }
        var v = WhatsAppTemplateVerifier.Verify(draft);
        if (v.Errors > 0)
        {
            return Fail("No se somete: la verificacion tiene errores. Corrigelos primero.\n" + v.ErrorDetail());
        }
        var r = await _templates.SubmitAsync(id, ct);
        if (!r.IsOk || r.Value is null) { return Fail(r.Error ?? "No se pudo someter la plantilla."); }
        context.TemplateChanged = true;
        return Ok(new
        {
            submitted = true,
            estado = r.Value.Status.ToString(),
            proveedor = r.Value.Provider?.ToString(),
            nota = r.Value.Provider == WhatsAppProvider.YCloud
                ? "Quedo en revision de Meta. El sistema consulta el estado solo cada 10 minutos; usa check_meta_status para verlo ya."
                : "La linea no es YCloud: no hay revision de Meta.",
        });
    }

    private async Task<string> CheckStatusAsync(TemplateAgentToolContext context, CancellationToken ct)
    {
        if (context.TemplateId is not Guid id) { return Fail("No hay plantilla abierta."); }
        var r = await _templates.SyncStatusAsync(id, ct);
        if (!r.IsOk || r.Value is null) { return Fail(r.Error ?? "No se pudo consultar el estado."); }
        var t = await _templates.GetAsync(id, ct);
        if (t is null) { return Fail("La plantilla ya no existe."); }
        var change = r.Value.Changes.FirstOrDefault();
        if (change is not null) { context.TemplateChanged = true; }
        return Ok(new
        {
            estado = t.Status.ToString(),
            estado_texto = WhatsAppTemplateStatusSync.Label(t.Status),
            cambio = change is null ? null : $"{WhatsAppTemplateStatusSync.Label(change.From)} -> {WhatsAppTemplateStatusSync.Label(change.To)}",
            motivo_rechazo = t.RejectionReason,
            avisos = r.Value.Messages,
            nota = t.Status switch
            {
                WhatsAppTemplateStatus.Approved => "Aprobada: ya se puede usar en el paso WhatsApp del gestor de contactos y en los flujos.",
                WhatsAppTemplateStatus.Rejected => "Rechazada: corrige segun el motivo, guarda el borrador (vuelve a borrador) y sometela de nuevo.",
                WhatsAppTemplateStatus.Submitted => "Sigue en revision de Meta (suele tardar de minutos a 24 h).",
                _ => null,
            },
        });
    }

    public async Task<TemplateVerifyResult?> VerifyAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var d = await DraftFromDbAsync(templateId, cancellationToken);
        return d is null ? null : WhatsAppTemplateVerifier.Verify(d);
    }

    public async Task<string?> SnapshotPayloadAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var t = await _templates.GetAsync(templateId, cancellationToken);
        if (t is null) { return null; }
        var snap = new WaSnapshot(t.Name, t.Language, t.Category, t.HeaderType, t.HeaderText, t.HeaderMediaUrl, t.BodyText, t.FooterText,
            t.Variables.ToList(), t.WhatsAppLineId, t.Buttons.ToList());
        return JsonSerializer.Serialize(snap, Json);
    }

    public async Task<bool> RestoreAsync(Guid templateId, string payloadJson, CancellationToken cancellationToken = default)
    {
        WaSnapshot? s;
        try { s = JsonSerializer.Deserialize<WaSnapshot>(payloadJson, Json); }
        catch (JsonException) { return false; }
        if (s is null) { return false; }
        var r = await _templates.UpdateAsync(templateId, new SaveWhatsAppTemplateRequest(
            s.Name, s.Language, s.Category, s.BodyText, s.LineId, s.HeaderType, s.HeaderText, s.HeaderMediaUrl, s.FooterText, s.Variables,
            s.Buttons), cancellationToken);
        return r.IsOk;
    }

    public async Task<string?> GetTitleAsync(Guid templateId, CancellationToken cancellationToken = default)
        => (await _templates.GetAsync(templateId, cancellationToken))?.Name;

    public Task<string> SystemPromptAsync(string tenantName, Guid? templateId, CancellationToken cancellationToken = default)
        => Task.FromResult(TemplateAgentHarness.WhatsAppSystemPrompt(tenantName, editingExisting: templateId is not null));

    private sealed record WaSnapshot(string Name, string Language, WhatsAppTemplateCategory Category, WhatsAppTemplateHeaderType? HeaderType,
        string? HeaderText, string? HeaderMediaUrl, string BodyText, string? FooterText, List<WhatsAppTemplateVariable> Variables, Guid LineId,
        // Versiones anteriores a ADR-0186 no traen botones (null = conservar los actuales al restaurar).
        List<WhatsAppTemplateButtonDto>? Buttons = null);

    private async Task<WhatsAppDraft?> DraftFromDbAsync(Guid id, CancellationToken ct)
    {
        var t = await _templates.GetAsync(id, ct);
        return t is null ? null : new WhatsAppDraft(t.Name, t.Language, t.Category, t.HeaderType, t.HeaderText, t.HeaderMediaUrl, t.BodyText, t.FooterText, t.Variables,
            t.Buttons);
    }

    internal static WhatsAppDraft DraftFromArgs(JsonElement root)
    {
        var cat = Enum.TryParse<WhatsAppTemplateCategory>(Str(root, "categoria"), ignoreCase: true, out var c) ? c : WhatsAppTemplateCategory.Utility;
        var vars = new List<WhatsAppTemplateVariable>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("variables", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                var token = Str(el, "token");
                if (string.IsNullOrWhiteSpace(token)) { continue; }
                vars.Add(new WhatsAppTemplateVariable(token.Trim().Trim('{', '}').Trim(), (Str(el, "ejemplo") ?? Str(el, "example") ?? string.Empty).Trim()));
            }
        }
        var encabezado = Str(root, "encabezado");
        var imagen = Str(root, "imagen_url");
        var headerType = !string.IsNullOrWhiteSpace(imagen) ? WhatsAppTemplateHeaderType.Image
            : string.IsNullOrWhiteSpace(encabezado) ? (WhatsAppTemplateHeaderType?)null : WhatsAppTemplateHeaderType.Text;
        return new WhatsAppDraft(Str(root, "nombre"), Str(root, "idioma") ?? "es", cat,
            headerType, headerType == WhatsAppTemplateHeaderType.Text ? encabezado : null,
            headerType == WhatsAppTemplateHeaderType.Image ? imagen!.Trim() : null,
            Str(root, "cuerpo"), Str(root, "pie"), vars, ButtonsFromArgs(root));
    }

    /// <summary>Botones del agente ("tipo": respuesta_rapida | sitio_web | llamar) -> DTO normalizado.</summary>
    internal static IReadOnlyList<WhatsAppTemplateButtonDto>? ButtonsFromArgs(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("botones", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var list = new List<WhatsAppTemplateButtonDto>();
        foreach (var el in arr.EnumerateArray())
        {
            var tipo = WhatsAppTemplateButtons.CanonicalType(Str(el, "tipo") ?? Str(el, "type"));
            var texto = Str(el, "texto") ?? Str(el, "text");
            if (tipo is null || string.IsNullOrWhiteSpace(texto)) { continue; }
            list.Add(new WhatsAppTemplateButtonDto(tipo, texto.Trim(),
                tipo == WhatsAppTemplateButtons.Url ? Str(el, "url")?.Trim() : null,
                tipo == WhatsAppTemplateButtons.Phone ? (Str(el, "telefono") ?? Str(el, "phone"))?.Trim() : null,
                false));
        }
        return WhatsAppTemplateButtons.Normalize(list);
    }

    private static bool HasProp(JsonElement root, string prop)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out _);

    private static string ButtonKind(string type) => type switch
    {
        WhatsAppTemplateButtons.QuickReply => "respuesta_rapida",
        WhatsAppTemplateButtons.Url => "sitio_web",
        WhatsAppTemplateButtons.Phone => "llamar",
        _ => type.ToLowerInvariant(),
    };

    /// <summary>Vista previa en texto con los ejemplos aplicados (para el chat y la tarjeta de confirmacion).</summary>
    internal static string Preview(WhatsAppDraft d)
    {
        var body = d.Cuerpo ?? string.Empty;
        foreach (var v in d.Variables)
        {
            body = System.Text.RegularExpressions.Regex.Replace(body, @"\{\{\s*" + System.Text.RegularExpressions.Regex.Escape(v.Token) + @"\s*\}\}",
                string.IsNullOrWhiteSpace(v.Example) ? "[" + v.Token + "]" : v.Example, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        var parts = new List<string>();
        if (d.TipoEncabezado == WhatsAppTemplateHeaderType.Image && !string.IsNullOrWhiteSpace(d.EncabezadoMediaUrl)) { parts.Add("[Imagen: " + d.EncabezadoMediaUrl!.Trim() + "]"); }
        else if (!string.IsNullOrWhiteSpace(d.Encabezado)) { parts.Add("*" + d.Encabezado!.Trim() + "*"); }
        parts.Add(body.Trim());
        if (!string.IsNullOrWhiteSpace(d.Pie)) { parts.Add("_" + d.Pie!.Trim() + "_"); }
        if (d.Botones is { Count: > 0 } bs)
        {
            parts.Add(string.Join("\n", bs.Select(b => "[ " + b.Text + " ]" + (b.Type == WhatsAppTemplateButtons.Url ? " -> " + b.Url
                : b.Type == WhatsAppTemplateButtons.Phone ? " -> " + b.PhoneNumber : string.Empty))));
        }
        return string.Join("\n\n", parts);
    }

    private static JsonDocument ParseArgs(string? json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException) { return JsonDocument.Parse("{}"); }
    }

    private static string? Str(JsonElement root, string prop)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Ok(object payload)
    {
        var node = JsonSerializer.SerializeToNode(payload, Json)!.AsObject();
        node["ok"] = true;
        return node.ToJsonString(Json);
    }

    private static string Fail(string error) => JsonSerializer.Serialize(new { ok = false, error }, Json);
}
