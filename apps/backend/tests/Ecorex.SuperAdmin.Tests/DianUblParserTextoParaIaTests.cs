using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>ADR-0191: texto del documento que recibe el asistente para extraer un campo.</summary>
public class DianUblParserTextoParaIaTests
{
    private const string Xml = """
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ID>FE123</cbc:ID>
          <cbc:Note>SEGUN   ORDEN
             12387 DE SOLDARCO</cbc:Note>
          <cac:OrderReference><cbc:ID>PED-55</cbc:ID></cac:OrderReference>
          <cac:InvoiceLine><cac:Item><cbc:Description>ELECTRODO 6013 OC 12387</cbc:Description></cac:Item></cac:InvoiceLine>
        </Invoice>
        """;

    [Fact]
    public void Junta_notas_referencias_e_items_con_espacios_colapsados()
    {
        var t = DianUblParser.TextoParaIa(Xml);
        Assert.Contains("Nota: SEGUN ORDEN 12387 DE SOLDARCO", t);
        Assert.Contains("OrderReference: PED-55", t);
        Assert.Contains("Item: ELECTRODO 6013 OC 12387", t);
    }

    [Fact]
    public void Recorta_al_maximo_y_tolera_xml_invalido()
    {
        Assert.True(DianUblParser.TextoParaIa(Xml, max: 20).Length <= 20);
        Assert.Equal("", DianUblParser.TextoParaIa("no es xml"));
    }
}
