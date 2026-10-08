using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Una factura electronica DIAN parseada (campos de negocio que consume la Conciliacion).</summary>
public sealed record DianDoc(
    string Cufe, string TipoDoc, DateTimeOffset Fecha, string NitEmisor, string NombreEmisor,
    string NumFactura, decimal SubtotalBruto, decimal Descuento, decimal SubtotalNeto, decimal Iva,
    decimal TotalAntesRet, decimal Retefuente, decimal ReteIca, decimal Total, string TipoPago);

/// <summary>
/// Parser TOLERANTE de la factura electronica DIAN (UBL 2.1). Trabaja por <c>local-name</c> (ignora
/// prefijos/namespaces), soporta el <c>Invoice</c>/<c>CreditNote</c>/<c>DebitNote</c> directo Y el
/// <c>AttachedDocument</c> que lo envuelve en un CDATA. Toma el proveedor de <c>AccountingSupplierParty</c>
/// (o la parte cuyo NIT no sea el nuestro) y lee IVA/retenciones por CODIGO de esquema (01=IVA, 06=
/// retefuente, 07=ICA), no por posicion. No lanza: ante un XML raro devuelve lo que pudo.
/// </summary>
public static class DianUblParser
{
    public static DianDoc? ParseZip(string zipPath, string nuestroNit)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null) { return null; }
            using var s = entry.Open();
            using var r = new StreamReader(s);
            return ParseXml(r.ReadToEnd(), nuestroNit);
        }
        catch { return null; }
    }

    public static DianDoc? ParseXml(string xml, string nuestroNit)
    {
        XElement? root;
        try { root = XDocument.Parse(xml).Root; } catch { return null; }
        if (root is null) { return null; }
        root = Unwrap(root);

        var tipoRaiz = root.Name.LocalName; // Invoice / CreditNote / DebitNote
        var cufe = FirstText(root, "UUID") ?? "";
        var numFactura = DirectChild(root, "ID")?.Value.Trim() ?? "";
        var tipoDoc = FirstText(root, "InvoiceTypeCode") ?? FirstText(root, "CreditNoteTypeCode")
            ?? FirstText(root, "DebitNoteTypeCode") ?? (tipoRaiz == "Invoice" ? "01" : tipoRaiz);
        var fecha = ParseFecha(FirstText(root, "IssueDate"));

        // Proveedor (emisor): AccountingSupplierParty; fallback a la parte cuyo NIT != el nuestro.
        string nit = "", nombre = "";
        var sup = Descendants(root, "AccountingSupplierParty").FirstOrDefault();
        if (sup is not null)
        {
            nit = FirstText(sup, "CompanyID") ?? "";
            nombre = FirstText(sup, "RegistrationName") ?? FirstText(sup, "Name") ?? "";
        }
        if (string.IsNullOrEmpty(nit) || nit == nuestroNit)
        {
            foreach (var pts in Descendants(root, "PartyTaxScheme"))
            {
                var cid = FirstText(pts, "CompanyID");
                if (!string.IsNullOrEmpty(cid) && cid != nuestroNit)
                {
                    nit = cid;
                    nombre = FirstText(pts, "RegistrationName") ?? nombre;
                    break;
                }
            }
        }

        // Totales
        var lmt = Descendants(root, "LegalMonetaryTotal").FirstOrDefault()
                  ?? Descendants(root, "RequestedMonetaryTotal").FirstOrDefault();
        decimal lineExt = Num(DescText(lmt, "LineExtensionAmount"));
        decimal descuento = Num(DescText(lmt, "AllowanceTotalAmount"));
        decimal total = Num(DescText(lmt, "PayableAmount"));

        // Impuestos por esquema DIAN
        decimal iva = 0, retefuente = 0, reteica = 0;
        foreach (var tt in Descendants(root, "TaxTotal"))
        {
            if (SchemeId(tt) == "01") { iva += Num(FirstText(tt, "TaxAmount")); }
        }
        foreach (var wt in Descendants(root, "WithholdingTaxTotal"))
        {
            var sid = SchemeId(wt);
            if (sid == "06") { retefuente += Num(FirstText(wt, "TaxAmount")); }
            else if (sid == "07") { reteica += Num(FirstText(wt, "TaxAmount")); }
        }

        // Forma de pago: cac:PaymentMeans/cbc:ID  1=Contado 2=Credito
        string pago = "";
        foreach (var pm in Descendants(root, "PaymentMeans"))
        {
            var pid = DirectChild(pm, "ID")?.Value.Trim();
            if (!string.IsNullOrEmpty(pid)) { pago = pid == "1" ? "Contado" : pid == "2" ? "Credito" : pid; break; }
        }

        var subtotalNeto = lineExt - descuento;
        return new DianDoc(cufe, tipoDoc, fecha, nit, nombre, numFactura, lineExt, descuento, subtotalNeto,
            iva, Math.Round(subtotalNeto + iva, 2), retefuente, reteica, total, pago);
    }

    // ---- helpers (local-name) ----
    private static XElement Unwrap(XElement root)
    {
        if (root.Name.LocalName != "AttachedDocument") { return root; }
        foreach (var e in root.Descendants())
        {
            if (e.Name.LocalName == "Description" && !string.IsNullOrWhiteSpace(e.Value)
                && (e.Value.Contains("<Invoice", StringComparison.Ordinal)
                    || e.Value.Contains(":Invoice", StringComparison.Ordinal)
                    || e.Value.Contains("CreditNote", StringComparison.Ordinal)
                    || e.Value.Contains("DebitNote", StringComparison.Ordinal)))
            {
                try { return XDocument.Parse(e.Value.Trim()).Root ?? root; } catch { return root; }
            }
        }
        return root;
    }

    private static IEnumerable<XElement> Descendants(XElement? el, string localName) =>
        el is null ? Enumerable.Empty<XElement>() : el.Descendants().Where(e => e.Name.LocalName == localName);

    private static XElement? DirectChild(XElement el, string localName) =>
        el.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? FirstText(XElement? el, string localName)
    {
        if (el is null) { return null; }
        if (el.Name.LocalName == localName && !string.IsNullOrWhiteSpace(el.Value)) { return el.Value.Trim(); }
        foreach (var e in el.Descendants())
        {
            if (e.Name.LocalName == localName && !string.IsNullOrWhiteSpace(e.Value)) { return e.Value.Trim(); }
        }
        return null;
    }

    private static string? DescText(XElement? el, string localName) => FirstText(el, localName);

    private static string? SchemeId(XElement taxTotal)
    {
        foreach (var e in taxTotal.Descendants())
        {
            if (e.Name.LocalName == "ID")
            {
                var v = e.Value.Trim();
                if (v is "01" or "02" or "03" or "04" or "05" or "06" or "07" or "20" or "22" or "ZZ") { return v; }
            }
        }
        return null;
    }

    private static decimal Num(string? s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static DateTimeOffset ParseFecha(string? s)
    {
        if (!string.IsNullOrWhiteSpace(s)
            && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            // La fecha de emision es un dia (sin hora util). Npgsql/timestamptz exige offset 0 (UTC): se
            // guarda como medianoche UTC de ese dia -> el anio/mes (periodo del CCD) se conserva.
            return new DateTimeOffset(dt.Date, TimeSpan.Zero);
        }
        return DateTimeOffset.UtcNow;
    }
}
