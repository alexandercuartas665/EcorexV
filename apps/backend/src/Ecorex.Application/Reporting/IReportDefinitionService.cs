using Ecorex.Application.Common;
using Ecorex.Application.Reporting.Authoring;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting;

/// <summary>
/// Persistencia de reportes guardados (ADR-0051, Ola 4): guarda/lista/obtiene/edita/archiva y EJECUTA
/// una <see cref="Ecorex.Domain.Entities.ReportDefinition"/>. El artefacto guardado es el JSON-spec
/// (mismo que edita la IA y el usuario). Todo tenant-scoped por el filtro global; ejecutar un reporte
/// pasa por el datasource tenant-safe, nunca por una cadena de conexion.
/// </summary>
public interface IReportDefinitionService
{
    Task<Guid> SaveAsync(ReportSpec spec, string? description, CancellationToken ct = default);

    /// <summary>Guarda un imprimible: genera el RDL desde el spec + el shape del resultado y lo
    /// persiste con Kind=Printable. Es el artefacto que abrira el editor/visor Bold (Ola 2).</summary>
    Task<Guid> SavePrintableAsync(ReportSpec spec, ReportDataSet ds, string? description, CancellationToken ct = default);

    /// <summary>Guarda un imprimible con un RDL YA CONSTRUIDO (p.ej. el reporte rico multi-pagina).</summary>
    Task<Guid> SavePrintableRdlAsync(ReportSpec spec, string rdl, string? description, CancellationToken ct = default);

    /// <summary>Crea un imprimible NUEVO en blanco (Kind=Printable) a partir de un punto de partida
    /// editable (tabla de actividades recientes), listo para abrirlo en el diseniador Bold y reestilizar.
    /// Devuelve el id del reporte creado. Tenant-scoped.</summary>
    Task<Guid> CreateBlankPrintableAsync(string? title, CancellationToken ct = default);

    Task UpdateSpecAsync(Guid id, ReportSpec spec, CancellationToken ct = default);

    /// <summary>
    /// Lista los reportes ACTIVOS que el usuario puede ver (gobernanza por rol, ADR-0051):
    /// <paramref name="seeAll"/> = true (Owner/Admin o permiso Administrar) devuelve todos; si no,
    /// devuelve los reportes SIN asignacion (visibles para todos) mas los asignados a
    /// <paramref name="rolId"/>. Tenant-scoped por el filtro global.
    /// </summary>
    Task<IReadOnlyList<ReportDefinitionSummary>> ListAsync(bool seeAll, Guid? rolId, CancellationToken ct = default);

    /// <summary>Admin (Reportes.Administrar): TODOS los reportes del tenant, activos y archivados.</summary>
    Task<IReadOnlyList<ReportDefinitionSummary>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Roles asignados a un reporte (ids). Vacio = visible para todos.</summary>
    Task<IReadOnlyList<Guid>> GetAssignedRolesAsync(Guid reportId, CancellationToken ct = default);

    /// <summary>Reemplaza el conjunto de roles asignados a un reporte (borra e inserta). Tenant-scoped.</summary>
    Task AssignRolesAsync(Guid reportId, IReadOnlyCollection<Guid> rolIds, CancellationToken ct = default);

    Task<ReportDefinitionDetail?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Galeria + doble vista (ADR-0051): asegura que el reporte tenga un RDL imprimible. Si ya lo
    /// tiene, no hace nada; si no y tiene un spec, lo GENERA desde el spec (ejecutando el datasource
    /// tenant-safe) SIN cambiar su Kind. Devuelve false si el reporte no existe o no tiene spec.
    /// </summary>
    Task<bool> EnsurePrintableRdlAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea unos reportes de ejemplo (tableros sobre actividades) para el tenant actual.
    /// Idempotente por nombre: no duplica los que ya existen activos.</summary>
    Task CreateExampleReportsAsync(CancellationToken ct = default);

    Task ArchiveAsync(Guid id, CancellationToken ct = default);

    Task<ReportRunResult?> RunAsync(Guid id, CancellationToken ct = default);

    /// <summary>Devuelve el RDL de un imprimible + su dataset ya ejecutado por el datasource tenant-safe,
    /// para alimentar al visor Bold. Null si no existe o no tiene RDL. Tenant-scoped por construccion.</summary>
    Task<ReportPrintable?> GetPrintableAsync(Guid id, CancellationToken ct = default);

    /// <summary>Devuelve solo el RDL crudo de un imprimible (para abrirlo en el diseniador). Tenant-scoped.</summary>
    Task<string?> GetRdlAsync(Guid id, CancellationToken ct = default);

    /// <summary>Persiste el RDL editado en el diseniador Bold. Tenant-scoped. Devuelve false si no existe.</summary>
    Task<bool> UpdateRdlAsync(Guid id, string rdl, CancellationToken ct = default);
}

