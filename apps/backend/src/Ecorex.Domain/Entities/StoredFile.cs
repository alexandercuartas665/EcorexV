using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Registro UNIFICADO y transversal de un archivo binario almacenado por el sistema (Azure Blob o disco).
/// Es el equivalente ECOREX del GEN_ARCHIVOS del legacy: cualquier modulo (Conciliacion DIAN, Gestor
/// Documental, items, avatares, chat, plantillas...) guarda sus binarios a traves de IFileStorageService y
/// queda una fila aqui. El <see cref="BaseEntity.Id"/> es la LLAVE del archivo (se usa en /archivo/{id}).
/// Multi-tenant: cada tenant solo ve/accede a los suyos (filtro global). El contenido real vive en
/// <see cref="Provider"/> (AzureBlob privado o disco), nunca en la BD.
/// </summary>
public sealed class StoredFile : TenantEntity
{
    /// <summary>Modulo/dominio dueño del archivo (p. ej. "ConciliacionDian", "GestorDocumental"). Para agrupar/filtrar.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Referencia logica 1 (p. ej. el CUFE de la factura). Lo que relaciona el archivo con una entidad de negocio.</summary>
    public string? Ref1 { get; set; }

    /// <summary>Referencia logica 2 (p. ej. "DIAN_XML", "DIAN_ZIP", "NEWTON_XML", "NEWTON_PDF"). Tipo/origen del archivo.</summary>
    public string? Ref2 { get; set; }

    /// <summary>Nombre original/legible del archivo (para mostrar y al descargar).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME del contenido (p. ej. application/pdf, text/xml). Para servirlo correctamente.</summary>
    public string? ContentType { get; set; }

    /// <summary>Extension saneada (con punto, p. ej. ".pdf").</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>Tamaño en bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Ruta/clave del binario DENTRO del contenedor (blob) o relativa a wwwroot/uploads (disco).
    /// Esquema: {module}/{tenant:N}/{ref1 saneado}/{id}{ext}. Nunca depende del nombre que mando el cliente.</summary>
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>Donde vive el binario: "AzureBlob" o "Local". Permite leer/migrar sin ambiguedad.</summary>
    public string Provider { get; set; } = string.Empty;
}
