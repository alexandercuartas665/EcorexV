using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>Archivo adjunto a una tarea del nucleo TaskItem. TENANT-SCOPED.</summary>
public class TaskItemAttachment : TenantEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public string FileName { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string? MimeType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Quien lo subio (PlatformUser id).</summary>
    public Guid? UploadedBy { get; set; }
    public string? UploadedByName { get; set; }
}
