using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

// ===========================================================================
// Gestor Documental - mitad "Archivo central".
// Portado del modulo 2.15 Documentos del hermano PROPIA. Repositorio del tenant:
// categoria -> carpetas (arbol) -> documentos, con versionado INMUTABLE, etiquetas M:N,
// destacados, bitacora append-only y registro de consumo.
//
// DIFERENCIA DE FONDO CON EL ORIGEN: en PROPIA las categorias y etiquetas "base" son GLOBALES
// (TenantId NULL + EsBase). Aqui eso es imposible: la regla 1 de CLAUDE.md exige TenantId
// obligatorio + filtro global, y una fila con TenantId nulo seria invisible o cross-tenant segun
// como se mire. Solucion: el seeder crea una COPIA de las base por tenant y EsBase pasa a
// significar "sembrada por el sistema" (no editable ni borrable), no "compartida entre tenants".
// ===========================================================================

/// <summary>
/// Categoria del documento (el primer nivel del archivo). Sembrada por el sistema
/// (<see cref="EsBase"/>) o creada por el tenant. TENANT-SCOPED.
/// </summary>
public class DocumentoCategoria : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }

    /// <summary>Clave del icono (ej. "folder"); la UI la mapea al SVG. No es el SVG.</summary>
    public string? Icono { get; set; }

    /// <summary>Color hex de la paleta del prototipo (ej. "#2F6FEB").</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Sembrada por el sistema: no se puede editar ni desactivar. NO significa "global":
    /// cada tenant tiene su propia copia (ver nota de cabecera).
    /// </summary>
    public bool EsBase { get; set; }

    /// <summary>Soft-delete (regla 5): nunca se borra fisicamente una categoria con historia.</summary>
    public bool Activa { get; set; } = true;

    public int Orden { get; set; }

    public ICollection<DocumentoCarpeta> Carpetas { get; set; } = new List<DocumentoCarpeta>();
}

/// <summary>
/// Carpeta dentro de una categoria. Arbol por <see cref="PadreId"/>; null = raiz de la categoria.
/// TENANT-SCOPED.
/// </summary>
public class DocumentoCarpeta : TenantEntity
{
    public Guid CategoriaId { get; set; }
    public DocumentoCategoria? Categoria { get; set; }

    /// <summary>Carpeta contenedora. Null = cuelga directo de la categoria.</summary>
    public Guid? PadreId { get; set; }
    public DocumentoCarpeta? Padre { get; set; }

    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }

    /// <summary>Soft-delete. Desactivar exige que no queden documentos activos dentro.</summary>
    public bool Activa { get; set; } = true;

    public int Orden { get; set; }

    public ICollection<DocumentoCarpeta> Subcarpetas { get; set; } = new List<DocumentoCarpeta>();
}

/// <summary>
/// Documento: la entidad central. Los METADATOS viven aqui; el archivo fisico vive en
/// wwwroot/uploads/documentos/{tenant} y cada subida queda como una
/// <see cref="DocumentoVersion"/> inmutable. TENANT-SCOPED.
///
/// Regla 5 (soft-delete): eliminar deja Activo=false + Estado=Archivado. El binario NO se borra,
/// porque las versiones anteriores son la prueba de que el documento fue lo que dice haber sido.
/// </summary>
public class Documento : TenantEntity
{
    public Guid CategoriaId { get; set; }
    public DocumentoCategoria? Categoria { get; set; }

    /// <summary>Null = el documento cuelga de la categoria sin carpeta.</summary>
    public Guid? CarpetaId { get; set; }
    public DocumentoCarpeta? Carpeta { get; set; }

    public string Titulo { get; set; } = null!;
    public string? Descripcion { get; set; }

    /// <summary>Nombre con el que llego el archivo, con extension. Solo informativo.</summary>
    public string NombreArchivoOriginal { get; set; } = null!;

    public OrigenDocumento Origen { get; set; } = OrigenDocumento.Manual;

    /// <summary>Id del registro que lo produjo (tarea, paso de flujo, respuesta de formulario...).
    /// Deliberadamente SIN FK: el origen es polimorfico y una FK obligaria a una columna por modulo.</summary>
    public Guid? OrigenEntidadId { get; set; }

    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Vigente;

    public VisibilidadDocumento Visibilidad { get; set; } = VisibilidadDocumento.Equipo;

