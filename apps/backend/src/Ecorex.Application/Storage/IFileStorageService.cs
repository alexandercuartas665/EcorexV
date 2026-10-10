namespace Ecorex.Application.Storage;

/// <summary>Datos de un archivo almacenado (sin el binario). Lo que ven los modulos.</summary>
public sealed record StoredFileDto(
    Guid Id, string Module, string? Ref1, string? Ref2, string FileName,
    string? ContentType, long SizeBytes, DateTimeOffset CreatedAt, string Provider)
{
    /// <summary>URL para descargar/ver el archivo (proxy por el server; contenedor privado).</summary>
    public string Url => $"/archivo/{Id}";
}

/// <summary>Peticion para guardar un binario. El servicio arma la ruta, sube a blob (o disco) y registra.</summary>
public sealed record FileStorageSaveRequest(
    string Module, string? Ref1, string? Ref2, string FileName, string? ContentType, byte[] Content);

/// <summary>Contenido de un archivo para servirlo (descarga).</summary>
public sealed record FileStorageContent(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Gestor UNIFICADO y transversal del almacenamiento de archivos del sistema (Azure Blob con fallback a disco).
/// Todo modulo que suba/lea binarios pasa por aqui: deja una fila en StoredFile (llave = Id) y el binario en
/// el proveedor activo. Multi-tenant por el filtro global (un tenant no lee archivos de otro). La implementacion
/// vive en la capa de presentacion porque usa el SDK de Azure y WebRootPath.
/// </summary>
public interface IFileStorageService
{
    /// <summary>Guarda el binario, registra el StoredFile y devuelve sus metadatos (incluida la llave Id y la Url).</summary>
    Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default);

    /// <summary>Lista los archivos de un modulo, opcionalmente filtrando por Ref1 (p. ej. todos los de un CUFE) y Ref2.</summary>
    Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default);

    /// <summary>Metadatos de un archivo por su llave (o null si no existe / no es del tenant).</summary>
    Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Contenido de un archivo por su llave, para servirlo (o null si no existe / no es del tenant).</summary>
    Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Elimina el archivo (binario + registro). Devuelve true si existia.</summary>
    Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Busca un archivo ya registrado por su clave logica (module+ref1+ref2+fileName). Para idempotencia
    /// (no re-subir dos veces el mismo documento). Null si no existe.</summary>
    Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default);
}
