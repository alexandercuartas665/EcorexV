using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Categoria de largo de cabello DEFINIDA POR EL SALON (modulo Medidas de cabello, capa 2). TENANT-SCOPED.
/// El salon arma su propia escala (los nombres y cuantas quiera) y sube imagenes de referencia que
/// "le ensenan" a la IA que es cada largo, para luego clasificar la foto de una clienta.
/// </summary>
public class HairLengthCategory : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Descripcion del largo (referencia para la IA y el equipo): "hasta los hombros", etc.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Imagen de referencia de una categoria de largo (archivo en wwwroot/uploads/hair). TENANT-SCOPED.
/// NO son fotos de clientas: son ejemplos del largo, por eso pueden ser publicas.</summary>
public class HairLengthReferenceImage : TenantEntity
{
    public Guid CategoryId { get; set; }
    public HairLengthCategory? Category { get; set; }

    /// <summary>Contenido del archivo guardado EN LA BD (bytea): persiste aunque el contenedor se recree
    /// (Railway tiene disco efimero). Se sirve por /media/hairref/{id}.</summary>
    public byte[]? Content { get; set; }
    public string? ContentType { get; set; }
    public string? FileName { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Resultado de clasificar por IA la foto de una clienta contra las medidas del salon. TENANT-SCOPED.
/// La foto se guarda en almacenamiento PROTEGIDO (fuera de wwwroot): aqui solo el nombre del archivo,
/// servido por un endpoint autorizado. Guardamos el resultado para historial/auditoria.
/// </summary>
public class HairLengthClassification : TenantEntity
{
    /// <summary>Nombre del archivo de la foto en el almacenamiento protegido (no es una URL publica).</summary>
    public string? PhotoFileName { get; set; }

    /// <summary>Categoria que la IA reconocio (si pudo mapearla a una del salon).</summary>
    public Guid? PredictedCategoryId { get; set; }

    /// <summary>Nombre del largo que devolvio la IA (tal cual), por si no mapea a una categoria.</summary>
    public string? PredictedName { get; set; }

    /// <summary>Confianza 0-100 reportada por la IA.</summary>
    public int Confidence { get; set; }

    /// <summary>Motivo/justificacion breve de la IA.</summary>
    public string? Rationale { get; set; }
}
