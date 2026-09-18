using Ecorex.SuperAdmin.Components.Shared.Forms;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Tokens de sistema del default_value de un campo: {hoy}/{hoy+N} (fecha yyyy-MM-dd), {ahora} (HH:mm) y
/// {numero} (RecordNumber del registro, ej. numero de OT). now se pasa en la zona local del tenant.
/// </summary>
public class FormSystemTokensTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 14, 5, 0, TimeSpan.FromHours(-5));   // 2026-09-18 14:05 America/Bogota

    [Fact]
    public void Hoy_devuelve_la_fecha_local_en_iso()
        => Assert.Equal("2026-09-18", FormSystemTokens.Resolve("{hoy}", null, Now));

    [Fact]
    public void Hoy_mas_n_suma_dias()
    {
        Assert.Equal("2026-09-21", FormSystemTokens.Resolve("{hoy+3}", null, Now));
        Assert.Equal("2026-09-18", FormSystemTokens.Resolve("{hoy+0}", null, Now));
        Assert.Equal("2026-10-01", FormSystemTokens.Resolve("{ hoy + 13 }", null, Now));   // tolera espacios
    }

    [Fact]
    public void Ahora_devuelve_la_hora_local_hhmm()
        => Assert.Equal("14:05", FormSystemTokens.Resolve("{ahora}", null, Now));

    [Fact]
    public void Numero_devuelve_el_record_number_o_vacio()
    {
        Assert.Equal("OT-000016", FormSystemTokens.Resolve("{numero}", "OT-000016", Now));
        Assert.Equal("", FormSystemTokens.Resolve("{numero}", null, Now));
    }

    [Fact]
    public void Mezcla_texto_y_tokens()
        => Assert.Equal("Entrega 2026-09-21 a las 14:05",
            FormSystemTokens.Resolve("Entrega {hoy+3} a las {ahora}", null, Now));

    [Fact]
    public void Sin_tokens_devuelve_el_texto_tal_cual()
        => Assert.Equal("texto fijo", FormSystemTokens.Resolve("texto fijo", "OT-1", Now));
}
