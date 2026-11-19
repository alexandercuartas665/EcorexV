using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Orden de compra de la factura DIAN: se toma de cac:OrderReference/cbc:ID tal cual; si no viene, se busca una
/// orden OMP en las notas (normalizada); si no hay nada, queda vacia.
/// </summary>
public class DianUblParserOrdenCompraTests
{
    private const string NuestroNit = "890322642";

    private static string Factura(string extra) => $"""
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2">
          <cbc:ID>FE123</cbc:ID>
          <cbc:UUID>cufe-abc</cbc:UUID>
          <cbc:IssueDate>2026-11-18</cbc:IssueDate>
          <cbc:InvoiceTypeCode>01</cbc:InvoiceTypeCode>
          {extra}
          <cac:AccountingSupplierParty><cac:Party><cac:PartyTaxScheme>
            <cbc:RegistrationName>PROVEEDOR SAS</cbc:RegistrationName><cbc:CompanyID>900111222</cbc:CompanyID>
          </cac:PartyTaxScheme></cac:Party></cac:AccountingSupplierParty>
          <cac:LegalMonetaryTotal><cbc:PayableAmount>100</cbc:PayableAmount></cac:LegalMonetaryTotal>
        </Invoice>
        """;

    [Fact]
    public void Toma_OrderReference_tal_cual()
    {
        var doc = DianUblParser.ParseXml(Factura("<cac:OrderReference><cbc:ID>OMP0012371</cbc:ID></cac:OrderReference>"), NuestroNit);
        Assert.Equal("OMP0012371", doc!.OrdenCompra);
    }

    [Fact]
    public void OrderReference_sin_formato_OMP_se_guarda_igual()
    {
        var doc = DianUblParser.ParseXml(Factura("<cac:OrderReference><cbc:ID>CO:890322642:1:4:PAG</cbc:ID></cac:OrderReference>"), NuestroNit);
        Assert.Equal("CO:890322642:1:4:PAG", doc!.OrdenCompra);
    }

    [Fact]
    public void Sin_OrderReference_busca_OMP_en_las_notas_y_normaliza()
    {
        var doc = DianUblParser.ParseXml(Factura("<cbc:Note>ORDEN DE COMPRA OMP.0012356 REMISION #13479</cbc:Note>"), NuestroNit);
        Assert.Equal("OMP0012356", doc!.OrdenCompra);
    }

    [Fact]
    public void OrderReference_gana_sobre_la_nota()
    {
        var doc = DianUblParser.ParseXml(Factura(
            "<cbc:Note>SEGUN O.C OMP0099999</cbc:Note><cac:OrderReference><cbc:ID>OMP0012340</cbc:ID></cac:OrderReference>"), NuestroNit);
        Assert.Equal("OMP0012340", doc!.OrdenCompra);
    }

    [Fact]
    public void Sin_orden_queda_vacia()
    {
        var doc = DianUblParser.ParseXml(Factura("<cbc:Note>Sin Datos de OC</cbc:Note>"), NuestroNit);
        Assert.Equal("", doc!.OrdenCompra);
    }
}
