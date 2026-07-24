using Ecorex.Domain.Enums;

namespace Ecorex.Application.Documentos;

/// <summary>
/// Almacen del binario de los documentos. Se abstrae para que el servicio no dependa de donde
/// vive el archivo: hoy es wwwroot/uploads/documentos/{tenant} (mismo patron que items y avatares),
/// y el dia que se mueva a S3/R2 solo cambia la implementacion.
///
/// DEUDA CONOCIDA (anotada en PROGRESO.md): backup.sh de produccion respalda la BD pero NO
/// wwwroot/uploads. Mientras eso siga asi, un restore deja los metadatos sin sus archivos.
/// </summary>
public interface IDocumentoFileStore
{
    /// <summary>Guarda los bytes y devuelve la RUTA PUBLICA (ej. "/uploads/documentos/ab.../x.pdf").</summary>
    Task<string> SaveAsync(Guid tenantId, byte[] contenido, string extension, CancellationToken ct = default);

    /// <summary>Lee los bytes a partir de la ruta publica. Null si el archivo ya no esta.</summary>
    Task<byte[]?> ReadAsync(string urlPublica, CancellationToken ct = default);
}

/// <summary>
/// Gestor Documental - mitad "Archivo central". Portado del modulo 2.15 del hermano PROPIA y
/// adaptado a las reglas de este proyecto:
///
/// - TENANT-SCOPED por el filtro global: aqui NO se filtra por TenantId a mano (regla 1).
/// - SOFT-DELETE en todo (regla 5): eliminar un documento lo deja Activo=false + Archivado, y el
///   binario NO se borra. Las versiones anteriores son la prueba de lo que el documento fue.
/// - Toda operacion que toca mas de una tabla va en TRANSACCION (regla 4).
/// - Cada cambio deja rastro en la bitacora append-only del documento.
///
/// La firma electronica (estados EnFirma/Firmado) NO esta implementada: los valores del enum
/// existen para no migrar la columna despues, pero ninguna transicion los produce.
/// </summary>
public interface IDocumentoService
{
    // ---- Categorias ----
    Task<IReadOnlyList<CategoriaDto>> ListarCategoriasAsync(bool incluirInactivas = false, CancellationToken ct = default);
    Task<DocumentoResult> CrearCategoriaAsync(GuardarCategoriaRequest req, CancellationToken ct = default);
    /// <summary>Las categorias sembradas por el sistema (EsBase) no se editan.</summary>
    Task<DocumentoResult> ActualizarCategoriaAsync(Guid id, GuardarCategoriaRequest req, CancellationToken ct = default);
    /// <summary>Soft-delete. Falla si la categoria tiene documentos activos.</summary>
    Task<DocumentoResult> DesactivarCategoriaAsync(Guid id, CancellationToken ct = default);

    // ---- Carpetas ----
    /// <summary>Arbol completo (anidado) de carpetas de una categoria.</summary>
    Task<IReadOnlyList<CarpetaDto>> ListarCarpetasAsync(Guid categoriaId, CancellationToken ct = default);
    Task<DocumentoResult> CrearCarpetaAsync(CrearCarpetaRequest req, CancellationToken ct = default);
    Task<DocumentoResult> RenombrarCarpetaAsync(Guid id, RenombrarCarpetaRequest req, CancellationToken ct = default);
    /// <summary>Soft-delete. Falla si tiene documentos activos o subcarpetas activas.</summary>
    Task<DocumentoResult> DesactivarCarpetaAsync(Guid id, CancellationToken ct = default);

    // ---- Etiquetas ----
    Task<IReadOnlyList<EtiquetaDto>> ListarEtiquetasAsync(bool incluirInactivas = false, CancellationToken ct = default);
    Task<DocumentoResult> CrearEtiquetaAsync(GuardarEtiquetaRequest req, CancellationToken ct = default);
    Task<DocumentoResult> ActualizarEtiquetaAsync(Guid id, GuardarEtiquetaRequest req, CancellationToken ct = default);
    Task<DocumentoResult> DesactivarEtiquetaAsync(Guid id, CancellationToken ct = default);

    // ---- Documentos ----
    Task<DocumentosPageDto> ListarDocumentosAsync(DocumentosFiltro filtro, CancellationToken ct = default);
    Task<DocumentoDetalleDto?> GetDocumentoAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea el documento y su version 1 en una sola transaccion.</summary>
    Task<DocumentoResult> SubirDocumentoAsync(SubirDocumentoRequest req, CancellationToken ct = default);

    /// <summary>Agrega una version y mueve el puntero de la vigente. Las anteriores se conservan.</summary>
    Task<DocumentoResult> SubirNuevaVersionAsync(Guid documentoId, NuevaVersionRequest req, CancellationToken ct = default);

    Task<DocumentoResult> ActualizarMetadatosAsync(Guid id, ActualizarMetadatosRequest req, CancellationToken ct = default);

    /// <summary>Transiciones soportadas: Borrador -> Vigente y Vigente -> Archivado (y su reactivacion).</summary>
    Task<DocumentoResult> CambiarEstadoAsync(Guid id, EstadoDocumento nuevoEstado, CancellationToken ct = default);

    /// <summary>Soft-delete: Activo=false + Archivado. El archivo permanece.</summary>
    Task<DocumentoResult> EliminarDocumentoAsync(Guid id, CancellationToken ct = default);

    // ---- Destacados ----
    /// <summary>Pin personal del usuario actual (alterna).</summary>
    Task<DocumentoResult> AlternarDestacadoPersonalAsync(Guid documentoId, CancellationToken ct = default);
    /// <summary>Destacado para todo el tenant.</summary>
    Task<DocumentoResult> MarcarDestacadoTenantAsync(Guid documentoId, bool destacar, CancellationToken ct = default);

    // ---- Consumo ----
    /// <summary>Devuelve los bytes de la version pedida (o la vigente) y registra la descarga.</summary>
    Task<(byte[] Contenido, string NombreArchivo, string TipoMime)?> DescargarAsync(
        Guid documentoId, Guid? versionId = null, CancellationToken ct = default);

    /// <summary>Registra que alguien abrio el documento, sin descargarlo.</summary>
    Task RegistrarVistaAsync(Guid documentoId, DispositivoConsumo dispositivo = DispositivoConsumo.Unknown, CancellationToken ct = default);

    // ---- Bitacora y metricas ----
    Task<IReadOnlyList<AuditoriaEventoDto>> ListarAuditoriaAsync(Guid documentoId, CancellationToken ct = default);
    Task<EstadisticasDocumentoDto> GetEstadisticasAsync(Guid documentoId, CancellationToken ct = default);
    Task<ResumenDocumentosDto> GetResumenAsync(CancellationToken ct = default);
}
