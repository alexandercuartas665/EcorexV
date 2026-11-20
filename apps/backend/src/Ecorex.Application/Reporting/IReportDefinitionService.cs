using Ecorex.Application.Common;
using Ecorex.Application.Reporting.Authoring;
using Ecorex.Application.Reporting.Panels;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Application.Reporting.Templates;
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

    /// <summary>Reportes ARCHIVADos del tenant activo (Status=Archived), para la pestana "Archivados" desde
    /// la que se reactivan. Tenant-scoped por el filtro global.</summary>
    Task<IReadOnlyList<ReportDefinitionSummary>> ListArchivedAsync(CancellationToken ct = default);

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

    /// <summary>Desarchiva un reporte (Status=Archived -> Active) para recuperarlo desde la UI. Tenant-scoped
    /// (filtro global); auditado por el interceptor (updated_at/by). Idempotente: si no existe, no hace nada.</summary>
    Task UnarchiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Configuracion de la tarjeta (ADR-0192): nombre, categoria y descripcion del reporte, sin tocar su spec
    /// ni su RDL. El nombre es obligatorio; categoria y descripcion vacias quedan en null. En un panel por spec
    /// tambien se sincroniza el "title" del spec para que el visor muestre el mismo nombre. Tenant-scoped.
    /// Devuelve false si el reporte no existe; lanza <see cref="ArgumentException"/> si el nombre es vacio.
    /// </summary>
    Task<bool> UpdateCardAsync(Guid id, string name, string? category, string? description, CancellationToken ct = default);

    /// <summary>Categorias en uso en el tenant (activos y archivados), distintas y ordenadas, para el selector
    /// de la tuerca y los chips de la galeria (ADR-0192). Tenant-scoped.</summary>
    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct = default);

    Task<ReportRunResult?> RunAsync(Guid id, CancellationToken ct = default);

    /// <summary>Devuelve el RDL de un imprimible + su dataset ya ejecutado por el datasource tenant-safe,
    /// para alimentar al visor Bold. Null si no existe o no tiene RDL. Tenant-scoped por construccion.</summary>
    Task<ReportPrintable?> GetPrintableAsync(Guid id, CancellationToken ct = default);

    /// <summary>Devuelve solo el RDL crudo de un imprimible (para abrirlo en el diseniador). Tenant-scoped.</summary>
    Task<string?> GetRdlAsync(Guid id, CancellationToken ct = default);

    /// <summary>Persiste el RDL editado en el diseniador Bold. Tenant-scoped. Devuelve false si no existe.</summary>
    Task<bool> UpdateRdlAsync(Guid id, string rdl, CancellationToken ct = default);

    // ---- Paneles GENERICOS por spec (ADR-0066): autoria como DATO, sin recompilar ni desplegar ----

    /// <summary>Devuelve el SpecJson crudo de un reporte (para editar un PanelSpec). Null si no existe.</summary>
    Task<string?> GetSpecJsonAsync(Guid id, CancellationToken ct = default);

    /// <summary>Valida un PanelSpec (JSON) contra el catalogo tenant-safe. Lista vacia = valido; en caso
    /// contrario, mensajes claros de error. Un JSON ilegible devuelve un unico error.</summary>
    Task<IReadOnlyList<string>> ValidatePanelSpecAsync(string specJson, CancellationToken ct = default);

    /// <summary>Guarda un PanelSpec como ReportDefinition (Kind=Dashboard, SourceKey="panel:spec"),
    /// tenant-scoped. Valida contra el catalogo; lanza <see cref="ReportValidationException"/> si es
    /// invalido. Aparece en la galeria y abre por SpecPanelRenderer. Sin recompilar ni desplegar.</summary>
    Task<Guid> SavePanelSpecAsync(string name, string specJson, CancellationToken ct = default);

    /// <summary>Actualiza el PanelSpec (nombre + JSON) de un reporte panel existente. Valida igual que
    /// el alta. Devuelve false si no existe.</summary>
    Task<bool> UpdatePanelSpecAsync(Guid id, string name, string specJson, CancellationToken ct = default);

    /// <summary>Duplica un reporte panel por spec (mismo SpecJson, nombre nuevo). Devuelve el nuevo id.</summary>
    Task<Guid> DuplicatePanelSpecAsync(Guid id, string? newName, CancellationToken ct = default);
}

