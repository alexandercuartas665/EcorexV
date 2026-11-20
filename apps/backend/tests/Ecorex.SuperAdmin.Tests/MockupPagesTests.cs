using Ecorex.SuperAdmin.Maqueta;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>Editor de maquetas (ADR-0180): lista de pantallas de una maqueta en HTML puro.</summary>
public sealed class MockupPagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ecx-maqueta-pages-" + Guid.NewGuid().ToString("N"));

    public MockupPagesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Put(string rel, string content)
    {
        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Scan_ListaPantallas_IndexPrimero_EIgnoraKitOcultosYPlantillas()
    {
        Put("index.html", "<html><head><title>Indice</title></head></html>");
        Put("rq01-dependencias.html",
            "<html><head><meta name=\"maq-titulo\" content=\"Dependencias\"><meta name=\"maq-modulo\" content=\"RQ01\"><title>X - TRONOX</title></head></html>");
        Put("admin/usuarios.html", "<html><head><title>Usuarios</title></head></html>");
        Put("anotador/anotador.html", "<html/>");
        Put("vendor/lib/demo.html", "<html/>");
        Put("_plantilla.html", "<html/>");
        Put(".git/x.html", "<html/>");
        Put("assets/app.css", "body{}");

        var site = MockupPages.Scan(_root);

        Assert.False(site.HasManifest);
        Assert.Equal(new[] { "index.html", "admin/usuarios.html", "rq01-dependencias.html" }, site.Pages.Select(p => p.File).ToArray());
        var dep = site.Pages.Single(p => p.File == "rq01-dependencias.html");
        Assert.Equal("Dependencias", dep.Title);
        Assert.Equal("RQ01", dep.Module);
        var usuarios = site.Pages.Single(p => p.File == "admin/usuarios.html");
        Assert.Equal("Usuarios", usuarios.Title);
        Assert.Equal("admin", usuarios.Module);
    }

    [Fact]
    public void Scan_DetectaManifiestoDeTemas()
    {
        Put("index.html", "<html/>");
        Put(MockupPages.ManifestFile, "window.ANOTADOR_MANIFEST = {};");

        Assert.True(MockupPages.Scan(_root).HasManifest);
    }

    [Fact]
    public void Parse_SinTitulo_UsaElArchivo_YDecodificaEntidades()
    {
        Assert.Equal("sin-titulo.html", MockupPages.Parse("<html><body>hola</body></html>", "sin-titulo.html").Title);
        Assert.Equal("Radicacion & PQR", MockupPages.Parse("<title>\n  Radicacion &amp; PQR \n</title>", "a.html").Title);
        Assert.Equal("Firmas", MockupPages.Parse("<meta content='Firmas' name='maq-titulo'>", "b.html").Title);
    }

    [Fact]
    public void Scan_CarpetaInexistente_DevuelveVacio()
    {
        var site = MockupPages.Scan(Path.Combine(_root, "no-existe"));
        Assert.Empty(site.Pages);
        Assert.False(site.HasManifest);
    }
}
