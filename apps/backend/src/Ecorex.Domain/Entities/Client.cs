using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Ficha del cliente del salon (Modelo de Datos seccion 7). Entidad TENANT-SCOPED.
/// Acumula historial y contadores para metricas de puntualidad y no-show.
/// </summary>
public class Client : TenantEntity
{
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public Guid? PreferredResourceId { get; set; }
    public string? PreferencesJson { get; set; }
    /// <summary>Valores de los campos configurables del cliente (jsonb), indexados por FieldKey.</summary>
    public string? FieldValuesJson { get; set; }
    /// <summary>Canales / unidades de negocio a las que pertenece el cliente (jsonb, lista de ids). Multi-canal.</summary>
    public string? BusinessUnitIdsJson { get; set; }
    public int VisitCount { get; set; }
    public int NoShowCount { get; set; }
    public int OnTimeCount { get; set; }
    public int LateCount { get; set; }
    public DateTimeOffset? LastVisitAt { get; set; }
    public bool WhatsAppOptIn { get; set; } = true;
}
