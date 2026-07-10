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
/// define CUANDO corre (reglas de tiempo). Solo CONFIGURACION en esta fase: se guarda el horario,
/// no hay motor que lo ejecute todavia (eso lo consumira el cliente/worker que se documenta aparte).
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

    public string Name { get; set; } = null!;

    public ImportScheduleKind ScheduleKind { get; set; } = ImportScheduleKind.Manual;

    /// <summary>Minutos entre corridas (para Interval).</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>Expresion cron (para Cron).</summary>
    public string? CronExpression { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Ultima corrida registrada (para el futuro ejecutor). Solo lectura por ahora.</summary>
    public DateTimeOffset? LastRunAt { get; set; }
}
