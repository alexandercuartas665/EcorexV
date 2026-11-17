using Ecorex.Application.Gestor;
using Microsoft.AspNetCore.DataProtection;

namespace Ecorex.Infrastructure.Security;

/// <summary>
/// Token del {enlace_baja} (ADR-0174) con Data Protection y un PROPOSITO PROPIO ("Ecorex.EmailUnsubscribe.v1"):
/// un token de baja no sirve para descifrar secretos ni al reves. La salida de IDataProtector.Protect(string)
/// ya es base64url (apta para la ruta /baja/{token}). Sin caducidad: un correo viejo debe seguir permitiendo
/// darse de baja. Las llaves de Data Protection se conservan al rotar, asi que los enlaces viejos siguen validos.
/// </summary>
public sealed class DataProtectionUnsubscribeTokenProtector : IUnsubscribeTokenProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionUnsubscribeTokenProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Ecorex.EmailUnsubscribe.v1");

    public string Protect(Guid tenantId, string email) => _protector.Protect($"{tenantId:N}|{email}");

    public (Guid TenantId, string Email)? Unprotect(string token)
    {
        try
        {
            var raw = _protector.Unprotect(token);
            var sep = raw.IndexOf('|');
            if (sep <= 0 || !Guid.TryParseExact(raw[..sep], "N", out var tenantId)) { return null; }
            var email = raw[(sep + 1)..].Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(email) ? null : (tenantId, email);
        }
        catch
        {
            // Token alterado, de otro entorno o con llaves perdidas: invalido (mensaje neutro en la pagina).
            return null;
        }
    }
}
