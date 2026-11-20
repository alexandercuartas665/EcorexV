using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0184: reconciliacion del estado de aprobacion de plantillas HSM contra lo que lista YCloud/Meta.
/// Solo el estado (status, motivo, id del proveedor, fecha de revision); nunca el contenido.
/// </summary>
public class WhatsAppTemplateStatusSyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static WhatsAppTemplate Local(string name = "aviso_prueba", string lang = "es",
        WhatsAppTemplateStatus status = WhatsAppTemplateStatus.Submitted, string? providerId = null)
        => new() { Name = name, Language = lang, Status = status, ProviderTemplateId = providerId, BodyText = "Hola {{nombre}}" };

    private static YCloudTemplateStatus Remote(string status, string name = "aviso_prueba", string lang = "es",
        string? id = "tpl-1", string? reason = null)
        => new(name, lang, status, id, reason, BodyText: "Hola {{1}}");

    [Theory]
    [InlineData("APPROVED", WhatsAppTemplateStatus.Approved)]
    [InlineData("rejected", WhatsAppTemplateStatus.Rejected)]
    [InlineData("PAUSED", WhatsAppTemplateStatus.Paused)]
    [InlineData("DELETED", WhatsAppTemplateStatus.Disabled)]
    [InlineData("PENDING", WhatsAppTemplateStatus.Submitted)]
    [InlineData("IN_APPEAL", WhatsAppTemplateStatus.Submitted)]
    public void Estado_de_meta_se_mapea(string meta, WhatsAppTemplateStatus expected)
        => Assert.Equal(expected, WhatsAppTemplateStatusSync.MapStatus(meta));

    [Fact]
    public void Estado_desconocido_no_cambia_nada()
    {
        Assert.Null(WhatsAppTemplateStatusSync.MapStatus("ALGO_NUEVO"));
        var t = Local();
        Assert.Null(WhatsAppTemplateStatusSync.Apply(t, Remote("ALGO_NUEVO"), Now));
        Assert.Equal(WhatsAppTemplateStatus.Submitted, t.Status);
    }

    [Fact]
    public void Aprobada_cambia_estado_y_fecha_de_revision_sin_tocar_el_cuerpo()
    {
        var t = Local();
        var c = WhatsAppTemplateStatusSync.Apply(t, Remote("APPROVED"), Now);
        Assert.NotNull(c);
        Assert.Equal(WhatsAppTemplateStatus.Submitted, c!.From);
        Assert.Equal(WhatsAppTemplateStatus.Approved, c.To);
        Assert.Equal(WhatsAppTemplateStatus.Approved, t.Status);
        Assert.Equal(Now, t.ReviewedAt);
        Assert.Equal("tpl-1", t.ProviderTemplateId);
        Assert.Equal("Hola {{nombre}}", t.BodyText); // el contenido con tokens con nombre se conserva
    }

    [Fact]
    public void Rechazada_trae_el_motivo()
    {
        var t = Local();
        var c = WhatsAppTemplateStatusSync.Apply(t, Remote("REJECTED", reason: "INVALID_FORMAT"), Now);
        Assert.Equal(WhatsAppTemplateStatus.Rejected, t.Status);
        Assert.Equal("INVALID_FORMAT", t.RejectionReason);
        Assert.Equal("INVALID_FORMAT", c!.Reason);
    }

    [Fact]
    public void Sin_cambio_devuelve_null_pero_completa_el_id_del_proveedor()
    {
        var t = Local();
        Assert.Null(WhatsAppTemplateStatusSync.Apply(t, Remote("PENDING", id: "tpl-9"), Now));
        Assert.Equal("tpl-9", t.ProviderTemplateId);
        Assert.Null(t.ReviewedAt);
    }

    [Fact]
    public void Busca_primero_por_id_del_proveedor_y_luego_por_nombre_e_idioma()
    {
        var remote = new[]
        {
            Remote("APPROVED", name: "otra", id: "tpl-1"),
            Remote("REJECTED", name: "aviso_prueba", id: "tpl-2"),
            Remote("APPROVED", name: "aviso_prueba", lang: "en", id: "tpl-3"),
        };
        Assert.Equal("tpl-1", WhatsAppTemplateStatusSync.FindRemote(Local(providerId: "tpl-1"), remote)!.Id);
        Assert.Equal("tpl-2", WhatsAppTemplateStatusSync.FindRemote(Local(), remote)!.Id);
        Assert.Equal("tpl-3", WhatsAppTemplateStatusSync.FindRemote(Local(lang: "EN"), remote)!.Id);
        Assert.Null(WhatsAppTemplateStatusSync.FindRemote(Local(name: "no_existe"), remote));
    }
}
