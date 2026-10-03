using System;
using System.Text.Json;
using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// PRUEBA DORADA del blindaje HeaderGridCalcError: un campo (encabezado) NO puede sumar una columna de grilla con
// un calc {#...}; debe usar rollup. El agente reincidia en subtotal.calc={#items.total_item}; el guard lo corta.
public class FormAuthoringGuardTests
{
    private static SaveFormQuestionRequest Field(string code, FormControlType type, string? calc)
        => new(null, code, code, type, CalcExpression: calc);

    [Fact]
    public void Rechaza_calc_de_campo_que_referencia_columna_de_grilla()
    {
        var err = FormAuthoringToolset.HeaderGridCalcError(Field("subtotal", FormControlType.Number, "{#items.total_item}"));
        Assert.NotNull(err);
        Assert.Contains("rollup", err!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Acepta_un_calc_normal_de_campo()
    {
        Assert.Null(FormAuthoringToolset.HeaderGridCalcError(Field("iva", FormControlType.Number, "{subtotal}*0.19")));
    }

    [Fact]
    public void Acepta_campo_sin_calc()
    {
        Assert.Null(FormAuthoringToolset.HeaderGridCalcError(Field("subtotal", FormControlType.Number, null)));
    }

    [Fact]
    public void No_aplica_a_GridDetail_su_calc_va_en_options()
    {
        // El calc de una grilla va dentro de options_json (por columna), no en calc_expression -> el guard lo ignora.
        Assert.Null(FormAuthoringToolset.HeaderGridCalcError(Field("items", FormControlType.GridDetail, "{#x}")));
    }

    // REGRESION del bug de perdida de datos: update_question es un PATCH. Al editar SOLO el label, el request
    // resultante debe CONSERVAR container_id y width (antes los reseteaba a null/12 -> campo huerfano y descuadrado).
    [Fact]
    public void Update_parcial_conserva_container_y_width_al_cambiar_solo_el_label()
    {
        var cont = Guid.NewGuid();
        var cur = new FormQuestionDto(
            Id: Guid.NewGuid(), ContainerId: cont, FieldCode: "concepto_honorarios", Label: "Honorarios",
            Caption: null, HelpText: null, ControlType: FormControlType.Paragraph, OptionsJson: null,
            Required: false, SortOrder: 0, GridCol: "col-md-3", Numeral: null, ValidationJson: null, Width: 3);
        // Lo que manda el agente: solo question_id + label (ni container_id ni width).
        var args = JsonDocument.Parse("{\"question_id\":\"" + cur.Id + "\",\"label\":\"Honorarios profesionales\",\"control_type\":\"Paragraph\"}").RootElement;

        var req = FormAuthoringToolset.BuildQuestionRequest(args, cur);

        Assert.Equal("Honorarios profesionales", req.Label); // el cambio pedido SI se aplica
        Assert.Equal(cont, req.ContainerId);                 // y lo NO enviado se conserva (no queda huerfano)
        Assert.Equal(3, req.Width);                          // width intacto (no se resetea a 12)
        Assert.Equal("concepto_honorarios", req.FieldCode);  // field_code intacto
    }

    // REGRESION (seguridad): update_container es PATCH. Renombrar una seccion debe CONSERVAR tipo, padre, ancho
    // y sobre todo allowed_cargos_json (acceso por cargo) y visible_when_json. Antes los reseteaba: la seccion
    // restringida quedaba Segment, en la raiz, a 12 y ABIERTA a todos.
    [Fact]
    public void Update_parcial_de_contenedor_conserva_tipo_padre_width_y_cargos()
    {
        var parent = Guid.NewGuid();
        var cur = new FormContainerDto(Guid.NewGuid(), "Facturacion", FormContainerType.Section, parent, 0, null,
            Width: 6, InlineLabels: true, AllowedCargosJson: "[\"c1\"]", VisibleWhenJson: "{\"field\":\"x\",\"op\":\"equals\",\"value\":\"si\"}");
        var args = JsonDocument.Parse("{\"container_id\":\"" + cur.Id + "\",\"name\":\"Facturacion electronica\"}").RootElement;

        var req = FormAuthoringToolset.BuildContainerRequest(args, cur);

        Assert.Equal("Facturacion electronica", req.Name);
        Assert.Equal(FormContainerType.Section, req.ContainerType);
        Assert.Equal(parent, req.ParentId);
        Assert.Equal(6, req.Width);
        Assert.True(req.InlineLabels);
        Assert.Equal("[\"c1\"]", req.AllowedCargosJson);
        Assert.NotNull(req.VisibleWhenJson);
    }

    // add_container (cur=null) sigue igual: defaults Segment / raiz / 12.
    [Fact]
    public void Add_contenedor_sin_baseline_usa_defaults()
    {
        var args = JsonDocument.Parse("{\"form_id\":\"" + Guid.NewGuid() + "\",\"name\":\"Datos\"}").RootElement;
        var req = FormAuthoringToolset.BuildContainerRequest(args, null);
        Assert.Equal("Datos", req.Name);
        Assert.Null(req.ParentId);
        Assert.Equal(12, req.Width);
        Assert.Null(req.AllowedCargosJson);
    }

    // add_question (cur=null) se comporta como antes: los campos no enviados caen a su default.
    [Fact]
    public void Add_sin_baseline_usa_defaults()
    {
        var conMenos = JsonDocument.Parse("{\"field_code\":\"x\",\"label\":\"X\"}").RootElement;
        Assert.Equal(12, FormAuthoringToolset.BuildQuestionRequest(conMenos, null).Width);

        var conWidth = JsonDocument.Parse("{\"field_code\":\"x\",\"label\":\"X\",\"width\":4}").RootElement;
        Assert.Equal(4, FormAuthoringToolset.BuildQuestionRequest(conWidth, null).Width);
    }
}
