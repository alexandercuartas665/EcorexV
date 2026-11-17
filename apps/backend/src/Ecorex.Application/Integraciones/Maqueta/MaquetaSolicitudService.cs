using System.Security.Cryptography;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Mcp;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Integraciones.Maqueta;

/// <summary>
/// Cuerpo de POST /api/maqueta/solicitudes (ADR-0176). Es el contrato que ya usa el admin de anotaciones de la
/// maqueta TRONOX (assets/maqueta/admin/admin.js, funcion enviar): NO cambiar nombres. JSON camelCase.
/// </summary>
public sealed class MaquetaSolicitudRequest
{
    public string? Origen { get; set; }
    public string? IdLocal { get; set; }
    public string? Tablero { get; set; }
    public string? Titulo { get; set; }
    public string? Descripcion { get; set; }
    public string? Categoria { get; set; }
    public string? Prioridad { get; set; }
    public string? Autor { get; set; }
    public string? Modulo { get; set; }
    public string? Pantalla { get; set; }
    public string? Ruta { get; set; }
    public string? ArchivoMaqueta { get; set; }
    public string? OrigenApp { get; set; }
    public string? Handoff { get; set; }
    public MaquetaElemento? Elemento { get; set; }
    public string? Tema { get; set; }
    public string? Viewport { get; set; }
    public string? CreadoEn { get; set; }
    public MaquetaCaptura? Captura { get; set; }
}

/// <summary>Elemento de la maqueta sobre el que se hizo la anotacion.</summary>
public sealed class MaquetaElemento
{
    public string? Tag { get; set; }
    public string? Texto { get; set; }
    public string? Clases { get; set; }
    public string? Selector { get; set; }
    public MaquetaRect? Rect { get; set; }
}

/// <summary>Caja del elemento en la pantalla (px).</summary>
public sealed class MaquetaRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>Captura PNG de la anotacion en base64 (sin prefijo data:). Puede venir null.</summary>
public sealed class MaquetaCaptura
{
    public string? Nombre { get; set; }
    public string? Mime { get; set; }
    public string? Base64 { get; set; }
}

/// <summary>Resultado: la tarjeta creada o actualizada.</summary>
/// <param name="Created">true = alta nueva; false = reenvio del mismo idLocal que actualizo la tarjeta existente.</param>
public sealed record MaquetaSolicitudResult(Guid TaskId, string Number, Guid? BoardId, bool Created, bool CaptureAttached);

/// <summary>
/// Recibe las solicitudes de ajuste del admin de anotaciones de la maqueta y las vuelve TARJETAS de un tablero
/// de actividades del tenant del token (ADR-0176). Idempotente por (origen, idLocal) via <see cref="ExternalTaskLink"/>.
/// </summary>
public interface IMaquetaSolicitudService
{
    Task<TaskCoreResult<MaquetaSolicitudResult>> RecibirAsync(MaquetaSolicitudRequest request, McpActor actor, CancellationToken cancellationToken = default);
}

public sealed class MaquetaSolicitudService : IMaquetaSolicitudService
{
    /// <summary>Tablero por defecto cuando la solicitud no trae "tablero".</summary>
    public const string DefaultBoardName = "Maqueta TRONOX - Ajustes";

    /// <summary>Modulo del gestor de archivos donde quedan las capturas.</summary>
    public const string FileModule = "maqueta";

    /// <summary>Tope de la captura ya decodificada (el contrato habla de 0.1-3 MB).</summary>
    public const int MaxCaptureBytes = 8 * 1024 * 1024;

    private const string AuditReason = "integ-maqueta";

    private static readonly JsonSerializerOptions MetaJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IActivityBoardService _boards;
    private readonly ITaskItemService _tasks;
    private readonly IFileStorageService _files;
    private readonly IAuditWriter _audit;

    public MaquetaSolicitudService(IApplicationDbContext db, ITenantContext tenant, IActivityBoardService boards,
        ITaskItemService tasks, IFileStorageService files, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _boards = boards;
        _tasks = tasks;
        _files = files;
        _audit = audit;
    }

