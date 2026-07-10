using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Columna/estado configurable de la "Bolsa de contactos" del Gestor de Clientes (000740):
/// el kanban por el que se mueven los Terceros (Sospechoso -> Incubadora -> Clientes ->
/// Seguimiento Cotizacion -> Cierre...). Editable por el usuario ("Modificar estados").
/// TENANT-SCOPED. Un Tercero apunta a la columna en la que esta (<see cref="Tercero.BolsaColumnaId"/>).
/// </summary>
public class BolsaColumna : TenantEntity
{
    public string Nombre { get; set; } = null!;

    /// <summary>Color del encabezado/borde de la columna (token CSS var, ej. "--t-blue").</summary>
    public string Color { get; set; } = "--t-slate";

    public int SortOrder { get; set; }

    /// <summary>Marca la columna terminal "Cliente ganado" (equivale a promover el tercero a Cliente).</summary>
    public bool EsCliente { get; set; }

    public bool IsArchived { get; set; }
}
