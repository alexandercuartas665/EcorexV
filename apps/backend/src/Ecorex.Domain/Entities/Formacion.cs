using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>Tipo de leccion de un curso de formacion (ADR-0202).</summary>
public enum CursoLeccionTipo
{
    /// <summary>Video subido al gestor de archivos (mp4/webm); se sirve por streaming con rangos.</summary>
    Video = 1,
    /// <summary>Enlace a YouTube o Vimeo (se embebe; no se sube archivo).</summary>
    VideoEnlace = 2,
    /// <summary>Manual o documento PDF (visor del navegador).</summary>
    Pdf = 3,
    /// <summary>Imagen (infografia, diagrama).</summary>
    Imagen = 4,
    /// <summary>Texto con formato (HTML saneado) escrito en el editor.</summary>
    Texto = 5,
    /// <summary>Wizard o manual interactivo en HTML: un .html suelto o un .zip con su carpeta (como las maquetas).</summary>
    WizardHtml = 6,
    /// <summary>Archivo descargable de cualquier otro tipo (Excel, Word, plantilla).</summary>
    Archivo = 7
}

/// <summary>A quien va dirigida una asignacion de curso (ADR-0202).</summary>
public enum CursoAsignacionDestino
{
    /// <summary>Todos los usuarios del tenant.</summary>
    Todos = 1,
    /// <summary>Un usuario puntual (TenantUser).</summary>
    Usuario = 2,
    /// <summary>Una Dependencia o Cargo del organigrama: sus funcionarios, miembros y responsable (y descendientes).</summary>
    Unidad = 3
}

/// <summary>
/// Curso del modulo de FORMACION (ADR-0202, basado en el modulo de capacitaciones de DokTrino). Agregado raiz:
/// modulos -> lecciones. Borrado logico. Un curso solo aparece en el catalogo cuando esta Publicado.
/// TENANT-SCOPED.
/// </summary>
public sealed class Curso : TenantEntity, ISoftDeletable
{
    public string Titulo { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    /// <summary>Categoria libre para agrupar el catalogo (ej. "Seguridad", "Comercial").</summary>
    public string? Categoria { get; set; }

    /// <summary>Imagen de portada (StoredFile, modulo Formacion).</summary>
    public Guid? PortadaFileId { get; set; }

    public bool Publicado { get; set; }

    /// <summary>Duracion estimada en minutos (informativa).</summary>
    public int? DuracionMinutos { get; set; }

    public string? CreatedByName { get; set; }

    /// <summary>Ola 2: el curso exige aprobar una evaluacion final (si ademas tiene preguntas).</summary>
    public bool EvaluacionActiva { get; set; }

    /// <summary>Puntaje minimo para aprobar (0..100).</summary>
    public int PuntajeMinimo { get; set; } = 70;

    /// <summary>Intentos permitidos antes de bloquear (el administrador puede desbloquear).</summary>
    public int IntentosMax { get; set; } = 3;

    public List<CursoModulo> Modulos { get; set; } = new();

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedByTenantUserId { get; set; }
}

/// <summary>Modulo (capitulo) de un curso. Vive y muere con el curso. TENANT-SCOPED.</summary>
public sealed class CursoModulo : TenantEntity
{
    public Guid CursoId { get; set; }

    public Curso? Curso { get; set; }

    public string Titulo { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public int Orden { get; set; }

    public List<CursoLeccion> Lecciones { get; set; } = new();
}

/// <summary>
/// Leccion de un modulo. Segun <see cref="Tipo"/> usa ArchivoId (video/PDF/imagen/archivo/wizard), Url (enlace de
/// video) o Contenido (texto). El wizard HTML guarda el ZIP (o el .html envuelto en ZIP) en el gestor de archivos y
/// se sirve como las maquetas, en /formacion/wizard/{leccionId}/. TENANT-SCOPED.
/// </summary>
public sealed class CursoLeccion : TenantEntity
{
    public Guid ModuloId { get; set; }

    public CursoModulo? Modulo { get; set; }

    /// <summary>Curso dueno (desnormalizado para consultar avance sin pasar por el modulo).</summary>
    public Guid CursoId { get; set; }

