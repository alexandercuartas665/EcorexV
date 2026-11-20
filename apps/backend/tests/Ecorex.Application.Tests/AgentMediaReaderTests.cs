using Ecorex.Application.Pdf;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Lector de medios de los agentes (ADR-0174, 2026-11-16): lector generico o propio del agente, PDF rasterizado
/// para DeepSeek/ChatGPT y transcriptor de plataforma para los proveedores que no oyen audio.
/// </summary>
public class AgentMediaReaderTests
{
    private sealed class FakeClient : IAiProviderClient
    {
        public readonly List<(AiProvider Provider, string Model, string System, IReadOnlyList<AiVisionPart> Content)> Vision = new();

        public Task<AiChatResult> CompleteAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiChatTurn> turns, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AiCompletion> CompleteWithToolsAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiToolMessage> messages, IReadOnlyList<AiToolSpec> tools, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AiChatResult> CompleteVisionAsync(AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
            IReadOnlyList<AiVisionPart> content, CancellationToken cancellationToken = default)
        {
            Vision.Add((provider, model, systemPrompt, content));
            return Task.FromResult(new AiChatResult(true, "QUE ES: lista de pedido\nTEXTO: Electrodo E7018 1/8 - 10 cajas", null, 10, 5));
        }
    }

    private sealed class FakeRasterizer(int pages) : IPdfRasterizer
    {
        public int Calls { get; private set; }
        public int? LastDpi { get; private set; }
        public int? LastMax { get; private set; }
        public int ContarPaginas(byte[] pdf) => pages;

        public IReadOnlyList<RasterPage> Rasterizar(byte[] pdf, int dpi = 150, int? maxPaginas = null, CancellationToken ct = default)
        {
            Calls++; LastDpi = dpi; LastMax = maxPaginas;
            return Enumerable.Range(1, Math.Min(pages, maxPaginas ?? pages))
                .Select(i => new RasterPage(i, new byte[] { 1, 2, (byte)i }, "image/jpeg")).ToList();
        }
    }

    private static readonly AgentMediaModel DeepSeek = new(AiProvider.DeepSeek, "k", null, "deepseek-v4-flash");
    private static readonly AgentMediaModel Gemini = new(AiProvider.Gemini, "g", null, "gemini-2.5-pro");
    private static readonly string Pdf = Convert.ToBase64String(new byte[] { 0x25, 0x50, 0x44, 0x46 });

    [Fact]
    public void Prompt_del_lector_propio_o_generico()
    {
        Assert.Equal(AgentMediaReader.GenericReaderPrompt, AgentMediaReader.ReaderPromptFor(null));
        Assert.Equal(AgentMediaReader.GenericReaderPrompt, AgentMediaReader.ReaderPromptFor("   "));
        Assert.Equal("CAMPOS: X", AgentMediaReader.ReaderPromptFor("  CAMPOS: X  "));
        // El generico ya no es la plantilla de factura de energia de EPRING.
        Assert.DoesNotContain("VALOR_TOTAL_COP", AgentMediaReader.GenericReaderPrompt);
    }

    [Fact]
    public async Task Imagen_usa_el_prompt_del_agente_o_el_generico()
    {
        var client = new FakeClient();
        var reader = new AgentMediaReader(client, null);
        await reader.ReadImageAsync(DeepSeek, null, "aW1n", "image/png", default);
        await reader.ReadImageAsync(DeepSeek, AgentMediaReader.EpringLegacyReaderPrompt, "aW1n", null, default);

        Assert.Equal(AgentMediaReader.GenericReaderPrompt, client.Vision[0].System);
        Assert.Equal("image/png", client.Vision[0].Content.Single(p => p.ImageBase64 is not null).ImageMime);
        Assert.Equal(AgentMediaReader.EpringLegacyReaderPrompt, client.Vision[1].System);
        Assert.Equal("image/jpeg", client.Vision[1].Content.Single(p => p.ImageBase64 is not null).ImageMime);
    }

