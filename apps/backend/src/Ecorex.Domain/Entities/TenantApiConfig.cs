using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Configuracion de la API publica de ingestion de leads por agencia (tenant). GLOBAL (lleva
/// TenantId pero no es ITenantScoped, para poder resolver el tenant a partir de la API key sin
/// contexto de sesion). La clave se guarda como hash (para buscar) y cifrada (para mostrarla en Mi cuenta).
/// </summary>
public class TenantApiConfig : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>SHA-256 (hex) de la API key, para resolver el tenant en cada request.</summary>
    public string? ApiKeyHash { get; set; }

    /// <summary>API key cifrada (ISecretProtector), para mostrarla al dueno en Mi cuenta.</summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>Si la API de ingestion esta activa para este tenant.</summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
