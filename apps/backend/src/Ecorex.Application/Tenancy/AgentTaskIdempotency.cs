using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Idempotencia del CIERRE de los agentes (ADR-0101 rev.2): evita tareas DUPLICADAS cuando el modelo llama a
/// crear_tarea / crear_actividad varias veces (mismo turno por el bucle de tool-calling, o en turnos
/// siguientes al "ya quedo?"). Dos capas:
///  - Capa 1 (intra-turno): la primera creacion OK de una herramienta se recuerda en AiToolRunContext; una
///    segunda llamada de ESA herramienta en el mismo turno devuelve el mismo ticket sin volver a insertar.
///  - Capa 2 (por CONVERSACION, entre turnos): antes de crear, si hay una conversacion en curso y ya existe
///    una tarea NO archivada creada para ESA conversacion dentro de una ventana corta, se devuelve ese ticket.
///
/// REGLA DE ORO: NO se deduplica por contenido ni por telefono (probaron NO ser fiables: el modelo alucina
/// el telefono y regenera el resumen). La llave estable es la CONVERSACION + una ventana CORTA, que solo
/// colapsa re-cierres/confirmaciones inmediatas; una solicitud NUEVA en el mismo chat, pasada la ventana,
/// crea una tarea nueva. Solo aplica al camino de los agentes (lo llaman los toolsets con AiToolRunContext).
/// </summary>
public static class AgentTaskIdempotency
{
    /// <summary>Ventana CORTA (minutos) para colapsar re-cierres de la MISMA conversacion como la misma
    /// solicitud. Corta a proposito: pasado este tiempo, un nuevo cierre de la conversacion crea tarea nueva.</summary>
    public const int ConversationWindowMinutes = 5;

    // ---- Capa 1: guardia intra-turno ----

    /// <summary>Resultado de cierre ya emitido por esta herramienta en el turno actual, o null.</summary>
    public static string? TryGetTurnResult(string toolKey) => AiToolRunContext.TryGetTurnResult(toolKey);

    /// <summary>Recuerda el resultado de cierre para que una segunda llamada de la misma herramienta en el turno lo reuse.</summary>
    public static void RememberTurnResult(string toolKey, string resultJson) => AiToolRunContext.SetTurnResult(toolKey, resultJson);

    // ---- Capa 2: dedup por CONVERSACION entre turnos ----

    /// <summary>
    /// Busca la tarea mas reciente (no archivada) creada para <paramref name="conversationId"/> dentro de la
    /// ventana corta. Devuelve (Id, Number) o null. El filtro global del DbContext ya acota por tenant, y una
    /// conversacion pertenece a un solo tenant, asi que la conversacion es llave suficiente.
    /// </summary>
    public static async Task<(Guid Id, string Number)?> FindRecentByConversationAsync(
        IApplicationDbContext db, TimeProvider clock, Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow().AddMinutes(-ConversationWindowMinutes);
        var hit = await db.TaskItems.AsNoTracking()
            .Where(t => t.ConversationId == conversationId && !t.IsArchived && t.CreatedAt >= cutoff)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Number })
            .FirstOrDefaultAsync(cancellationToken);
        return hit is null ? null : (hit.Id, hit.Number);
    }
}
