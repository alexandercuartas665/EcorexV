using Ecorex.Application.Actividades;
using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Requerimientos;

public sealed class RequerimientoConfigService : IRequerimientoConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IActividadCatalogoService _catalogo;
    private readonly IFormDefinitionService _forms;

    public RequerimientoConfigService(IApplicationDbContext db, ITenantContext tenant,
        IActividadCatalogoService catalogo, IFormDefinitionService forms)
    {
        _db = db;
        _tenant = tenant;
        _catalogo = catalogo;
        _forms = forms;
    }

    public async Task<IReadOnlyList<RequerimientoOrigenDto>> ListOrigenesAsync(CancellationToken ct = default)
    {
        var origenes = await _db.RequerimientoOrigenes.AsNoTracking().OrderBy(o => o.Nombre).ToListAsync(ct);
        var (subs, forms) = await NombresAsync(ct);
        return origenes.Select(o => Map(o, subs, forms)).ToList();
    }

    public async Task<RequerimientoOrigenDto?> SaveOrigenAsync(SaveRequerimientoOrigenRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var name = (req.Nombre ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { throw new InvalidOperationException("El nombre del origen es obligatorio."); }

        var clash = await _db.RequerimientoOrigenes
            .AnyAsync(o => o.Nombre == name && (req.Id == null || o.Id != req.Id), ct);
        if (clash) { throw new InvalidOperationException($"Ya existe un origen '{name}'."); }

        RequerimientoOrigen entity;
        if (req.Id is Guid id)
        {
            var existing = await _db.RequerimientoOrigenes.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new RequerimientoOrigen { TenantId = tenantId };
            _db.RequerimientoOrigenes.Add(entity);
        }

        entity.Nombre = name;
        entity.Area = (req.Area ?? "").Trim();
        entity.TipoFuente = req.TipoFuente;
        // La fuente solo aplica cuando el tipo la usa (Formulario/Contenedor); en Manual se limpia.
        entity.FuenteRefId = req.TipoFuente == Domain.Enums.RequerimientoFuenteTipo.Manual ? null : req.FuenteRefId;
        entity.SubcategoriaCompraId = req.SubcategoriaCompraId;
        entity.SubcategoriaCotizacionId = req.SubcategoriaCotizacionId;
        entity.Activo = req.Activo;
        await _db.SaveChangesAsync(ct);

        var (subs, forms) = await NombresAsync(ct);
        return Map(entity, subs, forms);
    }

    public async Task<bool> DeleteOrigenAsync(Guid id, CancellationToken ct = default)
    {
        var o = await _db.RequerimientoOrigenes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) { return false; }
        _db.RequerimientoOrigenes.Remove(o);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Modo de trabajo del modulo (Consolidado/Dividido): config por tenant en TenantConfiguration ----
    private const string ModoKey = "requerimientos.modo";

    public async Task<RequerimientoModo> GetModoAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ModoKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        return Enum.TryParse<RequerimientoModo>(val, ignoreCase: true, out var m) ? m : RequerimientoModo.Consolidado;
    }

    public async Task SetModoAsync(RequerimientoModo modo, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == ModoKey, ct);
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = ModoKey, ConfigValue = modo.ToString() });
        }
        else
        {
            row.ConfigValue = modo.ToString();
        }
        await _db.SaveChangesAsync(ct);
    }

    // ---- Agente evaluador de cotizaciones (modulo de agentes): config por tenant en TenantConfiguration ----
    private const string AgenteCotizacionesKey = "requerimientos.agente_cotizaciones";

    public async Task<Guid?> GetAgenteCotizacionesAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == AgenteCotizacionesKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        return Guid.TryParse(val, out var id) ? id : null;
    }

    public async Task SetAgenteCotizacionesAsync(Guid? agentId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var value = agentId is Guid g && g != Guid.Empty ? g.ToString() : "";
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == AgenteCotizacionesKey, ct);
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = AgenteCotizacionesKey, ConfigValue = value });
        }
        else
        {
            row.ConfigValue = value;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListAgentesAsync(CancellationToken ct = default)
        => await _db.AiAgents.AsNoTracking().Where(a => a.IsActive)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new RequerimientoOpcionDto(a.Id, a.Name)).ToListAsync(ct);

    // ---- Catalogo de unidades de medida: config por tenant en TenantConfiguration (una por linea) ----
    private const string UnidadesKey = "requerimientos.unidades";

    public async Task<IReadOnlyList<string>> GetUnidadesAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == UnidadesKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        var list = ParseUnidades(val);
        return list.Count > 0 ? list : RequerimientoUnidades.Comunes;
    }

    public async Task SetUnidadesAsync(IEnumerable<string> unidades, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        // Normaliza: recorta, descarta vacias y deduplica (insensible a mayusculas) conservando el orden.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<string>();
        foreach (var u in unidades ?? Enumerable.Empty<string>())
        {
            var t = (u ?? "").Trim();
            if (t.Length > 0 && seen.Add(t)) { clean.Add(t); }
        }
        var value = string.Join('\n', clean);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == UnidadesKey, ct);
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = UnidadesKey, ConfigValue = value });
        }
        else
        {
            row.ConfigValue = value;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static List<string> ParseUnidades(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListSubcategoriasAsync(CancellationToken ct = default)
        => (await _catalogo.ListSubcategoriasAsync(null, includeArchived: false, ct))
            .OrderBy(s => s.Nombre)
            .Select(s => new RequerimientoOpcionDto(s.Id, s.Nombre)).ToList();

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListFormulariosAsync(CancellationToken ct = default)
        => (await _forms.ListAsync(includeArchived: false, ct))
            .OrderBy(f => f.Title)
            .Select(f => new RequerimientoOpcionDto(f.Id, f.Title)).ToList();

    private async Task<(Dictionary<Guid, string> Subs, Dictionary<Guid, string> Forms)> NombresAsync(CancellationToken ct)
    {
        var subs = (await _catalogo.ListSubcategoriasAsync(null, includeArchived: true, ct))
            .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Nombre);
        var forms = (await _forms.ListAsync(includeArchived: true, ct))
            .GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First().Title);
        return (subs, forms);
    }

    // ---- Ola 4: matriz de proveedores ----

    // Extension de la matriz SIN migracion: condiciones extra (AND) y dias de vigencia por regla, y dias de
    // vigencia default por proveedor. Se guarda como un JSON en TenantConfiguration (clave MatrizExtKey).
    private const string MatrizExtKey = "requerimientos.matriz_ext";
    private sealed class MatrizExt
    {
        public Dictionary<string, ReglaExt> Reglas { get; set; } = new();
        public Dictionary<string, int> Prov { get; set; } = new();
        // Ola D: proceso de cotizacion por proveedor (proveedorId -> subcategoria Guid como texto).
        public Dictionary<string, string> ProvCot { get; set; } = new();
    }
    private sealed class ReglaExt
    {
        // Condiciones EXTRA (ademas de la primaria que vive en la entidad): [dimensionName, valorGuid].
        public List<string[]> Cond { get; set; } = new();
        public int? Vig { get; set; }
        // Ola F: proceso de cotizacion (subcategoria Guid como texto) del proveedor de ESTA fila-regla; null =
        // usa la precedencia del modulo. Una regla = varias filas (una por proveedor), cada una con su Proc.
        public string? Proc { get; set; }
        // Proceso de COMPRA (subcategoria Guid como texto) del proveedor de ESTA fila-regla; null = usa la
        // precedencia del modulo (matriz > origen > default de compra). Espejo de Proc, para el planificador.
        public string? ProcComp { get; set; }
    }

    private async Task<MatrizExt> LoadExtAsync(CancellationToken ct)
    {
        var val = await _db.TenantConfigurations.AsNoTracking().Where(c => c.ConfigKey == MatrizExtKey)
            .Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(val)) { return new MatrizExt(); }
        try { return System.Text.Json.JsonSerializer.Deserialize<MatrizExt>(val!) ?? new MatrizExt(); }
        catch { return new MatrizExt(); }
    }

    private async Task PersistExtAsync(MatrizExt ext, Guid tid, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ext);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == MatrizExtKey, ct);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = MatrizExtKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }

    private static RequerimientoDimension ParseDim(string s)
        => Enum.TryParse<RequerimientoDimension>(s, out var d) ? d : RequerimientoDimension.Grupo;

    public async Task<IReadOnlyList<RequerimientoReglaDto>> ListReglasAsync(CancellationToken ct = default)
    {
        var reglas = await _db.RequerimientoProveedorReglas.AsNoTracking()
            .OrderBy(r => r.Dimension).ThenBy(r => r.Prioridad).ToListAsync(ct);
        if (reglas.Count == 0) { return new List<RequerimientoReglaDto>(); }

        var ext = await LoadExtAsync(ct);

        var provIds = reglas.Select(r => r.ProveedorId).Distinct().ToList();
        var provs = await _db.Terceros.AsNoTracking().Where(t => provIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        // Nombres de los procesos de cotizacion (subcategorias) por fila-regla (Ola F).
        var (subNombres, _) = await NombresAsync(ct);

        // Todas las condiciones (primaria + extras) por dimension, para resolver nombres en lote.
        var pares = new HashSet<(RequerimientoDimension, string)>();
        foreach (var r in reglas)
        {
            pares.Add((r.Dimension, r.ValorDimension));
            if (ext.Reglas.TryGetValue(r.Id.ToString(), out var re))
            {
                foreach (var c in re.Cond)
                {
                    if (c.Length == 2) { pares.Add((ParseDim(c[0]), c[1])); }
                }
            }
        }
        var nombres = new Dictionary<(RequerimientoDimension, string), string>();
        foreach (var grp in pares.GroupBy(p => p.Item1))
        {
            var guids = grp.Select(p => p.Item2).Distinct().Where(v => Guid.TryParse(v, out _)).Select(Guid.Parse).ToList();
            var cat = await ValoresCatalogoAsync(grp.Key, guids, ct);
            foreach (var p in grp)
            {
                if (Guid.TryParse(p.Item2, out var g) && cat.TryGetValue(g, out var nm)) { nombres[(grp.Key, p.Item2)] = nm; }
            }
        }
        string? Nom(RequerimientoDimension d, string v) => nombres.TryGetValue((d, v), out var n) ? n : v;

        return reglas.Select(r =>
        {
            var cond = new List<ReglaCondicionResueltaDto> { new(r.Dimension, r.ValorDimension, Nom(r.Dimension, r.ValorDimension)) };
            int? vig = null;
            Guid? proc = null;
            Guid? procComp = null;
            if (ext.Reglas.TryGetValue(r.Id.ToString(), out var re))
            {
                foreach (var c in re.Cond)
                {
                    if (c.Length == 2) { var d = ParseDim(c[0]); cond.Add(new(d, c[1], Nom(d, c[1]))); }
                }
                vig = re.Vig;
                if (Guid.TryParse(re.Proc, out var pg)) { proc = pg; }
                if (Guid.TryParse(re.ProcComp, out var pgc)) { procComp = pgc; }
            }
            return new RequerimientoReglaDto(
                r.Id, r.Dimension, r.ValorDimension, Nom(r.Dimension, r.ValorDimension),
                r.ProveedorId, provs.TryGetValue(r.ProveedorId, out var pn) ? pn : null,
                r.Prioridad, r.Aprendida, r.Activo, cond, vig,
                proc, proc is Guid pc && subNombres.TryGetValue(pc, out var scn) ? scn : null,
                procComp, procComp is Guid pcc && subNombres.TryGetValue(pcc, out var scnc) ? scnc : null);
        }).ToList();
    }

    public async Task<RequerimientoReglaDto?> SaveReglaAsync(SaveReglaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var cond = (req.Condiciones ?? new List<ReglaCondicion>())
            .Select(c => new ReglaCondicion(c.Dimension, (c.Valor ?? "").Trim()))
            .Where(c => !string.IsNullOrWhiteSpace(c.Valor)).ToList();
        if (cond.Count == 0) { throw new InvalidOperationException("Agrega al menos una condicion (dimension + valor)."); }
        if (req.ProveedorId == Guid.Empty) { throw new InvalidOperationException("Elige un proveedor."); }

        var primaria = cond[0];
        RequerimientoProveedorRegla entity;
        if (req.Id is Guid id)
        {
            var existing = await _db.RequerimientoProveedorReglas.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new RequerimientoProveedorRegla { TenantId = tenantId };
            _db.RequerimientoProveedorReglas.Add(entity);
        }
        entity.Dimension = primaria.Dimension;
        entity.ValorDimension = primaria.Valor;
        entity.ProveedorId = req.ProveedorId;
        entity.Prioridad = req.Prioridad;
        entity.Activo = req.Activo;
        await _db.SaveChangesAsync(ct);

        // Condiciones extra (de la 2a en adelante) + vigencia -> blob de extension.
        var ext = await LoadExtAsync(ct);
        var re = new ReglaExt
        {
            Cond = cond.Skip(1).Select(c => new[] { c.Dimension.ToString(), c.Valor }).ToList(),
            Vig = req.VigenciaDias,
            Proc = req.SubcategoriaCotizacionId is Guid sc && sc != Guid.Empty ? sc.ToString() : null,
            ProcComp = req.SubcategoriaCompraId is Guid scc && scc != Guid.Empty ? scc.ToString() : null
        };
        if (re.Cond.Count == 0 && re.Vig is null && re.Proc is null && re.ProcComp is null) { ext.Reglas.Remove(entity.Id.ToString()); }
        else { ext.Reglas[entity.Id.ToString()] = re; }
        await PersistExtAsync(ext, tenantId, ct);

        return (await ListReglasAsync(ct)).FirstOrDefault(r => r.Id == entity.Id);
    }

    public async Task<bool> DeleteReglaAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.RequerimientoProveedorReglas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return false; }
        _db.RequerimientoProveedorReglas.Remove(r);
        await _db.SaveChangesAsync(ct);
        if (_tenant.TenantId is Guid tid)
        {
            var ext = await LoadExtAsync(ct);
            if (ext.Reglas.Remove(id.ToString())) { await PersistExtAsync(ext, tid, ct); }
        }
        return true;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetProveedorVigenciaMapAsync(CancellationToken ct = default)
    {
        var ext = await LoadExtAsync(ct);
        var map = new Dictionary<Guid, int>();
        foreach (var kv in ext.Prov)
        {
            if (Guid.TryParse(kv.Key, out var g)) { map[g] = kv.Value; }
        }
        return map;
    }

    public async Task SetProveedorVigenciaAsync(Guid proveedorId, int? dias, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var ext = await LoadExtAsync(ct);
        if (dias is int d && d > 0) { ext.Prov[proveedorId.ToString()] = d; }
        else { ext.Prov.Remove(proveedorId.ToString()); }
        await PersistExtAsync(ext, tid, ct);
    }

    // ---- Punto #3: scorecard de confiabilidad del proveedor (calificacion manual) ----
    // Se guardan los eventos de calificacion en TenantConfiguration (sin migracion); el score se agrega en memoria.
    private const string RatingsKey = "requerimientos.proveedor_ratings";
    private sealed class RatingStore { public List<RatingEvent> Events { get; set; } = new(); }
    private sealed class RatingEvent
    {
        public string P { get; set; } = "";   // proveedorId
        public int Pu { get; set; }            // puntualidad 1-5
        public int Ca { get; set; }            // calidad 1-5
        public string? N { get; set; }         // nota
        public string At { get; set; } = "";   // fecha (ISO)
        public string? By { get; set; }        // userId que califico
    }

    private async Task<RatingStore> LoadRatingsAsync(CancellationToken ct)
    {
        var val = await _db.TenantConfigurations.AsNoTracking().Where(c => c.ConfigKey == RatingsKey)
            .Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(val)) { return new RatingStore(); }
        try { return System.Text.Json.JsonSerializer.Deserialize<RatingStore>(val!) ?? new RatingStore(); }
        catch { return new RatingStore(); }
    }

    private static ProveedorScoreDto Aggregate(Guid prov, IReadOnlyList<RatingEvent> evs)
    {
        var n = evs.Count;
        var pu = Math.Round((decimal)evs.Average(e => e.Pu), 1);
        var ca = Math.Round((decimal)evs.Average(e => e.Ca), 1);
        var gen = Math.Round((pu + ca) / 2m, 1);
        var last = evs.OrderByDescending(e => e.At).First();
        DateTimeOffset? lastAt = DateTimeOffset.TryParse(last.At, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        return new ProveedorScoreDto(prov, n, pu, ca, gen, string.IsNullOrWhiteSpace(last.N) ? null : last.N, lastAt);
    }

    public async Task<IReadOnlyDictionary<Guid, ProveedorScoreDto>> GetProveedorScoresAsync(CancellationToken ct = default)
    {
        var store = await LoadRatingsAsync(ct);
        var map = new Dictionary<Guid, ProveedorScoreDto>();
        foreach (var grp in store.Events.Where(e => Guid.TryParse(e.P, out _)).GroupBy(e => Guid.Parse(e.P)))
        {
            map[grp.Key] = Aggregate(grp.Key, grp.ToList());
        }
        return map;
    }

    public async Task<ProveedorScoreDto> RateProveedorAsync(ProveedorRatingInput input, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant."); }
        if (input.ProveedorId == Guid.Empty) { throw new InvalidOperationException("Proveedor invalido."); }
        int Clamp(int v) => Math.Max(1, Math.Min(5, v));
        var ev = new RatingEvent
        {
            P = input.ProveedorId.ToString(),
            Pu = Clamp(input.Puntualidad),
            Ca = Clamp(input.Calidad),
            N = string.IsNullOrWhiteSpace(input.Nota) ? null : input.Nota!.Trim(),
            At = DateTimeOffset.UtcNow.ToString("O"),
            By = _tenant.UserId?.ToString(),
        };
        var store = await LoadRatingsAsync(ct);
        store.Events.Add(ev);
        var json = System.Text.Json.JsonSerializer.Serialize(store);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == RatingsKey, ct);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = RatingsKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
        var mine = store.Events.Where(e => e.P == ev.P).ToList();
        return Aggregate(input.ProveedorId, mine);
    }

    // ---- Ola D: precedencia del proceso de cotizacion (default modulo / por proveedor / por producto) ----
    private const string CotDefaultKey = "requerimientos.cotizacion_default";
    private const string CotProductoKey = "requerimientos.cotizacion_producto";
    // Proceso de COMPRA por defecto del modulo (ultimo recurso del planificador al Ejecutar).
    private const string CompraDefaultKey = "requerimientos.compra_default";

    public async Task<Guid?> GetCotizacionDefaultAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking().Where(c => c.ConfigKey == CotDefaultKey)
            .Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        return Guid.TryParse(val, out var g) ? g : null;
    }

    public async Task SetCotizacionDefaultAsync(Guid? subcategoriaId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == CotDefaultKey, ct);
        var json = subcategoriaId is Guid g && g != Guid.Empty ? g.ToString() : "";
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = CotDefaultKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> GetCompraDefaultAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking().Where(c => c.ConfigKey == CompraDefaultKey)
            .Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        return Guid.TryParse(val, out var g) ? g : null;
    }

    public async Task SetCompraDefaultAsync(Guid? subcategoriaId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == CompraDefaultKey, ct);
        var json = subcategoriaId is Guid g && g != Guid.Empty ? g.ToString() : "";
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = CompraDefaultKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetProveedorProcesoMapAsync(CancellationToken ct = default)
    {
        var ext = await LoadExtAsync(ct);
        var map = new Dictionary<Guid, Guid>();
        foreach (var kv in ext.ProvCot)
        {
            if (Guid.TryParse(kv.Key, out var pid) && Guid.TryParse(kv.Value, out var sub)) { map[pid] = sub; }
        }
        return map;
    }

    public async Task SetProveedorProcesoAsync(Guid proveedorId, Guid? subcategoriaId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var ext = await LoadExtAsync(ct);
        if (subcategoriaId is Guid g && g != Guid.Empty) { ext.ProvCot[proveedorId.ToString()] = g.ToString(); }
        else { ext.ProvCot.Remove(proveedorId.ToString()); }
        await PersistExtAsync(ext, tid, ct);
    }

    private async Task<Dictionary<string, string>> LoadProductoProcesoAsync(CancellationToken ct)
    {
        var val = await _db.TenantConfigurations.AsNoTracking().Where(c => c.ConfigKey == CotProductoKey)
            .Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(val)) { return new Dictionary<string, string>(); }
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(val!) ?? new(); }
        catch { return new Dictionary<string, string>(); }
    }

    public async Task<IReadOnlyDictionary<string, Guid>> GetProductoProcesoMapAsync(CancellationToken ct = default)
    {
        var raw = await LoadProductoProcesoAsync(ct);
        var map = new Dictionary<string, Guid>();
        foreach (var kv in raw)
        {
            if (Guid.TryParse(kv.Value, out var sub)) { map[kv.Key] = sub; }
        }
        return map;
    }

    public async Task<Guid?> GetProductoProcesoAsync(Guid? itemId, string? descripcion, CancellationToken ct = default)
    {
        var raw = await LoadProductoProcesoAsync(ct);
        return raw.TryGetValue(RequerimientoClave.Of(itemId, descripcion), out var v) && Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task SetProductoProcesoAsync(Guid? itemId, string? descripcion, Guid? subcategoriaId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var raw = await LoadProductoProcesoAsync(ct);
        var clave = RequerimientoClave.Of(itemId, descripcion);
        if (subcategoriaId is Guid g && g != Guid.Empty) { raw[clave] = g.ToString(); }
        else { raw.Remove(clave); }
        var json = System.Text.Json.JsonSerializer.Serialize(raw);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == CotProductoKey, ct);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = CotProductoKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListProveedoresAsync(CancellationToken ct = default)
        => await _db.Terceros.AsNoTracking()
            .Where(t => (t.Perfiles & TerceroPerfil.Proveedor) == TerceroPerfil.Proveedor)
            .OrderBy(t => t.Nombre)
            .Select(t => new RequerimientoOpcionDto(t.Id, t.Nombre)).Take(2000).ToListAsync(ct);

    public async Task<IReadOnlyList<RequerimientoOpcionDto>> ListValoresDimensionAsync(RequerimientoDimension dimension, CancellationToken ct = default)
        => dimension switch
        {
            RequerimientoDimension.Categoria => await _db.ItemTypes.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Grupo => await _db.ItemGroups.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Subgrupo => await _db.ItemSubgroups.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Marca => await _db.Brands.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, x.Name)).ToListAsync(ct),
            RequerimientoDimension.Referencia => await _db.Items.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
                .Select(x => new RequerimientoOpcionDto(x.Id, (x.Sku != null ? x.Sku + " - " : "") + x.Name)).Take(2000).ToListAsync(ct),
            _ => new List<RequerimientoOpcionDto>()
        };

    private async Task<Dictionary<Guid, string>> ValoresCatalogoAsync(RequerimientoDimension dim, List<Guid> ids, CancellationToken ct)
        => dim switch
        {
            RequerimientoDimension.Categoria => await _db.ItemTypes.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Grupo => await _db.ItemGroups.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Subgrupo => await _db.ItemSubgroups.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Marca => await _db.Brands.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct),
            RequerimientoDimension.Referencia => await _db.Items.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => (x.Sku != null ? x.Sku + " - " : "") + x.Name, ct),
            _ => new Dictionary<Guid, string>()
        };

    private static RequerimientoOrigenDto Map(RequerimientoOrigen o,
        Dictionary<Guid, string> subs, Dictionary<Guid, string> forms)
        => new(
            o.Id, o.Nombre, o.Area, o.TipoFuente,
            o.FuenteRefId,
            o.FuenteRefId is Guid f && forms.TryGetValue(f, out var fn) ? fn : null,
            o.SubcategoriaCompraId,
            o.SubcategoriaCompraId is Guid s && subs.TryGetValue(s, out var sn) ? sn : null,
            o.SubcategoriaCotizacionId,
            o.SubcategoriaCotizacionId is Guid sc && subs.TryGetValue(sc, out var scn) ? scn : null,
            o.Activo);

    // ---- Ola B: formulario de cotizacion (RFQ) ----
    // Mapeo (que formulario, que grilla, que columnas) para inyectar las lineas al proveedor. Un solo JSON por
    // tenant en TenantConfiguration (sin migracion), igual patron que la matriz extendida.
    private const string RfqFormKey = "requerimientos.rfq";

    public async Task<RfqFormMapDto> GetRfqFormMapAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking().Where(c => c.ConfigKey == RfqFormKey)
            .Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(val)) { return new RfqFormMapDto(null, null, null, null, null, null, null); }
        try { return System.Text.Json.JsonSerializer.Deserialize<RfqFormMapDto>(val!) ?? new RfqFormMapDto(null, null, null, null, null, null, null); }
        catch { return new RfqFormMapDto(null, null, null, null, null, null, null); }
    }

    public async Task SetRfqFormMapAsync(RfqFormMapDto map, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var clean = new RfqFormMapDto(
            map.FormDefinitionId, Norm(map.GridFieldCode),
            Norm(map.ColClave), Norm(map.ColDescripcion), Norm(map.ColCantidad), Norm(map.ColUnidad), Norm(map.ColFecha),
            Norm(map.ColPrecio), Norm(map.ColVigencia), Norm(map.ColLeadTime), Norm(map.ColMarca), Norm(map.ColNota));
        var json = System.Text.Json.JsonSerializer.Serialize(clean);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == RfqFormKey, ct);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = RfqFormKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RfqGridFieldDto>> ListGridFieldsAsync(Guid definitionId, CancellationToken ct = default)
    {
        var grids = await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == definitionId && q.ControlType == FormControlType.GridDetail)
            .OrderBy(q => q.SortOrder)
            .Select(q => new { q.FieldCode, q.Label, q.OptionsJson })
            .ToListAsync(ct);
        return grids.Select(g => new RfqGridFieldDto(
            g.FieldCode, g.Label,
            Forms.Calc.FormGridCalculator.ParseColumns(g.OptionsJson)
                .Select(c => new RfqGridColDto(c.Id, c.Label, c.Hidden)).ToList())).ToList();
    }
}
