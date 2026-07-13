using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Capa de busqueda de datos para los campos lookup del constructor de formularios
/// (Formularios avanzados, ola F1; doc 01 seccion D4). Toda consulta es server-side,
/// parametrizada y paginada, y SIEMPRE tenant-scoped por el filtro global de EF (nunca se
/// trae el catalogo completo al cliente ni se cruza el tenant). Los adaptadores concretos
/// (Tercero, Item, DataContainer) normalizan cada fuente a <see cref="FormLookupItem"/>.
/// </summary>
public interface IFormLookupSource
{
    /// <summary>Origen que atiende este adaptador.</summary>
    FormSourceKind Kind { get; }

    /// <summary>Busqueda paginada por texto sobre la fuente, acotada por <paramref name="request"/>.</summary>
    Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve un valor ya elegido (el id): valida que EXISTA y pertenezca al tenant (revalidacion
    /// de servidor) y devuelve sus campos para el autollenado. Null si no existe / no es del tenant.
    /// </summary>
    Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default);

    /// <summary>Fuentes disponibles para el configurador (contenedores, o la propia entidad).</summary>
    Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default);

    /// <summary>Campos disponibles de una fuente (para los selectores Mostrar/Guardar/Autollenar del designer).</summary>
    Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fachada que despacha al adaptador correcto segun <see cref="FormSourceKind"/>. Es el unico
/// punto que consume la UI (renderer + designer) y la validacion de servidor.
/// </summary>
public interface IFormLookupService
{
    Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default);

    Task<FormLookupItem?> ResolveAsync(FormSourceKind kind, string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(FormSourceKind kind, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(FormSourceKind kind, string? sourceRef, CancellationToken cancellationToken = default);
}

/// <summary>Parametros de una busqueda de lookup. <see cref="SourceRef"/> identifica la fuente
/// (id del contenedor, o vacio para las entidades del negocio). <see cref="Fields"/> son los
/// campos de la fuente que el llamador necesita devueltos (union de DisplayField + los del
/// mapa de autollenado), para no traer mas de lo necesario.</summary>
public sealed record FormLookupRequest(
    FormSourceKind SourceKind,
    string? SourceRef,
    string? Query,
    string? FilterJson,
    string? DisplayField,
    IReadOnlyList<string> Fields,
    int Skip = 0,
    int Take = 20);

/// <summary>Un resultado normalizado: el id que se guarda (<see cref="Value"/>), la etiqueta que
/// se muestra (<see cref="Display"/>) y los campos pedidos (<see cref="Fields"/>) para autollenar.</summary>
public sealed record FormLookupItem(
    string Value,
    string Display,
    IReadOnlyDictionary<string, string?> Fields);

/// <summary>Pagina de resultados con el total (para el paginador) y si hay mas.</summary>
public sealed record FormLookupPage(
    IReadOnlyList<FormLookupItem> Items,
    int Total,
    bool HasMore);

/// <summary>Una fuente elegible en el designer (ej. un contenedor concreto, o la entidad Directorio).</summary>
public sealed record FormLookupSourceOption(string Ref, string Label);

/// <summary>Un campo de la fuente ofrecido en los selectores del designer.</summary>
public sealed record FormLookupFieldMeta(string Key, string Label, bool IsDynamic = false);