    public string Titulo { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public int Orden { get; set; }

    public CursoLeccionTipo Tipo { get; set; } = CursoLeccionTipo.Texto;

    /// <summary>Archivo en el gestor unificado (StoredFile, modulo Formacion).</summary>
    public Guid? ArchivoId { get; set; }

    public string? ArchivoNombre { get; set; }

    public string? ArchivoMime { get; set; }

    public long? ArchivoBytes { get; set; }

    /// <summary>Enlace de YouTube/Vimeo (Tipo VideoEnlace).</summary>
    public string? Url { get; set; }

    /// <summary>Cuerpo de la leccion Texto (HTML saneado).</summary>
    public string? Contenido { get; set; }

    /// <summary>Wizard: carpeta raiz unica del ZIP que se quita al servir (null si no hay).</summary>
    public string? WizardRootPrefix { get; set; }

    /// <summary>Wizard: pagina de entrada relativa (ej. index.html).</summary>
    public string? WizardEntryPath { get; set; }

    /// <summary>Duracion estimada en minutos (informativa).</summary>
    public int? DuracionMinutos { get; set; }
}

/// <summary>
/// Asignacion de un curso como OBLIGATORIO a todos, a un usuario o a una unidad del organigrama, con fecha limite
/// opcional (fecha calendario del tenant). Sin asignacion el curso igual se puede tomar desde el catalogo.
/// TENANT-SCOPED.
/// </summary>
public sealed class CursoAsignacion : TenantEntity
{
    public Guid CursoId { get; set; }

    public Curso? Curso { get; set; }

    public CursoAsignacionDestino Destino { get; set; } = CursoAsignacionDestino.Todos;

    public Guid? TenantUserId { get; set; }

    public Guid? OrgUnitId { get; set; }

    /// <summary>Fecha limite (dia calendario en la zona del tenant); null = sin limite.</summary>
    public DateOnly? FechaLimite { get; set; }
}

/// <summary>
/// Avance de un usuario en un curso (equivale a inscripcion + resumen): se crea al abrir el curso. El porcentaje
/// se recalcula con las lecciones vistas; Completado cuando vio todas. TENANT-SCOPED.
/// </summary>
public sealed class CursoProgreso : TenantEntity
{
    public Guid CursoId { get; set; }

    public Curso? Curso { get; set; }

    public Guid TenantUserId { get; set; }

    public DateTimeOffset FechaInicio { get; set; }

    public DateTimeOffset? FechaCompletado { get; set; }

    public int LeccionesVistas { get; set; }

    public int Porcentaje { get; set; }

    public Guid? UltimaLeccionId { get; set; }

    // ---- Ola 2: evaluacion final y certificado ----

    /// <summary>Intentos totales rendidos (historico: no se reinicia al desbloquear).</summary>
    public int Intentos { get; set; }

    /// <summary>Intentos "perdonados" por desbloqueos: los disponibles son IntentosMax - (Intentos - IntentosPerdonados).</summary>
    public int IntentosPerdonados { get; set; }

    public int? MejorNota { get; set; }

    public bool Aprobado { get; set; }

    public DateTimeOffset? FechaAprobacion { get; set; }

    public bool Bloqueado { get; set; }

    public Guid? DesbloqueadoPorTenantUserId { get; set; }

    public DateTimeOffset? FechaDesbloqueo { get; set; }

    /// <summary>Codigo publico del certificado (unico global, no adivinable); se emite al terminar el curso.</summary>
    public string? CertificadoCodigo { get; set; }
}

/// <summary>Pregunta de opcion multiple (una correcta) de la evaluacion final de un curso. TENANT-SCOPED.</summary>
public sealed class CursoPregunta : TenantEntity
{
    public Guid CursoId { get; set; }

    public Curso? Curso { get; set; }

    public int Orden { get; set; }

    public string Enunciado { get; set; } = string.Empty;

    /// <summary>Opciones como arreglo JSON de textos.</summary>
    public string OpcionesJson { get; set; } = "[]";

    /// <summary>Indice (base 0) de la opcion correcta. Nunca se envia al alumno.</summary>
    public int IndiceCorrecto { get; set; }

    /// <summary>Explicacion que ve el alumno si falla la pregunta.</summary>
    public string? Retroalimentacion { get; set; }
}

/// <summary>Intento de la evaluacion final (auditoria: respuestas y resultado). TENANT-SCOPED.</summary>
public sealed class CursoIntento : TenantEntity
{
    public Guid CursoId { get; set; }

    public Curso? Curso { get; set; }

    public Guid TenantUserId { get; set; }

    public int Puntaje { get; set; }

    public int Correctas { get; set; }

    public int Total { get; set; }

    public bool Aprobado { get; set; }

    /// <summary>Respuestas dadas {preguntaId: indice} como JSON.</summary>
    public string RespuestasJson { get; set; } = "{}";

    public DateTimeOffset Fecha { get; set; }
}

/// <summary>Leccion marcada como vista por un usuario (avance persistido por leccion). TENANT-SCOPED.</summary>
public sealed class CursoLeccionVista : TenantEntity
{
    public Guid LeccionId { get; set; }

    public CursoLeccion? Leccion { get; set; }

    public Guid CursoId { get; set; }

    public Guid TenantUserId { get; set; }

    public DateTimeOffset VistaEn { get; set; }
}