    public async Task<TaskCoreResult<MaquetaSolicitudResult>> RecibirAsync(MaquetaSolicitudRequest request, McpActor actor, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId || tenantId != actor.TenantId)
        {
            return TaskCoreResult<MaquetaSolicitudResult>.Invalid("No hay tenant activo para la solicitud.");
        }

        var key = Clean(request.IdLocal, 150);
        if (key is null) { return TaskCoreResult<MaquetaSolicitudResult>.Invalid("'idLocal' es obligatorio."); }
        var title = Clean(request.Titulo, 300);
        if (title is null) { return TaskCoreResult<MaquetaSolicitudResult>.Invalid("'titulo' es obligatorio."); }
        var source = Clean(request.Origen, 60) ?? "maqueta";
        var boardName = Clean(request.Tablero, 150) ?? DefaultBoardName;
        var description = string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim();
        var priority = ParsePriority(request.Prioridad);
        var author = Clean(request.Autor, 150);
        var actorName = $"{actor.DisplayName} (via maqueta)";

        // Captura: se valida ANTES de tocar la BD (un base64 roto no debe dejar una tarjeta a medias).
        byte[]? capture = null;
        string? captureSha = null;
        if (request.Captura is { Base64: { Length: > 0 } b64 })
        {
            var comma = b64.IndexOf(',');
            if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) { b64 = b64[(comma + 1)..]; }
            try { capture = Convert.FromBase64String(b64.Trim()); }
            catch (FormatException) { return TaskCoreResult<MaquetaSolicitudResult>.Invalid("'captura.base64' no es base64 valido."); }
            if (capture.Length > MaxCaptureBytes)
            {
                return TaskCoreResult<MaquetaSolicitudResult>.Invalid($"La captura supera {MaxCaptureBytes / (1024 * 1024)} MB.");
            }
            if (capture.Length == 0) { capture = null; }
            else { captureSha = Convert.ToHexString(SHA256.HashData(capture)).ToLowerInvariant(); }
        }

        var tagIds = await EnsureTagsAsync(new[] { Clean(request.Modulo, 60), Clean(request.Categoria, 60) }, cancellationToken);
        var metadata = JsonSerializer.Serialize(new
        {
            request.Pantalla,
            request.Ruta,
            request.ArchivoMaqueta,
            request.OrigenApp,
            request.Handoff,
            request.Modulo,
            request.Categoria,
            request.Prioridad,
            request.Autor,
            request.Elemento,
            request.Tema,
            request.Viewport,
            request.CreadoEn
        }, MetaJson);

        var link = await _db.ExternalTaskLinks
            .FirstOrDefaultAsync(l => l.Source == source && l.ExternalKey == key, cancellationToken);

        Guid taskId;
        string number;
        Guid? boardId;
        bool created;
        if (link is null)
        {
            // ---- Alta: tablero (usar o crear) -> primera columna -> clasificacion -> tarea ----
            var board = await ResolveOrCreateBoardAsync(boardName, actor, actorName, cancellationToken);
            if (!board.IsOk) { return TaskCoreResult<MaquetaSolicitudResult>.Invalid(board.Error ?? "No se pudo preparar el tablero."); }
            var (bId, colId) = board.Value;

            var conceptId = await _db.ActividadSubcategorias.AsNoTracking()
                .Where(s => !s.IsArchived && s.TaskBoardId == bId)
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Nombre)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
            Guid? typeId = null;
            if (conceptId is null)
            {
                typeId = await EnsureActivityTypeAsync(tenantId, cancellationToken);
            }

            var res = await _boards.QuickCreateTaskAsync(new QuickCreateTaskRequest(
                BoardId: bId, ColumnId: colId, Title: title, Description: description, Priority: priority,
                TagIds: tagIds, ActivityTypeId: typeId, SubcategoriaId: conceptId), actor.PlatformUserId, actorName, cancellationToken);
            if (!res.IsOk || res.Value is null)
            {
                return TaskCoreResult<MaquetaSolicitudResult>.Invalid(res.Error ?? "No se pudo crear la tarjeta.");
            }
            taskId = res.Value.Item.Id;
            number = res.Value.Item.Number;
            boardId = bId;
            created = true;

            if (author is not null)
            {
                var entity = await _db.TaskItems.FirstAsync(t => t.Id == taskId, cancellationToken);
                entity.RequesterName = author;
            }
            link = new ExternalTaskLink
            {
                TenantId = tenantId,
                Source = source,
                ExternalKey = key,
                TaskItemId = taskId,
                MetadataJson = metadata
            };
            _db.ExternalTaskLinks.Add(link);
            _audit.Write(actor.PlatformUserId, "maqueta.solicitud.create", nameof(TaskItem), taskId,
                previousValue: null, newValue: new { number, source, key, board = boardName }, tenantId: tenantId, reason: AuditReason);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Carrera: otro envio del MISMO idLocal gano el indice unico. La tarjeta recien creada sobra.
                return TaskCoreResult<MaquetaSolicitudResult>.Conflict("La solicitud ya se esta procesando; reintenta en unos segundos.");
            }
        }
        else
        {
            // ---- Reenvio: actualizar la MISMA tarjeta (titulo, descripcion, prioridad, autor, etiquetas) ----
            taskId = link.TaskItemId;
            var detail = await _tasks.GetDetailAsync(taskId, cancellationToken);
            if (detail is null)
            {
                return TaskCoreResult<MaquetaSolicitudResult>.NotFound("La tarjeta de esta anotacion ya no existe.");
            }
            var item = detail.Item;
            number = item.Number;
            boardId = item.BoardId;
            created = false;

            var changed = item.Title != title
                || (detail.Description ?? "") != (description ?? "")
                || item.Priority != priority
                || (author is not null && detail.RequesterName != author);
            if (changed && item.Status != TaskItemStatus.Closed)
            {
                var upd = await _tasks.UpdateAsync(taskId, new UpdateTaskItemRequest(
                    Title: title, Description: description, ActivityTypeId: item.ActivityTypeId, Priority: priority,
                    DueDate: item.DueDate, RequesterName: author ?? detail.RequesterName, RequesterEmail: detail.RequesterEmail,
                    RequesterPhone: detail.RequesterPhone, CcEmails: detail.CcEmails, ProjectId: item.ProjectId, Color: item.Color,
                    Version: item.Version, RequesterDocument: detail.RequesterDocument, StartDate: item.StartDate,
                    MilestoneId: item.MilestoneId), actor.PlatformUserId, actorName, cancellationToken);
                if (!upd.IsOk)
                {
                    return new TaskCoreResult<MaquetaSolicitudResult>(upd.Status, null, upd.Error ?? "No se pudo actualizar la tarjeta.");
                }
            }
            foreach (var tagId in tagIds)
            {
                // Etiqueta no permitida en la columna actual: se ignora (la tarjeta ya avanzo en el tablero).
                await _tasks.AttachTagAsync(taskId, tagId, cancellationToken);
            }

            link.MetadataJson = metadata;
            link.ReceivedCount++;
            _audit.Write(actor.PlatformUserId, "maqueta.solicitud.update", nameof(TaskItem), taskId,
                previousValue: null, newValue: new { number, source, key, link.ReceivedCount }, tenantId: tenantId, reason: AuditReason);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Captura: se adjunta solo si es nueva o cambio (un reenvio con la misma imagen no la duplica).
        var attached = false;
        if (capture is not null && captureSha != link.LastCaptureSha256)
        {
            var mime = Clean(request.Captura!.Mime, 120) ?? "image/png";
            if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) { mime = "image/png"; }
            var fileName = SafeFileName(request.Captura.Nombre) ?? $"{SafeFileName(key) ?? "captura"}.png";
            var stored = await _files.SaveAsync(new FileStorageSaveRequest(FileModule, source, key, fileName, mime, capture), cancellationToken);
            var att = await _tasks.AddAttachmentAsync(new AddTaskAttachmentRequest(taskId, fileName, stored.Url, mime, capture.Length),
                actor.PlatformUserId, actorName, cancellationToken);
            if (att.IsOk)
            {
                link.LastCaptureSha256 = captureSha;
                await _db.SaveChangesAsync(cancellationToken);
                attached = true;
            }
        }

        return TaskCoreResult<MaquetaSolicitudResult>.Ok(new MaquetaSolicitudResult(taskId, number, boardId, created, attached));
    }

    /// <summary>Tablero de actividades por NOMBRE (sin distinguir mayusculas); si no existe se crea con las columnas
    /// por defecto. Devuelve el tablero y su PRIMERA columna.</summary>
    private async Task<TaskCoreResult<(Guid BoardId, Guid ColumnId)>> ResolveOrCreateBoardAsync(
        string boardName, McpActor actor, string actorName, CancellationToken ct)
    {
        var low = boardName.ToLower();
        var boardId = await _db.TaskBoards.AsNoTracking()
            .Where(b => b.Kind == TaskBoardKind.Activities && !b.IsArchived && b.Name.ToLower() == low)
            .OrderBy(b => b.SortOrder)
            .Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
        if (boardId is null)
        {
            var created = await _boards.CreateBoardAsync(new CreateActivityBoardRequest(boardName,
                Description: "Solicitudes de ajuste enviadas desde el admin de anotaciones de la maqueta."), actor.PlatformUserId, actorName, ct);
            if (!created.IsOk || created.Value is null)
            {
                return TaskCoreResult<(Guid, Guid)>.Invalid(created.Error ?? "No se pudo crear el tablero.");
            }
            boardId = created.Value.Id;
        }
        var columnId = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId).OrderBy(c => c.SortOrder)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (columnId is null) { return TaskCoreResult<(Guid, Guid)>.Invalid($"El tablero '{boardName}' no tiene columnas."); }
        return TaskCoreResult<(Guid, Guid)>.Ok((boardId.Value, columnId.Value));
    }

    /// <summary>Clasificacion de respaldo cuando el tablero no tiene concepto: primer tipo de actividad activo; si el
    /// tenant no tiene ninguno (tenant nuevo), se crea "Maqueta / Solicitud de ajuste".</summary>
    private async Task<Guid> EnsureActivityTypeAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await _db.ActivityTypes.AsNoTracking()
            .Where(t => !t.IsArchived)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Category).ThenBy(t => t.Name)
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (existing is Guid id) { return id; }
        var type = new ActivityType { TenantId = tenantId, Category = "Maqueta", Name = "Solicitud de ajuste" };
        _db.ActivityTypes.Add(type);
        await _db.SaveChangesAsync(ct);
        return type.Id;
    }

    /// <summary>Etiquetas del catalogo del tenant por nombre exacto; las que faltan se crean.</summary>
    private async Task<IReadOnlyList<Guid>> EnsureTagsAsync(IEnumerable<string?> names, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var name in names.Where(n => n is not null).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var low = name.ToLower();
            var id = await _db.TaskItemTags.AsNoTracking()
                .Where(t => t.Name.ToLower() == low).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
            if (id is null)
            {
                var created = await _tasks.CreateTagAsync(name, null, ct);
                id = created.Value?.Id;
            }
            if (id is Guid g && !ids.Contains(g)) { ids.Add(g); }
        }
        return ids;
    }

    private static TaskPriority ParsePriority(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "alta" or "high" or "urgente" or "critica" => TaskPriority.High,
        "baja" or "low" => TaskPriority.Low,
        _ => TaskPriority.Medium
    };

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }

    /// <summary>Nombre de archivo seguro (sin rutas ni caracteres raros), maximo 120.</summary>
    private static string? SafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var name = Path.GetFileName(value.Trim());
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray();
        var safe = new string(chars).Trim('-', '.');
        if (safe.Length == 0) { return null; }
        return safe.Length > 120 ? safe[..120] : safe;
    }
}
