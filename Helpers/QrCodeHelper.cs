using QRCoder;

namespace GharsPlatform.Helpers;

public static class QrCodeHelper
{
    /// <summary>
    /// Generates a PNG byte[] QR code for the provided payload.
    /// </summary>
    public static byte[] GeneratePng(string payload, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
