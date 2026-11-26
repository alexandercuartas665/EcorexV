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
/// Ademas de los campos FIJOS publica los campos de FICHA que configura cada tenant (ADR-0208,
/// <see cref="TerceroFichaReportFields"/>): por eso el catalogo se describe por tenant (<see cref="DescribeAsync"/>).
/// </summary>
public sealed class TerceroReportSource : IReportableSource, ITenantDescribedSource
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
        new ReportField("CreadoMes", "Mes de creacion", ReportFieldType.Text),
        new ReportField("Categoria", "Categoria", ReportFieldType.Text),
        // Ultima actualizacion a nivel de CONTACTO (cualquier guardado: lo sella el interceptor de auditoria).
        // Sin usuario = proceso automatico (importacion, agente, dron, llenado masivo) o historico.
        new ReportField("UpdatedAt", "Actualizado", ReportFieldType.Date),
        new ReportField("ActualizadoMes", "Mes de actualizacion", ReportFieldType.Text),
        new ReportField("UpdatedByName", "Actualizado por", ReportFieldType.Text)
    };

    private static readonly ReportSourceDescriptor Descriptor = new(SourceKey, "Directorio", ReportSourceKind.Native, FieldSet);

    private readonly IApplicationDbContextFactory _factory;

    public TerceroReportSource(IApplicationDbContextFactory factory) => _factory = factory;

    /// <summary>Parte FIJA (sin BD). El catalogo publica la completa con <see cref="DescribeAsync"/>.</summary>
    public ReportSourceDescriptor Describe() => Descriptor;

    public async Task<ReportSourceDescriptor> DescribeAsync(CancellationToken ct = default)
    {
        await using var db = _factory.Create();
        // Las claves sin definicion salen de los datos: se lee solo la columna de fichas (mismo tope).
        var jsons = await db.Terceros.AsNoTracking()
            .Where(t => t.FichasJson != null)
            .OrderByDescending(t => t.CreatedAt)
            .Take(MaterializeCap)
            .Select(t => t.FichasJson)
            .ToListAsync(ct);
        return Compose(await TerceroFichaReportFields.LoadAsync(db, FieldSet, jsons, ct));
    }

    private static ReportSourceDescriptor Compose(IReadOnlyList<TerceroFichaReportFields.FichaField> fichas)
        => fichas.Count == 0
            ? Descriptor
            : Descriptor with { Fields = FieldSet.Concat(fichas.Select(f => f.Field)).ToList() };

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
                t.CreatedAt,
                t.UpdatedAt,
                t.UpdatedBy,
                t.FichasJson,
                Categorias = t.Categorias.Select(c => c.CategoriaKey).ToList()
            })
            .ToListAsync(ct);

        // Categorias del motor Modular (multi-membership): titulo por clave; sin catalogo queda la clave.
        var titulos = db is IDirectorioModularDbContext modular
            ? await modular.DirectorioCategorias.AsNoTracking().ToDictionaryAsync(c => c.CategoriaKey, c => c.Title, ct)
            : new Dictionary<string, string>();

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

        // Autor de la ultima actualizacion: id de plataforma o de TenantUser -> nombre (solo usuarios del tenant).
        var autores = raw.Any(x => x.UpdatedBy is not null)
            ? await ReportLookups.AuditUsersAsync(db, ct)
            : new Dictionary<Guid, string>();

        // Campos de ficha (definidos + claves presentes en los datos), con el MISMO criterio que DescribeAsync.
        var fichas = await TerceroFichaReportFields.LoadAsync(db, FieldSet, raw.Select(t => t.FichasJson), ct);
        var descriptor = Compose(fichas);

        // Valores (texto crudo, listas ya resueltas a su etiqueta), convertidos al tipo de su campo.
        var fichaVals = fichas.Count == 0
            ? new Dictionary<Guid, string?[]>()
            : await TerceroFichaReportFields.ExtractAsync(db, fichas, raw.Select(t => (t.Id, t.FichasJson)), names, ct);

        var rows = raw.Select(t => Row(t.Id, new object?[]
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
            InMemoryReportEngine.MonthKey(t.CreatedAt, ctx.TenantZone),
            CategoriasTexto(t.Categorias, titulos),
            t.UpdatedAt,
            InMemoryReportEngine.MonthKey(t.UpdatedAt, ctx.TenantZone),
            t.UpdatedBy is Guid ub && autores.TryGetValue(ub, out var autor) ? autor : null
        })).ToList();

        return InMemoryReportEngine.Run(descriptor, rows, spec);

        object?[] Row(Guid id, object?[] fijos)
        {
            if (fichas.Count == 0) { return fijos; }
            var vals = fichaVals.TryGetValue(id, out var v) ? v : new string?[fichas.Count];
            var row = new object?[fijos.Length + fichas.Count];
            fijos.CopyTo(row, 0);
            for (var i = 0; i < fichas.Count; i++)
            {
                row[fijos.Length + i] = ReportValueConverter.Convert(vals[i], fichas[i].Field.Type);
            }
            return row;
        }
    }

    /// <summary>"Comercial, Fiscal" (ordenado, sin repetidos); null si el tercero no tiene categoria.</summary>
    private static string? CategoriasTexto(IEnumerable<string> claves, IReadOnlyDictionary<string, string> titulos)
    {
        var nombres = claves
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => titulos.TryGetValue(k, out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return nombres.Count == 0 ? null : string.Join(", ", nombres);
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
