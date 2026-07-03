using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Foto de un asesor/estacion (Resource), guardada EN LA BD (bytea) para que persista a redeploys.
/// Tabla aparte para mantener Resource liviano (Resource se consulta en cada turno del agente).
/// Una foto por recurso (unique ResourceId). Se sirve por /media/asesor/{resourceId}.
/// </summary>
public class ResourcePhoto : TenantEntity
{
    public Guid ResourceId { get; set; }
    public byte[] Content { get; set; } = null!;
    public string? ContentType { get; set; }
}
