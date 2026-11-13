using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Paso "Notificar" del dron (server-side, ADR-0167). Avisa a uno o varios usuarios que la tarea que dejo
/// "Crear actividad" en el contexto ({{TAREA}} / {{TAREA_NUEVA}}) ya existe, por el CANAL que elija el operador:
/// campana (in-app), correo, plantilla de WhatsApp, grupo de WhatsApp o Telegram. El mensaje es una PLANTILLA con
/// los tokens de la tarea ({tarea.numero}, {tarea.titulo}, {form.petof}, {tarea.enlace}...) del mismo resolvedor que usan
/// los avisos de los nodos de flujo, y admite tambien rutas del dron ({{DETALLE.cabecera.petof}}).
///
/// Es BEST-EFFORT: un fallo de envio no corta el bucle (se anota en la bitacora de la tarea y en el detalle);
/// solo una configuracion invalida devuelve Ok=false para que el operador la vea. Por defecto notifica SOLO si la
/// tarea se creo en esta corrida, asi re-correr el dron no repite avisos. La config vive en MappingJson.
/// </summary>
public static class ScrapeNotificar
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Regex ScrapePlaceholder = new(@"\{\{[^{}]*\}\}|@@[^@]*@@", RegexOptions.Compiled);

    public enum Canal { Campana = 0, Correo = 1, WhatsApp = 2, WhatsAppGrupo = 3, Telegram = 4 }

    public enum Destino { Responsable = 0, Usuarios = 1, Concepto = 2 }

    /// <summary>Config del paso, serializada en MappingJson (camelCase, enums como texto).</summary>
    public sealed record Config
    {
        /// <summary>Ruta/plantilla con el numero de la tarea (por defecto TAREA, la deja "Crear actividad").</summary>
        public string? Tarea { get; init; }
        /// <summary>Solo notificar si la tarea se creo en esta corrida (variable TAREA_NUEVA = "1").</summary>
        public bool SoloSiNueva { get; init; } = true;
        public Canal Canal { get; init; } = Canal.Campana;
        public Destino Destino { get; init; } = Destino.Responsable;
        /// <summary>TenantUsers cuando Destino = Usuarios.</summary>
        public List<Guid>? UsuarioIds { get; init; }
        /// <summary>Linea de WhatsApp (plantilla o grupo).</summary>
        public Guid? LineaId { get; init; }
        /// <summary>Nombre de la plantilla HSM aprobada (canal WhatsApp) y su idioma.</summary>
        public string? Plantilla { get; init; }
        public string? Idioma { get; init; }
        /// <summary>Variables de la plantilla de WhatsApp: variable -> expresion con tokens.</summary>
        public Dictionary<string, string>? Variables { get; init; }
        public string? GrupoJid { get; init; }
        public string? ChatId { get; init; }
        /// <summary>Asunto (correo) o titulo (campana). Plantilla con tokens.</summary>
        public string? Asunto { get; init; }
        /// <summary>Cuerpo del aviso (correo, campana, grupo, Telegram). Plantilla con tokens.</summary>
        public string? Mensaje { get; init; }
        /// <summary>Agrega el enlace directo a la tarea al final del mensaje (si hay URL publica configurada).</summary>
        public bool IncluirEnlace { get; init; } = true;
    }

    public sealed record Result(bool Ok, int Enviados, int Fallidos, string Detail);

    public static Config? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try { return JsonSerializer.Deserialize<Config>(json, Web); }
        catch (JsonException) { return null; }
    }

    public static string Serialize(Config cfg) => JsonSerializer.Serialize(cfg, Web);

    public const string DefaultAsunto = "Nueva tarea {tarea.numero}: {tarea.titulo}";
    public const string DefaultMensaje = "Se creo la tarea {tarea.numero} - {tarea.titulo}. Cliente: {tarea.cliente}.";

    /// <summary>
    /// Renderiza una plantilla: primero las rutas del dron ({{DETALLE.x}} / @@x@@, lo no resuelto se quita) y
    /// despues los tokens de la tarea ({tarea.numero}, {form.petof}...; lo desconocido queda vacio).
    /// </summary>
    public static string Render(ScrapeRunContext ctx, INotifyTokenResolver tokens, IReadOnlyDictionary<string, string> map, string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) { return string.Empty; }
        var s = template;
        if (s.Contains("{{") || s.Contains("@@"))
        {
            s = ScrapePlaceholder.Replace(ctx.Substitute(s) ?? string.Empty, string.Empty);
        }
        return tokens.Render(s, map).Trim();
    }

    public static async Task<Result> RunAsync(IServiceProvider sp, IApplicationDbContext db, Guid tenantId,
        ScrapeStep step, ScrapeRunContext ctx, string actorName, CancellationToken ct)
    {
        var cfg = Parse(step.MappingJson);
        if (cfg is null) { return Fail("El paso 'Notificar' no tiene configuracion."); }

        var tareaExpr = string.IsNullOrWhiteSpace(cfg.Tarea) ? ScrapeCrearActividad.DefaultOutputVar : cfg.Tarea!.Trim();
        var numero = ScrapeCrearActividad.Resolve(ctx, tareaExpr);
        if (numero.Length == 0) { return new Result(true, 0, 0, "Sin tarea en el contexto: no se notifica."); }
        if (cfg.SoloSiNueva)
        {
            var nueva = ScrapeCrearActividad.Resolve(ctx, NuevaVar(tareaExpr));
            if (nueva != "1") { return new Result(true, 0, 0, $"{numero}: ya existia, no se repite el aviso."); }
        }

        var task = await db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Number == numero, ct);
        if (task is null) { return Fail($"No existe la tarea {numero}."); }

        // Validacion de la config por canal (lo que falte es error del operador, no del envio).
        var error = cfg.Canal switch
        {
            Canal.WhatsApp when cfg.LineaId is null || string.IsNullOrWhiteSpace(cfg.Plantilla) => "WhatsApp: elige la linea y la plantilla aprobada.",
            Canal.WhatsAppGrupo when cfg.LineaId is null || string.IsNullOrWhiteSpace(cfg.GrupoJid) => "Grupo de WhatsApp: elige la linea y el grupo (JID ...@g.us).",
            Canal.Telegram when string.IsNullOrWhiteSpace(cfg.ChatId) => "Telegram: indica el chat (id).",
            _ => null,
        };
        if (error is not null) { return Fail(error); }

        var tokenSvc = sp.GetRequiredService<INotifyTokenResolver>();
        var map = new Dictionary<string, string>(await tokenSvc.BuildAsync(task, ct), StringComparer.OrdinalIgnoreCase);
        var link = sp.GetService<INotifyLinkBuilder>()?.BuildTaskLink(task.Id);
        // {tarea.enlace} para las plantillas (el renderer solo reconoce {ns.clave}); "enlace"/"url"/"link" sueltos para
        // las variables de WhatsApp que se casan por nombre.
        if (!string.IsNullOrWhiteSpace(link)) { map["tarea.enlace"] = link!; map["enlace"] = link!; map["url"] = link!; map["link"] = link!; }

        var asunto = Render(ctx, tokenSvc, map, string.IsNullOrWhiteSpace(cfg.Asunto) ? DefaultAsunto : cfg.Asunto);
        var mensaje = Render(ctx, tokenSvc, map, string.IsNullOrWhiteSpace(cfg.Mensaje) ? DefaultMensaje : cfg.Mensaje);
        if (cfg.IncluirEnlace && !string.IsNullOrWhiteSpace(link) && !mensaje.Contains(link!, StringComparison.Ordinal))
        {
            mensaje = $"{mensaje}\n{link}";
        }

        var sender = sp.GetService<INotificationChannelSender>();
        int ok = 0, fail = 0;
        var notas = new List<string>();

        // Canales de destino unico (no van por usuario).
        if (cfg.Canal is Canal.WhatsAppGrupo or Canal.Telegram)
        {
            var sent = cfg.Canal == Canal.WhatsAppGrupo
                ? sender is not null && await sender.SendWhatsAppGroupAsync(cfg.LineaId!.Value, cfg.GrupoJid!.Trim(), mensaje, Guid.Empty, ct)
                : sender is not null && await sender.SendTelegramAsync(cfg.ChatId!.Trim(), mensaje, ct);
            var dest = cfg.Canal == Canal.WhatsAppGrupo ? $"grupo {cfg.GrupoJid}" : $"Telegram {cfg.ChatId}";
            if (sent) { ok++; } else { fail++; }
            notas.Add(sent ? dest : $"{dest} (fallo)");
            await RecordAsync(db, tenantId, task, actorName, CanalTexto(cfg.Canal), notas, ct);
            return new Result(true, ok, fail, $"{numero}: aviso por {CanalTexto(cfg.Canal)} -> {string.Join(", ", notas)}.");
        }

        var destinatarios = await RecipientsAsync(db, task, cfg, ct);
        if (destinatarios.Count == 0)
        {
            return new Result(true, 0, 0, $"{numero}: sin destinatarios ({cfg.Destino}); no se notifica.");
        }

        var notifications = cfg.Canal == Canal.Campana ? sp.GetRequiredService<INotificationService>() : null;
        var broadcaster = cfg.Canal == Canal.Campana ? sp.GetService<INotificationBroadcaster>() : null;
        foreach (var u in destinatarios)
        {
            bool sent;
            string? motivo = null;
            switch (cfg.Canal)
            {
                case Canal.Campana:
                    await notifications!.CreateAsync(u.Id, NotificationKind.General, Trunc(asunto, 200), Trunc(mensaje, 2000),
                        "actividades", task.Id, actorName, ct);
                    if (broadcaster is not null) { try { await broadcaster.NotificationAddedAsync(u.Id, ct); } catch { /* best-effort */ } }
                    sent = true;
                    break;
                case Canal.Correo:
                    if (string.IsNullOrWhiteSpace(u.Email)) { sent = false; motivo = "sin correo"; break; }
                    sent = sender is not null && await sender.SendEmailAsync(u.Email!, asunto, ToHtml(mensaje, link), ct);
                    break;
                case Canal.WhatsApp:
                    if (string.IsNullOrWhiteSpace(u.Phone)) { sent = false; motivo = "sin telefono"; break; }
                    var wa = BindVariables(cfg.Variables, map, ctx, tokenSvc);
                    WhatsAppSendOutcome? outcome = sender is null ? null : await sender.SendWhatsAppTemplateAsync(cfg.LineaId!.Value, u.Phone!, cfg.Plantilla!.Trim(),
                        string.IsNullOrWhiteSpace(cfg.Idioma) ? null : cfg.Idioma!.Trim(), wa, Guid.Empty, cancellationToken: ct);
                    sent = outcome?.Ok == true;
                    motivo = sent ? null : outcome?.Error;
                    break;
                default:
                    sent = false;
                    break;
            }
            if (sent) { ok++; } else { fail++; }
            notas.Add(sent ? u.Nombre : $"{u.Nombre} (fallo{(motivo is null ? "" : ": " + motivo)})");
        }

        await RecordAsync(db, tenantId, task, actorName, CanalTexto(cfg.Canal), notas, ct);
        return new Result(true, ok, fail, $"{numero}: aviso por {CanalTexto(cfg.Canal)} a {ok} de {destinatarios.Count} -> {string.Join(", ", notas)}.");

        static Result Fail(string msg) => new(false, 0, 0, msg);
    }

    /// <summary>Variable "nueva" que acompana a la de la tarea: TAREA -> TAREA_NUEVA (o {{X}} -> X_NUEVA).</summary>
    public static string NuevaVar(string tareaExpr)
    {
        var t = tareaExpr.Trim();
        if (t.StartsWith("{{", StringComparison.Ordinal) && t.EndsWith("}}", StringComparison.Ordinal)) { t = t[2..^2].Trim(); }
        return t + "_NUEVA";
    }

    private sealed record Recipient(Guid Id, string Nombre, string? Email, string? Phone);

    private static async Task<List<Recipient>> RecipientsAsync(IApplicationDbContext db, TaskItem task, Config cfg, CancellationToken ct)
    {
        List<Guid> ids = cfg.Destino switch
        {
            Destino.Responsable => task.AssigneeTenantUserId is Guid a ? [a] : [],
            Destino.Usuarios => cfg.UsuarioIds?.Distinct().ToList() ?? [],
            Destino.Concepto => task.SubcategoriaId is Guid s
                ? await db.ActividadSubcategoriaNotificaciones.AsNoTracking().Where(n => n.SubcategoriaId == s).Select(n => n.TenantUserId).ToListAsync(ct)
                : [],
            _ => [],
        };
        if (ids.Count == 0) { return []; }
        var users = await db.TenantUsers.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.Phone, Name = u.PlatformUser != null ? u.PlatformUser.DisplayName : null })
            .ToListAsync(ct);
        return users.Select(u => new Recipient(u.Id, string.IsNullOrWhiteSpace(u.Name) ? u.Email : u.Name!, u.Email, u.Phone)).ToList();
    }

    /// <summary>Variables de la plantilla de WhatsApp (mismo criterio que los avisos de nodo): copia los tokens y
    /// pone cada variable (sin acentos, minuscula) con su expresion renderizada.</summary>
    public static IReadOnlyDictionary<string, string> BindVariables(IReadOnlyDictionary<string, string>? variables,
        IReadOnlyDictionary<string, string> map, ScrapeRunContext ctx, INotifyTokenResolver tokens)
    {
        var copy = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        if (variables is null) { return copy; }
        foreach (var (name, expr) in variables)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(expr)) { continue; }
            copy[StripAccents(name.Trim().ToLowerInvariant())] = Render(ctx, tokens, map, expr);
        }
        return copy;
    }

    private static async Task RecordAsync(IApplicationDbContext db, Guid tenantId, TaskItem task, string actorName,
        string canal, List<string> notas, CancellationToken ct)
    {
        db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = tenantId,
            TaskItemId = task.Id,
            Type = TaskActivityType.Action,
            ActorName = actorName,
            Text = Trunc($"Aviso por {canal}: {string.Join(", ", notas)}", 4000),
        });
        await db.SaveChangesAsync(ct);
    }

    public static string ToHtml(string mensaje, string? link)
    {
        var body = WebUtility.HtmlEncode(mensaje).Replace("\n", "<br>");
        var boton = string.IsNullOrWhiteSpace(link) ? string.Empty
            : $"<p><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"display:inline-block;padding:8px 14px;background:#0ea5e9;color:#fff;border-radius:6px;text-decoration:none\">Abrir la tarea</a></p>";
        return $"<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#0f172a\"><p>{body}</p>{boton}</div>";
    }

    public static string CanalTexto(Canal c) => c switch
    {
        Canal.Campana => "campana",
        Canal.Correo => "correo",
        Canal.WhatsApp => "WhatsApp",
        Canal.WhatsAppGrupo => "grupo de WhatsApp",
        Canal.Telegram => "Telegram",
        _ => c.ToString(),
    };

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

    private static string StripAccents(string text)
    {
        var d = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) { sb.Append(ch); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
