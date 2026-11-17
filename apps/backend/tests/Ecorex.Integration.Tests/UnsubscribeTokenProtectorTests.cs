using Ecorex.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Token del {enlace_baja} (ADR-0175): ida y vuelta (tenant + correo normalizado), rechazo de tokens
/// alterados y aislamiento de proposito (un token de baja no se descifra con el protector de secretos).
/// No requiere Docker.
/// </summary>
public class UnsubscribeTokenProtectorTests
{
    private static readonly IDataProtectionProvider Provider = new EphemeralDataProtectionProvider();

    [Fact]
    public void Ida_y_vuelta_devuelve_tenant_y_correo_normalizado()
    {
        var sut = new DataProtectionUnsubscribeTokenProtector(Provider);
        var tenant = Guid.NewGuid();
        var token = sut.Protect(tenant, "Ana@Cliente.Test ");
        var back = sut.Unprotect(token);
        Assert.NotNull(back);
        Assert.Equal(tenant, back!.Value.TenantId);
        Assert.Equal("ana@cliente.test", back.Value.Email);
        Assert.DoesNotContain("/", token); // base64url: apto para la ruta /baja/{token}
        Assert.DoesNotContain("+", token);
    }

    [Fact]
    public void Token_alterado_o_basura_es_invalido()
    {
        var sut = new DataProtectionUnsubscribeTokenProtector(Provider);
        var token = sut.Protect(Guid.NewGuid(), "a@b.test");
        Assert.Null(sut.Unprotect(token[..^4] + "AAAA"));
        Assert.Null(sut.Unprotect("prueba"));
    }

    [Fact]
    public void Proposito_aislado_del_protector_de_secretos()
    {
        var secrets = new DataProtectionSecretProtector(Provider);
        var bajas = new DataProtectionUnsubscribeTokenProtector(Provider);
        var token = bajas.Protect(Guid.NewGuid(), "a@b.test");
        Assert.ThrowsAny<Exception>(() => secrets.Unprotect(token));
        Assert.Null(bajas.Unprotect(secrets.Protect("sk-secreto")));
    }
}
