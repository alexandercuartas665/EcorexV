using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

public sealed record ConciliacionRuleDto(Guid Id, int SortOrder, string Name, string Kind, bool Enabled, string DefinitionJson);

/// <summary>Resultado de aplicar (o simular) el motor de reglas sobre un documento.</summary>
public sealed record ConciliacionReglasResult(
    int Renglones, int Encontrados, int Aprobados, int Cambiados, bool Simulado,
    IReadOnlyList<ConciliacionReglaPreview> Muestra, string? Error = null);

/// <summary>Vista previa de un renglon tras correr las reglas (para el dry-run).</summary>
public sealed record ConciliacionReglaPreview(
    Guid RenglonId, string NumFacturaProveedor, string Proveedor,
    string NumSoldarcoAntes, string NumSoldarcoDespues,
    bool AprobadaAntes, bool AprobadaDespues, bool Cambio);

/// <summary>
/// Motor de reglas CONFIGURABLE de la conciliacion DIAN. Las reglas del tenant corren en orden sobre cada
/// renglon: VLOOKUP (busca en un contenedor y escribe un campo) y CONDICION (si Y/O -> asigna). Encadena
/// campos. "Aplicar reglas" tiene modo simular (dry-run) que no escribe.
/// </summary>
public interface IConciliacionRuleService
{
    Task<IReadOnlyList<ConciliacionRuleDto>> ListAsync(CancellationToken ct = default);
    Task<ConciliacionRuleDto?> SaveAsync(ConciliacionRuleDto dto, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default);
    /// <summary>Contenedores de datos del tenant (con sus columnas) para elegir en el editor de VLOOKUP.</summary>
    Task<IReadOnlyList<RuleContenedorDto>> ListContenedoresAsync(CancellationToken ct = default);
    Task<ConciliacionReglasResult> AplicarReglasAsync(Guid documentoId, IReadOnlyList<Guid>? renglonIds, bool simular, CancellationToken ct = default);
    /// <summary>Inserta (idempotente por nombre) las 3 reglas del "cruce estandar" (ERP -> numsol, Newton -> plataforma,
    /// auto-aprobar). Devuelve cuantas se agregaron.</summary>
    Task<int> AgregarCruceEstandarAsync(CancellationToken ct = default);
}

