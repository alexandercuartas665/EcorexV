using Ecorex.Application.Common;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del helper de higiene Unicode (SurrogateText). Cubren el saneo de surrogates sueltos (la causa
/// del EncoderFallbackException de Npgsql al persistir) y el truncado que NO parte pares suplentes.
/// Los caracteres Unicode se construyen con casts numericos (char)0x... para mantener el archivo ASCII.
/// </summary>
public class SurrogateTextTests
{
    // U+1F600 (rostro sonriente) = par suplente bien formado U+D83D U+DE00 (2 UTF-16 code units).
    private static readonly string Emoji = new string(new[] { (char)0xD83D, (char)0xDE00 });
    private const char HighHalf = (char)0xD83D;   // mitad ALTA suelta
    private const char LowHalf = (char)0xDE00;    // mitad BAJA suelta
    private const char Repl = (char)0xFFFD;       // caracter de reemplazo U+FFFD

    // Un emoji bien formado CONTIENE code units surrogate (validos). Lo que no debe quedar es un SUELTO.
    private static void AssertNoLoneSurrogate(string value)
        => Assert.False(SurrogateText.HasLoneSurrogate(value), "quedo un surrogate suelto");

    // ---- StripLoneSurrogates ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Strip_nulo_o_vacio_se_devuelve_igual(string? input)
    {
        Assert.Equal(input, SurrogateText.StripLoneSurrogates(input));
    }

    [Fact]
    public void Strip_texto_limpio_devuelve_la_misma_referencia()
    {
        var s = "Hola, pedido listo. Gracias!";
        Assert.Same(s, SurrogateText.StripLoneSurrogates(s));
    }

    [Fact]
    public void Strip_emoji_bien_formado_se_conserva_y_no_asigna()
    {
        var s = "Listo " + Emoji + " ya quedo";
        Assert.Same(s, SurrogateText.StripLoneSurrogates(s));
    }

    [Fact]
    public void Strip_mitad_alta_suelta_se_reemplaza_por_FFFD()
    {
        var s = "Hola" + HighHalf + "mundo";
        var clean = SurrogateText.StripLoneSurrogates(s);
        Assert.Equal("Hola" + Repl + "mundo", clean);
        AssertNoLoneSurrogate(clean!);
    }

    [Fact]
    public void Strip_mitad_baja_suelta_se_reemplaza_por_FFFD()
    {
        var s = "Hola" + LowHalf + "mundo";
        var clean = SurrogateText.StripLoneSurrogates(s);
        Assert.Equal("Hola" + Repl + "mundo", clean);
        AssertNoLoneSurrogate(clean!);
    }

    [Fact]
    public void Strip_mitad_alta_al_final_se_reemplaza()
    {
        var s = "Terminado" + HighHalf;
        var clean = SurrogateText.StripLoneSurrogates(s);
        Assert.Equal("Terminado" + Repl, clean);
    }

    [Fact]
    public void Strip_emoji_valido_junto_a_suelto_conserva_el_valido()
    {
        // Emoji correcto + una mitad alta suelta despues.
        var s = Emoji + "x" + HighHalf + "y";
        var clean = SurrogateText.StripLoneSurrogates(s)!;
        Assert.StartsWith(Emoji, clean);           // el par bien formado sobrevive intacto
        Assert.Equal(Emoji + "x" + Repl + "y", clean);
        AssertNoLoneSurrogate(clean);
    }

    [Fact]
    public void Strip_dos_mitades_altas_seguidas_reemplaza_la_primera()
    {
        // La primera mitad alta no tiene baja (la sigue otra alta) -> suelta; la segunda si forma par.
        var s = HighHalf + Emoji;
        var clean = SurrogateText.StripLoneSurrogates(s)!;
        Assert.Equal(Repl + Emoji, clean);
    }

    // ---- HasLoneSurrogate ----

    [Fact]
    public void HasLone_detecta_y_descarta_correctamente()
    {
        Assert.False(SurrogateText.HasLoneSurrogate("texto normal"));
        Assert.False(SurrogateText.HasLoneSurrogate("con emoji " + Emoji));
        Assert.True(SurrogateText.HasLoneSurrogate("rota " + HighHalf));
        Assert.True(SurrogateText.HasLoneSurrogate(LowHalf + " rota"));
    }

    // ---- SafeTruncate ----

    [Fact]
    public void Truncate_null_se_devuelve_null()
    {
        Assert.Null(SurrogateText.SafeTruncate(null, 10));
    }

    [Fact]
    public void Truncate_mas_corto_que_el_tope_no_cambia()
    {
        var s = "corto";
        Assert.Same(s, SurrogateText.SafeTruncate(s, 100));
    }

    [Fact]
    public void Truncate_maxLength_cero_devuelve_vacio()
    {
        Assert.Equal(string.Empty, SurrogateText.SafeTruncate("algo", 0));
    }

    [Fact]
    public void Truncate_sin_surrogate_en_el_corte_recorta_normal()
    {
        Assert.Equal("abcde", SurrogateText.SafeTruncate("abcdefgh", 5));
    }

    [Fact]
    public void Truncate_que_partiria_un_par_retrocede_un_caracter()
    {
        // "ab" + emoji(2 units): longitud 4. Cortar a 3 caeria entre la mitad alta (indice 2) y la baja (indice 3).
        var s = "ab" + Emoji;
        var cut = SurrogateText.SafeTruncate(s, 3)!;
        Assert.Equal("ab", cut);                   // retrocede: no deja la mitad alta colgando
        AssertNoLoneSurrogate(cut);
    }

    [Fact]
    public void Truncate_justo_despues_del_par_conserva_el_emoji()
    {
        var s = "ab" + Emoji + "cd";   // longitud 6
        var cut = SurrogateText.SafeTruncate(s, 4)!;   // incluye el par completo (indices 2 y 3)
        Assert.Equal("ab" + Emoji, cut);
        AssertNoLoneSurrogate(cut);
    }

    [Fact]
    public void Truncate_nunca_deja_surrogate_suelto_en_cualquier_corte()
    {
        // Barrido: para una cadena con varios emojis, ningun corte debe dejar un surrogate al final.
        var s = "a" + Emoji + "b" + Emoji + Emoji + "cd";
        for (var n = 1; n <= s.Length; n++)
        {
            var cut = SurrogateText.SafeTruncate(s, n)!;
            Assert.False(cut.Length > 0 && char.IsHighSurrogate(cut[^1]),
                $"corte en {n} dejo una mitad alta colgando");
        }
    }
}
