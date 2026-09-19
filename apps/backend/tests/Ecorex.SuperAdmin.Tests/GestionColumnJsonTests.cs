using System.Text.Json;
using Ecorex.SuperAdmin.Components.Shared.Forms;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Editor de pildoras de gestion (FormBuilder OLA 1): escribe SOLO la columna type="gestion" del options_json
/// de un GridDetail, preservando las demas columnas. Modelo que consume el motor (ADR-0085): pills[{label,def,color}].
/// </summary>
public class GestionColumnJsonTests
{
    // Accesor seguro: no todas las columnas tienen todas las claves (ej. 'total' no tiene 'type').
    private static string? P(JsonElement e, string key) => e.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static readonly GestionColumnJson.Pill[] DosPills =
    {
        new("Cotizacion", "FRM-CRM-COT", "#0d9488"),
        new("Orden", "FRM-OT", null),
    };

    [Fact]
    public void Upsert_crea_la_columna_gestion_y_preserva_las_demas()
    {
        // options_json con dos columnas normales (calculo/valores propios).
        var original = """[{"id":"cantidad","label":"Cantidad","type":"text","min":0},{"id":"total","label":"Total","agg":"Sum","calc":"{cantidad}*{precio}"}]""";

        var result = GestionColumnJson.Upsert(original, "Gestiones", DosPills);

        using var doc = JsonDocument.Parse(result);
        var cols = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(3, cols.Count);   // 2 originales + gestion

        // Las columnas originales se preservan intactas (incluidas sus claves calc/agg/min).
        var total = cols.Single(c => P(c, "id") == "total");
        Assert.Equal("Sum", total.GetProperty("agg").GetString());
        Assert.Equal("{cantidad}*{precio}", total.GetProperty("calc").GetString());
        var cant = cols.Single(c => P(c, "id") == "cantidad");
        Assert.Equal(0, cant.GetProperty("min").GetInt32());

        // La columna gestion queda con el modelo esperado.
        var g = cols.Single(c => P(c, "type") == "gestion");
        Assert.Equal("gestion", g.GetProperty("id").GetString());
        Assert.Equal("Gestiones", g.GetProperty("label").GetString());
        var pills = g.GetProperty("pills").EnumerateArray().ToList();
        Assert.Equal(2, pills.Count);
        Assert.Equal("Cotizacion", pills[0].GetProperty("label").GetString());
        Assert.Equal("FRM-CRM-COT", pills[0].GetProperty("def").GetString());
        Assert.Equal("#0d9488", pills[0].GetProperty("color").GetString());
        // La pildora sin color no escribe la clave color.
        Assert.False(pills[1].TryGetProperty("color", out _));
        Assert.Equal("FRM-OT", pills[1].GetProperty("def").GetString());
    }

    [Fact]
    public void Upsert_sobre_columna_gestion_existente_reescribe_solo_sus_pills()
    {
        var original = """[{"id":"cantidad","type":"text"},{"id":"gestion","type":"gestion","label":"Acciones","pills":[{"label":"Vieja","def":"X"}]}]""";
        var result = GestionColumnJson.Upsert(original, "Acciones", new[] { new GestionColumnJson.Pill("Nueva", "FRM-Y", null) });

        using var doc = JsonDocument.Parse(result);
        var cols = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, cols.Count);   // NO duplica la columna gestion
        var g = cols.Single(c => P(c, "type") == "gestion");
        var pills = g.GetProperty("pills").EnumerateArray().ToList();
        Assert.Single(pills);
        Assert.Equal("FRM-Y", pills[0].GetProperty("def").GetString());
    }

    [Fact]
    public void Upsert_omite_pildoras_sin_def()
    {
        var result = GestionColumnJson.Upsert("[]", "Gestiones", new[]
        {
            new GestionColumnJson.Pill("Con def", "FRM-A", null),
            new GestionColumnJson.Pill("Sin def", "", null),
        });
        var read = GestionColumnJson.Read(result);
        Assert.NotNull(read);
        Assert.Single(read!.Value.Pills);
        Assert.Equal("FRM-A", read.Value.Pills[0].Def);
    }

    [Fact]
    public void Read_devuelve_null_si_no_hay_columna_gestion()
        => Assert.Null(GestionColumnJson.Read("""[{"id":"cantidad","type":"text"}]"""));

    [Fact]
    public void Read_roundtrip_label_y_pills()
    {
        var json = GestionColumnJson.Upsert(null, "Mis gestiones", DosPills);
        var read = GestionColumnJson.Read(json);
        Assert.NotNull(read);
        Assert.Equal("Mis gestiones", read!.Value.Label);
        Assert.Equal(2, read.Value.Pills.Count);
        Assert.Equal("FRM-CRM-COT", read.Value.Pills[0].Def);
    }

    [Fact]
    public void Remove_quita_la_columna_gestion_y_conserva_las_demas()
    {
        var original = """[{"id":"cantidad","type":"text"},{"id":"gestion","type":"gestion","pills":[]}]""";
        var result = GestionColumnJson.Remove(original);
        using var doc = JsonDocument.Parse(result);
        var cols = doc.RootElement.EnumerateArray().ToList();
        Assert.Single(cols);
        Assert.Equal("cantidad", cols[0].GetProperty("id").GetString());
    }

    [Fact]
    public void Json_invalido_no_lanza()
    {
        Assert.Null(GestionColumnJson.Read("no es json"));
        // Upsert sobre json invalido arranca de cero (arreglo con solo la columna gestion).
        var result = GestionColumnJson.Upsert("{no-array}", "Gestiones", DosPills);
        Assert.NotNull(GestionColumnJson.Read(result));
    }
}
