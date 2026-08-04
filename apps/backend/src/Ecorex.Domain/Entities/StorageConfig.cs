using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Configuracion GLOBAL de almacenamiento de archivos de la plataforma (Super Admin SaaS). Singleton.
/// Cuando esta habilitada y valida, los binarios (documentos, adjuntos) se guardan en Azure Blob
/// Storage en vez del disco local; si no, se usa el disco (wwwroot/uploads) como hasta ahora.
///
/// El aislamiento por tenant se mantiene por prefijo de carpeta dentro del contenedor (igual que en
/// disco). La cadena de conexion se guarda CIFRADA (ISecretProtector) y nunca se expone ni se loggea.
/// </summary>
public class StorageConfig : BaseEntity
{
    /// <summary>Proveedor de almacenamiento. Hoy solo "AzureBlob" (o "Disk" implicito si deshabilitado).</summary>
    public string Provider { get; set; } = "AzureBlob";

    /// <summary>Cadena de conexion del proveedor, cifrada en reposo (nunca en claro ni versionada).</summary>
    public string? ConnectionStringEncrypted { get; set; }

    /// <summary>Nombre del contenedor/bucket donde se guardan los archivos.</summary>
    public string? ContainerName { get; set; }

    /// <summary>Si esta habilitado el almacenamiento en blob (si no, se usa disco local).</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Ultima vez que la "prueba de conexion" fue exitosa.</summary>
    public DateTimeOffset? LastValidatedAt { get; set; }
}
