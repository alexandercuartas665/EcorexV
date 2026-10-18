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
    Task<ConciliacionReglasResult> AplicarReglasAsync(Guid documentoId, IReadOnlyList<Guid>? renglonIds, bool simular, CancellationToken ct = default);
}

public sealed class ConciliacionRuleService : IConciliacionRuleService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ConciliacionRuleService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    // ---------- CRUD (lo usa el seeder y la UI de Fase 2) ----------

    public async Task<IReadOnlyList<ConciliacionRuleDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.ConciliacionRules.AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct);
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
        var e = await _db.ConciliacionRules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { return false; }
        _db.ConciliacionRules.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
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
            var numsolAntes = ren.NumFacturaSoldarco;
            var aprobAntes = ren.FacturaAprobada;
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

            var cambio = numsolDespues != numsolAntes || aprobDespues != aprobAntes || ocDespues != ren.OrdenCompraSoldarco;
            if (huboHit) { encontrados++; }
            if (aprobDespues) { aprobados++; }
            if (cambio) { cambiados++; }

            if (!simular)
            {
                ren.NumFacturaSoldarco = numsolDespues;
                ren.OrdenCompraSoldarco = ocDespues;
                ren.FacturaAprobada = aprobDespues;
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

    private async Task<LookupIndex> BuildIndexAsync(RuleLookupSpec spec, CancellationToken ct)
    {
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
            if (idx.Map.TryGetValue(key, out _))
            {
                if (spec.OnMultiple == "blank") { idx.Map[key] = ""; } // ambiguo -> vacio
                // "first": se queda con el primero
            }
            else
            {
                idx.Map[key] = ret;
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
            ["ev30"] = r.Evento30 ? "1" : "0",
            ["ev31"] = r.Evento31 ? "1" : "0",
            ["ev32"] = r.Evento32 ? "1" : "0",
            ["ev33"] = r.Evento33 ? "1" : "0",
            ["ev34"] = r.Evento34 ? "1" : "0",
            ["estado"] = r.Estado.ToString(),
        };
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch { return null; }
    }
}
