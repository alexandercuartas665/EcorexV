using Ecorex.Application.Workflows;
using Microsoft.Extensions.Configuration;

namespace Ecorex.Infrastructure.Notifications;

/// <summary>
/// Construye el enlace absoluto a una tarea para las notificaciones. La URL base se toma de
/// configuracion: <c>Ecorex:PublicBaseUrl</c> o la variable de entorno <c>ECOREX_PUBLIC_BASE_URL</c>
/// (asi se fija por entorno en el docker-compose de prod). Si no hay URL base, devuelve null y el
/// mensaje sale sin enlace.
/// </summary>
public sealed class NotifyLinkBuilder : INotifyLinkBuilder
{
    private readonly string? _baseUrl;

    public NotifyLinkBuilder(IConfiguration configuration)
    {
        var url = configuration["Ecorex:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            url = Environment.GetEnvironmentVariable("ECOREX_PUBLIC_BASE_URL");
        }
        _baseUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
    }

    public string? BuildTaskLink(Guid taskId)
        => string.IsNullOrWhiteSpace(_baseUrl) ? null : $"{_baseUrl}/actividades?task={taskId}";

    public string? BuildDecisionLink(string token)
        => string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(token) ? null : $"{_baseUrl}/d/{token}";
}
