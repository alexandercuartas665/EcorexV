using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.External;

/// <summary>
/// Lee una fuente reportable de tipo EXTERNO (ADR-0064), analogo al ContainerReportReader pero contra
/// una base de datos AJENA. La clave de la fuente es "external:{externalDataSetId}".
///
/// Gobernanza (fail-closed): antes de tocar nada verifica que (1) el dataset y su fuente esten
/// habilitados y (2) EXISTA una concesion vigente de esa fuente para el TENANT ACTIVO. Un tenant sin
/// concesion NO ve el descriptor (catalogo) y NO puede ejecutar (QueryAsync lanza). Luego descifra la
/// cadena SOLO en memoria, enlaza los parametros (contexto de confianza + entrada tipada) y delega en el
/// executor de solo lectura parametrizado. La cadena nunca se persiste ni se loggea.
/// </summary>
public sealed class ExternalReportReader
{
    public const string KeyPrefix = "external:";

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IExternalQueryExecutor _executor;

    public ExternalReportReader(IApplicationDbContext db, ISecretProtector protector, IExternalQueryExecutor executor)
    {
        _db = db;
        _protector = protector;
        _executor = executor;
    }

    public static bool Handles(string sourceKey) => sourceKey.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase);

    public static string KeyFor(Guid dataSetId) => KeyPrefix + dataSetId;

    public static Guid? ParseId(string sourceKey) =>
        Guid.TryParse(sourceKey.AsSpan(KeyPrefix.Length), out var id) ? id : null;

    /// <summary>Mapea el tipo logico de un campo externo al tipo reportable neutro.</summary>
    public static ReportFieldType MapType(ExternalDataParameterType type) => type switch
    {
        ExternalDataParameterType.Int => ReportFieldType.Number,
        ExternalDataParameterType.Decimal => ReportFieldType.Decimal,
        ExternalDataParameterType.Date => ReportFieldType.Date,
        ExternalDataParameterType.Boolean => ReportFieldType.Boolean,
        _ => ReportFieldType.Text
    };

    /// <summary>
    /// True si el tenant tiene acceso concedido y vigente a ese dataset (fuente + dataset habilitados y
    /// concesion activa). Es la unica puerta de acceso; el resto del pipeline confia en esta.
    /// </summary>
    public async Task<bool> IsGrantedAsync(Guid dataSetId, Guid tenantId, CancellationToken ct = default)
    {
        return await _db.ExternalDataSets.AsNoTracking()
            .Where(ds => ds.Id == dataSetId && ds.IsEnabled)
            .AnyAsync(ds =>
                _db.ExternalDataSources.Any(src => src.Id == ds.ExternalDataSourceId && src.IsEnabled)
                && _db.ExternalDataSourceGrants.Any(g =>
                    g.ExternalDataSourceId == ds.ExternalDataSourceId && g.TenantId == tenantId && g.IsEnabled),
                ct);
    }

    /// <summary>Descriptor de catalogo del dataset (a partir de FieldsJson). Null si el tenant no tiene
    /// concesion vigente: asi el catalogo NO expone lo no concedido.</summary>
    public async Task<ReportSourceDescriptor?> DescribeAsync(Guid dataSetId, Guid tenantId, CancellationToken ct = default)
    {
        if (!await IsGrantedAsync(dataSetId, tenantId, ct))
        {
            return null;
        }

        var ds = await _db.ExternalDataSets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dataSetId, ct);
        if (ds is null)
        {
            return null;
        }

        var fields = ExternalDataJson.DeserializeFields(ds.FieldsJson)
            .Select(f => new ReportField(f.Name, f.Name, MapType(f.Type), CanFilter: false, CanGroup: false, CanAggregate: false))
            .ToList();

        return new ReportSourceDescriptor(KeyFor(dataSetId), ds.Name, ReportSourceKind.External, fields);
    }

    /// <summary>Datasets externos CONCEDIDOS al tenant activo, como descriptores de catalogo.</summary>
    public async Task<IReadOnlyList<ReportSourceDescriptor>> ListGrantedAsync(Guid tenantId, CancellationToken ct = default)
    {
        var grantedSourceIds = await _db.ExternalDataSourceGrants.AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.IsEnabled)
            .Select(g => g.ExternalDataSourceId)
            .Distinct()
            .ToListAsync(ct);

        if (grantedSourceIds.Count == 0)
        {
            return Array.Empty<ReportSourceDescriptor>();
        }

        var datasets = await _db.ExternalDataSets.AsNoTracking()
            .Where(ds => ds.IsEnabled && grantedSourceIds.Contains(ds.ExternalDataSourceId)
                && _db.ExternalDataSources.Any(src => src.Id == ds.ExternalDataSourceId && src.IsEnabled))
            .OrderBy(ds => ds.Name)
            .Select(ds => new { ds.Id, ds.Name, ds.FieldsJson })
            .ToListAsync(ct);

        return datasets.Select(ds =>
        {
            var fields = ExternalDataJson.DeserializeFields(ds.FieldsJson)
                .Select(f => new ReportField(f.Name, f.Name, MapType(f.Type), CanFilter: false, CanGroup: false, CanAggregate: false))
                .ToList();
            return new ReportSourceDescriptor(KeyFor(ds.Id), ds.Name, ReportSourceKind.External, fields);
        }).ToList();
    }

    /// <summary>
    /// Ejecuta el dataset externo para el tenant activo. Verifica concesion, descifra la cadena en
    /// memoria, enlaza los parametros (contexto + entrada tipada) y ejecuta via el executor de solo
    /// lectura. Lanza <see cref="ReportValidationException"/> si el tenant no tiene concesion.
    /// </summary>
    public async Task<ReportDataSet> QueryAsync(
        Guid dataSetId, ExternalRunContext context, IReadOnlyDictionary<string, string?>? inputs, CancellationToken ct = default)
    {
        if (!await IsGrantedAsync(dataSetId, context.TenantId, ct))
        {
            throw new ReportValidationException(
                "La fuente de datos externa no esta concedida a este tenant (o esta deshabilitada).");
        }

        var ds = await _db.ExternalDataSets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dataSetId, ct)
            ?? throw new ReportValidationException("El dataset externo no existe.");

        var source = await _db.ExternalDataSources.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ds.ExternalDataSourceId, ct)
            ?? throw new ReportValidationException("La fuente externa no existe.");

        if (string.IsNullOrWhiteSpace(source.ConnectionStringEncrypted))
        {
            throw new ReportValidationException("La fuente externa no tiene cadena de conexion configurada.");
        }

        string connectionString;
        try
        {
            connectionString = _protector.Unprotect(source.ConnectionStringEncrypted);
        }
        catch
        {
            throw new ReportValidationException(
                "La cadena de la fuente externa esta cifrada con una version anterior. Vuelve a guardarla.");
        }

        var declared = ExternalDataJson.DeserializeParameters(ds.ParametersJson);
        var bound = ExternalParameterBinder.Bind(declared, context, inputs);

        var query = new ExternalQuery(source.Provider, connectionString, ds.CommandText, bound);
        return await _executor.ExecuteAsync(query, ct);
    }
}
