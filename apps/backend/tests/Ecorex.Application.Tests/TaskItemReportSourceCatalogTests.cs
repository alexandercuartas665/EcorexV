using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Panels;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Tests;

/// <summary>
/// Catalogo de la fuente nativa 'Actividades' (<see cref="TaskItemReportSource"/>). Describe() es PURO
/// (no toca BD), asi que se prueba sin contexto. Verifica que la fuente expone los campos por NOMBRE
/// nuevos (Concepto/Categoria/Asignado/Tablero/Etapa), que son agrupables y filtrables, que conserva los
/// existentes, y que un PanelSpec puede agrupar/filtrar por ellos contra el catalogo (ADR-0066).
/// </summary>
public class TaskItemReportSourceCatalogTests
{
    // Describe() no usa el DbContext; para un test de catalogo se puede omitir.
    private static ReportSourceDescriptor Describe() => new TaskItemReportSource(null!).Describe();

    [Theory]
    [InlineData("Concepto", "Concepto")]
    [InlineData("Categoria", "Categoria")]
    [InlineData("AssigneeName", "Asignado")]
    [InlineData("Board", "Tablero")]
    [InlineData("Stage", "Etapa")]
    public void Catalog_ExposesNamedFields_GroupableAndFilterable(string key, string display)
    {
        var field = Describe().FindField(key);

        Assert.NotNull(field);
        Assert.Equal(display, field!.DisplayName);
        Assert.Equal(ReportFieldType.Text, field.Type);
        Assert.True(field.CanGroup);
        Assert.True(field.CanFilter);
    }

    [Fact]
    public void Catalog_KeepsExistingFields()
    {
        var d = Describe();

        foreach (var key in new[] { "Number", "Title", "Status", "Priority", "DueDate", "StartDate", "ClosedAt", "CreatedAt", "ProjectId", "AssigneeUserId", "IsArchived" })
        {
            Assert.NotNull(d.FindField(key));
        }
    }

    [Fact]
    public void PanelSpec_CanGroupByConcepto_AndFilterByTablero()
    {
        // Fuente principal por su nombre visible ("Actividades"); dim y filtro por los campos nuevos.
        var spec = PanelSpec.FromJson(@"{
          ""title"": ""Tareas por concepto"",
          ""sources"": { ""main"": { ""container"": ""Actividades"" } },
          ""filters"": [ { ""field"": ""Tablero"", ""control"": ""dropdown"" } ],
          ""widgets"": [ { ""type"": ""bar"", ""dim"": ""Concepto"", ""agg"": ""count"" } ]
        }")!;

        var errors = PanelSpecValidator.Validate(spec, new[] { Describe() });

        Assert.Empty(errors);
    }

    [Fact]
    public void PanelSpec_CanGroupByAsignado_AndFilterByCategoria()
    {
        var spec = PanelSpec.FromJson(@"{
          ""title"": ""Tareas por asignado"",
          ""sources"": { ""main"": { ""container"": ""Actividades"" } },
          ""filters"": [ { ""field"": ""Categoria"", ""control"": ""text"" } ],
          ""widgets"": [ { ""type"": ""bar"", ""dim"": ""Asignado"", ""agg"": ""count"" } ]
        }")!;

        var errors = PanelSpecValidator.Validate(spec, new[] { Describe() });

        Assert.Empty(errors);
    }
}
