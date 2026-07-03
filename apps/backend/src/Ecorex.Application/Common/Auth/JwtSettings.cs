namespace Ecorex.Application.Common.Auth;

/// <summary>Configuracion del JWT propio de ECOREX.tareas (seccion "Jwt").</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "Ecorex";
    public string Audience { get; set; } = "Ecorex";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
}
