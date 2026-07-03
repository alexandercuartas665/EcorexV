using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Documento adjunto a un lead (pasaporte, cotizacion, voucher, etc.). Entidad TENANT-SCOPED.
/// El binario vive en wwwroot/uploads/leads; aqui se guarda solo la metadata.
/// </summary>
public class LeadFile : TenantEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }

    /// <summary>Nombre original del archivo subido.</summary>
    public string FileName { get; set; } = null!;

    /// <summary>Ruta local servible (ej. /uploads/leads/lead-xxxx.pdf).</summary>
    public string Url { get; set; } = null!;

    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
}