public sealed class ConciliacionRuleService : IConciliacionRuleService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    // ADR-0157 Ola 1: las LECTURAS puras (ListAsync, ListContenedoresAsync) usan contexto EFIMERO propio
    // (ADR-0138) para no solapar con otra consulta en vuelo sobre el _db scoped del circuito. El motor
    // AplicarReglasAsync es una unidad de trabajo trackeada (lee + muta + SaveChanges) y sigue en _db.
    private readonly IApplicationDbContextFactory _dbFactory;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ConciliacionRuleService(IApplicationDbContext db, ITenantContext tenant, IApplicationDbContextFactory dbFactory)
    {
        _db = db; _tenant = tenant; _dbFactory = dbFactory;
    }

    // ---------- CRUD (lo usa el seeder y la UI de Fase 2) ----------

    public async Task<IReadOnlyList<ConciliacionRuleDto>> ListAsync(CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var rows = await db.ConciliacionRules.AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct);
        return rows.Select(r => new ConciliacionRuleDto(r.Id, r.SortOrder, r.Name, r.Kind, r.Enabled, r.DefinitionJson)).ToList();
    }

    public async Task<ConciliacionRuleDto?> SaveAsync(ConciliacionRuleDto dto, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        ConciliacionRule? e = dto.Id == Guid.Empty ? null : await _db.ConciliacionRules.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (e is null)
        {
            e = new ConciliacionRule { TenantId = tid };
            _db.ConciliacionRules.Add(e);
        }
        e.SortOrder = dto.SortOrder;
        e.Name = (dto.Name ?? "").Trim();
        e.Kind = dto.Kind;
        e.Enabled = dto.Enabled;
        e.DefinitionJson = string.IsNullOrWhiteSpace(dto.DefinitionJson) ? "{}" : dto.DefinitionJson;
        await _db.SaveChangesAsync(ct);
        return new ConciliacionRuleDto(e.Id, e.SortOrder, e.Name, e.Kind, e.Enabled, e.DefinitionJson);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // DELETE directo en vez de Remove+SaveChanges: el DbContext es scoped y en Blazor vive TODO el
        // circuito, asi que un SaveChanges aqui volcaria el change-tracker entero; un cambio ajeno
        // pendiente (p.ej. un borrado de contenedor a medias con ImportProcess/ImportRun) tumbaba el
        // borrado de una regla con un error que no era suyo. ExecuteDelete respeta el filtro global por
        // tenant (si la regla no es del tenant, no borra nada) y no toca entidades rastreadas de otros.
        var deleted = await _db.ConciliacionRules.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default)
    {
        var rows = await _db.ConciliacionRules.ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var e = rows.FirstOrDefault(r => r.Id == orderedIds[i]);
            if (e is not null) { e.SortOrder = i; }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RuleContenedorDto>> ListContenedoresAsync(CancellationToken ct = default)
    {
        // Fuentes INTEGRADAS del cruce (ERP / Newton): salen primero en el mismo dropdown "Contenedor" con sus
        // columnas virtuales. Leen tablas internas, no un Contenedor de datos (ver BuildBuiltInIndexAsync).
        var builtIn = new List<RuleContenedorDto>
        {
            new(ConciliacionRuleSources.ErpSourceId, "ERP DIAN (cruce)",
                new[] { ConciliacionRuleSources.ErpColReferencia, ConciliacionRuleSources.ErpColDocInterno }),
            new(ConciliacionRuleSources.NewtonSourceId, "Newton (plataforma)",
                new[] { ConciliacionRuleSources.NewtonColCufe, ConciliacionRuleSources.NewtonColTienePdf }),
        };

        // Contenedores (tablas) del tenant con su modelo y columnas. Tenant-scoped por el filtro global.
        await using var db = _dbFactory.Create();
        var conts = await db.DataContainers.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.ModelId })
            .ToListAsync(ct);
        if (conts.Count == 0) { return builtIn; }

        var modelIds = conts.Where(c => c.ModelId != null).Select(c => c.ModelId!.Value).Distinct().ToList();
        var models = await db.DataModels.AsNoTracking()
            .Where(m => modelIds.Contains(m.Id)).Select(m => new { m.Id, m.Name }).ToListAsync(ct);
        var modelName = models.ToDictionary(m => m.Id, m => m.Name);

        var contIds = conts.Select(c => c.Id).ToList();
        var cols = await db.DataContainerColumns.AsNoTracking()
            .Where(col => contIds.Contains(col.ContainerId))
            .OrderBy(col => col.SortOrder)
            .Select(col => new { col.ContainerId, col.Name }).ToListAsync(ct);
        var colsByCont = cols.GroupBy(x => x.ContainerId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name).ToList());

        var reales = conts
            .Select(c => new RuleContenedorDto(
                c.Id,
                (c.ModelId is Guid mid && modelName.TryGetValue(mid, out var mn) ? mn + " / " : "") + c.Name,
                colsByCont.TryGetValue(c.Id, out var cc) ? cc : Array.Empty<string>()))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase);
        return builtIn.Concat(reales).ToList();
    }

    // ---------- Motor ----------

    public async Task<ConciliacionReglasResult> AplicarReglasAsync(Guid documentoId, IReadOnlyList<Guid>? renglonIds, bool simular, CancellationToken ct = default)
    {
        var rules = await _db.ConciliacionRules.AsNoTracking().Where(r => r.Enabled).OrderBy(r => r.SortOrder).ToListAsync(ct);
        if (rules.Count == 0)
        {
            return new ConciliacionReglasResult(0, 0, 0, 0, simular, Array.Empty<ConciliacionReglaPreview>(), "No hay reglas configuradas.");
        }

        var q = _db.ConciliacionDianRenglones.Where(r => r.DocumentoId == documentoId
            && (r.Estado == EstadoConciliacionDian.Conciliar || r.Estado == EstadoConciliacionDian.Conciliado));
        if (renglonIds is { Count: > 0 }) { q = q.Where(r => renglonIds.Contains(r.Id)); }
        var renglones = await q.ToListAsync(ct);
        if (renglones.Count == 0)
        {
            return new ConciliacionReglasResult(0, 0, 0, 0, simular, Array.Empty<ConciliacionReglaPreview>(), "No hay renglones para procesar.");
        }

        // Campos DINAMICOS (EAV): definiciones del tenant + valores por renglon. Se cargan TRACKED para
        // poder persistir los cambios en el mismo SaveChanges del final. Se exponen al contexto como "dyn:{key}".
        var renglonIdList = renglones.Select(r => r.Id).ToList();
        var dynDefs = await _db.ConciliacionFieldDefinitions.AsNoTracking().OrderBy(d => d.SortOrder).ToListAsync(ct);
        var dynValByKey = new Dictionary<(Guid Renglon, Guid Def), ConciliacionFieldValue>();
        if (dynDefs.Count > 0)
        {
            var dynEntities = await _db.ConciliacionFieldValues.Where(v => renglonIdList.Contains(v.RenglonId)).ToListAsync(ct);
            foreach (var v in dynEntities) { dynValByKey[(v.RenglonId, v.FieldDefinitionId)] = v; }
        }
        var tid = _tenant.TenantId;

        // Parsear specs y pre-indexar los VLOOKUP una sola vez por corrida.
        var parsed = new List<(ConciliacionRule R, string Kind, RuleLookupSpec? L, RuleConditionSpec? C)>();
        var indexCache = new Dictionary<string, LookupIndex>();
        foreach (var r in rules)
        {
            if (string.Equals(r.Kind, "Lookup", StringComparison.OrdinalIgnoreCase))
            {
                var spec = Deserialize<RuleLookupSpec>(r.DefinitionJson);
                if (spec is null || spec.ContainerId == Guid.Empty || spec.Matches.Count == 0 || string.IsNullOrWhiteSpace(spec.ReturnColumn) || string.IsNullOrWhiteSpace(spec.TargetField))
                {
                    continue; // regla incompleta: se ignora
                }
                var cacheKey = spec.ContainerId + "|" + string.Join(",", spec.Matches.Select(m => m.Column)) + "|" + spec.ReturnColumn + "|" + JsonSerializer.Serialize(spec.Norm);
                if (!indexCache.ContainsKey(cacheKey))
                {
                    indexCache[cacheKey] = await BuildIndexAsync(spec, ct);
                }
                parsed.Add((r, "Lookup", spec, null));
            }
            else
            {
                var spec = Deserialize<RuleConditionSpec>(r.DefinitionJson);
                if (spec is null) { continue; }
                parsed.Add((r, "Condition", null, spec));
            }
        }

        int encontrados = 0, aprobados = 0, cambiados = 0;
        var muestra = new List<ConciliacionReglaPreview>();

        foreach (var ren in renglones)
        {
            var ctx = BuildContext(ren);
            foreach (var d in dynDefs)
            {
                ctx["dyn:" + d.FieldKey] = (dynValByKey.TryGetValue((ren.Id, d.Id), out var dv) ? dv.Value : null) ?? "";
            }
            var numsolAntes = ren.NumFacturaSoldarco;
            var aprobAntes = ren.FacturaAprobada;
            var platAntes = ren.PlataformaProveedor;
            var huboHit = false;

            foreach (var (_, kind, L, C) in parsed)
            {
                if (kind == "Lookup" && L is not null)
                {
                    var cacheKey = L.ContainerId + "|" + string.Join(",", L.Matches.Select(m => m.Column)) + "|" + L.ReturnColumn + "|" + JsonSerializer.Serialize(L.Norm);
                    var idx = indexCache[cacheKey];
                    var key = ConciliacionRuleEngine.MatchKey(L.Matches.Select(m => Ctx(ctx, m.SourceField)), L.Norm);
                    if (idx.Map.TryGetValue(key, out var found))
                    {
                        ctx[L.TargetField] = found ?? "";
                        huboHit = true;
                    }
                    else if (L.OnNotFound == "blank")
                    {
                        ctx[L.TargetField] = "";
                    }
                    // OnNotFound == "keep": no toca el campo
                }
                else if (C is not null)
                {
                    var ok = ConciliacionRuleEngine.Eval(C.When, k => Ctx(ctx, k));
                    foreach (var a in (ok ? C.Then : C.Else))
                    {
                        if (!string.IsNullOrWhiteSpace(a.Field))
                        {
                            ctx[a.Field] = ConciliacionRuleEngine.ResolveValue(a.Value, k => Ctx(ctx, k));
                        }
                    }
                }
            }

            // Volcar campos escribibles del contexto al renglon.
            var numsolDespues = ctx.TryGetValue("numsol", out var ns) ? ns : numsolAntes;
            var ocDespues = ctx.TryGetValue("oc", out var oc) ? oc : ren.OrdenCompraSoldarco;
            var aprobDespues = ctx.TryGetValue("aprobada", out var ap) ? ConciliacionRuleEngine.IsTrue(ap) : aprobAntes;
            var platDespues = ctx.TryGetValue("plataforma", out var pl) ? ConciliacionRuleEngine.IsTrue(pl) : platAntes;

            // Volcar campos dinamicos escritos por las reglas (EAV). Se persisten en el SaveChanges final.
            var dynCambio = false;
            foreach (var d in dynDefs)
            {
                var k = "dyn:" + d.FieldKey;
                if (!ctx.TryGetValue(k, out var nv)) { continue; }
                nv ??= "";
                dynValByKey.TryGetValue((ren.Id, d.Id), out var ex);
                var old = ex?.Value ?? "";
                if (nv == old) { continue; }
                dynCambio = true;
                if (!simular && tid is Guid tg)
                {
                    if (nv.Length == 0)
                    {
                        if (ex is not null) { _db.ConciliacionFieldValues.Remove(ex); dynValByKey.Remove((ren.Id, d.Id)); }
                    }
                    else if (ex is not null) { ex.Value = nv; }
                    else
                    {
                        var ne = new ConciliacionFieldValue { TenantId = tg, RenglonId = ren.Id, FieldDefinitionId = d.Id, Value = nv };
                        _db.ConciliacionFieldValues.Add(ne);
                        dynValByKey[(ren.Id, d.Id)] = ne;
                    }
                }
            }

            var cambio = numsolDespues != numsolAntes || aprobDespues != aprobAntes || platDespues != platAntes || ocDespues != ren.OrdenCompraSoldarco || dynCambio;
            if (huboHit) { encontrados++; }
            if (aprobDespues) { aprobados++; }
            if (cambio) { cambiados++; }

            if (!simular)
            {
                ren.NumFacturaSoldarco = numsolDespues;
                ren.OrdenCompraSoldarco = ocDespues;
                ren.FacturaAprobada = aprobDespues;
                ren.PlataformaProveedor = platDespues;
                // Estado derivado: aprobada + con num soldarco (y no marcado inconsistencia) -> Conciliado.
                var limpio = !string.IsNullOrWhiteSpace(numsolDespues) && numsolDespues != "Inconsistencia!";
                ren.Estado = (ren.FacturaAprobada && limpio) ? EstadoConciliacionDian.Conciliado : EstadoConciliacionDian.Conciliar;
            }

            if (muestra.Count < 50 && cambio)
            {
                muestra.Add(new ConciliacionReglaPreview(ren.Id, ren.NumFacturaProveedor, ren.NombreProveedor,
                    numsolAntes, numsolDespues, aprobAntes, aprobDespues, cambio));
            }
        }

        if (!simular) { await _db.SaveChangesAsync(ct); }

        return new ConciliacionReglasResult(renglones.Count, encontrados, aprobados, cambiados, simular, muestra);
    }

    // ---------- Indice del VLOOKUP ----------

    private sealed class LookupIndex { public Dictionary<string, string?> Map = new(StringComparer.Ordinal); }

    // Marcador que escribe el cruce cuando una referencia empareja con mas de un documento interno (igual
    // al MarcaInconsistencia del ConciliacionDianService; literal para no acoplar los dos servicios).
    private const string MarcaInconsistencia = "Inconsistencia!";

    private async Task<LookupIndex> BuildIndexAsync(RuleLookupSpec spec, CancellationToken ct)
    {
        // Fuentes INTEGRADAS (ERP / Newton): leen tablas internas en vez de un Contenedor de datos. Hoy son
        // las tablas DUMMY de Fase 1; en Fase 2 (ADR-0084) este metodo pasa a leer la conexion externa.
        if (ConciliacionRuleSources.IsBuiltIn(spec.ContainerId))
        {
            return await BuildBuiltInIndexAsync(spec, ct);
        }

        var idx = new LookupIndex();
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == spec.ContainerId)
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var colByName = cols.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var matchColIds = new List<Guid>();
        foreach (var m in spec.Matches)
        {
            if (!colByName.TryGetValue(m.Column, out var cid)) { return idx; } // columna inexistente -> indice vacio
            matchColIds.Add(cid);
        }
        if (!colByName.TryGetValue(spec.ReturnColumn, out var retColId)) { return idx; }

        var neededIds = matchColIds.Append(retColId).Distinct().ToList();
        // Todas las celdas de las columnas necesarias (columna pertenece a un solo contenedor -> acota al contenedor).
        var cells = await _db.DataContainerCells.AsNoTracking()
            .Where(ce => neededIds.Contains(ce.ColumnId))
            .Select(ce => new { ce.RowId, ce.ColumnId, ce.Value }).ToListAsync(ct);

        // Orden de filas estable (para "primero gana").
        var rowOrder = await _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == spec.ContainerId)
            .OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(ct);

        var byRow = cells.GroupBy(c => c.RowId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ColumnId, x => x.Value));
        foreach (var rowId in rowOrder)
        {
            if (!byRow.TryGetValue(rowId, out var cmap)) { continue; }
            var key = ConciliacionRuleEngine.MatchKey(matchColIds.Select(id => cmap.GetValueOrDefault(id)), spec.Norm);
            var ret = cmap.GetValueOrDefault(retColId);
            DedupInto(idx, key, ret, spec.OnMultiple);
        }
        return idx;
    }

    /// <summary>Inserta key->value respetando OnMultiple en una clave repetida: "first" conserva el primero,
    /// "blank" lo deja vacio (ambiguo), "inconsistencia" lo marca con el literal del cruce.</summary>
    private static void DedupInto(LookupIndex idx, string key, string? value, string onMultiple)
    {
        if (idx.Map.TryGetValue(key, out _))
        {
            if (onMultiple == "blank") { idx.Map[key] = ""; }
            else if (onMultiple == "inconsistencia") { idx.Map[key] = MarcaInconsistencia; }
            // "first": se queda con el primero
        }
        else
        {
            idx.Map[key] = value;
        }
    }

    /// <summary>Indice de una fuente INTEGRADA (ERP / Newton). Expone columnas virtuales y lee la tabla
    /// interna correspondiente (hoy las dummy de Fase 1). Tenant-scoped por el filtro global.</summary>
    private async Task<LookupIndex> BuildBuiltInIndexAsync(RuleLookupSpec spec, CancellationToken ct)
    {
        var idx = new LookupIndex();
        // Una sola columna de match y una de retorno (las fuentes integradas tienen 2 columnas virtuales).
        var matchCol = spec.Matches.FirstOrDefault()?.Column ?? "";

        if (spec.ContainerId == ConciliacionRuleSources.ErpSourceId)
        {
            var rows = await _db.ConciliacionDianErpRefDummies.AsNoTracking()
                .OrderBy(e => e.Referencia).Select(e => new { e.Referencia, e.DocumentoInterno }).ToListAsync(ct);
            foreach (var r in rows)
            {
                var matchVal = string.Equals(matchCol, ConciliacionRuleSources.ErpColDocInterno, StringComparison.OrdinalIgnoreCase) ? r.DocumentoInterno : r.Referencia;
                var retVal = string.Equals(spec.ReturnColumn, ConciliacionRuleSources.ErpColReferencia, StringComparison.OrdinalIgnoreCase) ? r.Referencia : r.DocumentoInterno;
                var key = ConciliacionRuleEngine.MatchKey(new[] { matchVal }, spec.Norm);
                DedupInto(idx, key, retVal, spec.OnMultiple);
            }
        }
        else if (spec.ContainerId == ConciliacionRuleSources.NewtonSourceId)
        {
            // Solo CUFEs con representacion grafica (GuidPdf) habilitan la plataforma; TienePdf virtual = "1".
            var cufes = await _db.ConciliacionDianNewtonDummies.AsNoTracking()
                .Where(n => n.GuidPdf != "").Select(n => n.Cufe).ToListAsync(ct);
            foreach (var cufe in cufes)
            {
                var retVal = string.Equals(spec.ReturnColumn, ConciliacionRuleSources.NewtonColCufe, StringComparison.OrdinalIgnoreCase) ? cufe : "1";
                var key = ConciliacionRuleEngine.MatchKey(new[] { cufe }, spec.Norm);
                DedupInto(idx, key, retVal, spec.OnMultiple);
            }
        }
        return idx;
    }

    // ---------- Contexto de trabajo (campo -> valor string) ----------

    private static string Ctx(Dictionary<string, string> ctx, string key) => ctx.TryGetValue(key, out var v) ? v : "";

    private static Dictionary<string, string> BuildContext(ConciliacionDianRenglon r)
    {
        string D(decimal d) => d.ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["numprov"] = r.NumFacturaProveedor,
            ["numsol"] = r.NumFacturaSoldarco,
            ["oc"] = r.OrdenCompraSoldarco,
            ["nit"] = r.NitProveedor,
            ["proveedor"] = r.NombreProveedor,
            ["tipodoc"] = r.TipoDocDian,
            ["tipopago"] = r.TipoPago,
            ["cufe"] = r.Cufe,
            ["fecha"] = r.FechaEmision.ToString("yyyy-MM-dd"),
            ["sub_bruto"] = D(r.SubtotalBruto),
            ["descuento"] = D(r.DescuentoComercial),
            ["sub_neto"] = D(r.SubtotalNeto),
            ["iva"] = D(r.IvaDescontable),
            ["total_antes_ret"] = D(r.TotalAntesRetenciones),
            ["retefuente"] = D(r.RetRetefuente),
            ["ica"] = D(r.RetIca),
            ["total"] = D(r.TotalFactura),
            ["plataforma"] = r.PlataformaProveedor ? "1" : "0",
            ["aprobada"] = r.FacturaAprobada ? "1" : "0",
            ["aprobada_manual"] = r.AprobadaManual ? "1" : "0",
            ["ev30"] = r.Evento30 ? "1" : "0",
            ["ev31"] = r.Evento31 ? "1" : "0",
            ["ev32"] = r.Evento32 ? "1" : "0",
            ["ev33"] = r.Evento33 ? "1" : "0",
            ["ev34"] = r.Evento34 ? "1" : "0",
            ["estado"] = r.Estado.ToString(),
        };
    }

    public async Task<int> AgregarCruceEstandarAsync(CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return 0; }
        var existentes = await _db.ConciliacionRules.Select(r => r.Name).ToListAsync(ct);
        var yaHay = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);
        var nextOrder = existentes.Count == 0 ? 0 : (await _db.ConciliacionRules.MaxAsync(r => (int?)r.SortOrder, ct) ?? -1) + 1;

        var agregadas = 0;
        foreach (var (name, kind, json) in ConciliacionRuleSeed.CruceEstandar())
        {
            if (yaHay.Contains(name)) { continue; }
            _db.ConciliacionRules.Add(new ConciliacionRule
            {
                TenantId = tid, Name = name, Kind = kind, Enabled = true,
                SortOrder = nextOrder++, DefinitionJson = json,
            });
            agregadas++;
        }
        if (agregadas > 0) { await _db.SaveChangesAsync(ct); }
        return agregadas;
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch { return null; }
    }
}

