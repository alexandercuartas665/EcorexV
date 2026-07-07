using System.Text.Json;
using Ecorex.Application.MenuConfig;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests PUROS (sin base de datos) de la Ola 2 del menu configurable: las reglas de anidamiento
/// del arbol (que Kind cuelga de que padre) y el round-trip JSON del documento de export/import
/// (misma estructura al serializar y volver a leer). La logica con BD (CRUD de nodos, SetDefault,
/// cascada, Move) se cubre en Ecorex.Integration.Tests (matriz dual PG/SQL Server).
/// </summary>
public class MenuConfigRulesTests
{
    // ---- Reglas de Kind ----

    [Theory]
    [InlineData(MenuNodeKind.Section, null)]        // seccion en primer nivel: OK
    [InlineData(MenuNodeKind.QuickLink, null)]      // enlace rapido en primer nivel: OK
    [InlineData(MenuNodeKind.Subgroup, MenuNodeKind.Section)]  // subgrupo bajo seccion: OK
    [InlineData(MenuNodeKind.Item, MenuNodeKind.Section)]      // item bajo seccion: OK
    [InlineData(MenuNodeKind.Item, MenuNodeKind.Subgroup)]     // item bajo subgrupo: OK
    public void Validate_AllowsCoherentNesting(MenuNodeKind kind, MenuNodeKind? parent)
    {
        Assert.Null(MenuNodeKindRules.Validate(kind, parent));
    }

    [Theory]
    [InlineData(MenuNodeKind.Section, MenuNodeKind.Section)]   // seccion no cuelga de nadie
    [InlineData(MenuNodeKind.QuickLink, MenuNodeKind.Section)] // enlace rapido no cuelga
    [InlineData(MenuNodeKind.Subgroup, MenuNodeKind.Item)]     // subgrupo no cuelga de item
    [InlineData(MenuNodeKind.Subgroup, MenuNodeKind.Subgroup)] // subgrupo no cuelga de subgrupo
    [InlineData(MenuNodeKind.Item, MenuNodeKind.Item)]         // item no cuelga de item
    [InlineData(MenuNodeKind.Item, null)]                      // item no puede ser raiz
    public void Validate_RejectsIncoherentNesting(MenuNodeKind kind, MenuNodeKind? parent)
    {
        Assert.NotNull(MenuNodeKindRules.Validate(kind, parent));
    }

    // ---- Export -> Import: round-trip del documento portable ----

    [Fact]
    public void ExportDocument_JsonRoundTrip_PreservesStructure()
    {
        var doc = new MenuExportDocument(
            "Completo",
            "Menu completo",
            new List<MenuExportNode>
            {
                new("Section", "Mis Procesos", "list", null, "misproc", null, null, "Ready", true, 0,
                    new List<MenuExportNode>
                    {
                        new("Item", "Crear", null, "000038", "crear-actividad", null, null, "Ready", true, 0, new()),
                        new("Subgroup", "Comercial", null, null, "sg-comercial", null, null, "Ready", true, 1,
                            new List<MenuExportNode>
                            {
                                new("Item", "Requerimientos", null, "000477", "actividades", null, null, "InDevelopment", false, 0, new())
                            })
                    })
            });

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var json = JsonSerializer.Serialize(doc, options);
        var back = JsonSerializer.Deserialize<MenuExportDocument>(json, options);

        Assert.NotNull(back);
        Assert.Equal("Completo", back!.Name);
        Assert.Equal(1, back.FormatVersion);
        var section = Assert.Single(back.Roots);
        Assert.Equal("Section", section.Kind);
        Assert.Equal("misproc", section.Route);
        Assert.Equal(2, section.Children.Count);

        var sub = section.Children.Single(c => c.Kind == "Subgroup");
        var nested = Assert.Single(sub.Children);
        Assert.Equal("Requerimientos", nested.Name);
        Assert.Equal("000477", nested.LegacyCode);
        Assert.Equal("InDevelopment", nested.State);
        Assert.False(nested.IsVisible);
    }

    [Fact]
    public void ExportDocument_DefaultsFormatVersionToOne()
    {
        var doc = new MenuExportDocument("X", null, new List<MenuExportNode>());
        Assert.Equal(1, doc.FormatVersion);
    }
}
