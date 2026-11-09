using Ecorex.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests del SurrogateSanitizingInterceptor: antes de guardar, sanea los valores string con surrogates
/// sueltos (la causa del EncoderFallbackException de Npgsql que reventaba la persistencia del agente y
/// disparaba reintentos que reenviaban el mensaje). Corre con el provider EN MEMORIA (sin Docker): basta
/// para verificar que el interceptor MUTA los valores antes del SaveChanges; el EncoderFallback real de
/// Npgsql no se puede reproducir en memoria, pero al no quedar surrogates sueltos ya no puede ocurrir.
/// Los caracteres Unicode se construyen con casts numericos (char)0x... para mantener el archivo ASCII.
/// </summary>
public class SurrogateSanitizingInterceptorTests
{
    private static readonly string Emoji = new string(new[] { (char)0xD83D, (char)0xDE00 }); // par bien formado
    private const char HighHalf = (char)0xD83D;   // mitad alta suelta
    private const char Repl = (char)0xFFFD;       // reemplazo U+FFFD

    private sealed class Doc
    {
        public int Id { get; set; }
        public string Body { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        public DbSet<Doc> Docs => Set<Doc>();
    }

    private static TestDb NewDb()
    {
        var options = new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase($"surrogate-{Guid.NewGuid()}")
            .AddInterceptors(new SurrogateSanitizingInterceptor())
            .Options;
        return new TestDb(options);
    }

    [Fact]
    public async Task Added_sanea_surrogate_suelto_y_conserva_emoji_valido()
    {
        // El interceptor fija property.CurrentValue sobre la propia instancia, asi que tras SaveChanges
        // la entidad en memoria ya refleja el valor saneado (sin necesidad de releer).
        using var db = NewDb();
        var doc = new Doc { Body = "Hola" + HighHalf + "mundo", Note = "ok " + Emoji };
        db.Docs.Add(doc);
        await db.SaveChangesAsync();

        Assert.Equal("Hola" + Repl + "mundo", doc.Body);   // mitad suelta -> FFFD
        Assert.False(HasLone(doc.Body));
        Assert.Equal("ok " + Emoji, doc.Note);             // emoji valido intacto
    }

    [Fact]
    public async Task Modified_sanea_la_propiedad_cambiada()
    {
        using var db = NewDb();
        var doc = new Doc { Body = "limpio", Note = null };
        db.Docs.Add(doc);
        await db.SaveChangesAsync();

        doc.Body = "roto" + HighHalf;
        await db.SaveChangesAsync();

        Assert.Equal("roto" + Repl, doc.Body);
        Assert.False(HasLone(doc.Body));
    }

    [Fact]
    public async Task Texto_limpio_no_se_altera()
    {
        using var db = NewDb();
        var doc = new Doc { Body = "Pedido listo. Gracias " + Emoji, Note = "sin novedad" };
        db.Docs.Add(doc);
        await db.SaveChangesAsync();

        Assert.Equal("Pedido listo. Gracias " + Emoji, doc.Body);
        Assert.Equal("sin novedad", doc.Note);
    }

    private static bool HasLone(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { i++; continue; }
                return true;
            }
            if (char.IsLowSurrogate(s[i])) { return true; }
        }
        return false;
    }
}
