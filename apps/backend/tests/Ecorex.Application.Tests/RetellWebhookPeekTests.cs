using Ecorex.Application.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecorex.Application.Tests;

// Peek del webhook: extrae event + call_id del raw body de forma defensiva (payload malformado -> nulls).
// No toca BD ni deps, asi que el procesador se construye con dependencias no usadas para este metodo.
public class RetellWebhookPeekTests
{
    private static RetellWebhookProcessor Processor() =>
        new(db: null!, protector: null!, forms: null!, clock: TimeProvider.System,
            log: NullLogger<RetellWebhookProcessor>.Instance);

    [Fact]
    public void Peek_ValidPayload_ExtractsEventAndCallId()
    {
        var (ev, callId) = Processor().Peek("{\"event\":\"call_analyzed\",\"call\":{\"call_id\":\"call_123\"}}");
        Assert.Equal("call_analyzed", ev);
        Assert.Equal("call_123", callId);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("{}")]
    [InlineData("{\"event\":\"call_ended\"}")]                 // sin call
    [InlineData("{\"call\":{\"call_id\":\"x\"}}")]             // sin event -> call_id ok, event null
    public void Peek_Malformed_ReturnsNullsOrPartial(string body)
    {
        var (ev, callId) = Processor().Peek(body);
        // En todos estos casos, o el event o el call_id (o ambos) es null: nunca se procesa a ciegas.
        Assert.True(ev is null || callId is null);
    }
}
