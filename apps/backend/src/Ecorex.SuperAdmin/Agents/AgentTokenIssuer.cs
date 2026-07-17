using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ecorex.Contracts.Agent;
using Microsoft.IdentityModel.Tokens;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Emite el JWT corto del agente (handshake opcion A, doc 02 s2) con claims <c>client_id</c> y
/// <c>tenant_id</c>. Firma HMAC-SHA256 con la misma clave/issuer/audience que valida el esquema
/// bearer "Agent", de modo que lo que se firma aqui lo acepta el hub.
/// </summary>
public sealed class AgentTokenIssuer
{
    private readonly int _minutes;

    public AgentTokenIssuer(SymmetricSecurityKey key, string issuer, string audience, int minutes)
    {
        Key = key;
        Issuer = issuer;
        Audience = audience;
        _minutes = minutes;
    }

    public SymmetricSecurityKey Key { get; }
    public string Issuer { get; }
    public string Audience { get; }

    public AgentTokenResponse Issue(string clientId, Guid tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_minutes);
        var credentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("client_id", clientId),
            new("tenant_id", tenantId.ToString()),
        };
        var token = new JwtSecurityToken(
            Issuer, Audience, claims,
            notBefore: now.UtcDateTime, expires: expires.UtcDateTime,
            signingCredentials: credentials);
        return new AgentTokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
