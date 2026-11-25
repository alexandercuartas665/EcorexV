using Ecorex.Application.Rules.Verbs;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>Referencia de un formulario -> numero de tarea (respuestas adicionales "{numero}-{n}").</summary>
public class TaskReferenceTests
{
    [Theory]
    [InlineData("T00016-2", "T00016")]
    [InlineData("T00016-12", "T00016")]
    [InlineData(" OT-0012-3 ", "OT-0012")]
    public void Quita_el_sufijo_numerico_de_respuestas_adicionales(string reference, string esperado)
        => Assert.Equal(esperado, TaskReference.SinSufijo(reference));

    [Theory]
    [InlineData("T00016")]
    [InlineData("T00016-")]
    [InlineData("-2")]
    [InlineData("T00016-A")]
    [InlineData("")]
    [InlineData(null)]
    public void Sin_sufijo_numerico_devuelve_null(string? reference)
        => Assert.Null(TaskReference.SinSufijo(reference));
}
