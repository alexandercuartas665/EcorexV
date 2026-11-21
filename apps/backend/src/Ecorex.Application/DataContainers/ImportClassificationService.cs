using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>Datos para configurar el paso de clasificacion de una programacion (ADR-0199).</summary>
/// <param name="TableId">Tabla a clasificar; null = la tabla destino del conector de la programacion.</param>
public sealed record ConfigurarPasoClasificacionRequest(
    Guid ProcessId,
    Guid? TableId,
    string Agente,
    string ColumnaClave,
    string ColumnaMarca,
    IReadOnlyList<string> ColumnasSalida,
    IReadOnlyList<string>? ColumnasEntrada,
    int? Limite,
    int? MaxLotes);

/// <summary>Resultado de configurar: el paso guardado, el nombre de la tabla y un aviso (ej. agente apagado).</summary>
public sealed record ConfigurarPasoClasificacionResultado(
    bool Ok, string? Error, ImportClassifyStep? Paso = null, string? Tabla = null, string? AgenteNombre = null, string? Aviso = null);

/// <summary>
/// Resultado de una corrida del paso de clasificacion. <see cref="Motivo"/> dice POR QUE paro:
/// completo (no quedan pendientes), tope_lotes, cupo (se agotaron los tokens del plan), error_proveedor (fallo
/// transitorio que no se recupero con los reintentos), sin_avance (el agente no devuelve nada util) o config.
/// </summary>
public sealed record ImportClassifyRunResult(
    bool Ok,
    string? Error,
    string Motivo,
    int Lotes = 0,
    int Procesados = 0,
    int Actualizados = 0,
    int SinRespuesta = 0,
    int Restantes = 0,
    int Reintentos = 0,
    int InputTokens = 0,
    int OutputTokens = 0)
{
    public const string MotivoCompleto = "completo";
    public const string MotivoTope = "tope_lotes";
    public const string MotivoCupo = "cupo";
    public const string MotivoProveedor = "error_proveedor";
    public const string MotivoSinAvance = "sin_avance";
    public const string MotivoConfig = "config";

    /// <summary>Linea corta para la bitacora de la corrida (la ve el operador en listar_corridas_importacion).</summary>
    public string Resumen()
    {
        var s = $"Clasificacion IA: {Lotes} lote(s), {Actualizados} clasificada(s), {Restantes} pendiente(s) [{Motivo}]";
        if (Reintentos > 0) { s += $", {Reintentos} reintento(s)"; }
        if (!string.IsNullOrWhiteSpace(Error)) { s += $": {Error}"; }
        return s + ".";
    }
}

/// <summary>
/// ADR-0199: CLASIFICACION COMO PASO DE LA IMPORTACION. La programacion de importacion (o de un flujo de extraccion)
/// lleva un paso opcional <see cref="ImportClassifyStep"/>; cuando una corrida termina bien, la bitacora la marca
/// <c>ClassifyRequestedAt</c> y el worker la toma y clasifica SOLO las filas con la marca vacia, lote a lote, con la
/// logica de <see cref="IContenedorClasificacionService"/> (cupo duro del plan, AiUsageLog, agente encendido).
/// Idempotente por la marca: lo que no alcanzo (cupo, tope, error) queda pendiente para la siguiente corrida.
/// Tenant-scoped por el filtro global; el barrido cross-tenant devuelve solo ids.
/// </summary>
public interface IImportClassificationService
{
    Task<ConfigurarPasoClasificacionResultado> ConfigurarAsync(ConfigurarPasoClasificacionRequest req, CancellationToken ct = default);

    /// <summary>Quita el paso (y la solicitud pendiente). false si la programacion no existe.</summary>
    Task<bool> QuitarAsync(Guid processId, CancellationToken ct = default);

    /// <summary>Pide clasificar en el proximo ciclo del worker ("ejecutar ahora"). Error si no hay paso.</summary>
    Task<string?> SolicitarAsync(Guid processId, CancellationToken ct = default);

    /// <summary>Corre el paso de UNA programacion: lotes hasta no quedar pendientes, el tope, el cupo o un error.</summary>
    Task<ImportClassifyRunResult> EjecutarAsync(Guid processId, CancellationToken ct = default);

