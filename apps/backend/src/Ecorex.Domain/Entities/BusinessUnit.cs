using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Unidad / canal de negocio del cliente (capa 2). Entidad TENANT-SCOPED. Clasifica los leads del
/// pipeline (ej. productos B2B, productos al detal, cursos, asesoria de imagen), les da un color y
/// define que modal abre su tarjeta (ModalKind). Configurable por el tenant.
/// </summary>
public class BusinessUnit : TenantEntity
{
    public string Name { get; set; } = null!;
    /// <summary>Color hex para pintar las tarjetas y el filtro.</summary>
    public string Color { get; set; } = "#A03DC9";
    /// <summary>Comportamiento al abrir la tarjeta de un lead de esta unidad.</summary>
    public BusinessUnitModalKind ModalKind { get; set; } = BusinessUnitModalKind.Generic;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
