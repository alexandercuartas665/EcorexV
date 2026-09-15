using System.Net;
using System.Text;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Ai;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Regresion del arreglo de VISION en el bucle de herramientas: una imagen adjunta a un mensaje de
/// usuario (AiToolMessage.Images) DEBE viajar en el request al proveedor para que el modelo la VEA.
/// Antes la imagen solo iba al contexto ambiental (herramientas) y nunca al mensaje, asi que el agente
/// respondia a ciegas. Se valida el cuerpo HTTP saliente con un handler que lo captura (sin red real):
///   - Gemini (endpoint OpenAI-compatible): content multimodal con image_url data-uri.
///   - Claude (messages): bloque {type:image, source:base64}.
///   - Sin imagen: no se agrega contenido de imagen (no regresiona el texto puro).
/// </summary>
public class AiProviderClientVisionTests
{
    private sealed class CapturingHandler(string response) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public string? LastUrl { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    // Respuesta NATIVA de Gemini (generateContent) con una llamada a herramienta cargar_productos.
    private const string GeminiNativeToolCallBody =
        "{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"cargar_productos\",\"args\":{\"productos\":[]}}}]}}]," +
        "\"usageMetadata\":{\"promptTokenCount\":3,\"candidatesTokenCount\":2}}";

    [Fact]
    public async Task Gemini_con_documento_usa_la_ruta_nativa_generateContent_con_inlineData_y_tools()
    {
        var handler = new CapturingHandler(GeminiNativeToolCallBody);
        var client = new AiProviderClient(new HttpClient(handler));

        var msg = new AiToolMessage("user", "extrae los productos del PDF",
            Documents: new[] { new AiInlineDocument("UERGBASE64", "application/pdf", "lista.pdf") });
        var tool = new AiToolSpec("cargar_productos", "carga filas al contenedor",
            "{\"type\":\"object\",\"properties\":{\"productos\":{\"type\":\"array\"}}}");

        var res = await client.CompleteWithToolsAsync(
            AiProvider.Gemini, "key", null, "gemini-2.5-pro", "sys", new[] { msg }, new[] { tool });

        Assert.True(res.Ok);
        Assert.NotNull(handler.LastUrl);
        Assert.Contains(":generateContent", handler.LastUrl);          // ruta NATIVA, no OpenAI-compat
        Assert.DoesNotContain("/openai/", handler.LastUrl);
        Assert.NotNull(handler.LastBody);
        Assert.Contains("inlineData", handler.LastBody);               // el PDF viaja como inlineData nativo
        Assert.Contains("application/pdf", handler.LastBody);
        Assert.Contains("UERGBASE64", handler.LastBody);
        Assert.Contains("functionDeclarations", handler.LastBody);     // las tools van en formato nativo
        // Y el functionCall de la respuesta se parsea como AiToolCall:
        Assert.Single(res.ToolCalls);
        Assert.Equal("cargar_productos", res.ToolCalls[0].Name);
    }

    [Fact]
    public async Task Gemini_SIN_documento_sigue_por_OpenAI_compat()
    {
        var handler = new CapturingHandler(GeminiOkBody);
        var client = new AiProviderClient(new HttpClient(handler));

        var res = await client.CompleteWithToolsAsync(
            AiProvider.Gemini, "key", null, "gemini-2.5-pro", "sys",
            new[] { new AiToolMessage("user", "hola sin adjunto") }, Array.Empty<AiToolSpec>());

        Assert.True(res.Ok);
        Assert.NotNull(handler.LastUrl);
        Assert.Contains("/openai/chat/completions", handler.LastUrl);  // sin documento: OpenAI-compat
    }

    private const string GeminiOkBody =
        "{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}";
    private const string ClaudeOkBody =
        "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";

    private static AiToolMessage UserWithImage() =>
        new("user", "que ves en la imagen?", Images: new[] { new AiInlineImage("QUJD", "image/png") });

    [Fact]
    public async Task Gemini_manda_la_imagen_como_image_url_en_el_request()
    {
        var handler = new CapturingHandler(GeminiOkBody);
        var client = new AiProviderClient(new HttpClient(handler));

        var res = await client.CompleteWithToolsAsync(
            AiProvider.Gemini, "key", null, "gemini-2.5-pro", "sys",
            new[] { UserWithImage() }, Array.Empty<AiToolSpec>());

        Assert.True(res.Ok);
        Assert.NotNull(handler.LastBody);
        Assert.Contains("image_url", handler.LastBody);
        Assert.Contains("data:image/png;base64,QUJD", handler.LastBody);
    }

    [Fact]
    public async Task Claude_manda_la_imagen_como_bloque_image_en_el_request()
    {
        var handler = new CapturingHandler(ClaudeOkBody);
        var client = new AiProviderClient(new HttpClient(handler));

        var res = await client.CompleteWithToolsAsync(
            AiProvider.Claude, "key", null, "claude-x", "sys",
            new[] { UserWithImage() }, Array.Empty<AiToolSpec>());

        Assert.True(res.Ok);
        Assert.NotNull(handler.LastBody);
        Assert.Contains("media_type", handler.LastBody);   // solo aparece en el bloque de imagen de Claude
        Assert.Contains("image/png", handler.LastBody);
        Assert.Contains("QUJD", handler.LastBody);          // el base64 de la imagen viaja en el body
    }

    [Fact]
    public async Task Sin_imagen_no_agrega_contenido_de_imagen()
    {
        var handler = new CapturingHandler(GeminiOkBody);
        var client = new AiProviderClient(new HttpClient(handler));

        var res = await client.CompleteWithToolsAsync(
            AiProvider.Gemini, "key", null, "gemini-2.5-pro", "sys",
            new[] { new AiToolMessage("user", "hola sin imagen") }, Array.Empty<AiToolSpec>());

        Assert.True(res.Ok);
        Assert.NotNull(handler.LastBody);
        Assert.DoesNotContain("image_url", handler.LastBody);
    }

    private static AiToolMessage UserWithAudio() =>
        new("user", "escucha esta nota", Audios: new[] { new AiInlineAudio("QUJD", "audio/ogg") });

    [Fact]
    public async Task Gemini_manda_la_nota_de_voz_como_input_audio_en_el_request()
    {
        var handler = new CapturingHandler(GeminiOkBody);
        var client = new AiProviderClient(new HttpClient(handler));

        var res = await client.CompleteWithToolsAsync(
            AiProvider.Gemini, "key", null, "gemini-2.5-pro", "sys",
            new[] { UserWithAudio() }, Array.Empty<AiToolSpec>());

        Assert.True(res.Ok);
        Assert.NotNull(handler.LastBody);
        Assert.Contains("input_audio", handler.LastBody);
        Assert.Contains("ogg", handler.LastBody);   // formato derivado del mime audio/ogg
        Assert.Contains("QUJD", handler.LastBody);  // el base64 del audio viaja en el body
    }

    [Fact]
    public async Task Claude_no_manda_audio_porque_no_lo_soporta()
    {
        var handler = new CapturingHandler(ClaudeOkBody);
        var client = new AiProviderClient(new HttpClient(handler));

        var res = await client.CompleteWithToolsAsync(
            AiProvider.Claude, "key", null, "claude-x", "sys",
            new[] { UserWithAudio() }, Array.Empty<AiToolSpec>());

        Assert.True(res.Ok);
        Assert.NotNull(handler.LastBody);
        Assert.DoesNotContain("input_audio", handler.LastBody);
        Assert.DoesNotContain("QUJD", handler.LastBody);   // el audio NO se envia a Claude
    }
}
