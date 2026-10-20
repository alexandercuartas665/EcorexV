using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Valor de un campo dinamico (<see cref="RequerimientoFieldDefinition"/>) para un "dueno" del modulo
/// Requerimientos (Capa 10, Ola 12). El dueno es una necesidad (RequerimientoCompra) o una linea de
/// planificacion (RequerimientoCompraHijo) segun el Scope de la definicion; por eso <see cref="OwnerId"/>
/// es polimorfico (no hay FK a una tabla concreta). EAV: una fila por (dueno, definicion). El valor se
/// guarda como texto y se tipa al leer. Clave logica: (TenantId, OwnerId, FieldDefinitionId).
/// </summary>
public sealed class RequerimientoFieldValue : TenantEntity
{
    /// <summary>Id del dueno: una RequerimientoCompra (Scope=Padre) o un RequerimientoCompraHijo (Scope=Hija).</summary>
    public Guid OwnerId { get; set; }

    public Guid FieldDefinitionId { get; set; }
    public RequerimientoFieldDefinition? FieldDefinition { get; set; }

    /// <summary>Valor como texto (numeros/fechas/bool se serializan y se tipan al leer).</summary>
    public string? Value { get; set; }
}