    /// <summary>Barrido de PLATAFORMA: tenants con alguna clasificacion pedida (solo ids).</summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithRequestsAsync(CancellationToken ct = default);

    /// <summary>Toma y corre las clasificaciones pedidas del tenant del contexto. Devuelve cuantas corrio.</summary>
    Task<int> RunRequestedForTenantAsync(CancellationToken ct = default);
}

public sealed class ImportClassificationService : IImportClassificationService
{
    /// <summary>Espera antes de cada reintento de un lote con falla transitoria (timeout, 5xx, JSON roto).</summary>
    private static readonly TimeSpan[] BackoffDefecto = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60) };

    /// <summary>Lotes seguidos sin escribir nada antes de cortar (el agente no responde algo util: no quemar tokens).</summary>
    private const int MaxLotesSinAvance = 2;

    private const int MaxPorCiclo = 20;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IContenedorClasificacionService _clasificador;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyList<TimeSpan> _backoff;

    public ImportClassificationService(IApplicationDbContext db, ITenantContext tenant,
        IContenedorClasificacionService clasificador, TimeProvider clock, IReadOnlyList<TimeSpan>? backoff = null)
    {
        _db = db;
        _tenant = tenant;
        _clasificador = clasificador;
        _clock = clock;
        _backoff = backoff ?? BackoffDefecto;
    }

    public async Task<ConfigurarPasoClasificacionResultado> ConfigurarAsync(ConfigurarPasoClasificacionRequest req, CancellationToken ct = default)
    {
        static ConfigurarPasoClasificacionResultado Falla(string e) => new(false, e);

        var process = await _db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == req.ProcessId, ct);
        if (process is null) { return Falla("La programacion no existe."); }

        // Tabla: la pedida o la destino del conector. Una programacion de flujo no tiene conector: hay que decirla.
        var tableId = req.TableId;
        if (tableId is null && process.ConnectorId is Guid connId)
        {
            tableId = await _db.DataConnectors.Where(c => c.Id == connId).Select(c => c.ContainerId).FirstOrDefaultAsync(ct);
        }
        if (tableId is not Guid tid)
        {
            return Falla("Indica la tabla a clasificar: la programacion no tiene un conector con tabla destino.");
        }
        var table = await _db.DataContainers.AsNoTracking()
            .Where(t => t.Id == tid).Select(t => new { t.Id, t.Name, t.ModelId, t.ParentContainerId }).FirstOrDefaultAsync(ct);
        if (table is null) { return Falla("La tabla no existe."); }
        if (table.ParentContainerId is not null) { return Falla("Solo se pueden clasificar tablas raiz (no sub-tablas)."); }
        if (process.ModelId is Guid modelId && table.ModelId != modelId)
        {
            return Falla($"La tabla '{table.Name}' no pertenece al contenedor de esta programacion.");
        }

        // Columnas.
        if (string.IsNullOrWhiteSpace(req.ColumnaClave) || string.IsNullOrWhiteSpace(req.ColumnaMarca))
        {
            return Falla("Indica columna_clave y columna_marca.");
        }
        var salida = req.ColumnasSalida.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (salida.Count == 0) { return Falla("Indica al menos una columna de salida (columnas_salida)."); }
        var entrada = (req.ColumnasEntrada ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cols = await ContenedorFilasPorClave.ColumnasAsync(_db, tid, ct);
        var faltantes = new[] { req.ColumnaClave.Trim(), req.ColumnaMarca.Trim() }.Concat(salida).Concat(entrada)
            .Where(n => !cols.ContainsKey(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (faltantes.Count > 0)
        {
            return Falla($"La tabla '{table.Name}' no tiene las columnas: {string.Join(", ", faltantes)}. Columnas: {string.Join(", ", cols.Keys)}.");
        }
        if (salida.Contains(req.ColumnaClave.Trim(), StringComparer.OrdinalIgnoreCase)
            || salida.Contains(req.ColumnaMarca.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return Falla("La columna clave y la marca no pueden ser columnas de salida.");
        }

        // Agente: se guarda su id.
        var agent = await ResolveAgentAsync(req.Agente, ct);
        if (agent is null) { return Falla($"No encontre el agente '{req.Agente}' (o el nombre se repite: usa el id)."); }

        var step = new ImportClassifyStep(
            tid, agent.Id.ToString(), req.ColumnaClave.Trim(), req.ColumnaMarca.Trim(), salida,
            entrada.Count > 0 ? entrada : null,
            Math.Clamp(req.Limite ?? ImportClassifyStep.LimiteDefecto, 1, ContenedorClasificacionService.MaxLimite),
            Math.Clamp(req.MaxLotes ?? ImportClassifyStep.MaxLotesDefecto, 1, ImportClassifyStep.MaxLotesTope));
        process.ClassifyStepJson = step.ToJson();
        await _db.SaveChangesAsync(ct);

        var aviso = agent.IsActive ? null
            : $"El agente '{agent.Name}' esta APAGADO: el paso no clasificara nada hasta que lo enciendas.";
        return new ConfigurarPasoClasificacionResultado(true, null, step, table.Name, agent.Name, aviso);
    }

    public async Task<bool> QuitarAsync(Guid processId, CancellationToken ct = default)
    {
        var process = await _db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (process is null) { return false; }
        process.ClassifyStepJson = null;
        process.ClassifyRequestedAt = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> SolicitarAsync(Guid processId, CancellationToken ct = default)
    {
        var process = await _db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (process is null) { return "La programacion no existe."; }
        if (ImportClassifyStep.Parse(process.ClassifyStepJson) is null)
        {
            return "Esta programacion no tiene paso de clasificacion: configuralo primero.";
        }
        process.ClassifyRequestedAt ??= _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<ImportClassifyRunResult> EjecutarAsync(Guid processId, CancellationToken ct = default)
    {
        var json = await _db.ImportProcesses.AsNoTracking()
            .Where(p => p.Id == processId).Select(p => p.ClassifyStepJson).FirstOrDefaultAsync(ct);
        var step = ImportClassifyStep.Parse(json);
        if (step is null)
        {
            return new ImportClassifyRunResult(false, "La programacion no tiene paso de clasificacion.", ImportClassifyRunResult.MotivoConfig);
        }

        var req = new ClasificarLoteRequest(step.TableId, step.Agente, step.ColumnaClave, step.ColumnaMarca,
            step.ColumnasSalida, step.ColumnasEntrada, step.Limite);
        int lotes = 0, procesados = 0, actualizados = 0, sinRespuesta = 0, restantes = 0, reintentos = 0, tin = 0, tout = 0;
        var sinAvance = 0;
        var motivo = ImportClassifyRunResult.MotivoTope;
        string? error = null;

        for (var i = 0; i < Math.Clamp(step.MaxLotes, 1, ImportClassifyStep.MaxLotesTope); i++)
        {
            ct.ThrowIfCancellationRequested();
            ClasificarLoteResultado r;
            var intento = 0;
            while (true)
            {
                r = await _clasificador.ClasificarLoteAsync(req, ct);
                tin += r.InputTokens;
                tout += r.OutputTokens;
                if (r.Ok || !r.EsTransitorio || intento >= _backoff.Count) { break; }
                var espera = _backoff[intento++];
                reintentos++;
                if (espera > TimeSpan.Zero) { await Task.Delay(espera, _clock, ct); }
            }

            if (!r.Ok)
            {
                error = r.Error;
                motivo = r.Codigo switch
                {
                    ClasificarLoteResultado.CodigoCupo => ImportClassifyRunResult.MotivoCupo,
                    ClasificarLoteResultado.CodigoTransitorio => ImportClassifyRunResult.MotivoProveedor,
                    _ => ImportClassifyRunResult.MotivoConfig
                };
                restantes = await PendientesAsync(step, ct);
                break;
            }

            restantes = r.Restantes;
            if (r.Procesados == 0) { motivo = ImportClassifyRunResult.MotivoCompleto; break; }
            lotes++;
            procesados += r.Procesados;
            actualizados += r.Actualizados;
            sinRespuesta += r.SinRespuesta;
            if (r.Restantes == 0) { motivo = ImportClassifyRunResult.MotivoCompleto; break; }

            sinAvance = r.Actualizados == 0 ? sinAvance + 1 : 0;
            if (sinAvance >= MaxLotesSinAvance) { motivo = ImportClassifyRunResult.MotivoSinAvance; break; }
        }

        // Cortar por cupo o por tope NO es un error: lo pendiente queda para la siguiente corrida.
        var ok = motivo is ImportClassifyRunResult.MotivoCompleto or ImportClassifyRunResult.MotivoTope
            or ImportClassifyRunResult.MotivoCupo;
        return new ImportClassifyRunResult(ok, error, motivo, lotes, procesados, actualizados, sinRespuesta, restantes,
            reintentos, tin, tout);
    }

    public async Task<IReadOnlyList<Guid>> FindTenantsWithRequestsAsync(CancellationToken ct = default)
        => await _db.ImportProcesses.IgnoreQueryFilters()
            .Where(p => p.ClassifyRequestedAt != null && p.ClassifyStepJson != null)
            .Select(p => p.TenantId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<int> RunRequestedForTenantAsync(CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) { return 0; }
        var pedidas = await _db.ImportProcesses
            .Where(p => p.ClassifyRequestedAt != null)
            .OrderBy(p => p.ClassifyRequestedAt)
            .Take(MaxPorCiclo)
            .ToListAsync(ct);

        var corridas = 0;
        foreach (var process in pedidas)
        {
            if (ct.IsCancellationRequested) { break; }
            // Se TOMA (limpia la solicitud) antes de correr: si la clasificacion tarda o falla, el siguiente ciclo no
            // la vuelve a disparar en bucle. Lo pendiente lo recoge la siguiente importacion (o "ejecutar ahora").
            process.ClassifyRequestedAt = null;
            await _db.SaveChangesAsync(ct);
            if (ImportClassifyStep.Parse(process.ClassifyStepJson) is null) { continue; }

            ImportClassifyRunResult res;
            try { res = await EjecutarAsync(process.Id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                res = new ImportClassifyRunResult(false, ex.Message, ImportClassifyRunResult.MotivoProveedor);
            }
            corridas++;
            await AnotarEnBitacoraAsync(process, res, ct);
        }
        return corridas;
    }

    /// <summary>Deja el resumen en la ultima corrida de la programacion (importacion o flujo), que es donde el
    /// operador mira (listar_corridas_importacion / bitacora del flujo). La columna admite 600.</summary>
    private async Task AnotarEnBitacoraAsync(Domain.Entities.ImportProcess process, ImportClassifyRunResult res, CancellationToken ct)
    {
        var resumen = res.Resumen();
        if (process.FlowId is Guid flowId)
        {
            var run = await _db.ScrapeFlowRuns.Where(r => r.FlowId == flowId).OrderByDescending(r => r.FiredAt).FirstOrDefaultAsync(ct);
            if (run is not null) { run.Detail = Append(run.Detail, resumen); }
        }
        else
        {
            var run = await _db.ImportRuns.Where(r => r.ProcessId == process.Id).OrderByDescending(r => r.FiredAt).FirstOrDefaultAsync(ct);
            if (run is not null) { run.Detail = Append(run.Detail, resumen); }
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string Append(string? detail, string extra)
    {
        var s = string.IsNullOrWhiteSpace(detail) ? extra : $"{detail} | {extra}";
        return s.Length <= 600 ? s : s[..597] + "...";
    }

    private async Task<int> PendientesAsync(ImportClassifyStep step, CancellationToken ct)
    {
        var cols = await ContenedorFilasPorClave.ColumnasAsync(_db, step.TableId, ct);
        if (!cols.TryGetValue(step.ColumnaClave, out var keyColId) || !cols.TryGetValue(step.ColumnaMarca, out var markColId)) { return 0; }
        return await _db.DataContainerRows.CountAsync(r => r.ContainerId == step.TableId && r.ParentRowId == null
            && _db.DataContainerCells.Any(c => c.RowId == r.Id && c.ColumnId == keyColId && c.Value != null && c.Value != "")
            && !_db.DataContainerCells.Any(c => c.RowId == r.Id && c.ColumnId == markColId && c.Value != null && c.Value != ""), ct);
    }

    private async Task<Domain.Entities.AiAgent?> ResolveAgentAsync(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) { return null; }
        if (Guid.TryParse(key, out var id))
        {
            return await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        }
        var name = key.Trim().ToLower();
        var hits = await _db.AiAgents.AsNoTracking().Where(a => a.Name.ToLower() == name).Take(2).ToListAsync(ct);
        return hits.Count == 1 ? hits[0] : null;
    }
}
