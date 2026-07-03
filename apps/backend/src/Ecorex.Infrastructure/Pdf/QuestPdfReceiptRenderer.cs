using System.Globalization;
using Ecorex.Application.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Ecorex.Infrastructure.Pdf;

/// <summary>Genera el comprobante de pago en PDF con QuestPDF (licencia Community).</summary>
public sealed class QuestPdfReceiptRenderer : IReceiptPdfRenderer
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");
    private const string Brand = "#6D28D9";   // violeta ECOREX
    private const string Ink = "#1F2937";
    private const string Muted = "#6B7280";
    private const string Line = "#E5E7EB";

    public byte[] Render(ReceiptData d)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink).FontFamily("Helvetica"));

                page.Header().Element(c => Header(c, d));
                page.Content().Element(c => Content(c, d));
                page.Footer().Element(Footer);
            });
        }).GeneratePdf();
    }

    private static void Header(IContainer container, ReceiptData d)
    {
        container.PaddingBottom(18).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("ECOREX.tareas").FontSize(18).Bold().FontColor(Brand);
                col.Item().Text("Comprobante de pago").FontSize(11).FontColor(Muted);
            });
            row.ConstantItem(200).AlignRight().Column(col =>
            {
                col.Item().AlignRight().Text(d.ReceiptNumber).FontSize(11).Bold();
                col.Item().AlignRight().Text($"Emitido: {d.IssuedAt.ToLocalTime():dd MMM yyyy HH:mm}").FontSize(9).FontColor(Muted);
            });
        });
    }

    private static void Content(IContainer container, ReceiptData d)
    {
        container.PaddingTop(10).Column(col =>
        {
            col.Item().BorderBottom(1).BorderColor(Line).PaddingBottom(6)
                .Text("Datos de la agencia").FontSize(9).Bold().FontColor(Muted);
            col.Item().PaddingTop(6).Text(d.TenantName).FontSize(13).Bold();
            if (!string.IsNullOrWhiteSpace(d.LegalName))
            {
                col.Item().Text(d.LegalName).FontColor(Muted);
            }
            var idLine = string.Join("  ·  ",
                new[]
                {
                    string.IsNullOrWhiteSpace(d.TaxId) ? null : $"NIT {d.TaxId}",
                    string.IsNullOrWhiteSpace(d.Country) ? null : d.Country
                }.Where(x => x is not null));
            if (!string.IsNullOrWhiteSpace(idLine))
            {
                col.Item().Text(idLine).FontColor(Muted).FontSize(9);
            }

            col.Item().PaddingTop(22).BorderBottom(1).BorderColor(Line).PaddingBottom(6)
                .Text("Detalle del cobro").FontSize(9).Bold().FontColor(Muted);

            col.Item().PaddingTop(8).Column(rows =>
            {
                KeyValue(rows, "Concepto", $"Suscripcion {d.PlanName}");
                KeyValue(rows, "Periodo facturado", $"{d.PeriodStart.ToLocalTime():dd MMM yyyy} - {d.PeriodEnd.ToLocalTime():dd MMM yyyy}");
                KeyValue(rows, "Medio de pago", d.Provider);
                if (!string.IsNullOrWhiteSpace(d.ProviderReference))
                {
                    KeyValue(rows, "Referencia", d.ProviderReference!);
                }
                KeyValue(rows, "Estado", d.StatusLabel);
            });

            col.Item().PaddingTop(24).Background("#F5F3FF").Padding(16).Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text("Total pagado").FontSize(12).Bold();
                r.ConstantItem(220).AlignRight().AlignMiddle()
                    .Text($"$ {d.Amount.ToString("N0", Co)} {d.Currency}").FontSize(18).Bold().FontColor(Brand);
            });

            col.Item().PaddingTop(18).Text(
                "Este documento es un comprobante de pago de tu suscripcion a ECOREX.tareas. No constituye factura electronica.")
                .FontSize(8).FontColor(Muted).Italic();
        });
    }

    private static void KeyValue(ColumnDescriptor col, string key, string value)
    {
        col.Item().PaddingVertical(3).Row(r =>
        {
            r.ConstantItem(160).Text(key).FontColor(Muted);
            r.RelativeItem().Text(value);
        });
    }

    private static void Footer(IContainer container)
    {
        container.BorderTop(1).BorderColor(Line).PaddingTop(8)
            .Text("ECOREX.tareas · Plataforma SaaS para agencias de viajes")
            .FontSize(8).FontColor(Muted);
    }
}
