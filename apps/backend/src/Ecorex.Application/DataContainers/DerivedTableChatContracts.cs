using Ecorex.Application.Tenancy;

namespace Ecorex.Application.DataContainers;

// ==== Asistente IA de tablas derivadas (ADR-0130, fase 2 de ADR-0127) ====

/// <summary>Resultado de abrir una conversacion del asistente sobre un contenedor.</summary>
public sealed record DerivedChatStartResult(bool Ok, string? Error, Guid ConversationId, string? Greeting)
{
    public static DerivedChatStartResult Fail(string error) => new(false, error, Guid.Empty, null);
}

/// <summary>
/// Resultado de un turno (enviar / confirmar / rechazar). <see cref="Preview"/> trae la vista previa si en el
/// turno corrio <c>proponer_derivacion</c> (la UI la pinta como tabla); <see cref="Created"/> trae la tabla
/// recien creada si se confirmo <c>crear_tabla_derivada</c> (la UI dispara el primer refresco y recarga el lienzo).
/// </summary>
public sealed record DerivedChatTurnResult(
    bool Ok,
    string? Error,
    Guid ConversationId,
    string? AssistantText,
    IReadOnlyList<ProposalDto> Proposals,
    bool AwaitingConfirmation,
    DerivationPreviewDto? Preview,
    DerivedTableCreatedDto? Created,
    string? CreatedName,
    int TurnInputTokens = 0,
    int TurnOutputTokens = 0,
    int TurnCachedInputTokens = 0,
    decimal TurnCostUsd = 0m)
{
    public static DerivedChatTurnResult Fail(Guid conversationId, string error)
        => new(false, error, conversationId, null, Array.Empty<ProposalDto>(), false, null, null, null);
}

/// <summary>
/// Asistente conversacional para CREAR una tabla derivada describiendola en lenguaje natural ("items por
/// categoria y marca con total de precio"). Ve las tablas del contenedor (columnas + tipos) y puede pedir una
/// muestra; propone la derivacion con VISTA PREVIA (sin crear nada) y, cuando el usuario esta de acuerdo, pide
/// crearla: esa accion SIEMPRE llega como propuesta con Confirmar/Rechazar. Al confirmar, la tabla nace por el
/// MISMO <see cref="IDerivedTableService"/> que el wizard (misma transaccion y validaciones). La conversacion
/// vive en memoria del circuito (scoped): se pierde al cerrar la pagina, igual que el wizard.
/// </summary>
public interface IDerivedTableChatService
{
    /// <summary>Abre una conversacion sobre el contenedor; <paramref name="sourceTableId"/> es la tabla desde la
    /// que el usuario abrio el wizard (el asistente la asume como origen salvo que digan otra cosa).</summary>
    Task<DerivedChatStartResult> StartAsync(Guid modelId, Guid? sourceTableId, Guid actorUserId, CancellationToken ct = default);

    Task<DerivedChatTurnResult> SendAsync(Guid conversationId, string userText, Guid actorUserId, CancellationToken ct = default);

    Task<DerivedChatTurnResult> ConfirmAsync(Guid conversationId, Guid actorUserId, CancellationToken ct = default);

    Task<DerivedChatTurnResult> RejectAsync(Guid conversationId, string? reason, Guid actorUserId, CancellationToken ct = default);
}