/// <summary>RDL de un imprimible + las filas ya filtradas por tenant que lo alimentan.</summary>
public sealed record ReportPrintable(string Rdl, ReportDataSet DataSet);

/// <summary>Tarjeta de la galeria. <see cref="Category"/> agrupa (ADR-0192); null = "Sin categoria".</summary>
public sealed record ReportDefinitionSummary(
    Guid Id, string Name, ReportDefinitionKind Kind, string? SourceKey,
    ReportDefinitionStatus Status, DateTimeOffset? UpdatedAt,
    string? Category = null, string? Description = null);

public sealed record ReportDefinitionDetail(
    Guid Id, string Name, string? Description, ReportDefinitionKind Kind, ReportSpec Spec, long Version);

public sealed record ReportRunResult(ReportSpec Spec, ReportDataSet DataSet, object? Option);

public sealed class ReportDefinitionService : IReportDefinitionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IReportDataSource _dataSource;
    private readonly IReportActivationService? _activation;
    private readonly IReportCatalog? _catalog;

    public ReportDefinitionService(
        IApplicationDbContext db, ITenantContext tenantContext, IReportDataSource dataSource,
        IReportActivationService? activation = null, IReportCatalog? catalog = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _dataSource = dataSource;
        _activation = activation;
        _catalog = catalog;
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
            .Select(d => new ReportDefinitionSummary(d.Id, d.Name, d.Kind, d.SourceKey, d.Status, d.UpdatedAt, d.Category, d.Description))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReportDefinitionSummary>> ListAllAsync(CancellationToken ct = default)
    {
        return await _db.ReportDefinitions.AsNoTracking()
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Select(d => new ReportDefinitionSummary(d.Id, d.Name, d.Kind, d.SourceKey, d.Status, d.UpdatedAt, d.Category, d.Description))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReportDefinitionSummary>> ListArchivedAsync(CancellationToken ct = default)
    {
        return await _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.Status == ReportDefinitionStatus.Archived)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Select(d => new ReportDefinitionSummary(d.Id, d.Name, d.Kind, d.SourceKey, d.Status, d.UpdatedAt, d.Category, d.Description))
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

        // ADR-0062: los PANELES (Panel de Actividades, Panel OCS...) ya no se hardcodean aqui: son
        // plantillas de plataforma que se ACTIVAN segun compatibilidad de fuente. Activar aqui todas
        // las plantillas publicadas compatibles con este tenant (nativas siempre; contenedor solo si el
        // contenedor existe) hace que, p.ej., el Panel OCS aparezca UNICAMENTE donde hay contenedor OCS.
        // Tenant-safe por el filtro global; idempotente (no re-activa lo ya activado).
        if (_activation is not null)
        {
            await _activation.ActivateCompatibleAsync(includeNative: true, ct);
        }

        // Ejemplos NATIVOS locales del tenant (dashboards sobre tareas), TemplateId=null: son reportes
        // propios de arranque, no plantillas. Idempotente por nombre.
        var src = TaskItemReportSource.SourceKey;
        var examples = new[]
        {
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

    public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return;
        }

        def.Status = ReportDefinitionStatus.Active;
        await _db.SaveChangesAsync(ct);
    }

    public const int CategoryMaxLength = 80;

    public async Task<bool> UpdateCardAsync(Guid id, string name, string? category, string? description, CancellationToken ct = default)
    {
        var nombre = (name ?? string.Empty).Trim();
        if (nombre.Length == 0)
        {
            throw new ArgumentException("El reporte necesita un nombre.", nameof(name));
        }
        if (nombre.Length > 200) { nombre = nombre[..200]; }

        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return false;
        }

        def.Name = nombre;
        var cat = NormalizeCategory(category);
        if (cat is not null)
        {
            // "ventas" y "Ventas" son la misma categoria: se reutiliza la escritura que ya existe en el tenant.
            var existentes = await ListCategoriesAsync(ct);
            cat = existentes.FirstOrDefault(e => string.Equals(e, cat, StringComparison.CurrentCultureIgnoreCase)) ?? cat;
        }
        def.Category = cat;
        var desc = (description ?? string.Empty).Trim();
        def.Description = desc.Length == 0 ? null : (desc.Length > 1000 ? desc[..1000] : desc);

        // Panel por spec: el visor toma el titulo del spec, asi que se sincroniza para que no se vea el viejo.
        // Si el JSON no se puede leer se deja intacto (renombrar nunca debe romper el panel).
        if (PanelSpecSource.Is(def.SourceKey) && !string.IsNullOrWhiteSpace(def.SpecJson))
        {
            try
            {
                if (System.Text.Json.Nodes.JsonNode.Parse(def.SpecJson) is System.Text.Json.Nodes.JsonObject obj)
                {
                    obj["title"] = nombre;
                    def.SpecJson = obj.ToJsonString();
                }
            }
            catch (System.Text.Json.JsonException) { /* spec ilegible: solo cambia el nombre de la tarjeta */ }
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var cats = await _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.Category != null && d.Category != "")
            .Select(d => d.Category!)
            .Distinct()
            .ToListAsync(ct);
        return cats.OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Espacios colapsados y recortada a <see cref="CategoryMaxLength"/>; vacia = null.</summary>
    public static string? NormalizeCategory(string? category)
    {
        var c = System.Text.RegularExpressions.Regex.Replace((category ?? string.Empty).Trim(), @"\s+", " ");
        if (c.Length == 0) { return null; }
        return c.Length <= CategoryMaxLength ? c : c[..CategoryMaxLength].TrimEnd();
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

    // ---- Paneles GENERICOS por spec (ADR-0066) ----

    public async Task<string?> GetSpecJsonAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => d.SpecJson)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ValidatePanelSpecAsync(string specJson, CancellationToken ct = default)
    {
        var spec = PanelSpec.FromJson(specJson);
        if (spec is null)
        {
            return new[] { "El JSON del panel no es valido (no se pudo interpretar)." };
        }

        if (_catalog is null)
        {
            return new[] { "El catalogo de reportes no esta disponible para validar." };
        }

        var sources = await _catalog.GetSourcesAsync(ct);
        return PanelSpecValidator.Validate(spec, sources);
    }

    public async Task<Guid> SavePanelSpecAsync(string name, string specJson, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        await EnsureValidPanelAsync(specJson, ct);

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(name) ? "Panel sin titulo" : name.Trim(),
            Description = "Panel generico por spec (ADR-0066)",
            Kind = ReportDefinitionKind.Dashboard,
            Status = ReportDefinitionStatus.Active,
            SourceKey = PanelSpecSource.SourceKey,
            SpecJson = specJson
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    public async Task<bool> UpdatePanelSpecAsync(Guid id, string name, string specJson, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return false;
        }

        await EnsureValidPanelAsync(specJson, ct);

        def.Name = string.IsNullOrWhiteSpace(name) ? def.Name : name.Trim();
        def.SourceKey = PanelSpecSource.SourceKey;
        def.Kind = ReportDefinitionKind.Dashboard;
        def.SpecJson = specJson;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid> DuplicatePanelSpecAsync(Guid id, string? newName, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        var src = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException("El panel no existe.");

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(newName) ? src.Name + " (copia)" : newName.Trim(),
            Description = src.Description,
            Kind = ReportDefinitionKind.Dashboard,
            Status = ReportDefinitionStatus.Active,
            SourceKey = PanelSpecSource.SourceKey,
            SpecJson = src.SpecJson
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    private async Task EnsureValidPanelAsync(string specJson, CancellationToken ct)
    {
        var errors = await ValidatePanelSpecAsync(specJson, ct);
        if (errors.Count > 0)
        {
            throw new ReportValidationException(string.Join(" ", errors));
        }
    }
}
