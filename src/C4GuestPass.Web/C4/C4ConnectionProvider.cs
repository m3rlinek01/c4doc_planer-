using System.ComponentModel.DataAnnotations;
using C4GuestPass.Data;
using C4GuestPass.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace C4GuestPass.C4;

/// <summary>Dane logowania do serwera C4 w postaci jawnej – tylko w pamięci procesu.</summary>
public sealed record C4ConnectionInfo(string? ServerUri, string? User, string? Password)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(ServerUri) && !string.IsNullOrWhiteSpace(User) && !string.IsNullOrEmpty(Password);

    public override string ToString() => $"{ServerUri} / {User}";   // nigdy nie wypisujemy hasła
}

/// <summary>
/// Bieżące połączenie z C4: zapisane w aplikacji (administrator budynku, Konfiguracja C4 → Połączenie)
/// albo – dopóki nikt go nie zapisał – z konfiguracji serwera (C4:ServerUri/User/Password, zmienne C4__*).
/// Dzięki temu na serwerze zdalnym (Linux, Docker) adres i konto C4 zmienia się w przeglądarce, bez edycji plików.
/// </summary>
public sealed class C4ConnectionProvider
{
    private readonly ISettingsStore _store;
    private readonly C4Options _seed;
    private readonly IDataProtector _protector;
    private readonly ILogger<C4ConnectionProvider> _log;
    private C4ConnectionInfo? _current;
    private bool _fromApp;

    public C4ConnectionProvider(ISettingsStore store, IOptions<C4Options> seed, IDataProtectionProvider dp, ILogger<C4ConnectionProvider> log)
    {
        _store = store;
        _seed = seed.Value;
        _protector = dp.CreateProtector("C4GuestPass.C4Connection.v1");
        _log = log;
    }

    /// <summary>Wołane z wątku SDK (synchronicznie) – pierwszy odczyt ładuje ustawienia z bazy.</summary>
    public C4ConnectionInfo Current => _current ?? LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>true = połączenie zapisane w aplikacji; false = z konfiguracji serwera.</summary>
    public bool FromApp { get { _ = Current; return _fromApp; } }

    private async Task<C4ConnectionInfo> LoadAsync(CancellationToken ct)
    {
        var saved = await _store.GetC4ConnectionAsync(ct);
        if (saved is null)
        {
            _fromApp = false;
            return _current = new C4ConnectionInfo(_seed.ServerUri, _seed.User, _seed.Password);
        }
        _fromApp = true;
        return _current = new C4ConnectionInfo(saved.ServerUri, saved.User, Unprotect(saved.ProtectedPassword));
    }

    /// <summary>Puste hasło = zostaw dotychczasowe (formularz nigdy nie dostaje hasła z serwera).</summary>
    public C4ConnectionInfo Merge(string? serverUri, string? user, string? password) =>
        new(NormalizeUri(serverUri), user?.Trim(), string.IsNullOrEmpty(password) ? Current.Password : password);

    public async Task<C4ConnectionInfo> SaveAsync(C4ConnectionInfo info, CancellationToken ct)
    {
        await _store.SaveC4ConnectionAsync(new C4ConnectionSettings
        {
            ServerUri = info.ServerUri,
            User = info.User,
            ProtectedPassword = string.IsNullOrEmpty(info.Password) ? null : _protector.Protect(info.Password),
        }, ct);
        _fromApp = true;
        return _current = info;
    }

    /// <summary>
    /// Adres serwera C4: sam host (https://c4server albo https://10.0.0.5). SDK dokleja /c4/sapi samo,
    /// więc częsty błąd – wklejony adres z /c4 na końcu – poprawiamy zamiast odrzucać.
    /// </summary>
    public static string NormalizeUri(string? raw)
    {
        var s = raw?.Trim() ?? "";
        if (s.Length == 0) throw new ValidationException("Podaj adres serwera C4, np. https://c4server.firma.local");
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http") || string.IsNullOrEmpty(u.Host))
            throw new ValidationException("Adres serwera C4 ma postać https://nazwa-serwera (albo https://adres-IP).");
        var path = u.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/c4/sapi", StringComparison.OrdinalIgnoreCase)) path = path[..^8];
        else if (path.EndsWith("/c4", StringComparison.OrdinalIgnoreCase)) path = path[..^3];
        return $"{u.Scheme}://{u.Authority}{path}";
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { return _protector.Unprotect(value); }
        catch (Exception ex)
        {
            // Klucze Data Protection zginęły (nowy serwer, skasowany katalog keys) – hasło trzeba wpisać ponownie.
            _log.LogError(ex, "Nie można odszyfrować hasła C4 zapisanego w aplikacji – wpisz je ponownie w Konfiguracji C4 → Połączenie.");
            return null;
        }
    }
}
