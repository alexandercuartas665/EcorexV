using System.Text.Json;
using Ecorex.Application.Rules;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Contrato del params_json de la regla AL ENVIAR "crear actividad" (FormBuilder OLA 4). Las claves deben
/// ser EXACTAMENTE las que lee GenerarTareasDesdeTablaVerb (camelCase): activityTypeId, assigneeUserId,
/// titlePrefix, autoComplete, tableField+titleKey (una por fila) o rows (una sola tarea).
/// </summary>
public class FormSubmitRuleParamsTests
{
    private static readonly Guid Act = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usr = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Build_tarea_fija_usa_rows_con_el_titulo()
    {
        var json = FormSubmitRuleParams.Build(Act, Usr, tableFieldCode: null, titleKey: null,
            fixedTitle: "  Contacto con el cliente  ", titlePrefix: "[WEB] ", autoComplete: false);

        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal(Act.ToString(), r.GetProperty("activityTypeId").GetString());
        Assert.Equal(Usr.ToString(), r.GetProperty("assigneeUserId").GetString());
        Assert.Equal("[WEB]", r.GetProperty("titlePrefix").GetString());   // trim
        Assert.False(r.TryGetProperty("autoComplete", out _));             // false no se escribe
        Assert.False(r.TryGetProperty("tableField", out _));
        var rows = r.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("Contacto con el cliente", rows[0].GetString());      // trim
    }

    [Fact]
    public void Build_por_fila_usa_tableField_y_titleKey_sin_rows()
    {
        var json = FormSubmitRuleParams.Build(Act, assigneeTenantUserId: null,
            tableFieldCode: "items", titleKey: "producto", fixedTitle: null, titlePrefix: null, autoComplete: true);

        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.Equal("items", r.GetProperty("tableField").GetString());
        Assert.Equal("producto", r.GetProperty("titleKey").GetString());
        Assert.True(r.GetProperty("autoComplete").GetBoolean());
        Assert.False(r.TryGetProperty("rows", out _));
        Assert.False(r.TryGetProperty("assigneeUserId", out _));           // sin asignar => omitido
    }

    [Fact]
    public void Build_omite_assignee_vacio_y_prefijo_vacio()
    {
        var json = FormSubmitRuleParams.Build(Act, Guid.Empty, null, null, "X", "   ", false);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        Assert.False(r.TryGetProperty("assigneeUserId", out _));
        Assert.False(r.TryGetProperty("titlePrefix", out _));
    }

    [Fact]
    public void Parse_roundtrip_tarea_fija()
    {
        var json = FormSubmitRuleParams.Build(Act, Usr, null, null, "Llamar", "[X] ", false);
        var p = FormSubmitRuleParams.Parse(json);
        Assert.Equal(Act, p.ActivityTypeId);
        Assert.Equal(Usr, p.AssigneeTenantUserId);
        Assert.Null(p.TableFieldCode);
        Assert.Equal("Llamar", p.FixedTitle);
        Assert.Equal("[X]", p.TitlePrefix);
        Assert.False(p.AutoComplete);
    }

    [Fact]
    public void Parse_roundtrip_por_fila()
    {
        var json = FormSubmitRuleParams.Build(Act, null, "items", "producto", null, null, true);
        var p = FormSubmitRuleParams.Parse(json);
        Assert.Equal(Act, p.ActivityTypeId);
        Assert.Null(p.AssigneeTenantUserId);
        Assert.Equal("items", p.TableFieldCode);
        Assert.Equal("producto", p.TitleKey);
        Assert.Null(p.FixedTitle);
        Assert.True(p.AutoComplete);
    }

    [Fact]
    public void Parse_lee_fixedTitle_desde_row_objeto_title()
    {
        var p = FormSubmitRuleParams.Parse("""{"activityTypeId":"11111111-1111-1111-1111-111111111111","rows":[{"title":"Desde objeto"}]}""");
        Assert.Equal("Desde objeto", p.FixedTitle);
    }

    [Fact]
    public void Parse_json_vacio_o_ilegible_todo_null()
    {
        foreach (var bad in new[] { null, "", "   ", "no es json", "[1,2,3]" })
        {
            var p = FormSubmitRuleParams.Parse(bad);
            Assert.Null(p.ActivityTypeId);
            Assert.Null(p.FixedTitle);
            Assert.False(p.AutoComplete);
        }
    }
}
