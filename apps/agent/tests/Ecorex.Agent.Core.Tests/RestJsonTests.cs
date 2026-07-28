using System.Text.Json;
using Ecorex.Agent.Core.Services;

namespace Ecorex.Agent.Core.Tests;

/// <summary>
/// Parseo tolerante del ejecutor REST: arreglo vs objeto-indexado, arrayPath, envoltorios, clave vacia
/// "" y rutas con indices. Es la parte con reglas sutiles, se prueba en aislamiento (sin red).
/// </summary>
public class RestJsonTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Collection_RootArray_YieldsElementsWithNullKey()
    {
        var root = Parse("""[{"a":1},{"a":2}]""");
        var items = RestJson.Collection(root, null);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Null(i.Key));
        Assert.Equal("1", RestJson.Scalar(items[0].Value.GetProperty("a")));
    }

    [Fact]
    public void Collection_IndexedObject_UsesPropertyNamesAsKeys()
    {
        // Forma OCS: /computers -> objeto indexado por id.
        var root = Parse("""{"1":{"hardware":{"NAME":"PC1"}},"228":{"hardware":{"NAME":"PC228"}}}""");
        var items = RestJson.Collection(root, null);

        Assert.Equal(2, items.Count);
        Assert.Equal("1", items[0].Key);
        Assert.Equal("228", items[1].Key);
        Assert.Equal("PC228", RestJson.Scalar(items[1].Value.GetProperty("hardware").GetProperty("NAME")));
    }

    [Fact]
    public void Collection_IndexedObject_IgnoresScalarMetadataProperties()
    {
        var root = Parse("""{"status":"ok","1":{"x":1}}""");
        var items = RestJson.Collection(root, null);

        Assert.Single(items);
        Assert.Equal("1", items[0].Key);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("items")]
    [InlineData("results")]
    [InlineData("records")]
    [InlineData("rows")]
    public void Collection_CommonWrappers_AreDetected(string wrapper)
    {
        var root = Parse($$"""{"{{wrapper}}":[{"a":1},{"a":2}]}""");
        var items = RestJson.Collection(root, null);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Collection_ExplicitArrayPath_Navigates()
    {
        var root = Parse("""{"payload":{"list":[{"a":1}]}}""");
        var items = RestJson.Collection(root, "payload.list");
        Assert.Single(items);
    }

    [Fact]
    public void Collection_FirstArrayFallback_WhenNoWrapper()
    {
        var root = Parse("""{"weird":[{"a":1},{"a":2},{"a":3}]}""");
        var items = RestJson.Collection(root, null);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void ChildArray_EmptyKey_ResolvesOcsSoftware()
    {
        // Detalle OCS: software resuelto bajo la clave vacia "".
        var root = Parse("""{"":[{"NAME":"Chrome"},{"NAME":"Office"}]}""");
        var children = RestJson.ChildArray(root, "");
        Assert.Equal(2, children.Count);
        Assert.Equal("Chrome", RestJson.Scalar(children[0].GetProperty("NAME")));
    }

    [Fact]
    public void ChildArray_NamedKey_Resolves()
    {
        var root = Parse("""{"software":[{"NAME":"Chrome"}]}""");
        Assert.Single(RestJson.ChildArray(root, "software"));
    }

    [Fact]
    public void ChildArray_TolerantNull_FindsFirstArray()
    {
        var root = Parse("""{"meta":1,"apps":[{"NAME":"Chrome"}]}""");
        Assert.Single(RestJson.ChildArray(root, null));
    }

    [Fact]
    public void ChildArray_MissingOrNonArray_ReturnsEmpty()
    {
        var root = Parse("""{"software":"not-an-array"}""");
        Assert.Empty(RestJson.ChildArray(root, "software"));
        Assert.Empty(RestJson.ChildArray(root, "nope"));
    }

    [Fact]
    public void UnwrapIndexed_DetailWrappedById_DescendsOneLevel()
    {
        // /computer/228 -> {"228": { "": [...] }}
        var root = Parse("""{"228":{"":[{"NAME":"Chrome"}]}}""");
        var inner = RestJson.UnwrapIndexed(root, "");
        var children = RestJson.ChildArray(inner, "");
        Assert.Single(children);
    }

    [Fact]
    public void UnwrapIndexed_WhenChildKeyPresentAtRoot_DoesNotDescend()
    {
        var root = Parse("""{"":[{"NAME":"Chrome"}],"other":{"x":1}}""");
        var inner = RestJson.UnwrapIndexed(root, "");
        // No debe descender: la clave "" ya esta en la raiz.
        Assert.Single(RestJson.ChildArray(inner, ""));
    }

    [Fact]
    public void TryResolve_DottedAndIndexedPaths()
    {
        var el = Parse("""
        {
          "hardware": { "NAME": "PC1", "MEMORY": 8192 },
          "bios": [ { "SSN": "SN-1", "SMODEL": "OptiPlex" } ],
          "accountinfo": [ { "TAG": "BOG-01" } ]
        }
        """);

        Assert.True(RestJson.TryResolve(el, "hardware.NAME", out var name));
        Assert.Equal("PC1", RestJson.Scalar(name));

        Assert.True(RestJson.TryResolve(el, "hardware.MEMORY", out var mem));
        Assert.Equal("8192", RestJson.Scalar(mem));

        Assert.True(RestJson.TryResolve(el, "bios[0].SSN", out var ssn));
        Assert.Equal("SN-1", RestJson.Scalar(ssn));

        Assert.True(RestJson.TryResolve(el, "accountinfo[0].TAG", out var tag));
        Assert.Equal("BOG-01", RestJson.Scalar(tag));
    }

    [Fact]
    public void TryResolve_MissingPath_ReturnsFalse()
    {
        var el = Parse("""{"hardware":{"NAME":"PC1"}}""");
        Assert.False(RestJson.TryResolve(el, "hardware.NOPE", out _));
        Assert.False(RestJson.TryResolve(el, "bios[3].SSN", out _));
        Assert.False(RestJson.TryResolve(el, "missing.deep", out _));
    }

    [Fact]
    public void TryResolve_EmptyPath_ReturnsElementItself()
    {
        var el = Parse("""{"a":1}""");
        Assert.True(RestJson.TryResolve(el, "", out var same));
        Assert.Equal(JsonValueKind.Object, same.ValueKind);
    }

    [Fact]
    public void Scalar_HandlesAllKinds()
    {
        Assert.Equal("txt", RestJson.Scalar(Parse("\"txt\"")));
        Assert.Equal("42", RestJson.Scalar(Parse("42")));
        Assert.Equal("true", RestJson.Scalar(Parse("true")));
        Assert.Equal("false", RestJson.Scalar(Parse("false")));
        Assert.Null(RestJson.Scalar(Parse("null")));
    }
}
