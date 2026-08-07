using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Application.Reporting.Authoring;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.External;

/// <summary>Mapeo de un dataset del RDL a un <see cref="ExternalDataSet"/> concedido + los valores de
/// entrada guardados (para los parametros Input; los Context los resuelve el conector).</summary>
public sealed record ExternalReportDataSetMapping(
    string RdlDataSetName,
    Guid ExternalDataSetId,
    IReadOnlyDictionary<string, string?> Inputs);

/// <summary>Vinculo completo de un reporte imprimible a datos externos: la lista de mapeos por dataset
/// del RDL. Se serializa a <see cref="ReportDefinition.ExternalBindingJson"/>.</summary>
public sealed record ExternalReportBinding(IReadOnlyList<ExternalReportDataSetMapping> DataSets);

/// <summary>Resultado de resolver un reporte externo: el RDL crudo + una tabla neutra por cada dataset
/// del RDL, ya ejecutada por el conector de solo lectura. El controlador Bold la inyecta en memoria.</summary>
public sealed record ExternalReportRender(string Rdl, IReadOnlyDictionary<string, ReportDataSet> DataSets);

/// <summary>
/// Importa y ejecuta reportes imprimibles alimentados por el conector externo (ADR-0064). Guarda una
/// <see cref="ReportDefinition"/> (Kind=Printable) tenant-scoped que referencia por Id datasets del
/// catalogo de plataforma; NUNCA guarda la cadena de conexion. Al renderizar, ejecuta cada dataset via
/// <see cref="ExternalReportReader"/> (que exige concesion del tenant) y devuelve las tablas ya filtradas.
/// </summary>
public interface IExternalReportBindingService
{
    /// <summary>Importa un RDL mapeando sus datasets/parametros a datasets externos concedidos. Crea la
    /// ReportDefinition (Kind=Printable) del tenant activo. Devuelve su Id.</summary>
    Task<Guid> ImportRdlAsync(string name, string rdl, ExternalReportBinding binding, string? description, CancellationToken ct = default);

    /// <summary>Devuelve el binding guardado de un reporte (o null si no es externo).</summary>
    Task<ExternalReportBinding?> GetBindingAsync(Guid reportDefinitionId, CancellationToken ct = default);

    /// <summary>Resuelve el RDL + los datasets ejecutados por el conector para alimentar el visor Bold.
    /// Null si el reporte no existe o no esta vinculado a datos externos. Tenant-scoped por construccion.</summary>
    Task<ExternalReportRender?> GetRenderAsync(Guid reportDefinitionId, CancellationToken ct = default);
}

public sealed class ExternalReportBindingService : IExternalReportBindingService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string ExternalSourceKey = "external";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ExternalReportReader _reader;

    public ExternalReportBindingService(IApplicationDbContext db, ITenantContext tenantContext, ExternalReportReader reader)
    {
        _db = db;
        _tenantContext = tenantContext;
        _reader = reader;
    }

    public async Task<Guid> ImportRdlAsync(string name, string rdl, ExternalReportBinding binding, string? description, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }
        if (string.IsNullOrWhiteSpace(rdl))
        {
            throw new ReportValidationException("El RDL esta vacio.");
        }
        if (binding.DataSets.Count == 0)
        {
            throw new ReportValidationException("El reporte externo debe mapear al menos un dataset.");
        }

        var title = string.IsNullOrWhiteSpace(name) ? "Reporte externo" : name.Trim();
        var spec = new ReportSpec { Title = title, SourceKey = ExternalSourceKey, Chart = ReportChartKind.Table };

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = title,
            Description = description,
            Kind = ReportDefinitionKind.Printable,
            Status = ReportDefinitionStatus.Active,
            SourceKey = ExternalSourceKey,
            SpecJson = spec.ToJson(),
            Rdl = rdl,
            ExternalBindingJson = JsonSerializer.Serialize(binding, Json)
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    public async Task<ExternalReportBinding?> GetBindingAsync(Guid reportDefinitionId, CancellationToken ct = default)
    {
        var json = await _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.Id == reportDefinitionId)
            .Select(d => d.ExternalBindingJson)
            .FirstOrDefaultAsync(ct);
        return Deserialize(json);
    }

    public async Task<ExternalReportRender?> GetRenderAsync(Guid reportDefinitionId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var def = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == reportDefinitionId, ct);
        if (def is null || string.IsNullOrWhiteSpace(def.Rdl) || string.IsNullOrWhiteSpace(def.ExternalBindingJson))
        {
            return null;
        }

        var binding = Deserialize(def.ExternalBindingJson);
        if (binding is null || binding.DataSets.Count == 0)
        {
            return null;
        }

        var context = new ExternalRunContext(tenantId, _tenantContext.UserId);
        var tables = new Dictionary<string, ReportDataSet>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in binding.DataSets)
        {
            // El conector re-verifica la concesion del tenant; un reporte importado no puede saltarse la
            // gobernanza si la fuente se revoca despues.
            var ds = await _reader.QueryAsync(map.ExternalDataSetId, context, map.Inputs, ct);
            tables[map.RdlDataSetName] = ds;
        }

        return new ExternalReportRender(def.Rdl!, tables);
    }

    private static ExternalReportBinding? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ExternalReportBinding>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
