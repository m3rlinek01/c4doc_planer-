using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using C4GuestPass.Data;
using C4GuestPass.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace C4GuestPass.Auth;

public sealed record CreateUserRequest(string Login, string DisplayName, string? Email, UserRole Role, Guid? CompanyId);
public sealed record UpdateUserRequest(string DisplayName, string? Email, UserRole Role, bool Active);
public sealed record CompanyRequest(string Name, bool Active, int MaxConcurrentGuests, List<CompanyZone> Zones);

/// <summary>Konta, role, firmy oraz reguły "kto może zarządzać kim".</summary>
public sealed class UserService(
    ITenancyStore store,
    IOptions<C4Options> c4,
    IOptions<BootstrapOptions> bootstrap,
    TimeProvider clock,
    ILogger<UserService> log)
{
    private static readonly PasswordHasher<AppUser> Hasher = new();
    public const int MinPasswordLength = 10;

    // ---------- logowanie ----------

    public async Task<AppUser?> ValidateCredentialsAsync(string login, string password, CancellationToken ct)
    {
        var u = await store.FindUserByLoginAsync(login, ct);
        if (u is null) { Hasher.HashPassword(null!, password); return null; }   // stały czas odpowiedzi
        if (Hasher.VerifyHashedPassword(u, u.PasswordHash, password) == PasswordVerificationResult.Failed) return null;
        if (!await IsAllowedToSignInAsync(u, ct)) return null;
        u.LastLoginAt = clock.GetUtcNow();
        await store.SaveUserAsync(u, ct);
        return u;
    }

    public async Task<bool> IsAllowedToSignInAsync(AppUser u, CancellationToken ct)
    {
        if (!u.Active) return false;
        if (u.Role == UserRole.BuildingAdmin) return true;
        var co = u.CompanyId is { } id ? await store.GetCompanyAsync(id, ct) : null;
        return co is { Active: true };
    }

    public static ClaimsPrincipal ToPrincipal(AppUser u, string scheme) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
        new Claim(ClaimTypes.Name, u.Login),
        new Claim("stamp", u.SecurityStamp),
    ], scheme));

    /// <summary>Weryfikacja sesji przy każdym żądaniu – blokada konta/firmy lub zmiana hasła działa natychmiast.</summary>
    public async Task<CurrentUser?> ResolveAsync(ClaimsPrincipal p, CancellationToken ct)
    {
        if (!Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var u = await store.GetUserAsync(id, ct);
        if (u is null || u.SecurityStamp != p.FindFirstValue("stamp") || !await IsAllowedToSignInAsync(u, ct)) return null;
        return new CurrentUser(u.Id, u.Login, u.DisplayName, u.Role, u.CompanyId);
    }

    public async Task ChangeOwnPasswordAsync(CurrentUser me, string current, string next, CancellationToken ct)
    {
        var u = await store.GetUserAsync(me.Id, ct) ?? throw new ValidationException("Konto nie istnieje.");
        if (Hasher.VerifyHashedPassword(u, u.PasswordHash, current) == PasswordVerificationResult.Failed)
            throw new ValidationException("Obecne hasło jest nieprawidłowe.");
        CheckPassword(next);
        u.PasswordHash = Hasher.HashPassword(u, next);
        u.MustChangePassword = false;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await store.SaveUserAsync(u, ct);
    }

    // ---------- użytkownicy ----------

    public async Task<IReadOnlyList<AppUser>> ListUsersAsync(CurrentUser me, CancellationToken ct)
    {
        if (!me.CanManageUsers) throw new UnauthorizedAccessException();
        return await store.ListUsersAsync(me.IsBuildingAdmin ? null : me.CompanyId, ct);
    }

    /// <summary>Tworzy konto i zwraca hasło tymczasowe (wymuszona zmiana przy pierwszym logowaniu).</summary>
    public async Task<(AppUser User, string TempPassword)> CreateUserAsync(CurrentUser me, CreateUserRequest r, CancellationToken ct)
    {
        if (!me.CanManageUsers) throw new UnauthorizedAccessException();
        var companyId = me.IsBuildingAdmin ? r.CompanyId : me.CompanyId;
        await CheckRoleAssignmentAsync(me, r.Role, companyId, ct);

        var login = r.Login?.Trim() ?? "";
        if (login.Length < 3 || login.Any(char.IsWhiteSpace)) throw new ValidationException("Login: min. 3 znaki, bez spacji.");
        if (string.IsNullOrWhiteSpace(r.DisplayName)) throw new ValidationException("Podaj imię i nazwisko.");
        if (await store.FindUserByLoginAsync(login, ct) is not null) throw new ValidationException("Taki login już istnieje.");

        var temp = NewTempPassword();
        var u = new AppUser
        {
            Login = login, DisplayName = r.DisplayName.Trim(), Email = r.Email?.Trim(), Role = r.Role,
            CompanyId = r.Role == UserRole.BuildingAdmin ? null : companyId,
            PasswordHash = "", MustChangePassword = true,
        };
        u.PasswordHash = Hasher.HashPassword(u, temp);
        await store.SaveUserAsync(u, ct);
        log.LogInformation("User {Login} ({Role}) created by {Me}", u.Login, u.Role, me.Login);
        return (u, temp);
    }

    public async Task<AppUser> UpdateUserAsync(CurrentUser me, Guid id, UpdateUserRequest r, CancellationToken ct)
    {
        var u = await GetManageableAsync(me, id, ct);
        await CheckRoleAssignmentAsync(me, r.Role, u.CompanyId, ct);
        if (u.Id == me.Id && (!r.Active || r.Role != u.Role)) throw new ValidationException("Nie możesz zablokować ani zmienić roli własnego konta.");
        if (string.IsNullOrWhiteSpace(r.DisplayName)) throw new ValidationException("Podaj imię i nazwisko.");
        var stampChange = u.Active != r.Active || u.Role != r.Role;
        u.DisplayName = r.DisplayName.Trim(); u.Email = r.Email?.Trim(); u.Role = r.Role; u.Active = r.Active;
        if (stampChange) u.SecurityStamp = Guid.NewGuid().ToString("N");
        await store.SaveUserAsync(u, ct);
        return u;
    }

    public async Task<string> ResetPasswordAsync(CurrentUser me, Guid id, CancellationToken ct)
    {
        var u = await GetManageableAsync(me, id, ct);
        var temp = NewTempPassword();
        u.PasswordHash = Hasher.HashPassword(u, temp);
        u.MustChangePassword = true;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        await store.SaveUserAsync(u, ct);
        log.LogInformation("Password of {Login} reset by {Me}", u.Login, me.Login);
        return temp;
    }

    private async Task<AppUser> GetManageableAsync(CurrentUser me, Guid id, CancellationToken ct)
    {
        if (!me.CanManageUsers) throw new UnauthorizedAccessException();
        var u = await store.GetUserAsync(id, ct) ?? throw new KeyNotFoundException();
        if (!me.IsBuildingAdmin && (u.CompanyId != me.CompanyId || u.Role == UserRole.BuildingAdmin)) throw new KeyNotFoundException();
        return u;
    }

    private async Task CheckRoleAssignmentAsync(CurrentUser me, UserRole role, Guid? companyId, CancellationToken ct)
    {
        if (role == UserRole.BuildingAdmin)
        {
            if (!me.IsBuildingAdmin) throw new UnauthorizedAccessException();
            return;
        }
        if (companyId is null || await store.GetCompanyAsync(companyId.Value, ct) is null)
            throw new ValidationException("Konto firmowe musi być przypisane do istniejącej firmy.");
    }

    // ---------- firmy ----------

    public async Task<IReadOnlyList<Company>> ListCompaniesAsync(CurrentUser me, CancellationToken ct)
    {
        var all = await store.ListCompaniesAsync(ct);
        return me.IsBuildingAdmin ? all : all.Where(c => c.Id == me.CompanyId).ToList();
    }

    public async Task<Company> SaveCompanyAsync(CurrentUser me, Guid? id, CompanyRequest r, CancellationToken ct)
    {
        if (!me.IsBuildingAdmin) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationException("Podaj nazwę firmy.");
        if (r.MaxConcurrentGuests < 0) throw new ValidationException("Limit nie może być ujemny.");
        var known = c4.Value.AccessProfiles.Select(p => p.Id).ToHashSet();
        var zones = (r.Zones ?? new()).Where(z => known.Contains(z.ProfileId)).DistinctBy(z => z.ProfileId).ToList();
        if (zones.Count == 0) throw new ValidationException("Przydziel firmie co najmniej jedną strefę.");

        var all = await store.ListCompaniesAsync(ct);
        if (all.Any(c => c.Id != id && string.Equals(c.Name, r.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Firma o tej nazwie już istnieje.");

        var co = id is { } existing ? all.FirstOrDefault(c => c.Id == existing) ?? throw new KeyNotFoundException()
                                    : new Company { Name = r.Name.Trim() };
        co.Name = r.Name.Trim(); co.Active = r.Active; co.MaxConcurrentGuests = r.MaxConcurrentGuests; co.Zones = zones;
        await store.SaveCompanyAsync(co, ct);
        return co;
    }

    // ---------- start ----------

    public async Task BootstrapAsync(CancellationToken ct)
    {
        if (await store.CountUsersAsync(ct) > 0) return;
        var b = bootstrap.Value;
        var password = string.IsNullOrWhiteSpace(b.AdminPassword) ? NewTempPassword() : b.AdminPassword;
        var admin = new AppUser
        {
            Login = b.AdminLogin, DisplayName = "Administrator budynku", Role = UserRole.BuildingAdmin,
            PasswordHash = "", MustChangePassword = string.IsNullOrWhiteSpace(b.AdminPassword),
        };
        admin.PasswordHash = Hasher.HashPassword(admin, password);
        await store.SaveUserAsync(admin, ct);
        if (string.IsNullOrWhiteSpace(b.AdminPassword))
            log.LogWarning("Utworzono konto administratora: login '{Login}', hasło tymczasowe: {Password}", b.AdminLogin, password);

        if (!b.SeedDemoData) return;
        var profiles = c4.Value.AccessProfiles;
        foreach (var (name, count, login) in new[] { ("ACME Logistics", 2, "acme"), ("Nordwave Software", 1, "nordwave") })
        {
            var co = new Company
            {
                Name = name, MaxConcurrentGuests = 25,
                Zones = profiles.Take(count).Select(p => new CompanyZone { ProfileId = p.Id }).ToList(),
            };
            await store.SaveCompanyAsync(co, ct);
            var u = new AppUser { Login = login, DisplayName = $"Administrator {name}", Role = UserRole.CompanyAdmin, CompanyId = co.Id, PasswordHash = "" };
            u.PasswordHash = Hasher.HashPassword(u, password);
            await store.SaveUserAsync(u, ct);
        }
        log.LogWarning("Dane demo: konta 'acme' i 'nordwave' (CompanyAdmin) z tym samym hasłem co admin.");
    }

    private static void CheckPassword(string p)
    {
        if (string.IsNullOrEmpty(p) || p.Length < MinPasswordLength)
            throw new ValidationException($"Hasło musi mieć co najmniej {MinPasswordLength} znaków.");
    }

    internal static string NewTempPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(14, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        });
    }
}
