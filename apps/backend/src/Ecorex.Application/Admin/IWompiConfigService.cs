namespace Ecorex.Application.Admin;

public interface IWompiConfigService
{
    Task<WompiConfigDto?> GetAsync(CancellationToken cancellationToken = default);
    Task<WompiConfigDto> SaveAsync(SaveWompiConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validacion estructural (sin cobro real): verifica que las llaves, el ambiente y la moneda
    /// sean coherentes. Punto de extension para un ping real a la API de Wompi mas adelante.
    /// </summary>
    Task<WompiValidationResult?> ValidateAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
