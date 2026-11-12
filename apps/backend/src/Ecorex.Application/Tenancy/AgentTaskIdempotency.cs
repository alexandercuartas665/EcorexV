using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Idempotencia del CIERRE de los agentes (ADR-0101, rev.4 = ADR-0163): evita tareas DUPLICADAS cuando el
/// modelo llama a crear_tarea / crear_actividad varias veces (mismo turno por el bucle de tool-calling, o en
/// turnos siguientes al "ya quedo?"). Dos capas:
///  - Capa 1 (intra-turno): la primera creacion OK de una herramienta se recuerda en AiToolRunContext; una
///    segunda llamada de ESA herramienta en el mismo turno devuelve el mismo ticket sin volver a insertar.
///  - Capa 2 (entre turnos, ADR-0163): misma conversacion + mismo tablero + MISMO TITULO dentro de 30 min
///    devuelve la tarea existente (<see cref="FindRecentSameTitleAsync"/>). Titulo distinto SIEMPRE crea.
///
/// Por que (ADR-0163): la Capa 2 de rev.3 (conversacion + tablero SIN ventana) se trago casos DISTINTOS: un
/// RECLAMO recibio el ticket de una COTIZACION abierta. El servidor no puede distinguir por si solo "tema
/// distinto" de "repeticion" (resumen y telefono no son estables), asi que decidir caso nuevo vs viejo lo hace
/// el agente con casos_del_cliente / anotar_caso; aqui solo queda una red de seguridad estrecha. Solo aplica al
/// camino de los agentes (lo llaman los toolsets con AiToolRunContext).
/// </summary>
public static class AgentTaskIdempotency
{
    // ---- Capa 1: guardia intra-turno ----

    /// <summary>Resultado de cierre ya emitido por esta herramienta en el turno actual, o null.</summary>
    public static string? TryGetTurnResult(string toolKey) => AiToolRunContext.TryGetTurnResult(toolKey);

    /// <summary>Recuerda el resultado de cierre para que una segunda llamada de la misma herramienta en el turno lo reuse.</summary>
    public static void RememberTurnResult(string toolKey, string resultJson) => AiToolRunContext.SetTurnResult(toolKey, resultJson);

    // ---- Capa 2 (ADR-0163, crear_tarea y crear_actividad): mismo TITULO en ventana corta ----

    /// <summary>Ventana de la red de seguridad de crear_tarea: un pedido con el MISMO titulo en la misma
    /// conversacion y tablero dentro de esta ventana se trata como repeticion (no como caso nuevo).</summary>
    public static readonly TimeSpan SameTitleWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// ADR-0163: busca una tarea NO archivada de la conversacion, en el mismo tablero, con el MISMO titulo
    /// (sin distinguir mayusculas/espacios) creada dentro de <see cref="SameTitleWindow"/>. Temas distintos
    /// (titulo distinto) NUNCA se fusionan: decidir si es caso nuevo o viejo lo hace el agente con
    /// casos_del_cliente/anotar_caso. Devuelve (Id, Number) o null.
    /// </summary>
    public static async Task<(Guid Id, string Number)?> FindRecentSameTitleAsync(
        IApplicationDbContext db, Guid conversationId, Guid? boardId, string title, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeTitle(title);
        if (key.Length == 0) { return null; }
        var since = now - SameTitleWindow;
        var candidates = await db.TaskItems.AsNoTracking()
            .Where(t => t.ConversationId == conversationId && t.BoardId == boardId && !t.IsArchived
                && t.CreatedAt >= since)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Number, t.Title })
            .Take(20)
            .ToListAsync(cancellationToken);
        var hit = candidates.FirstOrDefault(c => NormalizeTitle(c.Title) == key);
        return hit is null ? null : (hit.Id, hit.Number);
    }

    /// <summary>Titulo comparable: minusculas, sin espacios repetidos ni bordes.</summary>
    internal static string NormalizeTitle(string? title)
        => string.Join(' ', (title ?? string.Empty).Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---- Candado de cierre-por-flujo: SARA no crea tarea/lead si un flujo ya cerro este caso ----

    /// <summary>Ventana durante la cual una marca de cierre-por-flujo (Conversation.FlowHandledCloseAt) bloquea la
    /// creacion por SARA. El flujo la limpia al TOMAR la linea de un caso nuevo; la ventana es una red de seguridad
    /// para que una marca vieja no bloquee para siempre un cierre legitimo futuro en la misma conversacion.</summary>
    public static readonly TimeSpan FlowHandledCloseWindow = TimeSpan.FromHours(24);

    /// <summary>True si la conversacion tiene una marca RECIENTE (dentro de la ventana) de cierre gestionado por un
    /// flujo: en ese caso SARA NO debe crear tarea ni lead (el flujo ya es dueno del caso y evitamos duplicados).</summary>
    public static async Task<bool> IsFlowHandledCloseAsync(
        IApplicationDbContext db, Guid conversationId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var at = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId).Select(c => c.FlowHandledCloseAt)
            .FirstOrDefaultAsync(cancellationToken);
        return at is DateTimeOffset t && now >= t && (now - t) <= FlowHandledCloseWindow;
    }
}
