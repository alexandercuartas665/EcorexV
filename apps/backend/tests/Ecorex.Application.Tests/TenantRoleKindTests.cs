using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Rol simplificado del tenant (2026-11-16): en pantalla solo Administrador / Usuario; el enum no cambia y
/// al editar sin cambiar de grupo se conserva el valor original.
/// </summary>
public class TenantRoleKindTests
{
    [Theory]
    [InlineData(TenantRole.Owner, true, "Administrador")]
    [InlineData(TenantRole.Admin, true, "Administrador")]
    [InlineData(TenantRole.Supervisor, false, "Usuario")]
    [InlineData(TenantRole.Advisor, false, "Usuario")]
    public void Agrupa_en_administrador_y_usuario(TenantRole role, bool admin, string label)
    {
        Assert.Equal(admin, TenantRoleKind.IsAdmin(role));
        Assert.Equal(label, TenantRoleKind.Label(role));
    }

    [Fact]
    public void Nuevo_usuario_se_guarda_como_admin_o_advisor()
    {
        Assert.Equal(TenantRole.Admin, TenantRoleKind.ForNew(true));
        Assert.Equal(TenantRole.Advisor, TenantRoleKind.ForNew(false));
    }

    [Theory]
    // Mismo grupo: se conserva el valor original (Propietario y Supervisor no se pierden al guardar).
    [InlineData(TenantRole.Owner, true, TenantRole.Owner)]
    [InlineData(TenantRole.Admin, true, TenantRole.Admin)]
    [InlineData(TenantRole.Supervisor, false, TenantRole.Supervisor)]
    [InlineData(TenantRole.Advisor, false, TenantRole.Advisor)]
    // Cambio de grupo: Admin o Advisor.
    [InlineData(TenantRole.Owner, false, TenantRole.Advisor)]
    [InlineData(TenantRole.Admin, false, TenantRole.Advisor)]
    [InlineData(TenantRole.Supervisor, true, TenantRole.Admin)]
    [InlineData(TenantRole.Advisor, true, TenantRole.Admin)]
    public void Al_editar_solo_cambia_si_cambia_de_grupo(TenantRole current, bool admin, TenantRole expected)
    {
        Assert.Equal(expected, TenantRoleKind.Resolve(current, admin));
    }
}
