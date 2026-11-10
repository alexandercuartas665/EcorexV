using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Fuente reportable NATIVA de horas trabajadas (ADR-0158, Ola 1): un registro por <c>TaskWorkLog</c>,
/// enriquecido con la tarea (numero, titulo, tablero, proyecto, cliente). Permite sumar horas por
/// usuario, cliente, tablero o mes. Tenant-safe (filtro global en todas las tablas) y en contexto
/// EFIMERO propio.
/// </summary>
public sealed class WorkLogReportSource : IReportableSource
{
    public const string SourceKey = "native:worklog";
    private const int MaterializeCap = 50_000;

    private static readonly IReadOnlyList<ReportField> FieldSet = new[]
    {
        new ReportField("Fecha", "Fecha", ReportFieldType.Date),
        new ReportField("Mes", "Mes", ReportFieldType.Text),
        new ReportField("Horas", "Horas", ReportFieldType.Decimal, CanAggregate: true),
        new ReportField("Segundos", "Segundos", ReportFieldType.Number, CanAggregate: true),
        new ReportField("Tipo", "Tipo de registro", ReportFieldType.Text),
        new ReportField("Usuario", "Usuario", ReportFieldType.Text),
        new ReportField("TareaNumero", "Tarea", ReportFieldType.Text),
        new ReportField("TareaTitulo", "Titulo de la tarea", ReportFieldType.Text, CanGroup: false),
        new ReportField("Tablero", "Tablero", ReportFieldType.Text),
        new ReportField("Proyecto", "Proyecto", ReportFieldType.Text),
        new ReportField("TerceroId", "Cliente (ID)", ReportFieldType.Text, CanGroup: false),
        new ReportField("Cliente", "Cliente", ReportFieldType.Text),
        new ReportField("ClienteNit", "NIT cliente (sin DV)", ReportFieldType.Text)
    };

    private static readonly ReportSourceDescriptor Descriptor = new(SourceKey, "Horas trabajadas", ReportSourceKind.Native, FieldSet);

    private readonly IApplicationDbContextFactory _factory;

    public WorkLogReportSource(IApplicationDbContextFactory factory) => _factory = factory;

    public ReportSourceDescriptor Describe() => Descriptor;

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        await using var db = _factory.Create();

        // Join a la tarea en el servidor (ambas tablas con filtro global tenant).
        var raw = await (
            from w in db.TaskWorkLogs.AsNoTracking()
            join t in db.TaskItems.AsNoTracking() on w.TaskItemId equals t.Id into tj
            from t in tj.DefaultIfEmpty()
            orderby w.LoggedAt descending
            select new
            {
                w.LoggedAt,
                w.Seconds,
                w.Kind,
                w.TenantUserId,
                Number = t != null ? t.Number : null,
                Title = t != null ? t.Title : null,
                BoardId = t != null ? t.BoardId : null,
                ProjectId = t != null ? t.ProjectId : null,
                TerceroId = t != null ? t.TerceroId : null
            })
            .Take(MaterializeCap)
            .ToListAsync(ct);

        var users = await ReportLookups.UsersAsync(db, ct);
        var boards = await ReportLookups.BoardsAsync(db, ct);
        var projects = await ReportLookups.ProjectsAsync(db, ct);
        var terceros = await ReportLookups.TercerosAsync(db, ct);

        var rows = raw.Select(w =>
        {
            TerceroRef? tr = w.TerceroId is Guid tid && terceros.TryGetValue(tid, out var x) ? x : null;
            return new object?[]
            {
                w.LoggedAt,
                InMemoryReportEngine.MonthKey(w.LoggedAt, ctx.TenantZone),
                Math.Round(w.Seconds / 3600m, 4),
                (long)w.Seconds,
                w.Kind.ToString(),
                users.TryGetValue(w.TenantUserId, out var u) ? u : null,
                w.Number,
                w.Title,
                w.BoardId is Guid b && boards.TryGetValue(b, out var bn) ? bn : null,
                w.ProjectId is Guid p && projects.TryGetValue(p, out var pn) ? pn : null,
                w.TerceroId?.ToString(),
                tr?.Nombre,
                tr?.Nit
            };
        }).ToList();

        return InMemoryReportEngine.Run(Descriptor, rows, spec);
    }
}