    /// <summary>Version vigente. Null solo en el instante entre crear el documento y su v1.</summary>
    public Guid? VersionActualId { get; set; }
    public DocumentoVersion? VersionActual { get; set; }

    /// <summary>Cuantas versiones lleva. Se incrementa al subir una nueva; nunca baja.</summary>
    public int NumeroVersiones { get; set; }

    /// <summary>Destacado para TODO el tenant (distinto del pin personal de cada usuario).</summary>
    public bool Destacado { get; set; }

    /// <summary>Soft-delete.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Usuario del tenant que lo subio.</summary>
    public Guid SubidoPorUsuarioId { get; set; }

    public ICollection<DocumentoVersion> Versiones { get; set; } = new List<DocumentoVersion>();
    public ICollection<DocumentoEtiqueta> Etiquetas { get; set; } = new List<DocumentoEtiqueta>();
}

/// <summary>
/// Version inmutable. Cada subida crea una fila y el archivo anterior se conserva: por eso el
/// historial es auditable. Nada actualiza una version ya escrita. TENANT-SCOPED.
/// </summary>
public class DocumentoVersion : TenantEntity
{
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }

    /// <summary>Secuencial dentro del documento: 1, 2, 3...</summary>
    public int Numero { get; set; }

    public string NombreArchivo { get; set; } = null!;
    public string TipoMime { get; set; } = null!;
    public long TamanoBytes { get; set; }

    /// <summary>Ruta publica del archivo (ej. "/uploads/documentos/{tenant}/{archivo}").</summary>
    public string UrlStorage { get; set; } = null!;

    /// <summary>SHA-256 en hex del contenido. Permite detectar que el binario no fue alterado.</summary>
    public string HashSha256 { get; set; } = null!;

    /// <summary>Que cambio respecto de la version anterior (lo escribe quien sube).</summary>
    public string? NotasCambio { get; set; }

    public Guid SubidoPorUsuarioId { get; set; }
}

/// <summary>
/// Etiqueta del catalogo del tenant. Igual que las categorias: <see cref="EsBase"/> = sembrada
/// por el sistema, NO global. TENANT-SCOPED.
/// </summary>
public class DocumentoEtiquetaCatalogo : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public string? Color { get; set; }
    public bool EsBase { get; set; }
    public bool Activa { get; set; } = true;
}

/// <summary>Union M:N entre documento y etiqueta. TENANT-SCOPED.</summary>
public class DocumentoEtiqueta : TenantEntity
{
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }

    public Guid EtiquetaCatalogoId { get; set; }
    public DocumentoEtiquetaCatalogo? EtiquetaCatalogo { get; set; }
}

/// <summary>
/// Pin PERSONAL de un usuario sobre un documento. Distinto de <see cref="Documento.Destacado"/>,
/// que es del tenant entero. TENANT-SCOPED.
/// </summary>
public class DocumentoDestacadoPersonal : TenantEntity
{
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }

    public Guid UsuarioId { get; set; }
}

/// <summary>
/// Bitacora del documento. APPEND-ONLY: se inserta y jamas se actualiza ni se borra. Es la
/// contraparte por-documento de AdminAuditLog. TENANT-SCOPED.
/// </summary>
public class DocumentoAuditoria : TenantEntity
{
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }

    public TipoEventoDocumento TipoEvento { get; set; }

    /// <summary>Detalle del cambio en JSON (ej. {"versionAnterior":1,"versionNueva":2}).</summary>
    public string? DetalleJson { get; set; }

    public Guid UsuarioId { get; set; }
    public DateTimeOffset OcurridoAt { get; set; }
}

/// <summary>
/// Vista o descarga del documento. Append-only; alimenta las estadisticas de uso.
/// TENANT-SCOPED.
/// </summary>
public class DocumentoConsumo : TenantEntity
{
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }

    /// <summary>Version concreta que se consumio: sin esto no se puede auditar QUE vio la persona.</summary>
    public Guid VersionId { get; set; }
    public DocumentoVersion? Version { get; set; }

    public TipoEventoConsumo TipoEvento { get; set; }
    public DispositivoConsumo Dispositivo { get; set; } = DispositivoConsumo.Unknown;

    public Guid UsuarioId { get; set; }
    public DateTimeOffset OcurridoAt { get; set; }
}
