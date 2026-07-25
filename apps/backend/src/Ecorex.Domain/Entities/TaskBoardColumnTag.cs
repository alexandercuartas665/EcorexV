using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Etiqueta PERMITIDA en una columna de un tablero de actividades. Restringe que etiquetas
/// (<see cref="TaskItemTag"/>, catalogo por tenant) se pueden poner a una tarjeta segun la
/// columna en la que este.
///
/// Regla de la UI (no del modelo): una columna SIN filas aqui no restringe nada, se permiten
/// todas las etiquetas del catalogo. Asi los tableros que ya existen siguen funcionando igual
/// hasta que alguien decida acotar una columna.
///
/// La etiqueta sigue siendo del catalogo del tenant (reutilizable entre tableros): esto solo
/// dice "esta etiqueta es valida AQUI", no crea una etiqueta nueva por columna.
/// TENANT-SCOPED. Unico por (ColumnId, TagId).
/// </summary>
public class TaskBoardColumnTag : TenantEntity
{
    public Guid ColumnId { get; set; }
    public TaskBoardColumn? Column { get; set; }

    public Guid TagId { get; set; }
    public TaskItemTag? Tag { get; set; }
}
