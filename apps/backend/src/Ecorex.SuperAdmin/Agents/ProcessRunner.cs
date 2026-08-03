using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Resultado de disparar una programacion, para que la UI diga algo util.</summary>
public sealed record RunProcessResult(bool Ok, string? CorrelationId, string Message);

/// <summary>
/// Ejecuta una programacion (`ImportProcess`) que corre via agente. Es lo que hay detras del boton
/// "Actualizar datos" Y lo que llama el scheduler: si el horario tuviera su propia version, las dos
/// acabarian comportandose distinto.
///
/// El reparto del modelo (que ya existia, no se invento aqui):
///   - `DataConnector`  -> de donde salen los datos (host/base/credencial), la CONSULTA, y a que
///                         TABLA del contenedor alimentan.
///   - `ImportProcess`  -> que conector + que cliente remoto lo ejecuta + cada cuanto.
/// Por eso "via agente" no es un campo: es una programacion que tiene cliente.
/// </summary>
public interface IProcessRunner
{
    /// <param name="trigger">Quien dispara. Va a la bitacora: un fallo automatico de madrugada y un
    /// fallo al pulsar el boton no se diagnostican igual.</param>
    /// <param name="firedAt">
    /// La VENTANA que se ejecuta. Para el scheduler es la hora a la que TOCABA (es la mitad de la
    /// clave de idempotencia: dos workers en la misma ventana deben chocar). null = ahora.
    /// </param>
    Task<RunProcessResult> RunNowAsync(Guid processId, ImportRunTrigger trigger = ImportRunTrigger.Manual,
        DateTimeOffset? firedAt = null, CancellationToken ct = default);
}

