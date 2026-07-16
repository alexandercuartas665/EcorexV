namespace Ecorex.Application.Directorio;

/// <summary>Formulario ofrecido en el modal de tercero (con los datos de presentacion del formulario).</summary>
public sealed record TerceroFormLinkDto(
    Guid Id,
    Guid FormDefinitionId,
    string Title,
    string? Code,
    int SortOrder);

/// <summary>Formulario del tenant que AUN no esta ofrecido en el modal (candidato a agregar).</summary>
public sealed record TerceroFormCandidateDto(Guid FormDefinitionId, string Title, string? Code);

/// <summary>
/// Formularios dinamicos ofrecidos en el modal de tercero (Directorio General 000232 / Cargador de
/// contactos 000740). Solo CONFIGURACION: que formularios se pueden llenar por tercero. Las
/// respuestas se guardan en FormResponse ancladas por <see cref="ReferenceFor"/>. Tenant-scoped
/// (filtro global + estampado en alta); aqui NUNCA se filtra a mano por TenantId.
/// </summary>
public interface ITerceroFormService
{
    /// <summary>Ancla de la respuesta de un formulario para un tercero (FormResponse.Reference).</summary>
    static string ReferenceFor(Guid terceroId) => $"TERCERO:{terceroId:D}";

    /// <summary>Formularios ofrecidos en el modal, ordenados.</summary>
    Task<IReadOnlyList<TerceroFormLinkDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Formularios activos del tenant que aun no se ofrecen (para el selector de "agregar").</summary>
    Task<IReadOnlyList<TerceroFormCandidateDto>> ListCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Ofrece un formulario en el modal. Idempotente: si ya estaba, no duplica.</summary>
    Task<bool> AddAsync(Guid formDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Retira un formulario del modal (no borra la definicion ni las respuestas).</summary>
    Task<bool> RemoveAsync(Guid linkId, CancellationToken cancellationToken = default);
}
