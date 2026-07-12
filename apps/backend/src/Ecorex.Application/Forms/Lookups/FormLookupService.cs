using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Fachada de busqueda de lookups (ola F1): despacha al <see cref="IFormLookupSource"/> registrado
/// para cada <see cref="FormSourceKind"/>. La UI (renderer + designer) y la revalidacion de
/// servidor consumen SOLO esta interfaz; sumar una fuente nueva = registrar otro adaptador, sin
/// tocar a los consumidores (interfaz extensible, requisito de F1).
/// </summary>
public sealed class FormLookupService : IFormLookupService
{
    private readonly IReadOnlyDictionary<FormSourceKind, IFormLookupSource> _sources;

    public FormLookupService(IEnumerable<IFormLookupSource> sources)
    {
        var map = new Dictionary<FormSourceKind, IFormLookupSource>();
        foreach (var s in sources) { map[s.Kind] = s; }
        _sources = map;
    }

    public Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
        => Resolve(request.SourceKind).SearchAsync(request, cancellationToken);

    public Task<FormLookupItem?> ResolveAsync(FormSourceKind kind, string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        => Resolve(kind).ResolveAsync(sourceRef, value, fields, cancellationToken);

    public Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(FormSourceKind kind, CancellationToken cancellationToken = default)
        => Resolve(kind).ListSourcesAsync(cancellationToken);

    public Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(FormSourceKind kind, string? sourceRef, CancellationToken cancellationToken = default)
        => Resolve(kind).DescribeFieldsAsync(sourceRef, cancellationToken);

    private IFormLookupSource Resolve(FormSourceKind kind)
        => _sources.TryGetValue(kind, out var s)
            ? s
            : throw new InvalidOperationException($"No hay adaptador de lookup para el origen '{kind}'.");
}
