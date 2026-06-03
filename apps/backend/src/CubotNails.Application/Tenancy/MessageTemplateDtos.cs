using CubotNails.Domain.Enums;

namespace CubotNails.Application.Tenancy;

public sealed record MessageTemplateDto(
    Guid Id,
    string Category,
    string Body,
    MessageMediaType MediaType,
    string? MediaUrl,
    string? MediaMimeType,
    int SortOrder);

public sealed record CreateMessageTemplateRequest(
    string Category,
    string Body,
    MessageMediaType MediaType = MessageMediaType.None,
    string? MediaUrl = null,
    string? MediaMimeType = null);

public sealed record UpdateMessageTemplateRequest(
    string Category,
    string Body,
    MessageMediaType MediaType = MessageMediaType.None,
    string? MediaUrl = null,
    string? MediaMimeType = null);

/// <summary>Categorias estandar de pregrabados (clave + etiqueta para la UI).</summary>
public static class MessageTemplateCategories
{
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        ("saludo", "Saludo"),
        ("info", "Pedir info"),
        ("cotizacion", "Cotizacion"),
        ("seguimiento", "Seguimiento"),
        ("cierre", "Cierre")
    };

    public static string Label(string key) =>
        All.FirstOrDefault(c => c.Key == key).Label ?? key;
}
