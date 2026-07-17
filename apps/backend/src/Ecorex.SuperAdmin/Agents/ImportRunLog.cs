using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Bitacora de corridas de importacion. Existe porque una programacion corre SIN NADIE MIRANDO: sin
/// registro, un fallo de madrugada es indistinguible de "no habia datos nuevos".
///
/// El ciclo tiene dos mitades separadas en el tiempo, y esa es toda la razon de que esto sea un
/// servicio y no dos lineas dentro del runner:
///   - <see cref="OpenAsync"/> la abre en Running al despachar la orden;
///   - <see cref="CloseAsync"/> la cierra cuando el agente responde, que llega DESPUES y por otro
///     camino (el hub), sin el contexto de quien disparo.
/// El puente entre las dos es el correlationId.
/// </summary>
public interface IImportRunLog
{
    /// <summary>
    /// Abre la corrida. Devuelve null si esa VENTANA ya estaba registrada, que es como se rechaza un
    /// doble disparo: no se comprueba antes con un SELECT (dos workers pasarian los dos), se deja
    /// chocar contra el indice unico y se descarta al perdedor.
    /// </summary>
    Task<Guid?> OpenAsync(Guid processId, ImportRunTrigger trigger, DateTimeOffset firedAt,
        string correlationId, CancellationToken ct = default);

    /// <summary>Cierra por correlationId con lo que devolvio el agente. Idempotente: si la corrida ya
    /// estaba cerrada (ej. vencio el plazo y el agente respondio tarde), no la toca.</summary>
    Task CloseAsync(string correlationId, bool ok, int inserted, int updated, int deleted,
        string? detail, CancellationToken ct = default);

    /// <summary>Cierra una corrida que ni siquiera llego a despacharse (agente caido, conector sin
    /// consulta...). El motivo es el que ve el operador.</summary>
    Task FailAsync(Guid runId, string detail, CancellationToken ct = default);

    /// <summary>Marca una corrida como <see cref="ImportRunResult.PendingOffline"/>: no se llego a
    /// intentar porque el agente no estaba. Separado de <see cref="FailAsync"/> a proposito: un agente
    /// dormido no es un fallo, es algo que se reintenta solo cuando vuelve.</summary>
    Task MarkOfflineAsync(Guid runId, string detail, CancellationToken ct = default);
}

public sealed class ImportRunLog(IApplicationDbContext db, ILogger<ImportRunLog> log) : IImportRunLog
{
    public async Task<Guid?> OpenAsync(Guid processId, ImportRunTrigger trigger, DateTimeOffset firedAt,
        string correlationId, CancellationToken ct = default)
    {
        var run = new ImportRun
        {
            ProcessId = processId,
            FiredAt = firedAt,
            Trigger = trigger,
            Result = ImportRunResult.Running,
            CorrelationId = correlationId
        };
        db.ImportRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
            return run.Id;
        }
        catch (DbUpdateException)
        {
            // Choco el indice unico (TenantId, ProcessId, FiredAt): otro worker ya tomo esta ventana.
            // No es un error: es la idempotencia funcionando. El perdedor se retira en silencio.
            log.LogInformation("[BITACORA] ventana {Fired:o} del proceso {Process} ya estaba tomada; no se dispara dos veces",
                firedAt, processId);
            return null;
        }
    }

    public async Task CloseAsync(string correlationId, bool ok, int inserted, int updated, int deleted,
        string? detail, CancellationToken ct = default)
    {
        var run = await db.ImportRuns.FirstOrDefaultAsync(r => r.CorrelationId == correlationId, ct);
        if (run is null) { return; }

        // Solo se cierra lo que sigue corriendo. Si ya se cerro (tipico: vencio el plazo y el agente
        // contesto despues), la primera version gana: reabrirla borraria la razon del fallo.
        if (run.Result != ImportRunResult.Running) { return; }

        run.Result = ok ? ImportRunResult.Ok : ImportRunResult.Error;
        run.Inserted = inserted;
        run.Updated = updated;
        run.Deleted = deleted;
        run.Detail = Trim(detail);
        run.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(Guid runId, string detail, CancellationToken ct = default)
    {
        var run = await db.ImportRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Result != ImportRunResult.Running) { return; }
        run.Result = ImportRunResult.Error;
        run.Detail = Trim(detail);
        run.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkOfflineAsync(Guid runId, string detail, CancellationToken ct = default)
    {
        var run = await db.ImportRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Result != ImportRunResult.Running) { return; }
        run.Result = ImportRunResult.PendingOffline;
        run.Detail = Trim(detail);
        run.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // La columna admite 600; un mensaje de error de un motor de BD los pasa sin despeinarse y la
    // insercion moriria por longitud, perdiendo justo el registro del fallo que queriamos guardar.
    private static string? Trim(string? s) =>
        s is null ? null : s.Length <= 600 ? s : s[..597] + "...";
}
