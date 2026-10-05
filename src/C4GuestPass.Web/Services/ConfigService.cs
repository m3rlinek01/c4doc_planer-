using System.ComponentModel.DataAnnotations;
using C4GuestPass.C4;
using C4GuestPass.Data;
using C4GuestPass.Domain;
using Microsoft.Extensions.Options;

namespace C4GuestPass.Services;

public sealed record ZoneRequest(string Name, string? Description, Guid C4PersonFolderId, List<Guid>? AccessLevelIds);
public sealed record C4SettingsRequest(string CredentialType, Guid? CardTypeId, List<Guid>? AccessLevelIds);

/// <summary>Konfiguracja integracji z C4 i stref – wyłącznie dla administratora budynku.</summary>
public sealed class ConfigService(ISettingsStore settings, ITenancyStore tenancy, ILogger<ConfigService> log)
{
    public async Task<C4Settings> SaveC4Async(CurrentUser me, C4SettingsRequest r, CancellationToken ct)
    {
        Require(me);
        var type = r.CredentialType?.Trim();
        if (type is not ("Card" or "PIN")) throw new ValidationException("Wybierz rodzaj identyfikatora: karta albo PIN.");
        var s = new C4Settings
        {
            CredentialType = type,
            CardTypeId = type == "Card" ? r.CardTypeId : null,
            AccessLevelIds = (r.AccessLevelIds ?? new()).Where(id => id != Guid.Empty).Distinct().ToList(),
        };
        await settings.SaveC4Async(s, ct);
        log.LogInformation("C4 settings changed by {Op}: {Type}, card type {Card}, access levels {Levels}",
            me.Login, s.CredentialType, s.CardTypeId, string.Join(",", s.AccessLevelIds));
        return s;
    }

    public async Task<Zone> SaveZoneAsync(CurrentUser me, string? id, ZoneRequest r, CancellationToken ct)
    {
        Require(me);
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationException("Podaj nazwę strefy.");
        if (r.C4PersonFolderId == Guid.Empty) throw new ValidationException("Wybierz folder osób w C4, w którym będą zakładani goście.");

        var zones = (await settings.GetZonesAsync(ct)).ToList();
        if (zones.Any(z => z.Id != id && string.Equals(z.Name, r.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            throw new ValidationException("Strefa o tej nazwie już istnieje.");

        var zone = id is null
            ? new Zone { Id = Guid.NewGuid().ToString("N")[..12], Name = "" }
            : zones.FirstOrDefault(z => z.Id == id) ?? throw new KeyNotFoundException();
        zone.Name = r.Name.Trim();
        zone.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        zone.C4PersonFolderId = r.C4PersonFolderId;
        zone.AccessLevelIds = (r.AccessLevelIds ?? new()).Where(g => g != Guid.Empty).Distinct().ToList();
        if (id is null) zones.Add(zone);

        await settings.SaveZonesAsync(zones, ct);
        log.LogInformation("Zone {Id} '{Name}' saved by {Op}", zone.Id, zone.Name, me.Login);
        return zone;
    }

    public async Task DeleteZoneAsync(CurrentUser me, string id, CancellationToken ct)
    {
        Require(me);
        var zones = (await settings.GetZonesAsync(ct)).ToList();
        var zone = zones.FirstOrDefault(z => z.Id == id) ?? throw new KeyNotFoundException();
        var users = (await tenancy.ListCompaniesAsync(ct)).Where(c => c.Zones.Any(z => z.ProfileId == id)).Select(c => c.Name).ToList();
        if (users.Count > 0)
            throw new ValidationException($"Strefę mają przydzieloną firmy: {string.Join(", ", users)}. Najpierw odbierz ją tym firmom.");
        zones.Remove(zone);
        await settings.SaveZonesAsync(zones, ct);
        log.LogInformation("Zone {Id} '{Name}' deleted by {Op}", zone.Id, zone.Name, me.Login);
    }

    private static void Require(CurrentUser me)
    {
        if (!me.IsBuildingAdmin) throw new UnauthorizedAccessException();
    }
}

public sealed record C4ConnectionRequest(string? ServerUri, string? User, string? Password);

/// <summary>Hasło nigdy nie wraca do przeglądarki – tylko informacja, czy jest ustawione.</summary>
public sealed record C4ConnectionView(string Mode, string? ServerUri, string? User, bool HasPassword, bool FromApp);

/// <summary>Połączenie z serwerem C4 (adres, konto) – edytowane w przeglądarce, wyłącznie przez administratora budynku.</summary>
public sealed class C4ConnectionService(C4ConnectionProvider connection, IC4Gateway c4, IOptions<C4Options> opt, ILogger<C4ConnectionService> log)
{
    public C4ConnectionView Get(CurrentUser me)
    {
        Require(me);
        var c = connection.Current;
        return new C4ConnectionView(opt.Value.Mode.ToString(), c.ServerUri, c.User, !string.IsNullOrEmpty(c.Password), connection.FromApp);
    }

    public async Task<C4ConnectionView> SaveAsync(CurrentUser me, C4ConnectionRequest r, CancellationToken ct)
    {
        var info = Validate(me, r);
        await connection.SaveAsync(info, ct);
        log.LogInformation("C4 connection changed by {Op}: {Uri} as {User}", me.Login, info.ServerUri, info.User);
        return Get(me);
    }

    public Task<C4Health> TestAsync(CurrentUser me, C4ConnectionRequest r, CancellationToken ct) =>
        c4.TestConnectionAsync(Validate(me, r), ct);

    private C4ConnectionInfo Validate(CurrentUser me, C4ConnectionRequest r)
    {
        Require(me);
        if (string.IsNullOrWhiteSpace(r.User)) throw new ValidationException("Podaj login konta C4, którym aplikacja zakłada gości.");
        var info = connection.Merge(r.ServerUri, r.User, r.Password);
        if (string.IsNullOrEmpty(info.Password)) throw new ValidationException("Podaj hasło konta C4.");
        return info;
    }

    private static void Require(CurrentUser me)
    {
        if (!me.IsBuildingAdmin) throw new UnauthorizedAccessException();
    }
}
