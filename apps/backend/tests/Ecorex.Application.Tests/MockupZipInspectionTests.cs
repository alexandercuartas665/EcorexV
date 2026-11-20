using System.IO.Compression;
using Ecorex.Application.Integraciones.Maqueta;

namespace Ecorex.Application.Tests;

/// <summary>Maquetas en la nube (ADR-0179): validacion del ZIP subido y utilidades de slug/rutas.</summary>
public class MockupZipInspectionTests
{
    [Fact]
    public void ZipDeLaCarpeta_QuitaLaRaiz_DetectaAnotador_EIgnoraOcultos()
    {
        var zip = Zip(
            ("tronox.maqueta/index.html", "<html/>"),
            ("tronox.maqueta/anotador/anotador.html", "<html/>"),
            ("tronox.maqueta/assets/app.css", "body{}"),
            ("tronox.maqueta/.git/config", "x"),
            ("tronox.maqueta/.env", "SECRET=1"),
            ("__MACOSX/tronox.maqueta/._index.html", "x"));

        var r = MockupService.InspectZip(zip);

        Assert.Null(r.Error);
        Assert.Equal("tronox.maqueta/", r.RootPrefix);
        Assert.True(r.HasAnotador);
        // ADR-0180: la maqueta es HTML puro; el kit anotador/ heredado se conserva pero la entrada es index.html.
        Assert.Equal("index.html", r.EntryPath);
        Assert.Equal(3, r.FileCount);
    }

    [Fact]
    public void SoloKitAnotador_SinOtrasPaginas_UsaElKitComoEntrada()
    {
        var r = MockupService.InspectZip(Zip(("anotador/anotador.html", "<html/>"), ("anotador/anotador.js", ""), ("leeme.txt", "")));
        Assert.Null(r.Error);
        Assert.Equal(MockupService.AnotadorEntry, r.EntryPath);

        var conOtra = MockupService.InspectZip(Zip(("anotador/anotador.html", "<html/>"), ("pantallas/inicio.html", "<html/>")));
        Assert.Equal("pantallas/inicio.html", conOtra.EntryPath);
    }

    [Fact]
    public void SourceYTableroPorDefecto_DelProyecto()
    {
        Assert.Equal("maqueta-tronox-sgdea", MockupService.SourceFor(" TRONOX-SGDEA "));
        Assert.Equal("Maqueta TRONOX SGDEA - Ajustes", MockupService.DefaultBoardFor("TRONOX SGDEA"));
        Assert.Equal(150, MockupService.DefaultBoardFor(new string('x', 300)).Length);
    }

    [Fact]
    public void Metadata_LeePantallaArchivoYSelector_YToleraBasura()
    {
        var (pantalla, archivo, selector) = MockupService.ReadMetadata(
            "{\"pantalla\":\"Dependencias\",\"archivoMaqueta\":\"rq01-dependencias.html\",\"elemento\":{\"selector\":\"#guardar\"}}");
        Assert.Equal("Dependencias", pantalla);
        Assert.Equal("rq01-dependencias.html", archivo);
        Assert.Equal("#guardar", selector);

        Assert.Equal((null, null, null), MockupService.ReadMetadata("no es json"));
        Assert.Equal((null, null, null), MockupService.ReadMetadata(null));
        Assert.Equal((null, null, null), MockupService.ReadMetadata("[1,2]"));
    }

    [Fact]
    public void SinAnotador_UsaIndex_OLaPrimeraHtml()
    {
        var conIndex = MockupService.InspectZip(Zip(("index.html", "<html/>"), ("a/b.html", "<html/>")));
        Assert.Null(conIndex.RootPrefix);
        Assert.False(conIndex.HasAnotador);
        Assert.Equal("index.html", conIndex.EntryPath);

        var sinIndex = MockupService.InspectZip(Zip(("pages/z.html", "<html/>"), ("home.html", "<html/>"), ("x.css", "")));
        Assert.Equal("home.html", sinIndex.EntryPath);
    }

    [Theory]
    [InlineData("../fuera.html")]
    [InlineData("a/../../fuera.html")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/x.html")]
    public void RutasInseguras_SeRechazan(string entry)
    {
        var r = MockupService.InspectZip(Zip(("index.html", "<html/>"), (entry, "x")));
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void ZipInvalidoVacioOSinHtml_SeRechaza()
    {
        Assert.NotNull(MockupService.InspectZip(Array.Empty<byte>()).Error);
        Assert.NotNull(MockupService.InspectZip(new byte[] { 1, 2, 3, 4 }).Error);
        Assert.NotNull(MockupService.InspectZip(Zip(("a.css", "x"))).Error);
        Assert.NotNull(MockupService.InspectZip(Zip((".git/config", "x"))).Error);
    }

    [Theory]
    [InlineData("TRONOX SGDEA", "tronox-sgdea")]
    [InlineData("  Gestion Documental - Ola 2 ", "gestion-documental-ola-2")]
    [InlineData("Maqueta \u00d1andu Facil", "maqueta-nandu-facil")]
    public void SlugFrom_GeneraSegmentoValido(string name, string expected)
    {
        var slug = MockupService.SlugFrom(name);
        Assert.Equal(expected, slug);
        Assert.True(MockupService.IsValidSlug(slug));
    }

    private static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var e = archive.CreateEntry(path);
                using var w = new StreamWriter(e.Open());
                w.Write(content);
            }
        }
        return ms.ToArray();
    }
}
