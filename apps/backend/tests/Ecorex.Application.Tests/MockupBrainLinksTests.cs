using System.IO.Compression;
using System.Text;
using Ecorex.Application.Integraciones.Maqueta;

namespace Ecorex.Application.Tests;

/// <summary>Cerebro digital de la maqueta (ADR-0183): sintaxis de enlaces tipo Obsidian y lectura de un vault.</summary>
public class MockupBrainLinksTests
{
    [Fact]
    public void Parse_ClasificaNotasPantallasTarjetasYEtiquetas()
    {
        var body = """
            # Decision de menu
            Ver [[Modulo de Radicacion]] y [[modulo de radicacion|el modulo]] (repetido).
            La pantalla [[rq01-dependencias.html]] y la solicitud [[t00046]].
            Seccion: [[Flujo PQRS#Paso 2]]. Carpeta: [[Archivo/Tablas de retencion]].
            Etiquetas #ux y #ux/menu pero no # titulo ni #123 ni C#.
            Adjunto ![[captura.png]] y [[diagrama.pdf]] no son notas.
            """;

        var links = MockupBrainLinks.Parse(body);

        Assert.Contains(links, l => l is { Kind: "note", Key: "modulo de radicacion", Label: "Modulo de Radicacion" });
        Assert.Single(links, l => l.Key == "modulo de radicacion");
        Assert.Contains(links, l => l is { Kind: "page", Key: "rq01-dependencias.html" });
        Assert.Contains(links, l => l is { Kind: "task", Key: "T00046" });
        Assert.Contains(links, l => l is { Kind: "note", Key: "flujo pqrs" });
        Assert.Contains(links, l => l is { Kind: "note", Key: "tablas de retencion" });
        Assert.Contains(links, l => l is { Kind: "tag", Key: "ux" });
        Assert.Contains(links, l => l is { Kind: "tag", Key: "ux/menu" });
        Assert.DoesNotContain(links, l => l.Kind == "tag" && (l.Key == "123" || l.Key == "titulo"));
        Assert.DoesNotContain(links, l => l.Key.Contains("captura") || l.Key.Contains("diagrama"));
    }

    [Fact]
    public void Parse_IgnoraBloquesYCodigoEnLinea()
    {
        var body = "Antes [[Real]]\n```\n[[En bloque]] #nobloque\n```\nY `[[En linea]]` #si\n~~~js\n[[Otro bloque]]\n~~~\n";

        var links = MockupBrainLinks.Parse(body);

        Assert.Equal(new[] { "real", "si" }, links.Select(l => l.Key));
    }

    [Theory]
    [InlineData("  Mi   nota.md ", "Mi nota", null)]
    [InlineData("", null, "obligatorio")]
    [InlineData("Con [corchete]", null, "[ ]")]
    [InlineData("T00046", null, "tarjeta")]
    [InlineData("inicio.html", null, ".html")]
    [InlineData("Usuarios / Funcionarios", null, "/")]
    public void CleanTitle_ValidaYNormaliza(string input, string? expected, string? error)
    {
        var (title, err) = MockupBrainLinks.CleanTitle(input);
        Assert.Equal(expected, title);
        if (error is null) { Assert.Null(err); } else { Assert.Contains(error, err); }
    }

    [Fact]
    public void RewriteNoteLinks_ConservaSeccionAliasYCodigo()
    {
        var body = "[[Vieja]] y [[vieja#Parte|alias]] y [[Otra]] y `[[Vieja]]`";

        var result = MockupBrainLinks.RewriteNoteLinks(body, "vieja", "Nueva");

        Assert.Equal("[[Nueva]] y [[Nueva#Parte|alias]] y [[Otra]] y `[[Vieja]]`", result);
    }

    [Fact]
    public void Snippet_QuitaMarcasYUsaElAlias()
    {
        var s = MockupBrainLinks.Snippet("# Titulo\n\n**Hola** [[Nota|mundo]] y [[Otra]]\n```\ncodigo\n```");
        Assert.Equal("Titulo Hola mundo y Otra", s);
    }

    [Fact]
    public void ReadVault_TomaLosMd_IgnoraObsidianYAdjuntos_YDesambiguaDuplicados()
    {
        var zip = Zip(
            ("Vault/Inicio.md", "\uFEFFHola [[Ideas]]\r\nlinea 2"),
            ("Vault/Proyectos/Ideas.md", "Ideas #tema"),
            ("Vault/Archivo/Ideas.md", "Ideas viejas"),
            ("Vault/.obsidian/workspace.json", "{}"),
            ("Vault/img/foto.png", "png"),
            ("Vault/Raro [x].md", "nombre con corchetes"));

        var read = MockupBrainService.ReadVault(zip);

        Assert.Null(read.Error);
        Assert.Equal(new[] { "Inicio", "Raro -x- (nota)", "Ideas", "Ideas (Proyectos)" }.OrderBy(t => t), read.Notes.Select(n => n.Title).OrderBy(t => t));
        Assert.Equal("Hola [[Ideas]]\nlinea 2", read.Notes.Single(n => n.Title == "Inicio").Body);
        Assert.Equal(1, read.IgnoredFiles);
        Assert.Contains(read.Warnings, w => w.Contains("repite el titulo"));
    }

    [Fact]
    public void ReadVault_RechazaZipSinNotasOInvalido()
    {
        Assert.NotNull(MockupBrainService.ReadVault(Zip(("a/b.png", "x"))).Error);
        Assert.NotNull(MockupBrainService.ReadVault(new byte[] { 1, 2, 3 }).Error);
        Assert.NotNull(MockupBrainService.ReadVault(Zip(("../fuera.md", "x"))).Error);
    }

    private static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var w = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }
        return ms.ToArray();
    }
}
