namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de un campo (columna) de un Contenedor de datos (modulo Contenedor de datos). Portado del
/// DataColumnType del hermano CUBOT.redmanager y ampliado con <see cref="Submodel"/> para soportar
/// estructuras ANIDADAS (una factura con su matriz de items): un campo Submodel apunta a un
/// contenedor hijo cuya estructura se repite por cada fila del padre.
/// </summary>
public enum DataContainerColumnType
{
    Text,
    Number,
    Decimal,
    Date,
    Boolean,
    /// <summary>Campo que contiene una sub-tabla/matriz (contenedor hijo). Habilita el modelo anidado.</summary>
    Submodel,
    /// <summary>DEPRECADO (2026-07-15): las relaciones inter-tabla ahora son una entidad propia
    /// (<c>DataModelRelation</c>, arista del ER) y NO un tipo de columna. Se conserva el valor solo
    /// para que EF pueda leer columnas historicas durante el backfill; ya NO se ofrece en la UI.</summary>
    Reference,
    /// <summary>DEPRECADO (2026-07-15): ver <see cref="Reference"/>. Reemplazado por DataModelRelation N:N.</summary>
    RelationMany
}

/// <summary>
/// Cardinalidad de una relacion inter-tabla del Contenedor de datos (arista del ER, entidad
/// <c>DataModelRelation</c>). Reemplaza el modelado anterior donde la relacion era un TIPO de columna:
/// una relacion es una propiedad ORTOGONAL al tipo de dato de los campos.
/// </summary>
public enum DataModelRelationKind
{
    /// <summary>N:1 — muchos registros de la tabla origen apuntan a uno de la destino (lookup).</summary>
    ManyToOne,
    /// <summary>N:N — cada registro puede vincularse a varios de la tabla destino.</summary>
    ManyToMany
}

/// <summary>
/// Origen de los datos de un Contenedor (como llega la informacion). Manual/Excel son de captura;
/// WebService y WebhookPush habilitan la importacion automatica (solo CONFIGURACION en esta fase;
/// el motor de ejecucion y el cliente remoto se documentan aparte).
/// </summary>
public enum DataSourceKind
{
    /// <summary>Captura manual de filas en la UI.</summary>
    Manual,
    /// <summary>Importacion por archivo Excel (xlsx).</summary>
    Excel,
    /// <summary>El sistema consulta un web service (REST) de la fuente (ej. Alegra).</summary>
    WebService,
    /// <summary>Un cliente remoto empuja los datos al contenedor via webhook.</summary>
    WebhookPush
}

/// <summary>Tipo de autenticacion que un conector necesita para hablar con su fuente externa.</summary>
public enum ConnectorAuthKind
{
    None,
    /// <summary>Clave de API en header o query (ej. Alegra).</summary>
    ApiKey,
    /// <summary>Token portador (Authorization: Bearer ...).</summary>
    Bearer,
    /// <summary>Usuario y contrasena (Basic).</summary>
    Basic
}

/// <summary>Como se programa la corrida de un proceso de importacion.</summary>
public enum ImportScheduleKind
{
    /// <summary>Solo bajo demanda (no automatico).</summary>
    Manual,
    /// <summary>Cada N minutos.</summary>
    Interval,
    /// <summary>Expresion cron (5 campos, hora del tenant). Motor: Cronos (ADR-0041).</summary>
    Cron
}

/// <summary>Quien disparo una corrida de importacion. Distinguirlo importa al leer la bitacora:
/// "fallo a las 3am sola" y "fallo cuando le di al boton" no se diagnostican igual.</summary>
public enum ImportRunTrigger
{
    /// <summary>Alguien pulso "Actualizar datos".</summary>
    Manual,
    /// <summary>La disparo el horario.</summary>
    Scheduled
}

/// <summary>En que quedo una corrida de importacion.</summary>
public enum ImportRunResult
{
    /// <summary>Orden enviada al agente; esperando que devuelva las filas. Estado NO terminal.</summary>
    Running,
    /// <summary>Las filas llegaron y se ingirieron.</summary>
    Ok,
    /// <summary>Fallo: no se pudo despachar, el agente reporto error, o vencio el plazo.</summary>
    Error,
    /// <summary>No habia nada que hacer o no se dispara (ej. ya corria otra igual).</summary>
    Skipped,
    /// <summary>
    /// El agente no estaba conectado a la hora que tocaba. NO es un Error: es un "no llegue a
    /// intentarlo"; la programacion queda esperando y se reintenta sola cuando el agente vuelve. Se
    /// distingue de Error para no ensuciar los KPIs con "fallos" que en realidad son agentes dormidos.
    /// </summary>
    PendingOffline
}

/// <summary>Esquema de alimentacion de un conector: como llega la data al contenedor.</summary>
public enum ConnectorKind
{
    /// <summary>Archivo Excel (xlsx) subido manualmente.</summary>
    Excel,
    /// <summary>API REST (endpoint que devuelve JSON con las estructuras).</summary>
    RestApi,
    /// <summary>Conexion directa a una base de datos externa.</summary>
    Database
}

/// <summary>Motor de base de datos (para conectores de tipo Database y destinos de BD aliada).</summary>
public enum DbEngine
{
    PostgreSql,
    MySql,
    SqlServer,
    Oracle,
    MariaDb,
    SqLite
}

/// <summary>Donde deja los datos el cliente al importar (config para el cliente/motor a futuro).</summary>
public enum DestinationKind
{
    /// <summary>Dentro del propio sistema (las tablas EAV del contenedor).</summary>
    System,
    /// <summary>En una base de datos aliada/externa (motor + credenciales).</summary>
    AlliedDatabase
}
