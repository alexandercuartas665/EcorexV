using System.Text.Json;
using Ecorex.Application.DataContainers;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del resolver de rutas ANIDADAS/INDEXADAS del importador REST in-process
/// (<see cref="NestedJsonResolver"/>), replica de la logica del agente Colmena. Cubre el bug del
/// /run de la Config API: con <c>TryGetProperty</c> plano las rutas <c>id_type.name</c>,
/// <c>phones[0].number</c>, etc. quedaban vacias y, en Upsert, sobrescribian la data existente.
/// Se prueba con un FIXTURE con campos anidados/indexados (a.b, arr[0], arr[0].x, ruta ausente).
/// </summary>
public class NestedJsonResolverTests
{
    // Fixture que imita una fila de Siigo: tipo de id anidado, nombre indexado, ciudad de 3 niveles,
    // telefonos/contactos indexados con dot, y un metadata con fecha.
    private const string Fixture = """
    {
      "id": "900123",
      "id_type": { "name": "NIT", "code": "31" },
      "name": ["AGROMETALICAS", "S.A.S"],
      "address": { "city": { "city_name": "Medellin", "state_name": "Antioquia" } },
      "phones": [ { "number": "6041234567", "indicative": "57" } ],
      "contacts": [ { "email": "compras@agro.co", "first_name": "Ana" } ],
      "metadata": { "created": "2024-01-15" },
      "active": true,
      "vat_responsible": null
    }
    """;

    private static JsonElement Root()
    {
        using var doc = JsonDocument.Parse(Fixture);
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("id", "900123")]                       // plano
    [InlineData("id_type.name", "NIT")]                // a.b
    [InlineData("id_type.code", "31")]
    [InlineData("name[0]", "AGROMETALICAS")]           // arr[0]
    [InlineData("name[1]", "S.A.S")]
    [InlineData("address.city.city_name", "Medellin")] // a.b.c
    [InlineData("phones[0].number", "6041234567")]     // arr[0].x
    [InlineData("contacts[0].email", "compras@agro.co")]
    [InlineData("metadata.created", "2024-01-15")]
    [InlineData("active", "true")]                     // booleano como texto
    public void TryResolve_aterriza_los_valores_anidados_e_indexados(string path, string expected)
    {
        var ok = NestedJsonResolver.TryResolve(Root(), path, out var v);
        Assert.True(ok);
        Assert.Equal(expected, NestedJsonResolver.Scalar(v));
    }

    [Theory]
    [InlineData("id_type.missing")]     // propiedad ausente en objeto existente
    [InlineData("nope.deep.path")]      // primer segmento ausente
    [InlineData("name[5]")]             // indice fuera de rango
    [InlineData("phones[0].fax")]       // dot ausente tras indice valido
    [InlineData("phones[3].number")]    // indice fuera de rango antes del dot
    public void TryResolve_devuelve_false_cuando_la_ruta_no_existe(string path)
    {
        var ok = NestedJsonResolver.TryResolve(Root(), path, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryResolve_ruta_a_json_null_resuelve_con_valor_null()
    {
        // La ruta EXISTE pero el valor es JSON null: resuelve true, Scalar = null (limpiar explicito).
        var ok = NestedJsonResolver.TryResolve(Root(), "vat_responsible", out var v);
        Assert.True(ok);
        Assert.Null(NestedJsonResolver.Scalar(v));
    }

    [Fact]
    public void ProjectRow_incluye_rutas_resueltas_y_OMITE_las_ausentes()
    {
        var paths = new[]
        {
            "id_type.name",       // resuelve -> NIT
            "phones[0].number",   // resuelve -> 6041234567
            "name[0]",            // resuelve -> AGROMETALICAS
            "id_type.missing",    // ausente  -> se omite
            "phones[9].number",   // ausente  -> se omite
        };

        var row = NestedJsonResolver.ProjectRow(Root(), paths);

        Assert.Equal("NIT", row["id_type.name"]);
        Assert.Equal("6041234567", row["phones[0].number"]);
        Assert.Equal("AGROMETALICAS", row["name[0]"]);

        // La clave para el no-sobrescribir en Upsert: las rutas ausentes NO estan en el diccionario.
        Assert.False(row.ContainsKey("id_type.missing"));
        Assert.False(row.ContainsKey("phones[9].number"));
    }
}
