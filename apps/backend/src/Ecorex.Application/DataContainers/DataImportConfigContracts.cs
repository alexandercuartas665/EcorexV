using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

// ==== Configuracion de importacion (columna derecha del configurador), a nivel de CONTENEDOR (modelo) ====
// Solo CONFIGURACION en esta fase: se guarda el "que/desde donde/como/cuando/quien/a-donde", sin motor
// de ejecucion. Las credenciales viajan en claro SOLO al guardar (input) y se cifran con ISecretProtector;
// NUNCA se devuelven en claro.

// ---- Conector (fuente que alimenta el contenedor): Excel / API REST / Base de datos ----

public sealed record DataConnectorDto(
    Guid Id,
    Guid ModelId,
    string Name,
    ConnectorKind Kind,
    // API REST
    string? EndpointUrl,
    string? HttpMethod,
    ConnectorAuthKind AuthKind,
    // Base de datos
    DbEngine? DbEngine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    /// <summary>
    /// El SELECT que trae los datos. Va con la fuente (y no en el proceso) porque es parte de "de donde
    /// salen los datos": el mismo conector siempre trae lo mismo, lo dispare el boton o el horario.
    /// </summary>
    string? Query,
    // Comun
    bool HasCredentials,
    string? MappingJson,
    bool IsActive,

    /// <summary>
    /// TABLA que alimenta este conector (`DataContainer`), o null si aun no se eligio. Hasta ahora el
    /// conector solo sabia de que CONTENEDOR era (<see cref="ModelId"/>) y la tabla se elegia al
    /// pulsar "Importar": la ficha no podia decir a donde van los datos. Guardarla aqui es ademas lo
    /// que permite refrescar sin preguntar (boton "Actualizar datos" y, mas adelante, el scheduler).
    /// </summary>
    Guid? ContainerId = null,

    /// <summary>Nombre de esa tabla, para pintarlo sin otra consulta.</summary>
    string? ContainerName = null);

/// <summary>Alta/edicion de un conector. Credentials en claro (input); se cifra al persistir.
/// Si Credentials es null en edicion, se conservan las existentes.</summary>
public sealed record SaveDataConnectorRequest(
    Guid? Id,
    Guid ModelId,
    string Name,
    ConnectorKind Kind,
    string? EndpointUrl,
    string? HttpMethod,
    ConnectorAuthKind AuthKind,
    DbEngine? DbEngine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    string? Credentials,
    string? Query,
    string? MappingJson,
    bool IsActive = true,

    /// <summary>Tabla destino. La columna `container_id` ya existia en la BD sin usarse: no hay migracion.</summary>
    Guid? ContainerId = null);

// ---- Destino (a donde el cliente deja los datos): sistema o BD aliada ----

public sealed record DataDestinationDto(
    Guid ModelId,
    DestinationKind Kind,
    DbEngine? DbEngine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    bool HasCredentials);

public sealed record SaveDataDestinationRequest(
    Guid ModelId,
    DestinationKind Kind,
    DbEngine? DbEngine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    string? Credentials);

// ---- Cliente remoto (identidad para webhooks) ----

public sealed record DataClientDto(
    Guid Id,
    string Name,
    string? Description,
    string ClientId,
    bool HasSecret,
    bool IsActive);

/// <summary>Resultado de crear/rotar un cliente: incluye el secreto en claro UNA sola vez.</summary>
public sealed record DataClientSecretDto(Guid Id, string ClientId, string ClientSecret);

public sealed record SaveDataClientRequest(
    Guid? Id,
    string Name,
    string? Description,
    bool IsActive = true);

// ---- Proceso de importacion (horario), a nivel de contenedor ----

public sealed record ImportProcessDto(
    Guid Id,
    Guid ModelId,
    Guid? ConnectorId,
    string? ConnectorName,
    Guid? ClientId,
    string? ClientName,
    string Name,
    ImportScheduleKind ScheduleKind,
    int? IntervalMinutes,
    string? CronExpression,
    bool IsActive,
    DateTimeOffset? LastRunAt,
    /// <summary>Cuando corre la proxima vez (UTC). null = no programada.</summary>
    DateTimeOffset? NextRunAt = null,
    /// <summary>Por que se apago sola (ej. cron invalido).</summary>
    string? DisabledReason = null,
    /// <summary>Desde cuando espera a que su agente vuelva (UTC). null = no espera a nadie.</summary>
    DateTimeOffset? PendingSince = null);

/// <summary>Una corrida de la bitacora, para pintarla.</summary>
public sealed record ImportRunDto(
    Guid Id,
    DateTimeOffset FiredAt,
    ImportRunTrigger Trigger,
    ImportRunResult Result,
    int Inserted,
    int Updated,
    int Deleted,
    string? Detail,
    DateTimeOffset? FinishedAt);

public sealed record SaveImportProcessRequest(
    Guid? Id,
    Guid ModelId,
    Guid? ConnectorId,
    Guid? ClientId,
    string Name,
    ImportScheduleKind ScheduleKind,
    int? IntervalMinutes,
    string? CronExpression,
    bool IsActive = true);

/// <summary>
/// CRUD de la configuracion de importacion de un CONTENEDOR (DataModel): conectores (Excel/API/BD con
/// credenciales cifradas), destino (sistema o BD aliada), clientes remotos (ClientId + secreto cifrado)
/// y procesos (horarios). Tenant-scoped por filtro global.
/// </summary>
public interface IDataImportConfigService
{
    // Conectores (por contenedor/modelo).
    Task<IReadOnlyList<DataConnectorDto>> ListConnectorsAsync(Guid modelId, CancellationToken ct = default);
    Task<DataConnectorDto?> SaveConnectorAsync(SaveDataConnectorRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteConnectorAsync(Guid connectorId, Guid actorUserId, CancellationToken ct = default);

    // Destino (1:1 con el contenedor).
    Task<DataDestinationDto?> GetDestinationAsync(Guid modelId, CancellationToken ct = default);
    Task<DataDestinationDto?> SaveDestinationAsync(SaveDataDestinationRequest req, Guid actorUserId, CancellationToken ct = default);

    // Clientes (por tenant).
    Task<IReadOnlyList<DataClientDto>> ListClientsAsync(CancellationToken ct = default);
    /// <summary>Crea o edita un cliente. Si es nuevo, genera ClientId + secreto y lo devuelve UNA vez.</summary>
    Task<(DataClientDto Client, DataClientSecretDto? Secret)> SaveClientAsync(SaveDataClientRequest req, Guid actorUserId, CancellationToken ct = default);
    /// <summary>Rota el secreto de un cliente y devuelve el nuevo en claro UNA vez.</summary>
    Task<DataClientSecretDto?> RotateClientSecretAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteClientAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default);

    // Procesos de importacion (por contenedor/modelo).
    Task<IReadOnlyList<ImportProcessDto>> ListProcessesAsync(Guid modelId, CancellationToken ct = default);
    Task<ImportProcessDto?> SaveProcessAsync(SaveImportProcessRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteProcessAsync(Guid processId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Ultimas corridas de una programacion, de la mas reciente a la mas vieja.</summary>
    Task<IReadOnlyList<ImportRunDto>> ListRunsAsync(Guid processId, int take = 10, CancellationToken ct = default);
}
