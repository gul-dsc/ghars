using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GharsPlatform.Helpers;

/// <summary>
/// QuestPDF-based certificate PDF builder.
/// Keeps logic as a helper (not a service layer) to respect single-layer architecture.
/// </summary>
public static class CertificatePdfBuilder
{
    public sealed record CertificateRenderData(
        string CertificateNo,
        string ParticipantName,
        string ActivityTitle,
        string IssuedDateText,
        string VerifyUrl,
        byte[] QrPngBytes,
        bool IsRtl
    );

    public static byte[] Build(CertificateRenderData data)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(32);
                page.Size(PageSizes.A4.Landscape());
                page.DefaultTextStyle(x => x.FontSize(14));

                // QuestPDF "Canvas" API differs between versions and can cause compile issues.
                // We build a clean government-like double border using nested containers instead.
                page.Content()
                    .Padding(4)
                    .Border(2)
                    .BorderColor(Colors.Blue.Darken2)
                    .Padding(8)
                    .Border(1)
                    .BorderColor(Colors.Grey.Lighten2)
                    .Padding(20)
                    .Column(col =>
                {
                    col.Spacing(14);

                    col.Item().AlignCenter().Text(data.IsRtl ? "شهادة مشاركة" : "Certificate of Participation")
                        .FontSize(34).SemiBold();

                    col.Item().AlignCenter().Text(data.IsRtl ? "منصة غرس" : "Ghars Platform")
                        .FontSize(16).FontColor(Colors.Grey.Darken2);

                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    col.Item().PaddingTop(6).AlignCenter().Text(data.IsRtl ? "يشهد بأن" : "This is to certify that")
                        .FontSize(16);

                    col.Item().AlignCenter().Text(data.ParticipantName)
                        .FontSize(28).Bold();

                    col.Item().AlignCenter().Text(data.IsRtl ? "قد شارك في" : "has successfully participated in")
                        .FontSize(16);

                    col.Item().AlignCenter().Text(data.ActivityTitle)
                        .FontSize(22).SemiBold();

                    col.Item().AlignCenter().Text((data.IsRtl ? "تاريخ الإصدار: " : "Issued on: ") + data.IssuedDateText)
                        .FontSize(14);

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text((data.IsRtl ? "رقم الشهادة: " : "Certificate No: ") + data.CertificateNo).FontSize(12);
                            left.Item().Text((data.IsRtl ? "التحقق: " : "Verify: ") + data.VerifyUrl).FontSize(12).FontColor(Colors.Blue.Darken2);
                            left.Item().PaddingTop(10).Text(data.IsRtl ? "تم إصدار هذه الشهادة إلكترونياً." : "This certificate was issued electronically.").FontSize(11).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(140).AlignRight().Column(right =>
                        {
                            right.Item().Height(120).Width(120).Image(data.QrPngBytes);
                            right.Item().PaddingTop(6).AlignCenter().Text(data.IsRtl ? "امسح للتحقق" : "Scan to verify").FontSize(11).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    col.Item().AlignCenter().Text(data.IsRtl ? "منصة غرس" : "Ghars Platform")
                        .FontSize(12).FontColor(Colors.Grey.Darken2);
                });
            });
        });

        return doc.GeneratePdf();
    }
}
