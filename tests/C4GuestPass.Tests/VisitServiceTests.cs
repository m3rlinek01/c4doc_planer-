using System.ComponentModel.DataAnnotations;
using C4GuestPass;
using C4GuestPass.C4;
using C4GuestPass.Data;
using C4GuestPass.Domain;
using C4GuestPass.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace C4GuestPass.Tests;

public sealed class FakeMailer : IGuestMailer
{
    public List<Visit> Sent { get; } = new();
    public bool Fail { get; set; }
    public Task SendInvitationAsync(Visit visit, string hostCompany, string zoneName, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("smtp down");
        Sent.Add(visit);
        return Task.CompletedTask;
    }
}

public sealed class VisitServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
    private readonly MockC4Gateway _c4 = new(NullLogger<MockC4Gateway>.Instance);
    private readonly FakeMailer _mail = new();
    private readonly VisitService _svc;
    private readonly IVisitStore _store;
    private readonly ITenancyStore _tenancy;
    private readonly ISettingsStore _settings;
    private readonly Company _acme = new() { Name = "ACME", MaxConcurrentGuests = 3, Zones = [new CompanyZone { ProfileId = "lobby" }] };
    private readonly CurrentUser _me;

    public VisitServiceTests()
    {
        var cs = $"Data Source=t{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(cs);
        _keepAlive.Open();
        var db = new Database(cs);
        _store = new SqliteVisitStore(db);
        _tenancy = new SqliteTenancyStore(db);
        _tenancy.SaveCompanyAsync(_acme).GetAwaiter().GetResult();
        _me = new CurrentUser(Guid.NewGuid(), "op", "Operator", UserRole.Host, _acme.Id);
        var app = MsOptions.Create(new GuestPassOptions { ActivateMinutesBefore = 30, DeactivateMinutesAfter = 30 });
        var c4 = MsOptions.Create(new C4Options
        {
            AccessProfiles =
            [
                new AccessProfileOption { Id = "lobby", Name = "Hol", C4PersonFolderId = Guid.NewGuid() },
                new AccessProfileOption { Id = "server", Name = "Serwerownia", C4PersonFolderId = Guid.NewGuid() },
            ]
        });
        _svc = new VisitService(_store, _tenancy, _c4, new AccessCodeGenerator(_store, app), _mail, app, _settings = new SqliteSettingsStore(db, c4), _clock,
            NullLogger<VisitService>.Instance);
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task Company_quota_is_enforced()
    {
        for (var i = 0; i < 3; i++) await _svc.CreateAsync(Req(1), _me, default);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _svc.CreateAsync(Req(1), _me, default));
        Assert.Contains("Limit", ex.Message);
        await _svc.CreateAsync(Req(10), _me, default);   // inne okno czasowe – OK
    }

    [Fact]
    public async Task Other_company_cannot_see_visit()
    {
        var v = await _svc.CreateAsync(Req(0), _me, default);
        var other = new CurrentUser(Guid.NewGuid(), "x", "X", UserRole.CompanyAdmin, Guid.NewGuid());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetAsync(other, v.Id, default));
        Assert.Empty(await _svc.ListAsync(other, default));
        var admin = new CurrentUser(Guid.NewGuid(), "a", "A", UserRole.BuildingAdmin, null);
        Assert.Single(await _svc.ListAsync(admin, default));
    }

    [Fact]
    public async Task Finished_visits_are_purged_after_retention_period()
    {
        var v = await _svc.CreateAsync(Req(0), _me, default);
        await _svc.RevokeAsync(v, default);
        _clock.Advance(TimeSpan.FromDays(89));
        await _svc.RunCycleAsync(default);
        Assert.NotNull(await _store.GetAsync(v.Id));
        _clock.Advance(TimeSpan.FromDays(2));
        await _svc.RunCycleAsync(default);
        Assert.Null(await _store.GetAsync(v.Id));
    }

    [Fact]
    public async Task Checkout_removes_access_immediately()
    {
        var v = await _svc.CreateAsync(Req(0), _me, default);
        await _svc.CheckInAsync(v, default);
        await _svc.CheckOutAsync(v, default);
        Assert.Empty(_c4.Persons);
        var saved = (await _store.GetAsync(v.Id))!;
        Assert.Equal(VisitStatus.Expired, saved.Status);
        Assert.NotNull(saved.CheckedInAt);
        Assert.NotNull(saved.CheckedOutAt);
    }

    [Fact]
    public async Task Provisioning_uses_access_levels_and_credential_type_from_settings()
    {
        Guid common = Guid.NewGuid(), parking = Guid.NewGuid();
        await _settings.SaveC4Async(new C4Settings { CredentialType = "PIN", AccessLevelIds = [common] });
        var zones = (await _settings.GetZonesAsync()).ToList();
        zones.Single(z => z.Id == "lobby").AccessLevelIds = [parking, common];
        await _settings.SaveZonesAsync(zones);

        await _svc.CreateAsync(Req(0), _me, default);

        var sent = _c4.Persons.Values.Single();
        Assert.Equal([common, parking], sent.AccessLevelIds);      // wspólne + strefy, bez duplikatów
        Assert.Equal("PIN", sent.CredentialType);
        Assert.Equal(zones.Single(z => z.Id == "lobby").C4PersonFolderId, sent.C4PersonFolderId);
    }

    [Fact]
    public async Task Zone_configuration_is_validated_and_admin_only()
    {
        var cfg = new ConfigService(_settings, _tenancy, NullLogger<ConfigService>.Instance);
        var admin = new CurrentUser(Guid.NewGuid(), "admin", "Admin", UserRole.BuildingAdmin, null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => cfg.SaveZoneAsync(_me, null, new("X", null, Guid.NewGuid(), null), default));
        await Assert.ThrowsAsync<ValidationException>(() => cfg.SaveZoneAsync(admin, null, new("Bez folderu", null, Guid.Empty, null), default));
        await Assert.ThrowsAsync<ValidationException>(() => cfg.SaveZoneAsync(admin, null, new("hol", null, Guid.NewGuid(), null), default));
        await Assert.ThrowsAsync<ValidationException>(() => cfg.DeleteZoneAsync(admin, "lobby", default));   // przydzielona ACME
        await Assert.ThrowsAsync<ValidationException>(() => cfg.SaveC4Async(admin, new("Odcisk", null, null), default));

        var zone = await cfg.SaveZoneAsync(admin, null, new(" Parking ", "Szlaban", Guid.NewGuid(), [Guid.NewGuid()]), default);
        Assert.Equal("Parking", zone.Name);
        Assert.Contains(await _settings.GetZonesAsync(), z => z.Id == zone.Id);
        await cfg.DeleteZoneAsync(admin, zone.Id, default);
        Assert.DoesNotContain(await _settings.GetZonesAsync(), z => z.Id == zone.Id);
    }

    private CreateVisitRequest Req(double startInHours, double hours = 2) => new(
        "Jan", "Kowalski", "jan@example.com", null, "ACME", "Anna Nowak", "lobby",
        _clock.GetUtcNow().AddHours(startInHours), _clock.GetUtcNow().AddHours(startInHours + hours));

    [Fact]
    public async Task Visit_starting_now_is_provisioned_immediately_and_mailed()
    {
        var v = await _svc.CreateAsync(Req(0), _me, default);
        Assert.Equal(VisitStatus.Active, v.Status);
        Assert.NotNull(v.C4PersonId);
        Assert.Single(_c4.Persons);
        Assert.Equal(v.AccessCode, _c4.Persons.Values.Single().AccessCode);
        Assert.Single(_mail.Sent);
        Assert.NotNull(v.EmailSentAt);
    }

    [Fact]
    public async Task Future_visit_gets_qr_now_but_is_in_C4_only_inside_window()
    {
        var v = await _svc.CreateAsync(Req(24), _me, default);
        Assert.Equal(VisitStatus.Scheduled, v.Status);
        Assert.Empty(_c4.Persons);
        Assert.Single(_mail.Sent);

        _clock.Advance(TimeSpan.FromHours(23.4));           // 36 min przed – jeszcze nie
        await _svc.RunCycleAsync(default);
        Assert.Empty(_c4.Persons);

        _clock.Advance(TimeSpan.FromMinutes(10));           // 26 min przed – aktywacja
        await _svc.RunCycleAsync(default);
        Assert.Single(_c4.Persons);
        Assert.Equal(VisitStatus.Active, (await _store.GetAsync(v.Id))!.Status);

        _clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(60)); // po końcu + margines
        await _svc.RunCycleAsync(default);
        Assert.Empty(_c4.Persons);
        Assert.Equal(VisitStatus.Expired, (await _store.GetAsync(v.Id))!.Status);
    }

    [Fact]
    public async Task Stale_copy_does_not_overwrite_state_set_by_worker()
    {
        var v = await _svc.CreateAsync(Req(24), _me, default);
        var stale = (await _store.GetAsync(v.Id))!;          // np. wizyta wczytana przez żądanie API (Scheduled)

        _clock.Advance(TimeSpan.FromHours(23.6));           // worker zakłada gościa w C4
        await _svc.RunCycleAsync(default);
        Assert.Single(_c4.Persons);

        await _svc.CheckInAsync(stale, default);            // zapis ze starej kopii nie może cofnąć Active
        Assert.Equal(VisitStatus.Active, (await _store.GetAsync(v.Id))!.Status);
        await _svc.RunCycleAsync(default);
        Assert.Single(_c4.Persons);                         // brak drugiego założenia tej samej wizyty

        await _svc.RevokeAsync(stale, default);             // stara kopia nadal pozwala odebrać dostęp
        Assert.Empty(_c4.Persons);
        Assert.Equal(VisitStatus.Revoked, stale.Status);
    }

    [Fact]
    public async Task Revoke_removes_guest_from_C4()
    {
        var v = await _svc.CreateAsync(Req(0), _me, default);
        await _svc.RevokeAsync(v, default);
        Assert.Empty(_c4.Persons);
        Assert.Equal(VisitStatus.Revoked, (await _store.GetAsync(v.Id))!.Status);
    }

    [Fact]
    public async Task C4_failure_is_retried_by_worker()
    {
        _c4.FailNext = true;
        var v = await _svc.CreateAsync(Req(0), _me, default);
        Assert.Equal(VisitStatus.Scheduled, v.Status);
        Assert.StartsWith("C4:", v.LastError);

        await _svc.RunCycleAsync(default);
        var after = (await _store.GetAsync(v.Id))!;
        Assert.Equal(VisitStatus.Active, after.Status);
        Assert.Null(after.LastError);
    }

    [Fact]
    public async Task Mail_failure_keeps_visit_and_reports_error()
    {
        _mail.Fail = true;
        var v = await _svc.CreateAsync(Req(1), _me, default);
        Assert.Null(v.EmailSentAt);
        Assert.StartsWith("Mail:", v.LastError);
        Assert.NotNull(await _store.GetAsync(v.Id));
    }

    [Theory]
    [InlineData("", "Kowalski", "a@b.pl", "lobby", 1, 2)]
    [InlineData("Jan", "Kowalski", "zly-mail", "lobby", 1, 2)]
    [InlineData("Jan", "Kowalski", "a@b.pl", "nieznany", 1, 2)]
    [InlineData("Jan", "Kowalski", "a@b.pl", "server", 1, 2)]   // strefa nieprzydzielona firmie
    [InlineData("Jan", "Kowalski", "a@b.pl", "lobby", 2, 1)]
    [InlineData("Jan", "Kowalski", "a@b.pl", "lobby", 1, 200)]
    public async Task Invalid_requests_are_rejected(string fn, string ln, string em, string prof, int fromH, int toH)
    {
        var now = _clock.GetUtcNow();
        await Assert.ThrowsAsync<ValidationException>(() => _svc.CreateAsync(
            new CreateVisitRequest(fn, ln, em, null, null, null, prof, now.AddHours(fromH), now.AddHours(toH)), _me, default));
        Assert.Empty(_c4.Persons);
    }
}

public sealed class AccessCodeTests
{
    [Fact]
    public void Codes_have_requested_length_no_leading_zero_and_are_random()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => AccessCodeGenerator.Generate(12)).ToList();
        Assert.All(codes, c => { Assert.Equal(12, c.Length); Assert.NotEqual('0', c[0]); Assert.True(c.All(char.IsDigit)); });
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Qr_png_is_generated()
    {
        var png = new QrRenderer(MsOptions.Create(new GuestPassOptions())).Png("123456789012");
        Assert.True(png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }
}
