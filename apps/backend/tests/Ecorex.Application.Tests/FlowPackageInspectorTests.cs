using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Workflows;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del detector de formularios de nodo (FlowPackageInspector, ADR-0097 B3): decide si el asistente
/// de "Traer" debe preguntar por la migracion de los formularios vinculados a los nodos del flujo.
/// </summary>
public class FlowPackageInspectorTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static string Serialize(FlowPackage pkg) => JsonSerializer.Serialize(pkg, Json);

    private static FlowPackageNode Node(string id, params FlowPackageForm[] forms)
        => new(id, "task", id, 0, 0, null, null, "None", null, null, null,
            forms.ToList(), Array.Empty<string>(), null, Array.Empty<FlowPackageRule>());

    private static FlowPackageForm Form(int sort)
        => new("{\"code\":\"F\"}", sort, false, false);

    [Fact]
    public void SinFormularios_NoPregunta()
    {
        var pkg = new FlowPackage(1, "Flujo", null, null,
            new[] { Node("inicio"), Node("fin") }, Array.Empty<FlowPackageEdge>());
        var (has, count) = FlowPackageInspector.CountNodeForms(Serialize(pkg));
        Assert.False(has);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ConFormularios_CuentaTodosLosNodos()
    {
        var pkg = new FlowPackage(1, "Flujo", null, null,
            new[] { Node("a", Form(0), Form(1)), Node("b"), Node("c", Form(0)) },
            Array.Empty<FlowPackageEdge>());
        var (has, count) = FlowPackageInspector.CountNodeForms(Serialize(pkg));
        Assert.True(has);
        Assert.Equal(3, count); // 2 en 'a' + 0 en 'b' + 1 en 'c'
    }

    [Fact]
    public void PosicionDeNota_SobreviveElRoundTripDelPaquete()
    {
        // La posicion del post-it de la nota (offset relativo al nodo) viaja en el paquete y debe
        // conservarse tras exportar/importar (JSON). Paquetes viejos sin el campo => null (compat).
        var conPos = new FlowPackageNode("a", "task", "a", 0, 0, null, null, "None", null, null, null,
            Array.Empty<FlowPackageForm>(), Array.Empty<string>(), null, Array.Empty<FlowPackageRule>(),
            NoteOffsetX: 120, NoteOffsetY: -40);
        var pkg = new FlowPackage(1, "Flujo", null, null,
            new[] { conPos, Node("b") }, Array.Empty<FlowPackageEdge>());

        var back = JsonSerializer.Deserialize<FlowPackage>(Serialize(pkg), Json)!;

        var a = back.Nodes.Single(n => n.BpmnElementId == "a");
        Assert.Equal(120, a.NoteOffsetX);
        Assert.Equal(-40, a.NoteOffsetY);
        var b = back.Nodes.Single(n => n.BpmnElementId == "b");
        Assert.Null(b.NoteOffsetX);
        Assert.Null(b.NoteOffsetY);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ no es un paquete }")]
    [InlineData("[]")]
    public void JsonInvalidoOVacio_DevuelveFalseCero(string? json)
    {
        var (has, count) = FlowPackageInspector.CountNodeForms(json);
        Assert.False(has);
        Assert.Equal(0, count);
    }
}
