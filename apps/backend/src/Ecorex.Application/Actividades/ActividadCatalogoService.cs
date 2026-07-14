using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Actividades;

/// <summary>
/// Implementacion de <see cref="IActividadCatalogoService"/> (Conceptos de actividades, 000270).
/// Jerarquia de dos niveles Categoria -> Subcategoria. Aislamiento por tenant via filtro global
/// (nunca se filtra a mano por TenantId); el alta estampa el TenantId del contexto. Las relaciones
/// M:N (cargos/terceros) viven en tablas hijas Cascade y se reemplazan en transaccion.
/// </summary>
public sealed class ActividadCatalogoService : IActividadCatalogoService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ActividadCatalogoService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // ---- Categorias ----

    public async Task<IReadOnlyList<ActividadCategoriaDto>> ListCategoriasAsync(
        bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        var cats = await _db.ActividadCategorias.AsNoTracking()
            .Where(c => includeArchived || !c.IsArchived)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Codigo)
            .ToListAsync(cancellationToken);

        // Conteos de subcategorias por categoria (una sola consulta agrupada).
        var counts = await _db.ActividadSubcategorias.AsNoTracking()
            .GroupBy(s => s.CategoriaId)
            .Select(g => new { CategoriaId = g.Key, Total = g.Count(), Activas = g.Count(s => !s.IsArchived) })
            .ToListAsync(cancellationToken);
        var byCat = counts.ToDictionary(x => x.CategoriaId);

        return cats.Select(c =>
        {
            byCat.TryGetValue(c.Id, out var k);
            return new ActividadCategoriaDto(
                c.Id, c.Codigo, c.Nombre, c.Descripcion, c.SortOrder, c.IsArchived,
                k?.Activas ?? 0, k?.Total ?? 0);
        }).ToList();
    }

    public async Task<TaskCoreResult<ActividadCategoriaDto>> CreateCategoriaAsync(
        SaveCategoriaRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ActividadCategoriaDto>.Invalid("No hay tenant activo.");
        }
        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length == 0)
        {
            return TaskCoreResult<ActividadCategoriaDto>.Invalid("El nombre es obligatorio.");
        }
        if (nombre.Length > 150)
        {
            return TaskCoreResult<ActividadCategoriaDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }

        var codigo = await NextCategoriaCodigoAsync(cancellationToken);
        var sortOrder = request.SortOrder
            ?? (await _db.ActividadCategorias.Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        var entity = new ActividadCategoria
        {
            TenantId = tenantId,
            Codigo = codigo,
            Nombre = nombre,
            Descripcion = Normalize(request.Descripcion),
            SortOrder = sortOrder
        };
        _db.ActividadCategorias.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ActividadCategoriaDto>.Ok(
            new ActividadCategoriaDto(entity.Id, entity.Codigo, entity.Nombre, entity.Descripcion,
                entity.SortOrder, entity.IsArchived, 0, 0));
    }

    public async Task<TaskCoreResult<ActividadCategoriaDto>> UpdateCategoriaAsync(
        Guid categoriaId, SaveCategoriaRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActividadCategorias.FirstOrDefaultAsync(c => c.Id == categoriaId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<ActividadCategoriaDto>.NotFound("La categoria no existe.");
        }
        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length == 0)
        {
            return TaskCoreResult<ActividadCategoriaDto>.Invalid("El nombre es obligatorio.");
        }
        if (nombre.Length > 150)
        {
            return TaskCoreResult<ActividadCategoriaDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }

        entity.Nombre = nombre;
        entity.Descripcion = Normalize(request.Descripcion);
        if (request.SortOrder is int so) { entity.SortOrder = so; }
        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.ActividadSubcategorias.CountAsync(s => s.CategoriaId == categoriaId, cancellationToken);
        var activas = await _db.ActividadSubcategorias
            .CountAsync(s => s.CategoriaId == categoriaId && !s.IsArchived, cancellationToken);
        return TaskCoreResult<ActividadCategoriaDto>.Ok(
            new ActividadCategoriaDto(entity.Id, entity.Codigo, entity.Nombre, entity.Descripcion,
                entity.SortOrder, entity.IsArchived, activas, total));
    }

    public async Task<TaskCoreResult<ActividadCategoriaDto>> SetCategoriaArchivedAsync(
        Guid categoriaId, bool archived, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActividadCategorias.FirstOrDefaultAsync(c => c.Id == categoriaId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<ActividadCategoriaDto>.NotFound("La categoria no existe.");
        }
        if (entity.IsArchived == archived)
        {
            return TaskCoreResult<ActividadCategoriaDto>.Invalid(archived
                ? "La categoria ya esta archivada."
                : "La categoria no esta archivada.");
        }
        entity.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.ActividadSubcategorias.CountAsync(s => s.CategoriaId == categoriaId, cancellationToken);
        var activas = await _db.ActividadSubcategorias
            .CountAsync(s => s.CategoriaId == categoriaId && !s.IsArchived, cancellationToken);
        return TaskCoreResult<ActividadCategoriaDto>.Ok(
            new ActividadCategoriaDto(entity.Id, entity.Codigo, entity.Nombre, entity.Descripcion,
                entity.SortOrder, entity.IsArchived, activas, total));
    }

    // ---- Subcategorias ----

    public async Task<IReadOnlyList<ActividadSubcategoriaDto>> ListSubcategoriasAsync(
        Guid? categoriaId = null, bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        var query = _db.ActividadSubcategorias.AsNoTracking()
            .Include(s => s.Cargos)
            .Include(s => s.Terceros)
            .Include(s => s.Notificaciones)
            .AsQueryable();
        if (categoriaId is Guid cid) { query = query.Where(s => s.CategoriaId == cid); }
        if (!includeArchived) { query = query.Where(s => !s.IsArchived); }

        var rows = await query
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Nombre)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ActividadSubcategoriaDto?> GetSubcategoriaAsync(
        Guid subcategoriaId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActividadSubcategorias.AsNoTracking()
            .Include(s => s.Cargos)
            .Include(s => s.Terceros)
            .Include(s => s.Notificaciones)
            .FirstOrDefaultAsync(s => s.Id == subcategoriaId, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<TaskCoreResult<ActividadSubcategoriaDto>> CreateSubcategoriaAsync(
        SaveSubcategoriaRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.Invalid("No hay tenant activo.");
        }
        var validation = await ValidateSubcategoriaAsync(request, cancellationToken);
        if (validation is string error)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.Invalid(error);
        }

        var categoria = await _db.ActividadCategorias.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CategoriaId, cancellationToken);
        if (categoria is null)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.Invalid("La categoria destino no existe.");
        }

        var codigo = await NextSubcategoriaCodigoAsync(categoria.Codigo, cancellationToken);
        var sortOrder = request.SortOrder
            ?? (await _db.ActividadSubcategorias.Where(s => s.CategoriaId == request.CategoriaId)
                    .Select(s => (int?)s.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        var entity = new ActividadSubcategoria
        {
            TenantId = tenantId,
            CategoriaId = request.CategoriaId,
            Codigo = codigo,
            SortOrder = sortOrder
        };
        ApplyRequest(entity, request, tenantId);
        SyncCargos(entity, request.CargoIds, tenantId);
        SyncTerceros(entity, request.TerceroIds, tenantId);
        SyncNotificaciones(entity, request.NotificacionUserIds, tenantId);
        // Coherencia: cada concepto tiene su tablero. Si no se eligio uno, se crea y enlaza.
        await EnsureConceptBoardAsync(entity, tenantId, cancellationToken);

        // Alta con relaciones hijas: una sola operacion atomica (SaveChanges inserta padre e hijas).
        _db.ActividadSubcategorias.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var saved = await GetSubcategoriaAsync(entity.Id, cancellationToken);
        return TaskCoreResult<ActividadSubcategoriaDto>.Ok(saved!);
    }

    public async Task<TaskCoreResult<ActividadSubcategoriaDto>> UpdateSubcategoriaAsync(
        Guid subcategoriaId, SaveSubcategoriaRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.Invalid("No hay tenant activo.");
        }
        var entity = await _db.ActividadSubcategorias
            .Include(s => s.Cargos)
            .Include(s => s.Terceros)
            .Include(s => s.Notificaciones)
            .FirstOrDefaultAsync(s => s.Id == subcategoriaId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.NotFound("La subcategoria no existe.");
        }
        var validation = await ValidateSubcategoriaAsync(request, cancellationToken);
        if (validation is string error)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.Invalid(error);
        }
        if (request.CategoriaId != entity.CategoriaId
            && !await _db.ActividadCategorias.AnyAsync(c => c.Id == request.CategoriaId, cancellationToken))
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.Invalid("La categoria destino no existe.");
        }

        entity.CategoriaId = request.CategoriaId;
        if (request.SortOrder is int so) { entity.SortOrder = so; }
        ApplyRequest(entity, request, tenantId);

        // Reemplazo de relaciones M:N: se borran las viejas y se agregan las nuevas en el mismo
        // SaveChanges (transaccion implicita). Las FK hijas son Cascade sobre la subcategoria.
        SyncCargos(entity, request.CargoIds, tenantId);
        SyncTerceros(entity, request.TerceroIds, tenantId);
        SyncNotificaciones(entity, request.NotificacionUserIds, tenantId);
        // Coherencia: si tras editar el concepto queda sin tablero, se le crea y enlaza uno dedicado.
        await EnsureConceptBoardAsync(entity, tenantId, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var saved = await GetSubcategoriaAsync(entity.Id, cancellationToken);
        return TaskCoreResult<ActividadSubcategoriaDto>.Ok(saved!);
    }

    public async Task<TaskCoreResult<bool>> DeleteSubcategoriaAsync(
        Guid subcategoriaId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActividadSubcategorias
            .Include(s => s.Cargos)
            .Include(s => s.Terceros)
            .Include(s => s.Notificaciones)
            .FirstOrDefaultAsync(s => s.Id == subcategoriaId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<bool>.NotFound("La subcategoria no existe.");
        }

        // Borrado del concepto + sus relaciones hijas. Se quitan las hijas explicitamente y se
        // remueve la subcategoria en el mismo SaveChanges (operacion multi-tabla atomica).
        _db.ActividadSubcategoriaCargos.RemoveRange(entity.Cargos);
        _db.ActividadSubcategoriaTerceros.RemoveRange(entity.Terceros);
        _db.ActividadSubcategoriaNotificaciones.RemoveRange(entity.Notificaciones);
        _db.ActividadSubcategorias.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<ActividadSubcategoriaDto>> SetSubcategoriaArchivedAsync(
        Guid subcategoriaId, bool archived, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ActividadSubcategorias.FirstOrDefaultAsync(s => s.Id == subcategoriaId, cancellationToken);
        if (entity is null)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.NotFound("La subcategoria no existe.");
        }
        if (entity.IsArchived == archived)
        {
            return TaskCoreResult<ActividadSubcategoriaDto>.Invalid(archived
                ? "La subcategoria ya esta archivada."
                : "La subcategoria no esta archivada.");
        }
        entity.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);
        var saved = await GetSubcategoriaAsync(entity.Id, cancellationToken);
        return TaskCoreResult<ActividadSubcategoriaDto>.Ok(saved!);
    }

    // ---- Combos + KPIs ----

    public async Task<ActividadComboOptionsDto> GetComboOptionsAsync(CancellationToken cancellationToken = default)
    {
        var workflows = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(w => w.IsPublished && !w.IsArchived)
            .OrderBy(w => w.Name).ThenBy(w => w.ProcessCode)
            .Select(w => new WorkflowOptionDto(w.Id, w.ProcessCode, w.Name, w.Version))
            .ToListAsync(cancellationToken);

        var forms = await _db.FormDefinitions.AsNoTracking()
            .Where(f => !f.IsArchived)
            .OrderBy(f => f.Title).ThenBy(f => f.Code)
            .Select(f => new FormOptionDto(f.Id, f.Code, f.Title))
            .ToListAsync(cancellationToken);

        var boardRows = await _db.TaskBoards.AsNoTracking()
            .Where(b => !b.IsArchived)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(cancellationToken);
        var boardIds = boardRows.Select(b => b.Id).ToList();
        var columns = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.BoardId, c.Name, c.IsDone })
            .ToListAsync(cancellationToken);
        var boards = boardRows.Select(b => new BoardOptionDto(
            b.Id, b.Name,
            columns.Where(c => c.BoardId == b.Id)
                .Select(c => new BoardColumnOptionDto(c.Id, c.Name, c.IsDone)).ToList())).ToList();

        var cargos = await _db.OrgUnits.AsNoTracking()
            .Where(o => o.Classifier == OrgUnitClassifier.Cargo && !o.IsArchived)
            .OrderBy(o => o.Name)
            .Select(o => new CargoOptionDto(o.Id, o.Name))
            .ToListAsync(cancellationToken);

        var terceros = await _db.Terceros.AsNoTracking()
            .Where(t => t.Estado != TerceroEstado.Inactivo && t.EmpresaId == null)
            .OrderBy(t => t.Nombre)
            .Select(t => new TerceroOptionDto(t.Id, t.Nombre))
            .ToListAsync(cancellationToken);

        var usuarios = await _db.TenantUsers.AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UsuarioOptionDto(u.Id, u.Email))
            .ToListAsync(cancellationToken);

        return new ActividadComboOptionsDto(workflows, forms, boards, cargos, terceros, usuarios);
    }

    public async Task<IReadOnlyList<Guid>> ListEncargadoUserIdsAsync(
        IReadOnlyList<Guid> cargoIds, CancellationToken cancellationToken = default)
    {
        if (cargoIds is null || cargoIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }
        var ids = cargoIds.Distinct().ToList();
        var users = new HashSet<Guid>();

        // Funcionarios que ocupan esos cargos (cuelgan del cargo con un TenantUserId).
        var occupants = await _db.OrgUnits.AsNoTracking()
            .Where(o => o.ParentId != null && ids.Contains(o.ParentId.Value)
                && o.Classifier == OrgUnitClassifier.Funcionario && o.TenantUserId != null && !o.IsArchived)
            .Select(o => o.TenantUserId!.Value)
            .ToListAsync(cancellationToken);
        foreach (var u in occupants) { users.Add(u); }

        // Miembros directos de esos cargos.
        var members = await _db.OrgUnitMembers.AsNoTracking()
            .Where(m => ids.Contains(m.OrgUnitId))
            .Select(m => m.TenantUserId)
            .ToListAsync(cancellationToken);
        foreach (var u in members) { users.Add(u); }

        // Responsable/jefe de la unidad del cargo (PRE-4).
        var responsibles = await _db.OrgUnits.AsNoTracking()
            .Where(o => ids.Contains(o.Id) && o.ResponsibleTenantUserId != null)
            .Select(o => o.ResponsibleTenantUserId!.Value)
            .ToListAsync(cancellationToken);
        foreach (var u in responsibles) { users.Add(u); }

        return users.ToList();
    }

    public async Task<ActividadKpisDto> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        var categorias = await _db.ActividadCategorias.CountAsync(c => !c.IsArchived, cancellationToken);
        var subs = _db.ActividadSubcategorias.AsNoTracking().Where(s => !s.IsArchived);
        var subcategorias = await subs.CountAsync(cancellationToken);
        var conCliente = await subs.CountAsync(s => s.RequiereCliente, cancellationToken);
        var autoInicio = await subs.CountAsync(s => s.IniciaModulo, cancellationToken);
        return new ActividadKpisDto(categorias, subcategorias, conCliente, autoInicio);
    }

    // ---- Demo seed ----

    public async Task EnsureConceptosDemoAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return;
        }
        if (await _db.ActividadCategorias.AnyAsync(cancellationToken))
        {
            return;
        }

        var demo = new (string Codigo, string Nombre, string Desc, (string Nombre, string Chequeo, bool Cli, bool Ini, bool Cierre, string Titulo, string Detalle)[] Subs)[]
        {
            ("CAT-01", "Comercial", "Cotizaciones, seguimiento y cierre de negocios.", new[]
            {
                ("Requerimiento infraestructura", "Revisar sitio;Medir red;Cotizar equipos;Aprobar propuesta;Programar instalacion", true, true, true, "Requerimiento infra - @cliente", "Levantamiento tecnico de infraestructura en sitio del cliente."),
                ("Cotizacion de equipos", "Verificar stock;Aplicar lista de precios;Generar PDF", true, false, true, "", ""),
                ("Seguimiento postventa", "Llamar cliente;Registrar satisfaccion", true, false, false, "", "")
            }),
            ("CAT-02", "Operaciones", "Instalaciones, mantenimiento y soporte en sitio.", new[]
            {
                ("Visita tecnica", "Confirmar cita;Diagnostico;Ejecutar;Acta de servicio;Firma cliente;Cierre", true, true, true, "Visita tecnica - @cliente", "Atencion en sitio programada por el sistema."),
                ("Mantenimiento preventivo", "Checklist equipo;Limpieza;Reporte", false, false, true, "", "")
            }),
            ("CAT-03", "Gestion Humana", "Contratacion, permisos y evaluaciones.", new[]
            {
                ("Contratacion", "Solicitar documentos;Examen medico;Firmar contrato;Afiliaciones;Induccion;Entrega dotacion;Crear usuario;Activar nomina", false, true, false, "Contratacion - @cargo", "Proceso de vinculacion de nuevo funcionario."),
                ("Permiso laboral", "Registrar solicitud;Aprobar jefe", false, false, false, "", "")
            }),
            ("CAT-04", "Financiera", "Pagos, conciliaciones y facturacion.", new[]
            {
                ("Pago a proveedor", "Validar factura;Aprobar;Programar pago", false, true, false, "Pago proveedor - @tercero", "Causacion y programacion de pago.")
            })
        };

        var catOrder = 0;
        foreach (var (codigo, nombre, desc, subs) in demo)
        {
            var categoria = new ActividadCategoria
            {
                TenantId = tenantId,
                Codigo = codigo,
                Nombre = nombre,
                Descripcion = desc,
                SortOrder = catOrder++
            };
            var subOrder = 0;
            foreach (var s in subs)
            {
                categoria.Subcategorias.Add(new ActividadSubcategoria
                {
                    TenantId = tenantId,
                    Codigo = $"{codigo}-{(subOrder + 1):00}",
                    Nombre = s.Nombre,
                    Chequeo = string.IsNullOrEmpty(s.Chequeo) ? null : s.Chequeo,
                    SortOrder = subOrder++,
                    RequiereCliente = s.Cli,
                    IniciaModulo = s.Ini,
                    CierreManual = s.Cierre,
                    TituloAuto = string.IsNullOrEmpty(s.Titulo) ? null : s.Titulo,
                    DetalleAuto = string.IsNullOrEmpty(s.Detalle) ? null : s.Detalle
                });
            }
            _db.ActividadCategorias.Add(categoria);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---- Helpers ----

    private void ApplyRequest(ActividadSubcategoria entity, SaveSubcategoriaRequest request, Guid tenantId)
    {
        entity.Nombre = (request.Nombre ?? "").Trim();
        entity.Chequeo = NormalizeChequeo(request.Chequeo);
        entity.Descripcion = Normalize(request.Descripcion);
        entity.RequiereCliente = request.RequiereCliente;
        entity.IniciaModulo = request.IniciaModulo;
        entity.CierreManual = request.CierreManual;
        // Titulo/Detalle automaticos solo tienen sentido si inicia modulo; se conservan igual.
        entity.TituloAuto = Normalize(request.TituloAuto);
        entity.DetalleAuto = Normalize(request.DetalleAuto);
        entity.WorkflowDefinitionId = request.WorkflowDefinitionId;
        entity.FormDefinitionId = request.FormDefinitionId;
        entity.TaskBoardId = request.TaskBoardId;
        // La columna terminal solo aplica si hay tablero; si no, se limpia.
        entity.TaskBoardColumnId = request.TaskBoardId is null ? null : request.TaskBoardColumnId;
        entity.Sedes = NormalizeSedes(request.Sedes);
    }

    /// <summary>
    /// Coherencia concepto&lt;-&gt;tablero (1:1): si el concepto queda SIN tablero, crea uno dedicado
    /// (columnas default del prototipo + code CNC-) y lo enlaza, con la columna "Completado" como estado
    /// de cierre. Se agrega al mismo DbContext -> se persiste en el SaveChanges del alta/edicion. Asi la
    /// creacion del tablero vive DENTRO de Conceptos y ningun concepto queda huerfano de tablero.
    /// </summary>
    private async Task EnsureConceptBoardAsync(
        ActividadSubcategoria entity, Guid tenantId, CancellationToken cancellationToken)
    {
        if (entity.TaskBoardId is not null) { return; }

        var nextOrder = (await _db.TaskBoards.Select(b => (int?)b.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var board = new TaskBoard
        {
            TenantId = tenantId,
            Kind = TaskBoardKind.Activities,
            Name = string.IsNullOrWhiteSpace(entity.Nombre) ? "Tablero" : entity.Nombre,
            Status = TaskBoardStatus.OnTime,
            SortOrder = nextOrder
        };
        board.Code = "CNC-" + board.Id.ToString("N")[..12].ToUpperInvariant();
        _db.TaskBoards.Add(board);

        Guid? doneColumnId = null;
        for (int i = 0; i < TaskBoardService.DefaultColumns.Length; i++)
        {
            var (cname, ccolor, isDone) = TaskBoardService.DefaultColumns[i];
            var column = new TaskBoardColumn
            {
                TenantId = tenantId,
                BoardId = board.Id,
                Name = cname,
                Color = ccolor,
                SortOrder = i,
                IsDone = isDone
            };
            _db.TaskBoardColumns.Add(column);
            if (isDone) { doneColumnId = column.Id; }
        }

        entity.TaskBoardId = board.Id;
        entity.TaskBoardColumnId = doneColumnId;
    }

    private void SyncNotificaciones(ActividadSubcategoria entity, IReadOnlyList<Guid>? userIds, Guid tenantId)
    {
        _db.ActividadSubcategoriaNotificaciones.RemoveRange(entity.Notificaciones);
        entity.Notificaciones.Clear();
        if (userIds is null) { return; }
        foreach (var id in userIds.Distinct())
        {
            _db.ActividadSubcategoriaNotificaciones.Add(new ActividadSubcategoriaNotificacion
            {
                TenantId = tenantId,
                SubcategoriaId = entity.Id,
                TenantUserId = id
            });
        }
    }

    // Los hijos M:N se AGREGAN via DbSet (estado Added -> INSERT), no via la coleccion de
    // navegacion: agregar por navegacion con PK ya asignada hace que EF los trate como
    // Modified y genere un UPDATE espurio (DbUpdateConcurrencyException). Para el borrado si
    // se usa la coleccion cargada (Include) con RemoveRange.
    private void SyncCargos(ActividadSubcategoria entity, IReadOnlyList<Guid>? cargoIds, Guid tenantId)
    {
        _db.ActividadSubcategoriaCargos.RemoveRange(entity.Cargos);
        entity.Cargos.Clear();
        if (cargoIds is null) { return; }
        foreach (var id in cargoIds.Distinct())
        {
            _db.ActividadSubcategoriaCargos.Add(new ActividadSubcategoriaCargo
            {
                TenantId = tenantId,
                SubcategoriaId = entity.Id,
                OrgUnitId = id
            });
        }
    }

    private void SyncTerceros(ActividadSubcategoria entity, IReadOnlyList<Guid>? terceroIds, Guid tenantId)
    {
        _db.ActividadSubcategoriaTerceros.RemoveRange(entity.Terceros);
        entity.Terceros.Clear();
        if (terceroIds is null) { return; }
        foreach (var id in terceroIds.Distinct())
        {
            _db.ActividadSubcategoriaTerceros.Add(new ActividadSubcategoriaTercero
            {
                TenantId = tenantId,
                SubcategoriaId = entity.Id,
                TerceroId = id
            });
        }
    }

    private async Task<string?> ValidateSubcategoriaAsync(
        SaveSubcategoriaRequest request, CancellationToken cancellationToken)
    {
        var nombre = (request.Nombre ?? "").Trim();
        if (nombre.Length == 0) { return "El nombre es obligatorio."; }
        if (nombre.Length > 200) { return "El nombre no puede superar 200 caracteres."; }

        if (request.WorkflowDefinitionId is Guid wid
            && !await _db.WorkflowDefinitions.AnyAsync(w => w.Id == wid, cancellationToken))
        {
            return "El flujo vinculado no existe.";
        }
        if (request.FormDefinitionId is Guid fid
            && !await _db.FormDefinitions.AnyAsync(f => f.Id == fid, cancellationToken))
        {
            return "El formulario vinculado no existe.";
        }
        if (request.TaskBoardId is Guid bid)
        {
            if (!await _db.TaskBoards.AnyAsync(b => b.Id == bid, cancellationToken))
            {
                return "El tablero vinculado no existe.";
            }
            if (request.TaskBoardColumnId is Guid colId
                && !await _db.TaskBoardColumns.AnyAsync(c => c.Id == colId && c.BoardId == bid, cancellationToken))
            {
                return "El estado terminal no pertenece al tablero seleccionado.";
            }
        }
        return null;
    }

    /// <summary>Proximo codigo de categoria "CAT-NN" libre para el tenant activo.</summary>
    private async Task<string> NextCategoriaCodigoAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.ActividadCategorias.AsNoTracking()
            .Select(c => c.Codigo).ToListAsync(cancellationToken);
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (var n = 1; n < 10000; n++)
        {
            var candidate = $"CAT-{n:00}";
            if (!used.Contains(candidate)) { return candidate; }
        }
        return $"CAT-{Guid.NewGuid():N}";
    }

    /// <summary>Proximo codigo de subcategoria "{catCodigo}-NN" libre para el tenant activo.</summary>
    private async Task<string> NextSubcategoriaCodigoAsync(string categoriaCodigo, CancellationToken cancellationToken)
    {
        var existing = await _db.ActividadSubcategorias.AsNoTracking()
            .Select(s => s.Codigo).ToListAsync(cancellationToken);
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (var n = 1; n < 10000; n++)
        {
            var candidate = $"{categoriaCodigo}-{n:00}";
            if (!used.Contains(candidate)) { return candidate; }
        }
        return $"{categoriaCodigo}-{Guid.NewGuid():N}";
    }

    private static ActividadSubcategoriaDto ToDto(ActividadSubcategoria s) => new(
        s.Id, s.CategoriaId, s.Codigo, s.Nombre, s.Chequeo, s.Descripcion, s.SortOrder, s.IsArchived,
        s.RequiereCliente, s.IniciaModulo, s.CierreManual, s.TituloAuto, s.DetalleAuto,
        s.WorkflowDefinitionId, s.FormDefinitionId, s.TaskBoardId, s.TaskBoardColumnId,
        s.Cargos.Select(c => c.OrgUnitId).ToList(),
        s.Terceros.Select(t => t.TerceroId).ToList(),
        SplitSedes(s.Sedes),
        s.Notificaciones.Select(n => n.TenantUserId).ToList());

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Normaliza la lista de sedes (nombres libres): recorta, descarta vacios y duplicados,
    /// une con ';'.</summary>
    private static string? NormalizeSedes(IReadOnlyList<string>? sedes)
    {
        if (sedes is null) { return null; }
        var items = sedes
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return items.Count == 0 ? null : string.Join(";", items);
    }

    private static IReadOnlyList<string> SplitSedes(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Normaliza la lista de chequeo: recorta cada item y descarta vacios, une con ';'.</summary>
    private static string? NormalizeChequeo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var items = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Length == 0 ? null : string.Join(";", items);
    }
}
