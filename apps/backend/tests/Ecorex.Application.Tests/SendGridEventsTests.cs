using Ecorex.Application.Common;
using Ecorex.Application.Gestor;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>Logica pura del Event Webhook de SendGrid (ADR-0188): parseo, precedencia y supresiones.</summary>
public class SendGridEventsTests
{
    private static readonly Guid Tenant = Guid.Parse("019a0000-0000-7000-8000-000000000001");
    private static readonly Guid Run = Guid.Parse("019a0000-0000-7000-8000-000000000002");

    [Fact]
    public void Parse_ReadsBatch_WithUniqueArgs_AndMessagePrefix()
    {
        var json = $$"""
            [
              {"email":" Ana@Destino.TEST ","timestamp":1800000000,"event":"delivered","sg_message_id":"q-123.filterdrecv-1.0","ecx_tenant":"{{Tenant}}","ecx_run":"{{Run}}"},
              {"email":"b@d.test","timestamp":"1800000060","event":"bounce","type":"blocked","reason":"421 later"},
              {"email":"c@d.test","event":"deferred","response":"451 greylisted"},
              {"no_event":true},
              "basura"
            ]
            """;

        var list = SendGridEvents.Parse(json);

        Assert.Equal(3, list.Count);
        var d = list[0];
        Assert.Equal("delivered", d.Event);
        Assert.Equal("ana@destino.test", d.Email);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), d.AtUtc);
        Assert.Equal(Tenant, d.TenantId);
        Assert.Equal(Run, d.RunId);
        Assert.Equal("q-123", d.MessageId);
        Assert.Equal("blocked", list[1].BounceType);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_060), list[1].AtUtc);
        Assert.Equal("451 greylisted", list[2].Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("{\"event\":\"open\"}")]
    public void Parse_InvalidBody_IsEmpty(string body) => Assert.Empty(SendGridEvents.Parse(body));

    [Fact]
    public void ApplyToRun_OnlyRaises_AndKeepsFirstOpen()
    {
        var run = new ContactWorkflowRun();
        var t = DateTimeOffset.UtcNow;

        Assert.True(SendGridEvents.ApplyToRun(run, E("open", t)));
        Assert.False(SendGridEvents.ApplyToRun(run, E("delivered", t.AddSeconds(-30)))); // tardio: no cambia estado...
        Assert.Equal(EmailDeliveryStatus.Opened, run.DeliveryStatus);
        Assert.False(SendGridEvents.ApplyToRun(run, E("open", t.AddMinutes(5))));        // ...ni la primera apertura
        Assert.Equal(t, run.OpenedAtUtc);
        Assert.True(SendGridEvents.ApplyToRun(run, E("spamreport", t.AddMinutes(9))));
        Assert.Equal(EmailDeliveryStatus.SpamReport, run.DeliveryStatus);
        Assert.False(SendGridEvents.ApplyToRun(run, E("group_resubscribe", t.AddMinutes(10))));
    }

    [Fact]
    public void ApplyToRun_BounceKeepsReason()
    {
        var run = new ContactWorkflowRun();
        SendGridEvents.ApplyToRun(run, new SendGridEvent("bounce", "a@b.test", DateTimeOffset.UtcNow, null, null, null, "550 no such user", "bounce"));
        Assert.Equal(EmailDeliveryStatus.Bounced, run.DeliveryStatus);
        Assert.Equal("Rebote: 550 no such user", run.DeliveryDetail);
    }

    [Theory]
    [InlineData("bounce", "bounce", null, "rebote")]
    [InlineData("bounce", "blocked", null, null)]
    [InlineData("spamreport", null, null, "spam")]
    [InlineData("unsubscribe", null, null, "proveedor")]
    [InlineData("group_unsubscribe", null, null, "proveedor")]
    [InlineData("dropped", null, "Bounced Address", "rebote")]
    [InlineData("dropped", null, "Invalid", "rebote")]
    [InlineData("dropped", null, "Invalid SMTPAPI header", null)]
    [InlineData("dropped", null, "Spam Reporting Address", "spam")]
    [InlineData("dropped", null, "Unsubscribed Address", "proveedor")]
    [InlineData("dropped", null, "Exceeded Max Content Size", null)]
    [InlineData("delivered", null, null, null)]
    [InlineData("open", null, null, null)]
    public void SuppressionFor_MapsEvents(string evt, string? type, string? reason, string? expectedOrigen)
    {
        var s = SendGridEvents.SuppressionFor(new SendGridEvent(evt, "a@b.test", DateTimeOffset.UtcNow, null, null, null, reason, type));
        Assert.Equal(expectedOrigen, s?.Origen);
        if (s is not null) { Assert.False(s.Remove); }
    }

    [Fact]
    public void SuppressionFor_Resubscribe_Removes_AndNoEmailIsIgnored()
    {
        var s = SendGridEvents.SuppressionFor(new SendGridEvent("group_resubscribe", "a@b.test", DateTimeOffset.UtcNow, null, null, null, null, null));
        Assert.True(s!.Remove);
        Assert.Null(SendGridEvents.SuppressionFor(new SendGridEvent("bounce", null, DateTimeOffset.UtcNow, null, null, null, null, "bounce")));
    }

    [Fact]
    public void WithTracking_AddsRunAndTenant_AndKeepsSender()
    {
        var sender = new EmailSenderOverride("Ventas", "ventas@x.test", null);
        var tracked = ContactWorkflowDispatcher.WithTracking(sender, Tenant, Run);
        Assert.Equal("Ventas", tracked.FromName);
        Assert.Equal(Run.ToString(), tracked.CustomArgs![SendGridEvents.RunArg]);
        Assert.Equal(Tenant.ToString(), tracked.CustomArgs[SendGridEvents.TenantArg]);

        var bare = ContactWorkflowDispatcher.WithTracking(null, null, Run);
        Assert.Null(bare.FromEmail);
        Assert.False(bare.CustomArgs!.ContainsKey(SendGridEvents.TenantArg));
    }

    private static SendGridEvent E(string evt, DateTimeOffset at) => new(evt, "a@b.test", at, null, null, null, null, null);
}
