using System.Security.Cryptography;
using C4GuestPass.Data;
using Microsoft.Extensions.Options;

namespace C4GuestPass.Services;

/// <summary>
/// Generuje losowy numeryczny kod dostępu (CSPRNG). Liczba cyfr konfigurowalna:
/// 12 cyfr = 10^12 kombinacji – nie do zgadnięcia przy blokadzie czytnika, a mieści się w limicie 2N (4–15 cyfr).
/// Pierwsza cyfra != 0, żeby czytniki obcinające zera wiodące nie zmieniały wartości.
/// </summary>
public sealed class AccessCodeGenerator(IVisitStore store, IOptions<GuestPassOptions> options)
{
    public async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var code = Generate(options.Value.CodeDigits);
            if (!await store.IsCodeInUseAsync(code, ct)) return code;
        }
        throw new InvalidOperationException("Nie udało się wygenerować unikalnego kodu.");
    }

    public static string Generate(int digits)
    {
        if (digits is < 6 or > 18) throw new ArgumentOutOfRangeException(nameof(digits));
        Span<char> buf = stackalloc char[digits];
        buf[0] = (char)('1' + RandomNumberGenerator.GetInt32(9));
        for (var i = 1; i < digits; i++) buf[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(buf);
    }
}
