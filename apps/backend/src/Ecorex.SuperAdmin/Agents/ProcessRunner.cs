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
        if (process.ClientId is not Guid clientRowId)
        {
            return new(false, null, "Esta programacion no tiene cliente remoto asignado: eligelo para que la ejecute un agente.");
        }
        if (process.ConnectorId is not Guid connectorId)
        {
            return new(false, null, "Esta programacion no tiene conector: elige de que fuente trae los datos.");
        }

        var client = await db.DataClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientRowId, ct);
        if (client is null) { return new(false, null, "El cliente remoto ya no existe."); }

        var connector = await db.DataConnectors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectorId, ct);
        if (connector is null) { return new(false, null, "El conector ya no existe."); }
        if (connector.Kind != ConnectorKind.Database)
        {
            return new(false, null, $"Solo los conectores de tipo Base de datos se traen via agente (este es {connector.Kind}).");
        }
        if (connector.ContainerId is not Guid targetTableId)
        {
            return new(false, null, "El conector no tiene TABLA destino: eligela en el conector para poder refrescar solo.");
        }
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

        var spec = new ConnectorSpec(
            Kind: "Database",
            DbEngine: connector.DbEngine?.ToString(),
            Host: connector.Host,
            Port: connector.Port,
            Database: connector.DatabaseName,
            Username: connector.Username,
            // ADR-0040: la credencial VIAJA. Se descifra aqui y va en el mensaje. Si el conector no
            // tiene, se manda null y el agente usa su cadena local (opcion b).
            Secret: connector.CredentialsEncrypted is { } enc ? protector.Unprotect(enc) : null);

        try
        {
            // "Actualizar datos" es un REFRESCO: la tabla queda igual a la fuente. Append acumularia
            // duplicados en cada pulsacion, que no es lo que espera quien pulsa "actualizar".
            await imports.DispatchFetchAsync(
                client.ClientId, tenantId, targetTableId, mapping,
                ApiImportMode.Replace, keyColumnId: null,
                connector.Query!, spec, ct, correlationId);
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
