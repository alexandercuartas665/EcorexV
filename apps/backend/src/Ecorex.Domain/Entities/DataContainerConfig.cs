using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Conector de una fuente externa para un contenedor (la columna derecha del configurador:
/// "procesos de importacion"). Define COMO y DESDE DONDE se traen los datos: endpoint REST,
/// tipo de auth y credenciales CIFRADAS (DataProtection, nunca en claro), y el mapeo de la
/// estructura de la fuente a los campos del contenedor (incluye rutas JSON anidadas para
/// aterrizar payloads con matrices, ej. Alegra). Solo CONFIGURACION en esta fase (sin ejecutor).
/// TENANT-SCOPED. Vive y muere con el contenedor.
/// </summary>
public class DataConnector : TenantEntity
{
    /// <summary>Contenedor (modelo) que alimenta este conector. Reemplaza a ContainerId del diseno anterior.</summary>
    public Guid? ModelId { get; set; }
    public DataModel? Model { get; set; }

    /// <summary>DEPRECADO por el rediseno (los conectores ahora cuelgan del modelo, no de una tabla).</summary>
    public Guid? ContainerId { get; set; }
    public DataContainer? Container { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Esquema de alimentacion: Excel, API REST o Base de datos.</summary>
    public ConnectorKind Kind { get; set; } = ConnectorKind.RestApi;

    // ---- API REST (Kind == RestApi) ----
    /// <summary>Endpoint REST de la fuente.</summary>
    public string? EndpointUrl { get; set; }
    /// <summary>Metodo HTTP (GET/POST...). Texto libre corto.</summary>
    public string? HttpMethod { get; set; }
    public ConnectorAuthKind AuthKind { get; set; } = ConnectorAuthKind.None;

    // ---- Base de datos (Kind == Database) ----
    public DbEngine? DbEngine { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? Username { get; set; }

    /// <summary>Credenciales cifradas (protegidas con ISecretProtector). NUNCA en claro.</summary>
    public string? CredentialsEncrypted { get; set; }

    /// <summary>Mapeo estructura-fuente -> tablas/campos del contenedor (jsonb). Soporta rutas anidadas.</summary>
    public string? MappingJson { get; set; }

    /// <summary>
    /// Consulta SOLO-LECTURA que trae los datos, para conectores de tipo Database (ej.
    /// <c>SELECT id, nombre FROM ciudades</c>). Es el unico dato que faltaba para poder refrescar sin
    /// preguntarle nada al operador.
    ///
    /// Vive en el CONECTOR y no en la programacion porque es parte de "de donde salen los datos",
    /// igual que el host y la base: dos horarios sobre la misma fuente traen lo mismo.
    ///
    /// El agente la ejecuta parametrizada y su `QueryGuard` rechaza lo que no sea SELECT: aunque el
    /// servidor estuviera comprometido, no podria escribir en la base del cliente.
    /// </summary>
    public string? Query { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Cliente (identidad del conector remoto) del tenant. Un cliente es un agente que corre en un
/// equipo remoto y empuja datos al sistema via webhook; se autentica con <see cref="ClientId"/>
/// (publico) + un secreto CIFRADO. Un mismo cliente puede alimentar varios contenedores (via
/// <see cref="ImportProcess"/>). En esta fase solo se CONFIGURA (el cliente remoto y el endpoint
/// receptor se documentan aparte para otra sesion). TENANT-SCOPED.
/// </summary>
public class DataClient : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Identificador publico del cliente (unico por tenant), usado en la auth del webhook.</summary>
    public string ClientId { get; set; } = null!;

    /// <summary>Secreto del cliente cifrado (DataProtection). Se muestra una sola vez al generarlo.</summary>
    public string? ClientSecretEncrypted { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Proceso de importacion: liga un contenedor con su conector y (opcional) un cliente remoto, y
/// define CUANDO corre (reglas de tiempo). Lo ejecuta `ImportSchedulerWorker` llamando al MISMO
/// `IProcessRunner` que el boton "Actualizar datos", y cada corrida deja un <see cref="ImportRun"/>.
/// TENANT-SCOPED. Vive y muere con el contenedor.
/// </summary>
public class ImportProcess : TenantEntity
{
    /// <summary>Contenedor (modelo) del proceso. Reemplaza a ContainerId del diseno anterior.</summary>
    public Guid? ModelId { get; set; }
    public DataModel? Model { get; set; }

    /// <summary>DEPRECADO por el rediseno (los procesos ahora cuelgan del modelo).</summary>
    public Guid? ContainerId { get; set; }
    public DataContainer? Container { get; set; }

    /// <summary>Conector (fuente) que usa este proceso. NO ACTION.</summary>
    public Guid? ConnectorId { get; set; }
    public DataConnector? Connector { get; set; }

    /// <summary>Cliente remoto que ejecuta este proceso (para webhook). NO ACTION.</summary>
    public Guid? ClientId { get; set; }
    public DataClient? Client { get; set; }

    /// <summary>
    /// Si esta puesto, este proceso NO refresca un contenedor via conector: PROGRAMA UN FLUJO de
    /// extraccion (modulo 000730, Ola 5). El dispatcher lo detecta y, en vez del runner de importacion,
    /// dispara `IBrowserRunService.RunFlowNowAsync`, cuya corrida vive en `ScrapeFlowRun` (ADR-0042), no
    /// en `ImportRun`. Referencia SUAVE (sin FK): reusa la carcasa de programacion (recurrencia,
    /// NextRunAt, PendingSince, worker) sin acoplar el modelo de horarios al de flujos ni crear rutas de
    /// cascada nuevas. Un proceso de flujo tiene `ModelId`/`ConnectorId` nulos y este puesto.
    /// </summary>
    public Guid? FlowId { get; set; }

    public string Name { get; set; } = null!;

    public ImportScheduleKind ScheduleKind { get; set; } = ImportScheduleKind.Manual;

    /// <summary>Minutos entre corridas (para Interval).</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>Expresion cron (para Cron).</summary>
    public string? CronExpression { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Ultima corrida registrada.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Cuando toca la proxima corrida (UTC). Es lo que barre el worker, y por eso esta indexado: sin
    /// esto habria que recalcular la recurrencia de TODAS las programaciones en cada pasada.
    /// null = no programada (Manual, inactiva, o no se pudo calcular).
    /// </summary>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>
    /// Por que se desactivo sola (cron invalido, por ejemplo). Se muestra al operador: una
    /// programacion que deja de disparar sin decir por que es peor que una que falla ruidosamente.
    /// </summary>
    public string? DisabledReason { get; set; }

    /// <summary>
    /// Desde cuando esta ESPERANDO a que su agente vuelva (UTC). Se pone cuando el horario dispara y el
    /// agente esta caido, y se limpia en cuanto se le alcanza. Es lo que hace que el worker sepa "esta
    /// programacion tiene una carga pendiente por reintentar cuando el agente reconecte", sin recorrer
    /// la bitacora entera; por eso esta indexado. null = no esta esperando a nadie.
    /// </summary>
    public DateTimeOffset? PendingSince { get; set; }
}

/// <summary>
/// Bitacora de UNA corrida de importacion: quien la disparo, cuando, y en que quedo. Es lo que hace
/// operable el horario: una programacion corre sin nadie mirando, y sin este registro un fallo de
/// madrugada es indistinguible de "no habia datos nuevos".
///
/// Mismo patron que <c>ScheduledJobRun</c> del modulo 000889, incluido lo importante: el indice
/// UNICO (TenantId, ProcessId, FiredAt) es lo que da IDEMPOTENCIA. Si dos instancias del worker
/// barren la misma ventana, la segunda choca al guardar y se descarta, en vez de pedirle al agente
/// el mismo refresco dos veces.
///
/// Ojo con el ciclo de vida: una corrida NACE en <see cref="ImportRunResult.Running"/> porque el
/// resultado no se sabe al despachar; lo trae el agente despues, por el canal. La cierra
/// <c>IImportRunLog.CloseAsync</c> contra el <see cref="CorrelationId"/>.
/// TENANT-SCOPED.
/// </summary>
public class ImportRun : TenantEntity
{
    /// <summary>Programacion que corrio. Restrict: la bitacora SOBREVIVE al proceso que la genero
    /// (borrar la programacion no debe borrar su historia).</summary>
    public Guid ProcessId { get; set; }
    public ImportProcess? Process { get; set; }

    /// <summary>
    /// La VENTANA que se ejecuta, no "ahora": para una corrida programada es la hora a la que TOCABA.
    /// Es asi a proposito, porque es la mitad de la clave de idempotencia; si guardara el instante
    /// real, dos workers en la misma ventana no chocarian y ambos dispararian.
    /// </summary>
    public DateTimeOffset FiredAt { get; set; }

    public ImportRunTrigger Trigger { get; set; }

    public ImportRunResult Result { get; set; } = ImportRunResult.Running;

    /// <summary>Orden que se le mando al agente. Es la unica forma de cerrar esta corrida cuando la
    /// respuesta llega (asincrona, por el hub), y de rastrearla en los logs del agente.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Cuando dejo de estar Running (null mientras corre).</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }

    /// <summary>El porque, en cristiano, para el operador. En error, el mensaje del fallo.</summary>
    public string? Detail { get; set; }
}
