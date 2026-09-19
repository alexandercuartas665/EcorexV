using Ecorex.Application.Common;
using Ecorex.Application.Reporting.External;
using Ecorex.Application.Tenancy.DataConnections;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Adaptador de lookup sobre un DATASET EXTERNO (SQL) del tenant (Ola 6/A2; reusa ADR-0064/0084). SourceRef es
/// el id del <see cref="Ecorex.Domain.Entities.ExternalDataSet"/> (consulta curada, tenant-scoped por
/// <see cref="ITenantDataConnectionService"/>). Modelo de COPIA (decision del usuario): el valor guardado ES el
/// texto mostrado (columna DisplayField), y el mapa de autollenado copia las demas columnas al elegir. No corre
/// SQL crudo: delega en RunDatasetAsync (guard read-only, parametros tipados, tope de filas). Registra un
/// IFormLookupSource mas; sin tocar consumidores (fachada por Kind).
/// </summary>
public sealed class ExternalDatasetLookupSource : IFormLookupSource
{
    // Tope de filas que se traen para paginar/filtrar en memoria (un picker no es un catalogo masivo).
    private const int FetchCap = 200;

    private readonly ITenantDataConnectionService _conn;
    private readonly ITenantContext _tenant;

    public ExternalDatasetLookupSource(ITenantDataConnectionService conn, ITenantContext tenant)
    {
        _conn = conn;
        _tenant = tenant;
    }

    public FormSourceKind Kind => FormSourceKind.ExternalDataset;

    public async Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(request.SourceRef, out var datasetId))
        {
            return new FormLookupPage(Array.Empty<FormLookupItem>(), 0, false);
        }

        var actor = _tenant.UserId ?? Guid.Empty;
        var res = await _conn.RunDatasetAsync(datasetId, inputs: null, maxRows: FetchCap, actorUserId: actor, ct: cancellationToken);
        if (!res.Ok || res.Grid is null)
        {
            return new FormLookupPage(Array.Empty<FormLookupItem>(), 0, false);
        }

        var items = ExternalDatasetProjection.Project(res.Grid.Columns, res.Grid.Rows, request.DisplayField, request.Fields);
        items = ExternalDatasetProjection.Filter(items, request.Query);

        var total = items.Count;
        var page = items
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .ToList();
        return new FormLookupPage(page, total, request.Skip + page.Count < total);
    }

    // Modelo de copia: el valor guardado ES el texto mostrado, asi que resolver la etiqueta es un eco (rapido,
    // sin re-consultar la BD externa en cada carga). El autollenado ya copio los campos dependientes al elegir.
    public Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        => Task.FromResult<FormLookupItem?>(
            string.IsNullOrEmpty(value) ? null : new FormLookupItem(value, value, EmptyFields));

    public async Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        var options = new List<FormLookupSourceOption>();
        var connections = await _conn.ListAsync(cancellationToken);
        foreach (var c in connections)
        {
            if (!c.IsEnabled) { continue; }
            var datasets = await _conn.ListDatasetsAsync(c.Id, cancellationToken);
            foreach (var d in datasets)
            {
                if (!d.IsEnabled) { continue; }
                options.Add(new FormLookupSourceOption(d.Id.ToString(), $"{c.Name} / {d.Name}"));
            }
        }
        return options;
    }

    public async Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sourceRef, out var datasetId)) { return Array.Empty<FormLookupFieldMeta>(); }
        var detail = await _conn.GetDatasetAsync(datasetId, cancellationToken);
        if (detail is null) { return Array.Empty<FormLookupFieldMeta>(); }

        // Columnas declaradas (fields_json). Si el dataset no las declaro, se infieren corriendo 1 fila.
        var declared = ExternalDataJson.DeserializeFields(detail.FieldsJson);
        if (declared.Count > 0)
        {
            return declared.Select(f => new FormLookupFieldMeta(f.Name, f.Name)).ToList();
        }
        try
        {
            var actor = _tenant.UserId ?? Guid.Empty;
            var res = await _conn.RunDatasetAsync(datasetId, inputs: null, maxRows: 1, actorUserId: actor, ct: cancellationToken);
            if (res.Ok && res.Grid is not null)
            {
                return res.Grid.Columns.Select(c => new FormLookupFieldMeta(c, c)).ToList();
            }
        }
        catch (Exception) { /* si el dataset no corre, no ofrece campos (nunca rompe el disenador) */ }
        return Array.Empty<FormLookupFieldMeta>();
    }

    private static readonly IReadOnlyDictionary<string, string?> EmptyFields =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
