namespace Ecorex.Application.Documentos;

/// <summary>
/// Gestor Documental - mitad "Expedientes" (Tabla de Retencion Documental). Portado del modulo
/// 2.15 del hermano PROPIA.
///
/// Dos planos que NO se mezclan:
///   - La TRD (serie -> subserie -> tipologias/campos) es CONFIGURACION: se edita libremente.
///   - El expediente es una INSTANCIA: al abrirlo se copian tipologias y campos de la subserie y
///     se guarda el nombre de serie/subserie como texto. Editar la TRD despues no altera los
///     expedientes ya abiertos, que es justo lo que hace auditable un expediente.
///
/// Tenant-scoped por el filtro global (regla 1). Soft-delete en series, subseries y expedientes.
/// </summary>
public interface IExpedienteService
{
    // ---- Configuracion documental (TRD) ----

    /// <summary>Arbol completo serie -> subserie -> tipologias + campos.</summary>
    Task<IReadOnlyList<TrdSerieDto>> ListarTrdAsync(CancellationToken ct = default);

    Task<DocumentoResult> CrearSerieAsync(CrearSerieRequest req, CancellationToken ct = default);
    /// <summary>Soft-delete. Los expedientes ya abiertos NO se ven afectados (guardan snapshot).</summary>
    Task<DocumentoResult> EliminarSerieAsync(Guid id, CancellationToken ct = default);

    Task<DocumentoResult> CrearSubserieAsync(CrearSubserieRequest req, CancellationToken ct = default);
    Task<DocumentoResult> EliminarSubserieAsync(Guid id, CancellationToken ct = default);

    Task<DocumentoResult> CrearTipologiaCfgAsync(CrearTipologiaCfgRequest req, CancellationToken ct = default);
    Task<DocumentoResult> EliminarTipologiaCfgAsync(Guid id, CancellationToken ct = default);

    Task<DocumentoResult> CrearCampoCfgAsync(CrearCampoCfgRequest req, CancellationToken ct = default);
    Task<DocumentoResult> EliminarCampoCfgAsync(Guid id, CancellationToken ct = default);

    // ---- Expedientes ----

    Task<IReadOnlyList<ExpedienteListaDto>> ListarExpedientesAsync(CancellationToken ct = default);
    Task<ExpedienteDetalleDto?> GetExpedienteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Abre un expediente copiando las tipologias y campos de la subserie. El codigo se genera
    /// como EXP-{3 letras de la serie}-{consecutivo}.
    /// </summary>
    Task<DocumentoResult> CrearExpedienteAsync(CrearExpedienteRequest req, CancellationToken ct = default);

    /// <summary>Soft-delete del expediente. Los archivos cargados permanecen.</summary>
    Task<DocumentoResult> EliminarExpedienteAsync(Guid id, CancellationToken ct = default);

    Task<DocumentoResult> AgregarTipologiaAsync(Guid expedienteId, AgregarTipologiaExpRequest req, CancellationToken ct = default);
    Task<DocumentoResult> EliminarTipologiaAsync(Guid tipologiaId, CancellationToken ct = default);
    Task<DocumentoResult> AgregarCampoAsync(Guid expedienteId, AgregarCampoExpRequest req, CancellationToken ct = default);
    Task<DocumentoResult> EliminarCampoAsync(Guid campoId, CancellationToken ct = default);

    /// <summary>Carga (o reemplaza) el archivo de una casilla del checklist y guarda sus metadatos.</summary>
    Task<DocumentoResult> CargarTipologiaAsync(Guid tipologiaId, CargarTipologiaRequest req, CancellationToken ct = default);

    /// <summary>Deja la casilla vacia. El binario anterior NO se borra del disco.</summary>
    Task<DocumentoResult> QuitarArchivoAsync(Guid tipologiaId, CancellationToken ct = default);

    /// <summary>Bytes del archivo de una casilla, para descargarlo.</summary>
    Task<(byte[] Contenido, string NombreArchivo, string TipoMime)?> DescargarTipologiaAsync(
        Guid tipologiaId, CancellationToken ct = default);
}
