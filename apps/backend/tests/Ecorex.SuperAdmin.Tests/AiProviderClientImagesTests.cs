using System.Net;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Ai;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Imagenes al modelo principal en DeepSeek/ChatGPT (ADR-0174, 2026-11-16). Antes solo Gemini las recibia y en
/// DeepSeek se descartaban en silencio. Si el modelo no tiene vision (400 por image_url), un reintento sin imagenes.
/// </summary>
public class AiProviderClientImagesTests
{
    private const string OkChatBody =
        "{\"choices\":[{\"message\":{\"content\":\"listo\"}}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2}}";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public readonly List<string> Bodies = new();
        public readonly Queue<(HttpStatusCode Status, string Body)> Responses = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var (status, body) = Responses.Count > 0 ? Responses.Dequeue() : (HttpStatusCode.OK, OkChatBody);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly AiInlineImage Img = new("aW1hZ2Vu", "image/jpeg");

    private static Task<AiCompletion> Ask(AiProviderClient client, AiProvider provider, params AiToolMessage[] messages) =>
        client.CompleteWithToolsAsync(provider, "key", null, "m", "sys", messages, Array.Empty<AiToolSpec>());

    private static JsonElement LastUserContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var user = doc.RootElement.GetProperty("messages").EnumerateArray().Last(m => m.GetProperty("role").GetString() == "user");
        return user.GetProperty("content").Clone();
    }

    [Theory]
    [InlineData(AiProvider.DeepSeek)]
    [InlineData(AiProvider.ChatGpt)]
    [InlineData(AiProvider.Gemini)]
    public async Task La_imagen_del_usuario_viaja_como_image_url(AiProvider provider)
    {
        var handler = new CapturingHandler();
        var client = new AiProviderClient(new HttpClient(handler));

        var r = await Ask(client, provider, new AiToolMessage("user", "mira la foto", Images: new[] { Img }));

        Assert.True(r.Ok);
        Assert.Null(r.Notice);
        var content = LastUserContent(Assert.Single(handler.Bodies));
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        var parts = content.EnumerateArray().ToList();
        Assert.Equal("text", parts[0].GetProperty("type").GetString());
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
        Assert.Equal("data:image/jpeg;base64,aW1hZ2Vu", parts[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task En_DeepSeek_el_audio_no_viaja_y_sin_media_el_content_es_texto()
    {
        var handler = new CapturingHandler();
        var client = new AiProviderClient(new HttpClient(handler));

        await Ask(client, AiProvider.DeepSeek,
            new AiToolMessage("user", "nota", Audios: new[] { new AiInlineAudio("YXU=", "audio/wav") }));

        var content = LastUserContent(Assert.Single(handler.Bodies));
        Assert.Equal(JsonValueKind.String, content.ValueKind);
        Assert.Equal("nota", content.GetString());
    }

    [Fact]
    public async Task Modelo_sin_vision_reintenta_una_vez_sin_imagenes_y_avisa()
    {
        var handler = new CapturingHandler();
        handler.Responses.Enqueue((HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"Failed to deserialize the JSON body: unknown variant `image_url`, expected `text`\"}}"));
        var client = new AiProviderClient(new HttpClient(handler));

        var r = await Ask(client, AiProvider.DeepSeek, new AiToolMessage("user", "mira", Images: new[] { Img }));

        Assert.True(r.Ok);
        Assert.Equal("listo", r.Text);
        Assert.Contains("no acepto las imagenes", r.Notice);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains("image_url", handler.Bodies[0]);
        Assert.DoesNotContain("image_url", handler.Bodies[1]);
        Assert.Equal("mira", LastUserContent(handler.Bodies[1]).GetString());
    }

    [Fact]
    public async Task Un_400_sin_imagenes_no_se_reintenta()
    {
        var handler = new CapturingHandler();
        handler.Responses.Enqueue((HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"bad request\"}}"));
        var client = new AiProviderClient(new HttpClient(handler));

        var r = await Ask(client, AiProvider.DeepSeek, new AiToolMessage("user", "hola"));

        Assert.False(r.Ok);
        Assert.Single(handler.Bodies);
    }
}
