using System.IO.Compression;
using Ecorex.Application.Integraciones.Maqueta;

namespace Ecorex.Application.Tests;

/// <summary>Exportar el cerebro como vault de Obsidian y titulo al restaurar desde la papelera (ADR-0183, v0.16.352).</summary>
public class MockupBrainExportTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
    private static readonly DateTimeOffset At = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_IdaYVuelta_ConReadVault_ConservaNotasEnlacesYAdjuntos()
    {
        var notes = new[]
        {
            new MockupBrainExportNote("Inicio", "Ver [[Ideas]] y [[rq01.html]] #tema\n![[boceto.png|300]]", At),
            new MockupBrainExportNote("Ideas", "- [ ] [[Inicio]]", At)
        };
        var assets = new[] { new MockupBrainExportAsset("boceto.png", Png), new MockupBrainExportAsset("Manual.pdf", "%PDF-1.4 x"u8.ToArray()) };

        var zip = MockupBrainExport.Build("tronox-sgdea", notes, assets);

        Assert.Equal(2, zip.Notes);
        Assert.Equal(2, zip.Assets);
        Assert.Empty(zip.Renamed);
        Assert.Equal(new[] { "tronox-sgdea/Ideas.md", "tronox-sgdea/Inicio.md", "tronox-sgdea/adjuntos/Manual.pdf", "tronox-sgdea/adjuntos/boceto.png" },
            Entries(zip.Zip).OrderBy(e => e, StringComparer.Ordinal));

        var read = MockupBrainService.ReadVault(zip.Zip);
        Assert.Null(read.Error);
        Assert.Equal(0, read.IgnoredFiles);
        Assert.Equal(notes.Select(n => (n.Title, n.Body)).OrderBy(t => t.Title), read.Notes.Select(n => (n.Title, n.Body)).OrderBy(t => t.Title));
        Assert.Equal(new[] { "Manual.pdf", "boceto.png" }, read.Assets.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Build_TitulosQueWindowsNoAcepta_RenombraArchivoYReescribeEnlaces()
    {
        var notes = new[]
        {
            new MockupBrainExportNote("Hora: 10am?", "reunion", At),
            new MockupBrainExportNote("Hora- 10am-", "otra que ya se llamaba asi", At),
            new MockupBrainExportNote("CON", "nombre reservado", At),
            new MockupBrainExportNote("Indice", "[[Hora: 10am?]] [[hora: 10am?#Agenda|la reunion]] [[CON]] `[[Hora: 10am?]]`", At)
        };

        var zip = MockupBrainExport.Build("demo", notes, Array.Empty<MockupBrainExportAsset>());

        Assert.Contains("Hora: 10am? -> Hora- 10am- (2).md", zip.Renamed);
        Assert.Contains("CON -> CON (nota).md", zip.Renamed);
        Assert.Contains("demo/LEEME-exportacion.txt", Entries(zip.Zip));
        var read = MockupBrainService.ReadVault(zip.Zip);
        Assert.Equal("[[Hora- 10am- (2)]] [[Hora- 10am- (2)#Agenda|la reunion]] [[CON (nota)]] `[[Hora: 10am?]]`",
            read.Notes.Single(n => n.Title == "Indice").Body);
        Assert.Equal("otra que ya se llamaba asi", read.Notes.Single(n => n.Title == "Hora- 10am-").Body);
    }

    [Theory]
    [InlineData("Normal", "Normal")]
    [InlineData("a<b>c*d\"e", "a-b-c-d-e")]
    [InlineData("Final.", "Final")]
    [InlineData("aux", "aux (nota)")]
    public void FileTitle_LimpiaSoloLoQueWindowsNoAcepta(string title, string expected)
        => Assert.Equal(expected, MockupBrainExport.FileTitle(title));

    [Fact]
    public void RestoredTitle_ConservaElTitulo_OAgregaRestaurada()
    {
        Assert.Equal("Idea", MockupBrainService.RestoredTitle("Idea", new HashSet<string>()));
        Assert.Equal("Idea (restaurada)", MockupBrainService.RestoredTitle("Idea", new HashSet<string> { "idea" }));
        Assert.Equal("Idea (restaurada 2)", MockupBrainService.RestoredTitle("Idea", new HashSet<string> { "idea", "idea (restaurada)" }));
        var largo = new string('x', 200);
        var r = MockupBrainService.RestoredTitle(largo, new HashSet<string> { largo });
        Assert.Equal(200, r.Length);
        Assert.EndsWith(" (restaurada)", r);
    }

    private static List<string> Entries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToList();
    }
}