/// <summary>RDL de un imprimible + las filas ya filtradas por tenant que lo alimentan.</summary>
public sealed record ReportPrintable(string Rdl, ReportDataSet DataSet);

public sealed record ReportDefinitionSummary(
    Guid Id, string Name, ReportDefinitionKind Kind, string? SourceKey,
    ReportDefinitionStatus Status, DateTimeOffset? UpdatedAt);

public sealed record ReportDefinitionDetail(
    Guid Id, string Name, string? Description, ReportDefinitionKind Kind, ReportSpec Spec, long Version);

public sealed record ReportRunResult(ReportSpec Spec, ReportDataSet DataSet, object? Option);

public sealed class ReportDefinitionService : IReportDefinitionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IReportDataSource _dataSource;

    public ReportDefinitionService(IApplicationDbContext db, ITenantContext tenantContext, IReportDataSource dataSource)
    {
        _db = db;
        _tenantContext = tenantContext;
        _dataSource = dataSource;
    }

    public async Task<Guid> SaveAsync(ReportSpec spec, string? description, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(spec.Title) ? "Reporte sin titulo" : spec.Title.Trim(),
            Description = description,
            Kind = ReportDefinitionKind.Dashboard,
            Status = ReportDefinitionStatus.Active,
            SourceKey = spec.SourceKey,
            SpecJson = spec.ToJson()
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    public async Task<Guid> SavePrintableAsync(ReportSpec spec, ReportDataSet ds, string? description, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(spec.Title) ? "Imprimible sin titulo" : spec.Title.Trim(),
            Description = description,
            Kind = ReportDefinitionKind.Printable,
            Status = ReportDefinitionStatus.Active,
            SourceKey = spec.SourceKey,
            SpecJson = spec.ToJson(),
            Rdl = ReportSpecToRdl.ToRdl(spec, ds)
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    public async Task<Guid> SavePrintableRdlAsync(ReportSpec spec, string rdl, string? description, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(spec.Title) ? "Imprimible sin titulo" : spec.Title.Trim(),
            Description = description,
            Kind = ReportDefinitionKind.Printable,
            Status = ReportDefinitionStatus.Active,
            SourceKey = spec.SourceKey,
            SpecJson = spec.ToJson(),
            Rdl = rdl
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    public async Task<Guid> CreateBlankPrintableAsync(string? title, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        // Punto de partida editable: una tabla de actividades recientes cuya fuente el datasource
        // tenant-safe sabe servir. El usuario reordena columnas y reestiliza el layout en el diseniador.
        var spec = new ReportSpec
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Nuevo imprimible" : title.Trim(),
            SourceKey = TaskItemReportSource.SourceKey,
            Chart = ReportChartKind.Table,
            Fields = { "Number", "Title", "Status", "Priority", "CreatedAt" },
            Sort = { new ReportSortSpec { Field = "CreatedAt", Desc = true } },
            Top = 50
        };

        var ds = await _dataSource.QueryAsync(spec.ToQuerySpec(), new ReportContext(tenantId), ct);
        var rdl = ReportSpecToRdl.ToRdl(spec, ds);
        return await SavePrintableRdlAsync(spec, rdl, "Imprimible en blanco (editable en el diseniador)", ct);
    }

    public async Task UpdateSpecAsync(Guid id, ReportSpec spec, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException("El reporte no existe.");
        def.Name = string.IsNullOrWhiteSpace(spec.Title) ? def.Name : spec.Title.Trim();
        def.SourceKey = spec.SourceKey;
        def.SpecJson = spec.ToJson();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReportDefinitionSummary>> ListAsync(bool seeAll, Guid? rolId, CancellationToken ct = default)
    {
        var q = _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.Status == ReportDefinitionStatus.Active);

        if (!seeAll)
        {
            // Visible si el reporte NO tiene ninguna asignacion (para todos), o si alguna de sus
            // asignaciones coincide con el rol del usuario. Ambas tablas son tenant-scoped por el
            // filtro global, asi que el cruce jamas sale del tenant.
            q = q.Where(d => !_db.ReportDefinitionRoles.Any(r => r.ReportDefinitionId == d.Id)
                          || (rolId != null && _db.ReportDefinitionRoles.Any(r => r.ReportDefinitionId == d.Id && r.RolId == rolId)));
        }

        return await q
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Select(d => new ReportDefinitionSummary(d.Id, d.Name, d.Kind, d.SourceKey, d.Status, d.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReportDefinitionSummary>> ListAllAsync(CancellationToken ct = default)
    {
        return await _db.ReportDefinitions.AsNoTracking()
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Select(d => new ReportDefinitionSummary(d.Id, d.Name, d.Kind, d.SourceKey, d.Status, d.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetAssignedRolesAsync(Guid reportId, CancellationToken ct = default)
    {
        return await _db.ReportDefinitionRoles.AsNoTracking()
            .Where(r => r.ReportDefinitionId == reportId)
            .Select(r => r.RolId)
            .ToListAsync(ct);
    }

    public async Task AssignRolesAsync(Guid reportId, IReadOnlyCollection<Guid> rolIds, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        // El reporte debe existir dentro del tenant (filtro global) para poder asignarlo.
        if (!await _db.ReportDefinitions.AnyAsync(d => d.Id == reportId, ct))
        {
            return;
        }

        var actuales = await _db.ReportDefinitionRoles.Where(r => r.ReportDefinitionId == reportId).ToListAsync(ct);
        _db.ReportDefinitionRoles.RemoveRange(actuales);
        foreach (var rolId in rolIds.Distinct())
        {
            _db.ReportDefinitionRoles.Add(new ReportDefinitionRole
            {
                Id = Guid.CreateVersion7(),
                ReportDefinitionId = reportId,
                RolId = rolId
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ReportDefinitionDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return null;
        }

        var spec = ReportSpec.FromJson(def.SpecJson) ?? new ReportSpec { Title = def.Name };
        return new ReportDefinitionDetail(def.Id, def.Name, def.Description, def.Kind, spec, def.Version);
    }

    public async Task<bool> EnsurePrintableRdlAsync(Guid id, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return false;
        }

        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(def.Rdl))
        {
            return true;
        }

        var spec = ReportSpec.FromJson(def.SpecJson);
        if (spec is null)
        {
            return false;
        }

        var ds = await _dataSource.QueryAsync(spec.ToQuerySpec(), new ReportContext(tenantId), ct);
        def.Rdl = ReportSpecToRdl.ToRdl(spec, ds);
        // No se cambia el Kind: el reporte sigue siendo lo que era; solo gana una version imprimible.
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task CreateExampleReportsAsync(CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            return;
        }

        var src = TaskItemReportSource.SourceKey;
        var examples = new[]
        {
            // Reporte tipo PANEL: al abrirlo muestra el DASHBOARD completo (KPIs + varios graficos +
            // tabla), no un solo grafico. La galeria lo reconoce por el SourceKey "panel:...".
            new ReportSpec
            {
                Title = "Tablero de actividades", SourceKey = "panel:system-activities", Chart = ReportChartKind.Table
            },
            new ReportSpec
            {
                Title = "Actividades por estado", SourceKey = src, Chart = ReportChartKind.Pie,
                GroupBy = { "Status" },
                Aggregates = { new ReportAggregateSpec { Field = "Status", Function = ReportAggregateFunction.Count } }
            },
            new ReportSpec
            {
                Title = "Actividades por prioridad", SourceKey = src, Chart = ReportChartKind.Bar,
                GroupBy = { "Priority" },
                Aggregates = { new ReportAggregateSpec { Field = "Priority", Function = ReportAggregateFunction.Count } }
            },
            new ReportSpec
            {
                Title = "Actividades recientes", SourceKey = src, Chart = ReportChartKind.Table,
                Fields = { "Number", "Title", "Status", "CreatedAt" },
                Sort = { new ReportSortSpec { Field = "CreatedAt", Desc = true } },
                Top = 20
            }
        };

        foreach (var spec in examples)
        {
            var existe = await _db.ReportDefinitions
                .AnyAsync(d => d.Name == spec.Title && d.Status == ReportDefinitionStatus.Active, ct);
            if (existe)
            {
                continue;
            }
            await SaveAsync(spec, "Reporte de ejemplo del sistema", ct);
        }
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return;
        }

        def.Status = ReportDefinitionStatus.Archived;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ReportRunResult?> RunAsync(Guid id, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var def = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return null;
        }

        var spec = ReportSpec.FromJson(def.SpecJson);
        if (spec is null)
        {
            return null;
        }

        var ds = await _dataSource.QueryAsync(spec.ToQuerySpec(), new ReportContext(tenantId), ct);
        var option = ReportSpecRenderer.BuildOption(spec, ds);
        return new ReportRunResult(spec, ds, option);
    }

    public async Task<ReportPrintable?> GetPrintableAsync(Guid id, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var def = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null || string.IsNullOrWhiteSpace(def.Rdl))
        {
            return null;
        }

        var spec = ReportSpec.FromJson(def.SpecJson);
        if (spec is null)
        {
            return null;
        }

        var ds = await _dataSource.QueryAsync(spec.ToQuerySpec(), new ReportContext(tenantId), ct);
        return new ReportPrintable(def.Rdl!, ds);
    }

    public async Task<string?> GetRdlAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => d.Rdl)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> UpdateRdlAsync(Guid id, string rdl, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return false;
        }

        def.Rdl = rdl;
        def.Kind = ReportDefinitionKind.Printable;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
