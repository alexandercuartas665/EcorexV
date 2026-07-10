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
    /// <summary>Referencia N:1 a un registro de OTRA tabla independiente (llave foranea/lookup).
    /// La columna apunta a un contenedor raiz via ReferencedContainerId; la celda guarda el id del
    /// registro destino.</summary>
    Reference,
    /// <summary>Relacion N:N con otra tabla independiente: un registro puede vincularse a varios
    /// registros de la tabla destino. Los vinculos se guardan en <see cref="DataContainerLink"/>.</summary>
    RelationMany
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

/// <summary>Como se programa la corrida de un proceso de importacion (solo config; sin ejecutor aun).</summary>
public enum ImportScheduleKind
{
    /// <summary>Solo bajo demanda (no automatico).</summary>
    Manual,
    /// <summary>Cada N minutos.</summary>
    Interval,
    /// <summary>Expresion cron.</summary>
    Cron
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
