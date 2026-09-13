using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Idempotencia del CIERRE de los agentes (ADR-0101): evita tareas DUPLICADAS cuando el modelo llama a
/// crear_tarea / crear_actividad varias veces (mismo turno por el bucle de tool-calling, o en turnos
/// siguientes al "ya quedo?"). Dos capas:
///  - Capa 1 (intra-turno): la primera creacion OK de una herramienta se recuerda en AiToolRunContext; una
///    segunda llamada de ESA herramienta en el mismo turno devuelve el mismo ticket sin volver a insertar.
///  - Capa 2 (por CONTENIDO entre turnos): antes de crear, se busca una tarea reciente del MISMO contacto,
///    mismo tablero/concepto y mismo titulo+descripcion NORMALIZADOS dentro de una ventana; si existe, se
///    devuelve ese ticket.
///
/// REGLA DE ORO: NO se deduplica por conversacion. Una solicitud NUEVA (titulo/descripcion distintos) en el
/// MISMO chat crea una tarea nueva. Solo aplica al camino de los agentes (lo llaman los toolsets).
/// </summary>
public static class AgentTaskIdempotency
{
    /// <summary>Ventana reciente para considerar dos altas iguales como la misma solicitud (minutos).</summary>
    public const int WindowMinutes = 45;

    /// <summary>Cuantos candidatos recientes se traen para comparar contenido en memoria (normalizacion).</summary>
    private const int CandidateLimit = 20;

    // ---- Capa 1: guardia intra-turno ----

    /// <summary>Resultado de cierre ya emitido por esta herramienta en el turno actual, o null.</summary>
    public static string? TryGetTurnResult(string toolKey) => AiToolRunContext.TryGetTurnResult(toolKey);

    /// <summary>Recuerda el resultado de cierre para que una segunda llamada de la misma herramienta en el turno lo reuse.</summary>
    public static void RememberTurnResult(string toolKey, string resultJson) => AiToolRunContext.SetTurnResult(toolKey, resultJson);

    // ---- Capa 2: dedup por contenido entre turnos ----

    /// <summary>
    /// Busca una tarea reciente que sea claramente la MISMA solicitud: mismo contacto (telefono, o nombre si
    /// no hay telefono) + mismo tablero (o concepto) + mismo titulo+descripcion normalizados + no archivada +
    /// dentro de la ventana. Devuelve (Id, Number) o null. Si no hay contacto, NO deduplica (evita falsos
    /// positivos entre clientes distintos).
    /// </summary>
    public static async Task<(Guid Id, string Number)?> FindRecentDuplicateAsync(
        IApplicationDbContext db, TimeProvider clock,
        string title, string? description,
        string? requesterPhone, string? requesterName,
        Guid? boardId, Guid? subcategoriaId,
        CancellationToken cancellationToken = default)
    {
        var phone = Clean(requesterPhone);
        var name = Clean(requesterName);
        if (phone is null && name is null) { return null; }

        var cutoff = clock.GetUtcNow().AddMinutes(-WindowMinutes);
        var normTitle = Normalize(title);
        var normDesc = Normalize(description);

        // El filtro global del DbContext ya acota por tenant.
        var q = db.TaskItems.AsNoTracking().Where(t => !t.IsArchived && t.CreatedAt >= cutoff);
        if (boardId is Guid b) { q = q.Where(t => t.BoardId == b); }
        if (subcategoriaId is Guid s) { q = q.Where(t => t.SubcategoriaId == s); }
        if (phone is not null) { q = q.Where(t => t.RequesterPhone == phone); }
        else { q = q.Where(t => t.RequesterName == name); }

        var candidates = await q
            .OrderByDescending(t => t.CreatedAt)
            .Take(CandidateLimit)
            .Select(t => new { t.Id, t.Number, t.Title, t.Description })
            .ToListAsync(cancellationToken);

        foreach (var c in candidates)
        {
            if (Normalize(c.Title) == normTitle && Normalize(c.Description) == normDesc)
            {
                return (c.Id, c.Number);
            }
        }
        return null;
    }

    /// <summary>Normaliza para comparar: trim + colapsar espacios + minusculas (cultura invariante).</summary>
    public static string Normalize(string? s)
        => string.IsNullOrWhiteSpace(s) ? string.Empty : Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
