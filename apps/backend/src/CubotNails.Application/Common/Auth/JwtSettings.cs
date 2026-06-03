namespace CubotNails.Application.Common.Auth;

/// <summary>Configuracion del JWT propio de CUBOT.nails (seccion "Jwt").</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CubotNails";
    public string Audience { get; set; } = "CubotNails";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
}
