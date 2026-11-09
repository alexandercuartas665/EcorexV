using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Fuente reportable NATIVA del Directorio (ADR-0158, Ola 1): un registro por <c>Tercero</c>.
/// Tenant-safe por construccion (filtro global de <c>Terceros</c>). Consulta en un contexto EFIMERO
/// propio (<see cref="IApplicationDbContextFactory"/>, ADR-0138/0157) para no chocar con el DbContext
/// del circuito Blazor cuando varios widgets cargan a la vez. Filtro/agrupacion en
/// <see cref="InMemoryReportEngine"/> sobre filas con tope.
/// </summary>
public sealed class TerceroReportSource : IReportableSource
{
    public const string SourceKey = "native:tercero";
    private const int MaterializeCap = 50_000;

    private static readonly IReadOnlyList<ReportField> FieldSet = new[]
    {
        new ReportField("Id", "Id", ReportFieldType.Text, CanGroup: false),
        new ReportField("Nombre", "Nombre", ReportFieldType.Text),
        new ReportField("Tipo", "Tipo", ReportFieldType.Text),
        new ReportField("Estado", "Estado", ReportFieldType.Text),
        new ReportField("Perfiles", "Perfiles", ReportFieldType.Text),
        new ReportField("EsCliente", "Es cliente", ReportFieldType.Boolean),
        new ReportField("EsProveedor", "Es proveedor", ReportFieldType.Boolean),
        new ReportField("EsSospechoso", "Es sospechoso", ReportFieldType.Boolean),
        new ReportField("EsEmpleado", "Es empleado", ReportFieldType.Boolean),
        new ReportField("Vendedor", "Vendedor", ReportFieldType.Text),
        new ReportField("Ciudad", "Ciudad", ReportFieldType.Text),
        new ReportField("Sector", "Sector", ReportFieldType.Text),
        new ReportField("Cargo", "Cargo", ReportFieldType.Text),
        new ReportField("Nit", "Identificacion", ReportFieldType.Text),
        new ReportField("NitNormalizado", "NIT (sin DV)", ReportFieldType.Text),
        new ReportField("Email", "Correo", ReportFieldType.Text, CanGroup: false),
        new ReportField("Telefono", "Telefono", ReportFieldType.Text, CanGroup: false),
        new ReportField("Empresa", "Empresa", ReportFieldType.Text),
        new ReportField("CreatedAt", "Creado", ReportFieldType.Date),
        new ReportField("CreadoMes", "Mes de creacion", ReportFieldType.Text)
    };

    private static readonly ReportSourceDescriptor Descriptor = new(SourceKey, "Directorio", ReportSourceKind.Native, FieldSet);

    private readonly IApplicationDbContextFactory _factory;

    public TerceroReportSource(IApplicationDbContextFactory factory) => _factory = factory;

    public ReportSourceDescriptor Describe() => Descriptor;

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        await using var db = _factory.Create();

        var raw = await db.Terceros.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(MaterializeCap)
            .Select(t => new
            {
                t.Id,
                t.Nombre,
                t.Tipo,
                t.Estado,
                t.Perfiles,
                t.Vendedor,
                t.Ciudad,
                t.Sector,
                t.Cargo,
                t.IdValor,
                t.Email,
                t.Telefono,
                t.EmpresaId,
                t.CreatedAt
            })
            .ToListAsync(ct);

        // Nombre de la empresa (auto-referencia) resuelto en memoria sobre el mismo conjunto acotado.
        var names = raw.ToDictionary(x => x.Id, x => x.Nombre);
        var missing = raw.Where(x => x.EmpresaId is Guid g && !names.ContainsKey(g)).Select(x => x.EmpresaId!.Value).Distinct().ToList();
        if (missing.Count > 0)
        {
            foreach (var e in await db.Terceros.AsNoTracking().Where(t => missing.Contains(t.Id)).Select(t => new { t.Id, t.Nombre }).ToListAsync(ct))
            {
                names[e.Id] = e.Nombre;
            }
        }

        var rows = raw.Select(t => new object?[]
        {
            t.Id.ToString(),
            t.Nombre,
            t.Tipo.ToString(),
            t.Estado.ToString(),
            PerfilesTexto(t.Perfiles),
            t.Perfiles.HasFlag(TerceroPerfil.Cliente),
            t.Perfiles.HasFlag(TerceroPerfil.Proveedor),
            t.Perfiles.HasFlag(TerceroPerfil.Sospechoso),
            t.Perfiles.HasFlag(TerceroPerfil.Empleado),
            Blank(t.Vendedor),
            Blank(t.Ciudad),
            Blank(t.Sector),
            Blank(t.Cargo),
            Blank(t.IdValor),
            NitNormalizer.Normalize(t.IdValor),
            Blank(t.Email),
            Blank(t.Telefono),
            t.EmpresaId is Guid g && names.TryGetValue(g, out var n) ? n : null,
            t.CreatedAt,
            InMemoryReportEngine.MonthKey(t.CreatedAt)
        }).ToList();

        return InMemoryReportEngine.Run(Descriptor, rows, spec);
    }

    internal static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string PerfilesTexto(TerceroPerfil p)
    {
        if (p == TerceroPerfil.Ninguno)
        {
            return "Ninguno";
        }

        var parts = new List<string>();
        foreach (var flag in new[] { TerceroPerfil.Cliente, TerceroPerfil.Proveedor, TerceroPerfil.Sospechoso, TerceroPerfil.Empleado })
        {
            if (p.HasFlag(flag))
            {
                parts.Add(flag.ToString());
            }
        }

        return string.Join(", ", parts);
    }
}
