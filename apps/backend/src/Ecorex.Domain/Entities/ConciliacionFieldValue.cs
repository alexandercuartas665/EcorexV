using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Valor de un campo dinamico (<see cref="ConciliacionFieldDefinition"/>) para un renglon de conciliacion.
/// EAV: una fila por (renglon, definicion). El valor se guarda como texto y se tipa al leer. Clave logica:
/// (TenantId, RenglonId, FieldDefinitionId).
/// </summary>
public sealed class ConciliacionFieldValue : TenantEntity
{
    public Guid RenglonId { get; set; }
    public ConciliacionDianRenglon? Renglon { get; set; }

    public Guid FieldDefinitionId { get; set; }
    public ConciliacionFieldDefinition? FieldDefinition { get; set; }

    /// <summary>Valor como texto (numeros/fechas/bool se serializan y se tipan al leer).</summary>
    public string? Value { get; set; }
}
