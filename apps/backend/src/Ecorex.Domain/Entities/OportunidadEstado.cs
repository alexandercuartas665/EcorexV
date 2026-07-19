using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Etapa CONFIGURABLE del pipeline de Oportunidades del CRM (000740). Cada tenant define las suyas
/// (nombre, orden, color y tipo Abierta/Ganada/Perdida). Reemplaza al enum fijo OportunidadEtapa:
/// cada <see cref="Oportunidad"/> apunta a un OportunidadEstado por FK. TENANT-SCOPED (filtro global).
/// Se administran en la pagina "Estados de oportunidad" del grupo CRM.
/// </summary>
public class OportunidadEstado : TenantEntity
{
    /// <summary>Nombre visible de la etapa (ej. "Nueva", "Propuesta enviada", "Ganada").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Orden en el pipeline (0 = primera columna del kanban).</summary>
    public int SortOrder { get; set; }

    /// <summary>Token de color del tema (ej. "--t-blue", "--t-green") o hex; para el chip/columna.</summary>
    public string Color { get; set; } = "--t-slate";

    /// <summary>Abierta / Ganada / Perdida: gobierna los KPIs y si la oportunidad esta cerrada.</summary>
    public OportunidadEstadoTipo Tipo { get; set; } = OportunidadEstadoTipo.Abierta;

    /// <summary>Archivada (soft): no se ofrece para nuevas oportunidades pero conserva historicas.</summary>
    public bool IsArchived { get; set; }
}
