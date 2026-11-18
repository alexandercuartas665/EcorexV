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
        Assert.Equal(MockupService.AnotadorEntry, r.EntryPath);
        Assert.Equal(3, r.FileCount);
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
