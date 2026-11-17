using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>
/// Herramientas del agente para PLANTILLAS DE CORREO (ADR-0175):
///  - get_template / list_variables / verify_email: solo lectura (sin gate);
///  - save_template: guarda asunto + cuerpo completos (crea la plantilla si es nueva) -> gate + version previa;
///  - send_test: envia la plantilla con datos de ejemplo AL CORREO DEL USUARIO ACTUAL -> gate.
/// </summary>
public sealed class EmailTemplateAgentToolset : ITemplateAgentToolset
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal) { "get_template", "list_variables", "verify_email", "list_images" };
    private static readonly HashSet<string> Mutating = new(StringComparer.Ordinal) { "save_template" };
    private static readonly HashSet<string> Irreversible = new(StringComparer.Ordinal);

    // Formatos que los clientes de correo muestran (los SVG legados de la galeria no se ofrecen al agente).
    private static readonly HashSet<string> EmailImageMimes = new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif" };

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEmailSender _email;
    private readonly INotifyLinkBuilder _links;
    private readonly IEmailBrandService _brand;

    public EmailTemplateAgentToolset(IApplicationDbContext db, ITenantContext tenant, IEmailSender email, INotifyLinkBuilder links, IEmailBrandService brand)
    {
        _db = db;
        _tenant = tenant;
        _email = email;
        _links = links;
        _brand = brand;
    }

    public TemplateAgentKind Kind => TemplateAgentKind.Email;
    public IReadOnlySet<string> ReadOnlyTools => ReadOnly;
    public IReadOnlySet<string> IrreversibleTools => Irreversible;
    public IReadOnlySet<string> TemplateMutatingTools => Mutating;

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec("get_template",
            "Lee la plantilla de correo que se esta editando (nombre, asunto, cuerpo HTML). Llamala ANTES de proponer cambios a una plantilla existente.",
            """{"type":"object","properties":{}}"""),
        new AiToolSpec("list_variables",
            "Lista las variables de merge permitidas ({nombre}, {empresa}, ...) y la marca del tenant (nombre, logo, colores, estilo por defecto, datos de contacto del pie).",
            """{"type":"object","properties":{}}"""),
        new AiToolSpec("list_images",
            "Lista las imagenes de la GALERIA del tenant (url publica, descripcion/alt, ancho y alto en px). Usala cuando el correo lleve imagenes (hero, fotos, banners, iconos) o el usuario mencione una imagen de la galeria. Usa SOLO estas URLs, la del logo o las que el usuario escriba.",
            """{"type":"object","properties":{}}"""),
        new AiToolSpec("verify_email",
            "Verifica un borrador (o la plantilla guardada si no se pasan argumentos): variables desconocidas, HTML no apto para correo, enlaces, alt de imagenes, tamano, asunto y enlace de baja. Usala antes de save_template.",
            """{"type":"object","properties":{"asunto":{"type":"string"},"cuerpo_html":{"type":"string"}}}"""),
        new AiToolSpec("save_template",
            "Guarda la plantilla COMPLETA (reemplaza asunto y cuerpo). Si no hay plantilla abierta, la crea con el nombre dado. Requiere confirmacion del usuario.",
            """{"type":"object","properties":{"nombre":{"type":"string","description":"Nombre interno de la plantilla (obligatorio si es nueva)"},"asunto":{"type":"string"},"preheader":{"type":"string","description":"Texto corto que se ve junto al asunto en la bandeja (opcional)"},"cuerpo_html":{"type":"string","description":"Documento HTML completo apto para correo"},"estilo":{"type":"string","description":"Clave del estilo de diseno aplicado (corporativo, moderno, minimalista, editorial, elegante, promocional, personal)"}},"required":["asunto","cuerpo_html"]}"""),
        new AiToolSpec("send_test",
            "Envia la plantilla GUARDADA, con datos de ejemplo, al correo del usuario actual para revisarla en su bandeja. Requiere confirmacion.",
            """{"type":"object","properties":{}}"""),
    };

    public async Task<string> ExecuteAsync(TemplateAgentToolContext context, string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var args = ParseArgs(argumentsJson);
        var root = args.RootElement;
        switch (toolName)
        {
            case "get_template":
                {
                    if (context.TemplateId is not Guid id) { return Ok(new { exists = false, note = "No hay plantilla abierta: es una plantilla NUEVA. Usa save_template con un nombre para crearla." }); }
                    var t = await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                    if (t is null) { return Fail("La plantilla ya no existe."); }
                    return Ok(new { exists = true, template_id = t.Id, nombre = t.Nombre, asunto = t.Asunto, cuerpo_html = t.CuerpoHtml, activa = t.Activa, size_bytes = Encoding.UTF8.GetByteCount(t.CuerpoHtml ?? string.Empty) });
                }
            case "list_variables":
                {
                    var brand = await _brand.GetForAgentAsync(cancellationToken);
                    return Ok(new
                    {
                        variables = EmailTemplateService.Variables.Select(v => new { token = "{" + v.Token + "}", descripcion = v.Descripcion, ejemplo = v.Ejemplo }),
                        marca = new
                        {
                            empresa = brand.Empresa,
                            logo_url = brand.LogoUrl,
                            color_principal = brand.ColorPrimario,
                            color_acento = brand.ColorAcento,
                            estilo_por_defecto = brand.EstiloPorDefecto,
                            sitio_web = brand.SitioWeb,
                            direccion = brand.Direccion,
                            ciudad = brand.Ciudad,
                            telefono = brand.Telefono,
                            correo = brand.Correo,
                            pie_texto = brand.PieTexto,
                        },
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
                        .Where(a => a.MimeType is not null && EmailImageMimes.Contains(a.MimeType))
                        .Select(a => new { nombre = a.FileName, url = _links.ToAbsoluteUrl(a.Url) ?? a.Url, alt = a.Alt ?? a.FileName, ancho = a.Width, alto = a.Height })
                        .ToList();
                    return Ok(new
                    {
                        count = list.Count,
                        images = list,
                        note = list.Count == 0 ? "La galeria esta vacia: si el correo necesita fotos, pide al usuario que las suba en el boton Galeria del editor." : null,
                    });
                }
            case "verify_email":
                {
                    string? asunto = Str(root, "asunto");
                    string? cuerpo = Str(root, "cuerpo_html");
                    if (asunto is null && cuerpo is null)
                    {
                        if (context.TemplateId is not Guid id) { return Fail("No hay plantilla guardada que verificar; pasa asunto y cuerpo_html."); }
                        var t = await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                        if (t is null) { return Fail("La plantilla ya no existe."); }
                        asunto = t.Asunto; cuerpo = t.CuerpoHtml;
                    }
                    var r = EmailTemplateVerifier.Verify(asunto, cuerpo);
                    return Ok(new { errors = r.Errors, warnings = r.Warnings, issues = r.Issues });
                }
            case "save_template":
                return await SaveAsync(context, root, cancellationToken);
            case "send_test":
                return await SendTestAsync(context, cancellationToken);
            default:
                return Fail($"Herramienta desconocida: {toolName}.");
        }
    }

    private async Task<string> SaveAsync(TemplateAgentToolContext context, JsonElement root, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tenantId) { return Fail("Sin tenant activo."); }
        var asunto = (Str(root, "asunto") ?? string.Empty).Trim();
        var cuerpo = Str(root, "cuerpo_html") ?? string.Empty;
        var preheader = Str(root, "preheader");
        if (string.IsNullOrWhiteSpace(asunto) || string.IsNullOrWhiteSpace(cuerpo)) { return Fail("asunto y cuerpo_html son obligatorios."); }
        if (asunto.Length > 300) { asunto = asunto[..300]; }
        cuerpo = WithPreheader(cuerpo, preheader);

        EmailTemplate? entity = null;
        if (context.TemplateId is Guid id)
        {
            entity = await _db.EmailTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null) { return Fail("La plantilla ya no existe."); }
        }
        if (entity is null)
        {
            var nombre = (Str(root, "nombre") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nombre)) { nombre = asunto; }
            entity = new EmailTemplate { TenantId = tenantId, Nombre = nombre.Length > 200 ? nombre[..200] : nombre, Activa = true };
            _db.EmailTemplates.Add(entity);
        }
        else if (Str(root, "nombre") is { } nuevoNombre && !string.IsNullOrWhiteSpace(nuevoNombre))
        {
            entity.Nombre = nuevoNombre.Trim().Length > 200 ? nuevoNombre.Trim()[..200] : nuevoNombre.Trim();
        }
        entity.Asunto = asunto;
        entity.CuerpoHtml = cuerpo;
        if (EmailDesignStyles.Normalize(Str(root, "estilo")) is { } estilo) { entity.Estilo = estilo; }
        await _db.SaveChangesAsync(ct);

        context.TemplateId = entity.Id;
        context.TemplateChanged = true;
        var v = EmailTemplateVerifier.Verify(entity.Asunto, entity.CuerpoHtml);
        return Ok(new { template_id = entity.Id, nombre = entity.Nombre, saved = true, verify = new { errors = v.Errors, warnings = v.Warnings, issues = v.Issues } });
    }

    private async Task<string> SendTestAsync(TemplateAgentToolContext context, CancellationToken ct)
    {
        if (context.TemplateId is not Guid id) { return Fail("Primero guarda la plantilla (save_template)."); }
        var t = await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) { return Fail("La plantilla ya no existe."); }
        var me = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == context.ActorUserId)
            .Select(u => new { u.Email, u.FirmaHtml })
            .FirstOrDefaultAsync(ct);
        if (me is null || string.IsNullOrWhiteSpace(me.Email)) { return Fail("No encontre tu correo de usuario para enviar la prueba."); }

        var sample = EmailTemplateService.SampleFields with
        {
            Firma = string.IsNullOrWhiteSpace(me.FirmaHtml) ? EmailTemplateService.SampleFields.Firma : me.FirmaHtml,
            // Enlace de baja de PRUEBA: abre la pagina publica en modo "enlace no valido" (no da de baja a nadie).
            EnlaceBaja = _links.BuildUnsubscribeLink("prueba") ?? "#",
        };
        var subject = "[PRUEBA] " + EmailTemplateService.RenderTemplate(t.Asunto, sample, htmlEscapeValues: false);
        var html = EmailTemplateService.RenderTemplate(t.CuerpoHtml, sample, htmlEscapeValues: true);
        var r = await _email.SendAsync(me.Email, subject, html, ct);
        return r.Ok ? Ok(new { sent_to = me.Email }) : Fail("No se pudo enviar la prueba: " + (r.Error ?? "error del servidor de correo"));
    }

    public async Task<TemplateVerifyResult?> VerifyAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var t = await _db.EmailTemplates.AsNoTracking().Where(x => x.Id == templateId)
            .Select(x => new { x.Asunto, x.CuerpoHtml }).FirstOrDefaultAsync(cancellationToken);
        return t is null ? null : EmailTemplateVerifier.Verify(t.Asunto, t.CuerpoHtml);
    }

    public async Task<string?> SnapshotPayloadAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var t = await _db.EmailTemplates.AsNoTracking().Where(x => x.Id == templateId)
            .Select(x => new EmailSnapshot(x.Nombre, x.Asunto, x.CuerpoHtml)).FirstOrDefaultAsync(cancellationToken);
        return t is null ? null : JsonSerializer.Serialize(t, Json);
    }

    public async Task<bool> RestoreAsync(Guid templateId, string payloadJson, CancellationToken cancellationToken = default)
    {
        EmailSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<EmailSnapshot>(payloadJson, Json); }
        catch (JsonException) { return false; }
        if (snap is null) { return false; }
        var t = await _db.EmailTemplates.FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken);
        if (t is null) { return false; }
        if (!string.IsNullOrWhiteSpace(snap.Nombre)) { t.Nombre = snap.Nombre; }
        t.Asunto = snap.Asunto ?? string.Empty;
        t.CuerpoHtml = snap.CuerpoHtml ?? string.Empty;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<string?> GetTitleAsync(Guid templateId, CancellationToken cancellationToken = default)
        => await _db.EmailTemplates.AsNoTracking().Where(x => x.Id == templateId).Select(x => x.Nombre).FirstOrDefaultAsync(cancellationToken);

    public async Task<string> SystemPromptAsync(string tenantName, Guid? templateId, CancellationToken cancellationToken = default)
    {
        var brand = await _brand.GetForAgentAsync(cancellationToken);
        // Estilo con que se compuso la plantilla abierta (si lo tiene): el agente lo conserva salvo que el usuario elija otro.
        string? templateStyle = null;
        if (templateId is Guid id)
        {
            templateStyle = await _db.EmailTemplates.AsNoTracking().Where(x => x.Id == id).Select(x => x.Estilo).FirstOrDefaultAsync(cancellationToken);
        }
        return TemplateAgentHarness.EmailSystemPrompt(tenantName, editingExisting: templateId is not null, brand, EmailDesignStyles.Normalize(templateStyle));
    }

    private sealed record EmailSnapshot(string? Nombre, string? Asunto, string? CuerpoHtml);

    // Preheader: texto oculto al inicio del cuerpo que los clientes muestran junto al asunto.
    internal static string WithPreheader(string html, string? preheader)
    {
        if (string.IsNullOrWhiteSpace(preheader) || html.Contains("data-preheader", StringComparison.OrdinalIgnoreCase)) { return html; }
        var block = "<div data-preheader=\"1\" style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">"
            + System.Net.WebUtility.HtmlEncode(preheader.Trim()) + "</div>";
        var bodyIdx = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyIdx >= 0)
        {
            var close = html.IndexOf('>', bodyIdx);
            if (close > 0) { return html.Insert(close + 1, block); }
        }
        return block + html;
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
