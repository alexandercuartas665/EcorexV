using Ecorex.Application.Voice;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Resultado visible de una llamada del motor de acciones y regla de reintento por "no contesto" (ADR-0190).
/// </summary>
public class VoiceCallOutcomesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(VoiceCallStatus.Ended, "dial_no_answer", VoiceCallOutcome.NoContesto)]
    [InlineData(VoiceCallStatus.Ended, "dial_busy", VoiceCallOutcome.Ocupado)]
    [InlineData(VoiceCallStatus.Ended, "voicemail_reached", VoiceCallOutcome.Buzon)]
    [InlineData(VoiceCallStatus.Ended, "user_declined", VoiceCallOutcome.Rechazada)]
    [InlineData(VoiceCallStatus.Analyzed, "user_hangup", VoiceCallOutcome.Contesto)]
    [InlineData(VoiceCallStatus.Analyzed, "AGENT_HANGUP", VoiceCallOutcome.Contesto)]
    [InlineData(VoiceCallStatus.Ended, "dial_failed", VoiceCallOutcome.NoContesto)]
    [InlineData(VoiceCallStatus.Ended, "invalid_destination", VoiceCallOutcome.Fallo)]
    [InlineData(VoiceCallStatus.Registered, null, VoiceCallOutcome.Llamando)]
    [InlineData(VoiceCallStatus.Ongoing, null, VoiceCallOutcome.EnCurso)]
    [InlineData(VoiceCallStatus.Analyzed, null, VoiceCallOutcome.Contesto)]
    [InlineData(VoiceCallStatus.Error, null, VoiceCallOutcome.Fallo)]
    public void Clasifica_por_motivo_y_estado(VoiceCallStatus status, string? reason, VoiceCallOutcome expected)
        => Assert.Equal(expected, VoiceCallOutcomes.Classify(status, reason));

    [Fact]
    public void Todo_motivo_sin_contacto_de_los_flujos_se_reintenta()
    {
        foreach (var reason in new[] { "dial_no_answer", "voicemail_reached", "dial_busy", "dial_failed", "no_valid_payment" })
        {
            Assert.True(RetellVoiceService.IsNoContactReason(reason));
            Assert.True(VoiceCallOutcomes.IsRetryable(VoiceCallOutcomes.Classify(VoiceCallStatus.Ended, reason)), reason);
        }
    }

    [Fact]
    public void Reintenta_si_no_contesto_quedan_intentos_y_ya_paso_la_espera()
        => Assert.True(VoiceCallOutcomes.ShouldRetry(VoiceCallOutcome.NoContesto, attempts: 1, retries: 2, retryMinutes: 15,
            lastAttemptUtc: Now.AddMinutes(-16), nowUtc: Now));

    [Fact]
    public void No_reintenta_antes_de_la_espera()
        => Assert.False(VoiceCallOutcomes.ShouldRetry(VoiceCallOutcome.Buzon, attempts: 1, retries: 2, retryMinutes: 15,
            lastAttemptUtc: Now.AddMinutes(-10), nowUtc: Now));

    [Fact]
    public void No_reintenta_si_se_agotaron_los_intentos()
        => Assert.False(VoiceCallOutcomes.ShouldRetry(VoiceCallOutcome.NoContesto, attempts: 3, retries: 2, retryMinutes: 5,
            lastAttemptUtc: Now.AddHours(-2), nowUtc: Now));

    [Theory]
    [InlineData(VoiceCallOutcome.Contesto)]
    [InlineData(VoiceCallOutcome.Fallo)]
    [InlineData(VoiceCallOutcome.EnCurso)]
    public void No_reintenta_si_contesto_fallo_o_sigue_en_curso(VoiceCallOutcome outcome)
        => Assert.False(VoiceCallOutcomes.ShouldRetry(outcome, attempts: 1, retries: 5, retryMinutes: 5,
            lastAttemptUtc: Now.AddHours(-2), nowUtc: Now));

    [Fact]
    public void Sin_reintentos_configurados_nunca_reintenta()
        => Assert.False(VoiceCallOutcomes.ShouldRetry(VoiceCallOutcome.NoContesto, attempts: 1, retries: null, retryMinutes: null,
            lastAttemptUtc: Now.AddHours(-2), nowUtc: Now));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-3, 0)]
    [InlineData(9, VoiceCallOutcomes.MaxRetries)]
    public void Acota_reintentos(int? input, int expected) => Assert.Equal(expected, VoiceCallOutcomes.ClampRetries(input));

    [Theory]
    [InlineData(null, VoiceCallOutcomes.DefaultRetryMinutes)]
    [InlineData(1, VoiceCallOutcomes.MinRetryMinutes)]
    [InlineData(999, 240)]
    public void Acota_minutos(int? input, int expected) => Assert.Equal(expected, VoiceCallOutcomes.ClampRetryMinutes(input));
}
