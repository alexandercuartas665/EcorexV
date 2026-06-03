using CubotNails.Application.Common;
using Microsoft.AspNetCore.DataProtection;

namespace CubotNails.Infrastructure.Security;

/// <summary>Cifra secretos en reposo usando ASP.NET Core Data Protection.</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("CubotNails.TenantSecrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
