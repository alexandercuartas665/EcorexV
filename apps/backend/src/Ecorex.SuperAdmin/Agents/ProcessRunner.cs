using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Resultado de disparar una programacion, para que la UI diga algo util.</summary>
public sealed record RunProcessResult(bool Ok, string? CorrelationId, string Message);

/// <summary>
/// Ejecuta AHORA una programacion (`ImportProcess`) que corre via agente. Es lo que hay detras del
/// boton "Actualizar datos", y sera lo MISMO que llame el scheduler cuando exista (Ola 4): si el
/// horario hiciera su propia version, las dos acabarian comportandose distinto.
///
/// El reparto del modelo (que ya existia, no se invento aqui):
///   - `DataConnector`  -> de donde salen los datos (host/base/credencial), la CONSULTA, y a que
///                         TABLA del contenedor alimentan.
///   - `ImportProcess`  -> que conector + que cliente remoto lo ejecuta + cada cuanto.
/// Por eso "via agente" no es un campo: es una programacion que tiene cliente.
/// </summary>
public interface IProcessRunner
{
    Task<RunProcessResult> RunNowAsync(Guid processId, CancellationToken ct = default);
}

public sealed class ProcessRunner(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    ISecretProtector protector,
    IAgentRegistry registry,
    IAgentImportService imports) : IProcessRunner
{
    public async Task<RunProcessResult> RunNowAsync(Guid processId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not Guid tenantId)
        {
            return new(false, null, "Sin tenant en contexto.");
        }

        var process = await db.ImportProcesses.AsNoTracking().FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (process is null) { return new(false, null, "La programacion no existe."); }

        // Cada "no" explica QUE falta y donde arreglarlo: este boton lo pulsa un operador, no un dev,
        // y un "no se pudo" seco lo deja sin saber que hacer.
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

        // Se comprueba ANTES de despachar: si no hay agente, el mensaje del hub se perderia en el
        // vacio y el operador se quedaria esperando sin saber por que.
        if (!registry.IsOnline(client.ClientId))
        {
            return new(false, null, $"El agente '{client.Name}' no esta conectado. Abre la colmena en el equipo o revisa su configuracion.");
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

        // "Actualizar datos" es un REFRESCO: la tabla queda igual a la fuente. Append acumularia
        // duplicados en cada pulsacion, que no es lo que espera quien pulsa "actualizar".
        var corr = await imports.DispatchFetchAsync(
            client.ClientId, tenantId, targetTableId, mapping,
            ApiImportMode.Replace, keyColumnId: null,
            connector.Query!, spec, ct);

        return new(true, corr, $"Orden enviada al agente '{client.Name}'. Trayendo datos...");
    }
}