/// <summary>Definicion de las 3 reglas del "cruce estandar" (reemplazan al boton Cruzar). Centralizada para que
/// el boton de la UI y el seeder siembren exactamente lo mismo. Serializa los specs a DefinitionJson.</summary>
public static class ConciliacionRuleSeed
{
    public const string NombreCruceErp = "Cruce ERP (estandar)";
    public const string NombreNewton = "Newton: plataforma (estandar)";
    public const string NombreAutoAprobar = "Auto-aprobar cruzadas (estandar)";

    public static IReadOnlyList<(string Name, string Kind, string DefinitionJson)> CruceEstandar()
    {
        var erp = new RuleLookupSpec
        {
            ContainerId = ConciliacionRuleSources.ErpSourceId,
            ContainerLabel = "ERP DIAN (cruce)",
            Matches = { new RuleLookupMatch { Column = ConciliacionRuleSources.ErpColReferencia, SourceField = "numprov" } },
            ReturnColumn = ConciliacionRuleSources.ErpColDocInterno,
            TargetField = "numsol",
            OnNotFound = "keep",
            OnMultiple = "inconsistencia",
            Norm = new RuleNorm { Trim = true, Alnum = true, Upper = true },
        };
        var newton = new RuleLookupSpec
        {
            ContainerId = ConciliacionRuleSources.NewtonSourceId,
            ContainerLabel = "Newton (plataforma)",
            Matches = { new RuleLookupMatch { Column = ConciliacionRuleSources.NewtonColCufe, SourceField = "cufe" } },
            ReturnColumn = ConciliacionRuleSources.NewtonColTienePdf,
            TargetField = "plataforma",
            OnNotFound = "blank",
            OnMultiple = "first",
            Norm = new RuleNorm { Trim = true, Upper = true },
        };
        var auto = new RuleConditionSpec
        {
            When = new RuleCond
            {
                Kind = "group", Op = "and",
                Children =
                {
                    new RuleCond { Kind = "cond", Field = "numsol", Operator = "nempty" },
                    new RuleCond { Kind = "cond", Field = "numsol", Operator = "neq", Value = "Inconsistencia!" },
                    new RuleCond { Kind = "cond", Field = "aprobada", Operator = "isfalse" },
                    new RuleCond { Kind = "cond", Field = "aprobada_manual", Operator = "isfalse" },
                },
            },
            Then = { new RuleAssign { Field = "aprobada", Value = "true" } },
        };
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return new List<(string, string, string)>
        {
            (NombreCruceErp, "Lookup", JsonSerializer.Serialize(erp, opts)),
            (NombreNewton, "Lookup", JsonSerializer.Serialize(newton, opts)),
            (NombreAutoAprobar, "Condition", JsonSerializer.Serialize(auto, opts)),
        };
    }
}
