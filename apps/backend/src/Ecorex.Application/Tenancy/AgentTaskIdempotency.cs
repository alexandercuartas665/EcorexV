using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Idempotencia del CIERRE de los agentes (ADR-0101 rev.3): evita tareas DUPLICADAS cuando el modelo llama a
/// crear_tarea / crear_actividad varias veces (mismo turno por el bucle de tool-calling, o en turnos
/// siguientes al "ya quedo?" o al continuar el chat). Dos capas:
///  - Capa 1 (intra-turno): la primera creacion OK de una herramienta se recuerda en AiToolRunContext; una
///    segunda llamada de ESA herramienta en el mismo turno devuelve el mismo ticket sin volver a insertar.
///  - Capa 2 (por CONVERSACION + TABLERO, entre turnos, SIN ventana de tiempo): antes de crear, si ya existe
///    una tarea NO archivada para ESA conversacion EN EL MISMO TABLERO, se devuelve ese ticket.
///
/// REGLA DE ORO: NO se deduplica por contenido ni por telefono (probaron NO ser fiables: el modelo alucina
/// el telefono y regenera el resumen). La llave estable es CONVERSACION + TABLERO: una conversacion = un lead
/// abierto POR TABLERO, asi que un re-cierre/confirmacion/continuacion del MISMO lead devuelve el mismo ticket
/// sin importar cuanto tiempo pase (rev.3: se QUITO la ventana de 5 min de rev.2, que dejaba pasar duplicados
/// a los minutos u horas). Se conserva el caso multi-tema: si el agente enruta a OTRO tablero, el board_id
/// difiere y SI se crea una tarea nueva. Si el lead ya se archivo, un nuevo cierre crea tarea nueva. Solo
/// aplica al camino de los agentes (lo llaman los toolsets con AiToolRunContext).
/// </summary>
public static class AgentTaskIdempotency
{
    // ---- Capa 1: guardia intra-turno ----

    /// <summary>Resultado de cierre ya emitido por esta herramienta en el turno actual, o null.</summary>
    public static string? TryGetTurnResult(string toolKey) => AiToolRunContext.TryGetTurnResult(toolKey);

    /// <summary>Recuerda el resultado de cierre para que una segunda llamada de la misma herramienta en el turno lo reuse.</summary>
    public static void RememberTurnResult(string toolKey, string resultJson) => AiToolRunContext.SetTurnResult(toolKey, resultJson);

    // ---- Capa 2: dedup por CONVERSACION + TABLERO entre turnos (sin ventana) ----

    /// <summary>
    /// Busca la tarea mas reciente NO archivada de <paramref name="conversationId"/> en el tablero
    /// <paramref name="boardId"/> (null-safe: null empareja tareas sin tablero). Sin limite de tiempo. Devuelve
    /// (Id, Number) o null. El filtro global del DbContext ya acota por tenant, y una conversacion pertenece a
    /// un solo tenant, asi que (conversacion + tablero) es llave suficiente.
    /// </summary>
    public static async Task<(Guid Id, string Number)?> FindByConversationAndBoardAsync(
        IApplicationDbContext db, Guid conversationId, Guid? boardId,
        CancellationToken cancellationToken = default)
    {
        var hit = await db.TaskItems.AsNoTracking()
            .Where(t => t.ConversationId == conversationId && t.BoardId == boardId && !t.IsArchived)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Number })
            .FirstOrDefaultAsync(cancellationToken);
        return hit is null ? null : (hit.Id, hit.Number);
    }
}
