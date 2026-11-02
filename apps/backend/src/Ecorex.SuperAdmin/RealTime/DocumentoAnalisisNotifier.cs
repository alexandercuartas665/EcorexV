namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Aviso EN PROCESO de cambios en documentos del Analizador (carga -> EnAnalisis -> Analizado/Error), para que
/// la parrilla Blazor refresque el spinner por archivo sin recargar la pagina. Es un singleton con un evento:
/// el worker lo dispara al cambiar un documento y la pagina (suscrita) recarga su lista (tenant-scoped). No usa
/// SignalR porque worker y UI viven en el mismo proceso (SuperAdmin); el evento es solo un "ping" con el id.
/// </summary>
public interface IDocumentoAnalisisNotifier
{
    /// <summary>Se dispara cuando un documento cambia de estado o termina de procesarse. Lleva su id.</summary>
    event Action<Guid>? DocumentoCambiado;

    /// <summary>Notifica que el documento dado cambio (lo llama el worker/servicio tras guardar).</summary>
    void NotificarCambio(Guid documentoId);
}

/// <inheritdoc />
public sealed class DocumentoAnalisisNotifier : IDocumentoAnalisisNotifier
{
    public event Action<Guid>? DocumentoCambiado;

    public void NotificarCambio(Guid documentoId) => DocumentoCambiado?.Invoke(documentoId);
}
