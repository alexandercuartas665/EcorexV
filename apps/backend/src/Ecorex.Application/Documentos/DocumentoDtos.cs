using Ecorex.Domain.Enums;

namespace Ecorex.Application.Documentos;

// ===========================================================================
// DTOs del Gestor Documental - mitad "Archivo central".
//
// DIFERENCIA CON EL ORIGEN (PROPIA): alla el contenido del archivo viajaba en BASE64 dentro del
// DTO, porque la pagina hablaba por HTTP con una API aparte. Aqui la consola es Blazor Server y
// llama al servicio en proceso, asi que el binario viaja como byte[]: nada de inflar un 33% cada
// archivo ni de cargarlo dos veces en memoria.
// ===========================================================================

/// <summary>Resultado de una operacion de escritura. Idioma del proyecto: Ok + Error legible.</summary>
public sealed record DocumentoResult(bool IsOk, string? Error = null, Guid? Id = null)
{
    public static DocumentoResult Ok(Guid? id = null) => new(true, null, id);
    public static DocumentoResult Fail(string error) => new(false, error);
}

// ---- Categorias ----

public sealed record CategoriaDto(
    Guid Id,
    bool EsBase,
    string Nombre,
    string? Descripcion,
    string? Icono,
    string? Color,
    bool Activa,
    int Orden,
    int NumeroDocumentos);

public sealed record GuardarCategoriaRequest(
    string Nombre,
    string? Descripcion,
    string? Icono,
    string? Color,
    int Orden = 0);

// ---- Carpetas ----

/// <summary>Nodo del arbol de carpetas. <paramref name="Subcarpetas"/> viene ya anidado.</summary>
public sealed record CarpetaDto(
    Guid Id,
    Guid CategoriaId,
    Guid? PadreId,
    string Nombre,
    string? Descripcion,
    int Orden,
    bool Activa,
    int NumeroDocumentos,
    IReadOnlyList<CarpetaDto> Subcarpetas);

public sealed record CrearCarpetaRequest(
    Guid CategoriaId,
    Guid? PadreId,
    string Nombre,
    string? Descripcion);

public sealed record RenombrarCarpetaRequest(string Nombre, string? Descripcion);

// ---- Etiquetas ----

public sealed record EtiquetaDto(
    Guid Id,
    bool EsBase,
    string Nombre,
    string? Color,
    bool Activa,
    int NumeroDocumentos);

public sealed record EtiquetaResumenDto(Guid Id, string Nombre, string? Color);

public sealed record GuardarEtiquetaRequest(string Nombre, string? Color);

// ---- Documentos ----

public sealed record DocumentoListaDto(
    Guid Id,
    string Titulo,
    string NombreArchivoOriginal,
    Guid CategoriaId,
    string CategoriaNombre,
    Guid? CarpetaId,
    string? CarpetaNombre,
    EstadoDocumento Estado,
    OrigenDocumento Origen,
    VisibilidadDocumento Visibilidad,
    int NumeroVersiones,
    long TamanoBytes,
    string TipoMime,
    string? UrlStorage,
    bool Destacado,
    bool DestacadoPersonal,
    DateTimeOffset CreadoAt,
    DateTimeOffset? ActualizadoAt,
    IReadOnlyList<EtiquetaResumenDto> Etiquetas);

public sealed record VersionDto(
    Guid Id,
    int Numero,
    string NombreArchivo,
    string TipoMime,
    long TamanoBytes,
    string HashSha256,
    string UrlStorage,
    string? NotasCambio,
    Guid SubidoPorUsuarioId,
    DateTimeOffset CreadoAt);

public sealed record DocumentoDetalleDto(
    Guid Id,
    string Titulo,
    string? Descripcion,
    string NombreArchivoOriginal,
    Guid CategoriaId,
    string CategoriaNombre,
    Guid? CarpetaId,
    string? CarpetaNombre,
    EstadoDocumento Estado,
    OrigenDocumento Origen,
    Guid? OrigenEntidadId,
    VisibilidadDocumento Visibilidad,
    int NumeroVersiones,
    bool Destacado,
    bool DestacadoPersonal,
    DateTimeOffset CreadoAt,
    Guid SubidoPorUsuarioId,
    VersionDto? VersionActual,
    IReadOnlyList<VersionDto> Historial,
    IReadOnlyList<EtiquetaResumenDto> Etiquetas);

/// <summary>Filtros de la bandeja. Todos opcionales: sin ninguno se ve el archivo completo.</summary>
public sealed record DocumentosFiltro(
    Guid? CategoriaId = null,
    Guid? CarpetaId = null,
    OrigenDocumento? Origen = null,
    EstadoDocumento? Estado = null,
    VisibilidadDocumento? Visibilidad = null,
    string? TextoBusqueda = null,
    Guid? EtiquetaId = null,
    bool SoloDestacados = false,
    int Page = 1,
    int PageSize = 30);

public sealed record DocumentosPageDto(IReadOnlyList<DocumentoListaDto> Items, int Total, int Page, int PageSize);

/// <summary>
/// Alta de documento. <paramref name="Contenido"/> son los bytes ya leidos y VALIDADOS por quien
/// llama (la pagina usa DocumentUploadGuard antes de construir esto).
/// </summary>
public sealed record SubirDocumentoRequest(
    Guid CategoriaId,
    Guid? CarpetaId,
    string Titulo,
    string? Descripcion,
    string NombreArchivo,
    string TipoMime,
    byte[] Contenido,
    VisibilidadDocumento Visibilidad = VisibilidadDocumento.Equipo,
    IReadOnlyList<Guid>? EtiquetaIds = null,
    OrigenDocumento Origen = OrigenDocumento.Manual,
    Guid? OrigenEntidadId = null);

public sealed record NuevaVersionRequest(
    string NombreArchivo,
    string TipoMime,
    byte[] Contenido,
    string? NotasCambio);

public sealed record ActualizarMetadatosRequest(
    string Titulo,
    string? Descripcion,
    Guid CategoriaId,
    Guid? CarpetaId,
    VisibilidadDocumento Visibilidad,
    IReadOnlyList<Guid>? EtiquetaIds);

// ---- Bitacora y estadisticas ----

public sealed record AuditoriaEventoDto(
    Guid Id,
    TipoEventoDocumento TipoEvento,
    string? DetalleJson,
    Guid UsuarioId,
    string? UsuarioNombre,
    DateTimeOffset OcurridoAt);

public sealed record EstadisticasDocumentoDto(
    int TotalVistas,
    int TotalDescargas,
    int UsuariosUnicos,
    DateTimeOffset? UltimoAcceso);

public sealed record ResumenDocumentosDto(
    int TotalDocumentos,
    int TotalCategorias,
    int TotalCarpetas,
    long TamanoTotalBytes,
    int DocumentosUltimos30Dias);
