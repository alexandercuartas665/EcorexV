namespace CubotTravels.Application.Tenancy;

/// <summary>
/// Conecta las lineas WhatsApp del tenant activo con el servidor Evolution (maestro de la
/// plataforma o propio de la agencia): crea instancias, entrega el QR, refresca el estado y
/// desconecta. Resuelve el servidor efectivo segun la eleccion de la agencia.
/// </summary>
public interface IWhatsAppConnectorService
{
    Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default);

    /// <summary>Define si la agencia usa el servidor maestro o uno propio (URL + API key). Null si no hay tenant.</summary>
    Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Crea/recupera la instancia de la linea en Evolution y devuelve el QR para escanear.</summary>
    Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado real en Evolution y actualiza el estado de la linea. Null si la linea no existe.</summary>
    Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Cierra sesion y elimina la instancia; deja la linea como desconectada.</summary>
    Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia un mensaje de prueba desde la linea a un numero (con codigo de pais).</summary>
    Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, CancellationToken cancellationToken = default);
}