    [Fact]
    public async Task Pdf_en_DeepSeek_se_rasteriza_con_tope_y_se_lee_como_imagenes()
    {
        var client = new FakeClient();
        var raster = new FakeRasterizer(pages: 8);
        var reader = new AgentMediaReader(client, raster);

        var r = await reader.ReadDocumentAsync(DeepSeek, null, Pdf, "application/pdf", default);

        Assert.True(r!.Ok);
        Assert.Equal(1, raster.Calls);
        Assert.Equal(AgentMediaReader.PdfDpi, raster.LastDpi);
        Assert.Equal(AgentMediaReader.MaxPdfPages, raster.LastMax);
        var call = Assert.Single(client.Vision);
        Assert.Equal(AiProvider.DeepSeek, call.Provider);
        Assert.Equal(AgentMediaReader.MaxPdfPages, call.Content.Count(p => p.ImageBase64 is not null));
        Assert.DoesNotContain(call.Content, p => p.DocBase64 is not null);
        Assert.Equal(AgentMediaReader.GenericReaderPrompt, call.System);
    }

    [Fact]
    public async Task Pdf_en_Gemini_va_nativo_sin_rasterizar()
    {
        var client = new FakeClient();
        var raster = new FakeRasterizer(pages: 2);
        var reader = new AgentMediaReader(client, raster);

        await reader.ReadDocumentAsync(Gemini, "PROPIO", Pdf, null, default);

        Assert.Equal(0, raster.Calls);
        var call = Assert.Single(client.Vision);
        Assert.Equal("application/pdf", call.Content.Single(p => p.DocBase64 is not null).DocMime);
        Assert.Equal("PROPIO", call.System);
    }

    [Fact]
    public async Task Pdf_sin_rasterizador_o_sin_paginas_no_llama_al_proveedor()
    {
        var client = new FakeClient();
        Assert.Null(await new AgentMediaReader(client, null).ReadDocumentAsync(DeepSeek, null, Pdf, null, default));
        Assert.Null(await new AgentMediaReader(client, new FakeRasterizer(0)).ReadDocumentAsync(DeepSeek, null, Pdf, null, default));
        Assert.Empty(client.Vision);
    }

    [Fact]
    public void Transcriptor_de_plataforma_para_quien_no_oye_audio()
    {
        // Agente en Gemini: transcribe con su propio modelo.
        Assert.Same(Gemini, AgentMediaReader.PickTranscriber(Gemini, "g2", null, "otro"));

        // Agente en DeepSeek: Gemini de plataforma con el modelo barato por defecto (no el del agente).
        var t = AgentMediaReader.PickTranscriber(DeepSeek, "gkey", "https://gw", null)!;
        Assert.Equal(AiProvider.Gemini, t.Provider);
        Assert.Equal(AgentMediaReader.DefaultTranscriptionModel, t.Model);
        Assert.Equal("gkey", t.ApiKey);
        Assert.Equal("https://gw", t.BaseUrl);

        // Modelo configurado por el admin de plataforma.
        Assert.Equal("gemini-2.5-flash-lite", AgentMediaReader.PickTranscriber(DeepSeek, "gkey", null, " gemini-2.5-flash-lite ")!.Model);

        // Sin Gemini habilitado: no hay transcriptor (comportamiento previo).
        Assert.Null(AgentMediaReader.PickTranscriber(DeepSeek, null, null, null));
    }

    [Fact]
    public async Task Transcribe_manda_el_audio_al_modelo_elegido()
    {
        var client = new FakeClient();
        var transcriber = AgentMediaReader.PickTranscriber(DeepSeek, "gkey", null, null)!;
        await new AgentMediaReader(client, null).TranscribeAsync(transcriber, "d2F2", "audio/wav", default);

        var call = Assert.Single(client.Vision);
        Assert.Equal(AiProvider.Gemini, call.Provider);
        Assert.Equal(AgentMediaReader.DefaultTranscriptionModel, call.Model);
        Assert.Equal("audio/wav", call.Content.Single(p => p.AudioBase64 is not null).AudioMime);
    }
}