public sealed class ProcessRunner(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ISecretProtector protector,
    IAgentRegistry registry,
    IAgentImportService imports,
    IApiImportService api,
    IImportRunLog runLog) : IProcessRunner
{
    public async Task<RunProcessResult> RunNowAsync(Guid processId, ImportRunTrigger trigger = ImportRunTrigger.Manual,
        DateTimeOffset? firedAt = null, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not Guid tenantId)
        {
            return new(false, null, "Sin tenant en contexto.");
        }

        var process = await db.ImportProcesses.AsNoTracking().FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (process is null) { return new(false, null, "La programacion no existe."); }

        // Las precondiciones se comprueban ANTES de abrir la corrida: que falte la consulta no es una
        // "ejecucion fallida", es una configuracion incompleta, y llenar la bitacora de eso taparia
        // los fallos de verdad. Cada "no" explica QUE falta y donde arreglarlo: este boton lo pulsa un
        // operador, no un dev, y un "no se pudo" seco lo deja sin saber que hacer.
        if (process.ConnectorId is not Guid connectorId)
        {
            return new(false, null, "Esta programacion no tiene conector: elige de que fuente trae los datos.");
        }

        var connector = await db.DataConnectors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectorId, ct);
        if (connector is null) { return new(false, null, "El conector ya no existe."); }
        if (connector.Kind != ConnectorKind.Database && connector.Kind != ConnectorKind.RestApi)
        {
            return new(false, null, $"Solo los conectores de tipo Base de datos o API REST se pueden refrescar (este es {connector.Kind}).");
        }
        if (connector.ContainerId is not Guid targetTableId)
        {
            return new(false, null, "El conector no tiene TABLA destino: eligela en el conector para poder refrescar solo.");
        }

        // RestApi -> SERVER-DIRECT, el MISMO camino que el `/run` de la Config API (ADR-0058/0059):
        // ConnectorRunPlanner resuelve las rutas ANIDADAS/INDEXADAS (NestedJsonResolver) y aplica el
        // Mode/KeyColumn PERSISTIDOS del proceso. Antes el disparo programado iba fijo en Replace y no
        // podia reconciliar (Upsert); este es el punto que arregla el sync agendado (ADR-0060). El
        // servidor hace el GET (no un agente): auth de 2 pasos y headers estaticos ya viven en
        // ApiImportService, asi que Siigo y compania funcionan sin agente.
        if (connector.Kind == ConnectorKind.RestApi)
        {
            return await RunRestServerDirectAsync(process, connector, targetTableId, trigger, firedAt, ct);
        }

        // Database -> via agente: la BD del cliente vive en su red, la trae el agente con SQL solo-lectura.
        if (process.ClientId is not Guid clientRowId)
        {
            return new(false, null, "Esta programacion no tiene cliente remoto asignado: eligelo para que la ejecute un agente.");
        }
        var client = await db.DataClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientRowId, ct);
        if (client is null) { return new(false, null, "El cliente remoto ya no existe."); }
        if (string.IsNullOrWhiteSpace(connector.Query))
        {
            return new(false, null, "El conector no tiene consulta: escribe el SELECT que trae los datos.");
        }

        // Mapeo por NOMBRE: cada columna de la tabla se llena con el campo del mismo nombre que
        // devuelva la consulta. Es predecible y no obliga a configurar un mapeo para el caso normal
        // (SELECT que ya trae los nombres correctos). Si sobran o faltan campos, esas columnas quedan
        // vacias en vez de fallar.
        var columns = await db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == targetTableId)
            .ToListAsync(ct);
        if (columns.Count == 0)
        {
            return new(false, null, "La tabla destino no tiene columnas.");
        }
        var mapping = columns.ToDictionary(c => c.Id, c => c.Name);

        // El modo/columna clave persistidos aplican tambien al camino via agente: por defecto Replace
        // (comportamiento historico del refresco), o Upsert por la columna clave si asi se configuro.
        var mode = (ApiImportMode)process.Mode;
        Guid? keyColumnId = null;
        if (mode == ApiImportMode.Upsert)
        {
            keyColumnId = columns.FirstOrDefault(c => string.Equals(c.Name, process.KeyColumn, StringComparison.OrdinalIgnoreCase))?.Id;
            if (keyColumnId is null)
            {
                return new(false, null, $"El modo Upsert necesita la columna clave '{process.KeyColumn}', que no existe en la tabla destino.");
            }
        }

        // El id de la orden se decide AQUI, no en el canal: la corrida tiene que quedar guardada con
        // el antes de despachar. Si lo generara el canal, un agente rapido podria contestar antes de
        // que la bitacora supiera a que corrida pertenece esa respuesta, y el resultado se perderia.
        var correlationId = AgentImportService.NewCorrelationId();
        var window = firedAt ?? DateTimeOffset.UtcNow;

        var runId = await runLog.OpenAsync(processId, trigger, window, correlationId, ct);
        if (runId is null)
        {
            // Otro worker ya tomo esta ventana. No es un fallo.
            return new(false, null, "Esa ejecucion ya la tomo otro proceso.");
        }

        // Se comprueba DESPUES de abrir la corrida, a proposito: que el agente no este conectado a la
        // hora a la que tocaba tiene que quedar registrado. Pero NO como fallo: se marca PendingOffline
        // y se PARQUEA la programacion (`PendingSince`) para que el worker la reintente sola cuando el
        // agente vuelva, en vez de esperar al siguiente horario natural (que puede ser dentro de horas).
        if (!registry.IsOnline(client.ClientId))
        {
            var detail = $"El agente '{client.Name}' no estaba conectado; se reintentara cuando vuelva.";
            await runLog.MarkOfflineAsync(runId.Value, detail, ct);
            await SetPendingSinceAsync(processId, window, ct);
            return new(false, null, detail);
        }

        // ADR-0040: la credencial VIAJA. Se descifra aqui y va en el mensaje. Si el conector no
        // tiene, se manda null (Database: el agente usa su cadena local).
        var secret = connector.CredentialsEncrypted is { } enc ? protector.Unprotect(enc) : null;

        var spec = new ConnectorSpec(
            Kind: "Database",
            DbEngine: connector.DbEngine?.ToString(),
            Host: connector.Host,
            Port: connector.Port,
            Database: connector.DatabaseName,
            Username: connector.Username,
            Secret: secret);

        try
        {
            await imports.DispatchFetchAsync(
                client.ClientId, tenantId, targetTableId, mapping,
                mode, keyColumnId,
                connector.Query!, spec, ct, correlationId, rest: null);
        }
        catch (Exception ex)
        {
            await runLog.FailAsync(runId.Value, $"No se pudo enviar la orden: {ex.Message}", ct);
            return new(false, null, $"No se pudo enviar la orden al agente: {ex.Message}");
        }

        // LastRunAt marca el DISPARO, no el resultado (que llega despues por el canal). El desenlace
        // vive en la bitacora, que es donde hay que mirarlo. Y se LIMPIA `PendingSince`: alcanzamos al
        // agente, asi que ya no esta "esperando". Ojo: se limpia al DESPACHAR, no al ingerir; "esperar
        // por agente offline" es especificamente "no llegue a el". Si luego el fetch falla, eso es otro
        // problema (Error), no un offline que haya que reintentar al reconectar.
        var tracked = await db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (tracked is not null)
        {
            tracked.LastRunAt = window;
            tracked.PendingSince = null;
            await db.SaveChangesAsync(ct);
        }

        return new(true, correlationId, $"Orden enviada al agente '{client.Name}'. Trayendo datos...");
    }

    /// <summary>
    /// Corre un conector RestApi SERVER-DIRECT (sin agente), el mismo camino que el `/run` de la Config
    /// API: <see cref="ConnectorRunPlanner"/> traduce el mapeo persistido (rutas anidadas incluidas) y
    /// <see cref="IApiImportService"/> hace el GET+ingesta aplicando el <paramref name="process"/>.Mode y
    /// KeyColumn persistidos. Deja una corrida en la bitacora (idempotente por la ventana) y la cierra
    /// sincronicamente, porque aqui no se espera a ningun agente.
    /// </summary>
    private async Task<RunProcessResult> RunRestServerDirectAsync(
        Domain.Entities.ImportProcess process, Domain.Entities.DataConnector connector, Guid targetTableId,
        ImportRunTrigger trigger, DateTimeOffset? firedAt, CancellationToken ct)
    {
        // Columnas de la tabla destino: nombre -> id (mismo mapa que arma el /run de la Config API).
        var cols = await db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == targetTableId)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cols) { byName[c.Name] = c.Id; }

        // El cast es seguro: ImportRunMode espeja EXACTAMENTE a ApiImportMode (mismos valores/orden).
        var mode = (ApiImportMode)process.Mode;
        var plan = ConnectorRunPlanner.Build(connector.Id, targetTableId, connector.MappingJson, byName, mode, process.KeyColumn);
        if (!plan.Ok) { return new(false, null, plan.Error ?? "No se pudo planear la corrida del conector."); }

        var correlationId = AgentImportService.NewCorrelationId();
        var window = firedAt ?? DateTimeOffset.UtcNow;

        var runId = await runLog.OpenAsync(process.Id, trigger, window, correlationId, ct);
        if (runId is null)
        {
            // Otra instancia del worker ya tomo esta ventana (indice unico). No es un fallo.
            return new(false, null, "Esa ejecucion ya la tomo otro proceso.");
        }

        ApiImportOutcome outcome;
        try
        {
            // actorUserId = SystemActor (Guid.Empty): el disparo programado no tiene usuario, igual que el /run.
            outcome = await api.ImportAsync(plan.Request!, Guid.Empty, ct);
        }
        catch (Exception ex)
        {
            await runLog.FailAsync(runId.Value, $"No se pudo importar del API: {ex.Message}", ct);
            return new(false, correlationId, $"No se pudo importar del API: {ex.Message}");
        }

        var detail = outcome.Errors.Count > 0 ? string.Join(" | ", outcome.Errors) : null;
        await runLog.CloseAsync(correlationId, outcome.Success, outcome.Inserted, outcome.Updated, outcome.Deleted, detail, ct);

        // LastRunAt marca el disparo; server-direct no espera a nadie, asi que PendingSince no aplica.
        var tracked = await db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == process.Id, ct);
        if (tracked is not null)
        {
            tracked.LastRunAt = window;
            tracked.PendingSince = null;
            await db.SaveChangesAsync(ct);
        }

        var msg = outcome.Success
            ? $"Importadas: {outcome.Inserted} nueva(s), {outcome.Updated} actualizada(s), {outcome.Deleted} borrada(s)."
            : (detail ?? "La importacion no trajo resultados.");
        return new(outcome.Success, correlationId, msg);
    }

    /// <summary>Parquea la programacion "esperando al agente", sin pisar una espera anterior (el
    /// PendingSince mas viejo es el que interesa mostrar).</summary>
    private async Task SetPendingSinceAsync(Guid processId, DateTimeOffset window, CancellationToken ct)
    {
        var tracked = await db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (tracked is not null && tracked.PendingSince is null)
        {
            tracked.PendingSince = window;
            await db.SaveChangesAsync(ct);
        }
    }
}
