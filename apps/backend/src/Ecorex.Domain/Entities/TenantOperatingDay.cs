using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Dia NO operativo del tenant (Fase 1 - plazos de flujo): un festivo o dia sin operacion que el modo "habil"
/// de los plazos debe SALTAR (ademas de sabados y domingos). Se administra en la configuracion de la entidad.
/// La fecha es en la zona horaria del tenant. Unico por (TenantId, Date).
/// </summary>
public class TenantOperatingDay : TenantEntity
{
    /// <summary>Fecha no operativa (dia local del tenant).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Motivo legible (ej. "Dia de la Independencia"). Opcional.</summary>
    public string? Reason { get; set; }
}
