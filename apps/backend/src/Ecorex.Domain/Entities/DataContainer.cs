using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Contenedor de datos: un modelo (tabla logica) dinamico creado por el usuario. Portado del
/// DataContainer del hermano CUBOT.redmanager y evolucionado a un ARBOL para soportar modelos
/// ANIDADOS: un contenedor puede ser raiz o ser un sub-contenedor (matriz) de otro, enlazado por
/// el campo Submodel del padre (<see cref="ParentContainerId"/> + <see cref="ParentFieldId"/>).
/// Los valores de las filas se guardan como celdas EAV (<see cref="DataContainerCell"/>).
/// TENANT-SCOPED. Se borra en cascada (columnas, filas, celdas y sub-contenedores).
/// </summary>
public class DataContainer : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Contenedor (modelo) al que pertenece esta tabla. Null solo para datos heredados del
    /// diseno anterior (una tabla nueva siempre pertenece a un <see cref="DataModel"/>).</summary>
    public Guid? ModelId { get; set; }
    public DataModel? Model { get; set; }

    /// <summary>Posicion X de la tabla en el lienzo ER del contenedor.</summary>
    public double CanvasX { get; set; }
    /// <summary>Posicion Y de la tabla en el lienzo ER del contenedor.</summary>
    public double CanvasY { get; set; }

    /// <summary>Como llega la informacion a este contenedor (manual, Excel, web service, webhook).
    /// DEPRECADO por el rediseno: la alimentacion ahora se configura por conector a nivel del modelo.</summary>
    public DataSourceKind SourceKind { get; set; } = DataSourceKind.Manual;

    // ---- Anidamiento (submodelos / matrices) ----
    /// <summary>Contenedor padre si este es un sub-contenedor; null si es raiz.</summary>
    public Guid? ParentContainerId { get; set; }
    public DataContainer? ParentContainer { get; set; }

    /// <summary>Campo (tipo Submodel) del padre que ancla este sub-contenedor; null si es raiz.</summary>
    public Guid? ParentFieldId { get; set; }

    public ICollection<DataContainerColumn> Columns { get; set; } = new List<DataContainerColumn>();
    public ICollection<DataContainerRow> Rows { get; set; } = new List<DataContainerRow>();
}

/// <summary>
/// Columna (campo) de un contenedor: su esquema. El tipo define como se captura/valida el valor.
/// Un campo de tipo <see cref="DataContainerColumnType.Submodel"/> NO guarda celdas: su
/// <see cref="ChildContainerId"/> apunta al contenedor hijo cuya estructura se repite por fila.
/// TENANT-SCOPED. Unica por (contenedor, nombre).
/// </summary>
public class DataContainerColumn : TenantEntity
{
    public Guid ContainerId { get; set; }
    public DataContainer? Container { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public DataContainerColumnType Type { get; set; } = DataContainerColumnType.Text;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }

    /// <summary>Solo para Type == Submodel: contenedor hijo que define la sub-tabla anidada.</summary>
    public Guid? ChildContainerId { get; set; }
    public DataContainer? ChildContainer { get; set; }

    /// <summary>Solo para Type == Reference o RelationMany: tabla (contenedor RAIZ) independiente a la
    /// que apunta la relacion. A diferencia de ChildContainerId (composicion/anidado), aqui la tabla
    /// destino existe por si sola y NO se borra en cascada (FK NO ACTION).</summary>
    public Guid? ReferencedContainerId { get; set; }
    public DataContainer? ReferencedContainer { get; set; }
}

/// <summary>
/// Fila de un contenedor. No guarda valores directamente (ver <see cref="DataContainerCell"/>).
/// Para modelos anidados, una fila hija se enlaza a su fila padre via <see cref="ParentRowId"/> +
/// <see cref="ParentFieldId"/> (el arbol de filas espeja el arbol de contenedores). TENANT-SCOPED.
/// </summary>
public class DataContainerRow : TenantEntity
{
    public Guid ContainerId { get; set; }
    public DataContainer? Container { get; set; }

    /// <summary>Fila padre si esta fila pertenece a una sub-tabla anidada; null si es de nivel raiz.</summary>
    public Guid? ParentRowId { get; set; }
    public DataContainerRow? ParentRow { get; set; }

    /// <summary>Campo Submodel del padre bajo el que cuelga esta fila hija; null en raiz.</summary>
    public Guid? ParentFieldId { get; set; }

    public ICollection<DataContainerCell> Cells { get; set; } = new List<DataContainerCell>();
}

/// <summary>
/// Valor de una celda (columna x fila) en el modelo EAV. El valor SIEMPRE se persiste como string;
/// la conversion al tipo real (numero, decimal, fecha, booleano) se hace al leer segun el
/// <see cref="DataContainerColumn.Type"/>. Las columnas Submodel no tienen celdas. TENANT-SCOPED.
/// Unica por (fila, columna).
/// </summary>
public class DataContainerCell : TenantEntity
{
    public Guid RowId { get; set; }
    public DataContainerRow? Row { get; set; }

    public Guid ColumnId { get; set; }
    public DataContainerColumn? Column { get; set; }

    public string? Value { get; set; }
}

/// <summary>
/// Vinculo de una relacion N:N (campo <see cref="DataContainerColumnType.RelationMany"/>). Une un
/// registro DE ESTE contenedor (<see cref="RowId"/>) con un registro de la tabla destino
/// (<see cref="TargetRowId"/>), bajo el campo de relacion (<see cref="ColumnId"/>). Muchos vinculos
/// por (columna, fila). Para N:N CON atributos (ej. cantidad) usar submodelo anidado + Reference.
/// TENANT-SCOPED. Se borra en cascada cuando muere la columna o cualquiera de los dos registros.
/// </summary>
public class DataContainerLink : TenantEntity
{
    /// <summary>Campo de relacion (tipo RelationMany) al que pertenece el vinculo.</summary>
    public Guid ColumnId { get; set; }
    public DataContainerColumn? Column { get; set; }

    /// <summary>Registro del contenedor dueno del campo.</summary>
    public Guid RowId { get; set; }
    public DataContainerRow? Row { get; set; }

    /// <summary>Registro de la tabla destino con el que se vincula.</summary>
    public Guid TargetRowId { get; set; }
    public DataContainerRow? TargetRow { get; set; }
}
