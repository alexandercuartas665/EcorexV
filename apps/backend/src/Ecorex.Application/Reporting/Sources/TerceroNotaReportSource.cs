using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Fuente reportable NATIVA de la bitacora del Directorio (ADR-0158, Ola 1): un registro por
/// <c>TerceroNota</c> (nota, llamada, visita, gestion con valor). Permite medir gestion comercial por
/// accion, autor, mes y sumar <c>Valor</c>. Tenant-safe (filtro global) y en contexto EFIMERO propio.
/// </summary>
public sealed class TerceroNotaReportSource : IReportableSource
{
    public const string SourceKey = "native:tercero-nota";
    private const int MaterializeCap = 50_000;

    private static readonly IReadOnlyList<ReportField> FieldSet = new[]
    {
        new ReportField("Fecha", "Fecha", ReportFieldType.Date),
        new ReportField("Mes", "Mes", ReportFieldType.Text),
        new ReportField("Accion", "Accion", ReportFieldType.Text),
        new ReportField("Categoria", "Categoria", ReportFieldType.Text),
        new ReportField("Subcategoria", "Subcategoria", ReportFieldType.Text),
        new ReportField("Autor", "Autor", ReportFieldType.Text),
        new ReportField("Valor", "Valor", ReportFieldType.Decimal, CanAggregate: true),
        new ReportField("TerceroId", "Tercero (ID)", ReportFieldType.Text, CanGroup: false),
        new ReportField("Tercero", "Tercero", ReportFieldType.Text),
        new ReportField("TerceroNit", "NIT tercero (sin DV)", ReportFieldType.Text),
        new ReportField("Vendedor", "Vendedor del tercero", ReportFieldType.Text)
    };

    private static readonly ReportSourceDescriptor Descriptor = new(SourceKey, "Directorio - bitacora", ReportSourceKind.Native, FieldSet);

    private readonly IApplicationDbContextFactory _factory;

    public TerceroNotaReportSource(IApplicationDbContextFactory factory) => _factory = factory;

    public ReportSourceDescriptor Describe() => Descriptor;

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        await using var db = _factory.Create();

        var raw = await db.TerceroNotas.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(MaterializeCap)
            .Select(n => new { n.CreatedAt, n.Accion, n.Categoria, n.Subcategoria, n.Autor, n.Valor, n.TerceroId })
            .ToListAsync(ct);

        var terceros = await ReportLookups.TercerosAsync(db, ct);

        var rows = raw.Select(n =>
        {
            terceros.TryGetValue(n.TerceroId, out var t);
            return new object?[]
            {
                n.CreatedAt,
                InMemoryReportEngine.MonthKey(n.CreatedAt, ctx.TenantZone),
                TerceroReportSource.Blank(n.Accion),
                TerceroReportSource.Blank(n.Categoria),
                TerceroReportSource.Blank(n.Subcategoria),
                TerceroReportSource.Blank(n.Autor),
                n.Valor,
                n.TerceroId.ToString(),
                t?.Nombre,
                t?.Nit,
                TerceroReportSource.Blank(t?.Vendedor)
            };
        }).ToList();

        return InMemoryReportEngine.Run(Descriptor, rows, spec);
    }
}
