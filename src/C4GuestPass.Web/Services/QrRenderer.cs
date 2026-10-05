using Microsoft.Extensions.Options;
using QRCoder;

namespace C4GuestPass.Services;

public sealed class QrRenderer(IOptions<GuestPassOptions> options)
{
    /// <summary>Treść QR = [prefiks] + kod. Czytnik dekoduje tekst i wysyła liczbę do kontrolera/C4.</summary>
    public string Payload(string accessCode) => options.Value.QrPayloadPrefix + accessCode;

    public byte[] Png(string accessCode, int pixelsPerModule = 10)
    {
        using var gen = new QRCodeGenerator();
        // ECC Q – dobrze czyta się z ekranu telefonu z pękniętą szybką / przy odblaskach
        using var data = gen.CreateQrCode(Payload(accessCode), QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
