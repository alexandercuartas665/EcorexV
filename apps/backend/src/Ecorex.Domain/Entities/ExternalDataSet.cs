using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Consulta CURADA sobre una <see cref="ExternalDataSource"/> (ADR-0064). Es el LIMITE DE SEGURIDAD del
/// conector externo: SOLO se ejecutan consultas registradas aqui; jamas SQL libre proveniente del
/// reporte. Cada dataset lleva su <see cref="CommandText"/> (un SELECT parametrizado, curado por el
/// operador de plataforma) y la lista tipada de parametros que admite.
///
/// Entidad de PLATAFORMA (no tenant-scoped): se administra desde PlatformAdmin. El acceso desde un
/// tenant se hereda de la concesion de su <see cref="ExternalDataSource"/>.
/// </summary>
public class ExternalDataSet : BaseEntity
{
    /// <summary>Fuente externa a la que pertenece (FK dura; ambas son entidades de plataforma).</summary>
    public Guid ExternalDataSourceId { get; set; }

    public ExternalDataSource? ExternalDataSource { get; set; }

    /// <summary>Nombre logico del dataset (p.ej. "DataSet1", "ENCARGADOS"). Suele coincidir con el
    /// nombre del dataset dentro del RDL para facilitar el mapeo al importar.</summary>
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>El SELECT parametrizado curado. Los parametros se referencian con la sintaxis del motor
    /// (@nombre en SQL Server, @nombre tambien aceptado por Npgsql). El conector NUNCA concatena valores:
    /// enlaza cada parametro declarado en <see cref="ParametersJson"/> como parametro tipado.</summary>
    public string CommandText { get; set; } = null!;

    /// <summary>JSON con la lista de parametros admitidos (nombre/tipo/binding/clave de contexto). El
    /// conector solo enlaza parametros presentes aqui; cualquier @otro que aparezca en el texto sin
    /// declararse es un error de configuracion, no una via de inyeccion.</summary>
    public string? ParametersJson { get; set; }

    /// <summary>JSON con los metadatos de campos de salida (nombre/tipo), para armar el descriptor de
    /// catalogo sin ejecutar la consulta. Opcional: si falta, el conector infiere el shape al leer.</summary>
    public string? FieldsJson { get; set; }

    /// <summary>Si el dataset esta habilitado para ejecutarse.</summary>
    public bool IsEnabled { get; set; } = true;
}
