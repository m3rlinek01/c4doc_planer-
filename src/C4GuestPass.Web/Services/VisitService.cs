using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using C4GuestPass.C4;
using C4GuestPass.Data;
using C4GuestPass.Domain;
using Microsoft.Extensions.Options;

namespace C4GuestPass.Services;

/// <param name="CompanyId">Wymagane tylko dla administratora budynku (zaprasza w imieniu wybranej firmy).</param>
public sealed record CreateVisitRequest(
    string FirstName, string LastName, string Email, string? Phone, string? Company, string? HostName,
    string AccessProfileId, DateTimeOffset ValidFrom, DateTimeOffset ValidTo, Guid? CompanyId = null);

/// <summary>
/// Orkiestracja: zaproszenie -> kod -> mail z QR -> (w oknie ważności) osoba+identyfikator w C4 -> usunięcie.
/// Uprawnienie w C4 istnieje TYLKO w oknie [ValidFrom - margines, ValidTo + margines] – nawet jeśli wersja SDK
/// nie obsługuje pól ważności, kod poza oknem nie otworzy drzwi, bo identyfikatora po prostu nie ma w systemie.
/// Firma może nadawać gościom wyłącznie strefy przydzielone jej przez administratora budynku.
/// </summary>
public sealed class VisitService(
    IVisitStore store,
    ITenancyStore tenancy,
    IC4Gateway c4,
    AccessCodeGenerator codes,
    IGuestMailer mailer,
    IOptions<GuestPassOptions> appOptions,
    IOptions<C4Options> c4Options,
    TimeProvider clock,
    ILogger<VisitService> log)
{
    private readonly GuestPassOptions _app = appOptions.Value;

    private IReadOnlyList<AccessProfileOption> Profiles => c4Options.Value.AccessProfiles;

    /// <summary>Strefy, które firma może nadawać gościom.</summary>
    public IEnumerable<AccessProfileOption> ZonesFor(Company company) =>
        Profiles.Where(p => company.Zones.Any(z => z.ProfileId == p.Id));

    public async Task<IReadOnlyList<Visit>> ListAsync(CurrentUser me, CancellationToken ct) =>
        await store.ListAsync(clock.GetUtcNow().AddDays(-14), me.IsBuildingAdmin ? null : me.CompanyId, ct);

    public async Task<Visit> GetAsync(CurrentUser me, Guid id, CancellationToken ct)
    {
        var v = await store.GetAsync(id, ct);
        return v is not null && me.CanSee(v.CompanyId) ? v : throw new KeyNotFoundException();
    }

    public async Task<Visit> CreateAsync(CreateVisitRequest r, CurrentUser me, CancellationToken ct)
    {
        var companyId = (me.IsBuildingAdmin ? r.CompanyId : me.CompanyId)
                        ?? throw new ValidationException("Wybierz firmę, w imieniu której zapraszasz gościa.");
        var company = await tenancy.GetCompanyAsync(companyId, ct);
        if (company is not { Active: true }) throw new ValidationException("Firma nie istnieje lub jest zablokowana.");

        Validate(r, company);
        if (company.MaxConcurrentGuests > 0 &&
            await store.CountOverlappingAsync(company.Id, r.ValidFrom, r.ValidTo, ct) >= company.MaxConcurrentGuests)
            throw new ValidationException($"Limit firmy: maks. {company.MaxConcurrentGuests} jednocześnie ważnych zaproszeń.");

        var visit = new Visit
        {
            CompanyId = company.Id,
            FirstName = r.FirstName.Trim(), LastName = r.LastName.Trim(), Email = r.Email.Trim(),
            Phone = Clean(r.Phone), Company = Clean(r.Company), HostName = Clean(r.HostName) ?? me.DisplayName,
            AccessProfileId = r.AccessProfileId, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo,
            AccessCode = await codes.NewUniqueCodeAsync(ct),
            CreatedBy = me.Login,
        };
        await store.InsertAsync(visit, ct);
        log.LogInformation("Visit {Id} for {Name} created by {Op} for company {Company}", visit.Id, visit.FullName, me.Login, company.Name);

        if (IsInActivationWindow(visit)) await ProvisionAsync(visit, ct);
        await SendMailAsync(visit, ct);
        return visit;
    }

    public async Task SendMailAsync(Visit visit, CancellationToken ct)
    {
        try
        {
            var company = await tenancy.GetCompanyAsync(visit.CompanyId, ct);
            var zone = Profiles.FirstOrDefault(p => p.Id == visit.AccessProfileId)?.Name ?? visit.AccessProfileId;
            await mailer.SendInvitationAsync(visit, company?.Name ?? "", zone, ct);
            visit.EmailSentAt = clock.GetUtcNow();
            if (visit.LastError?.StartsWith("Mail") == true) visit.LastError = null;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Mail to {Email} failed", visit.Email);
            visit.LastError = "Mail: " + ex.Message;
        }
        await store.UpdateAsync(visit, ct);
    }

    public async Task RevokeAsync(Visit visit, CancellationToken ct)
    {
        if (visit.Status is VisitStatus.Revoked or VisitStatus.Expired) return;
        await DeprovisionAsync(visit, VisitStatus.Revoked, ct);
    }

    /// <summary>Recepcja: gość przyszedł / wyszedł. Wyjście kończy wizytę i od razu usuwa kod z C4.</summary>
    public async Task CheckInAsync(Visit v, CancellationToken ct)
    {
        if (v.Status is not (VisitStatus.Scheduled or VisitStatus.Active)) throw new ValidationException("Wizyta jest zakończona.");
        v.CheckedInAt ??= clock.GetUtcNow();
        await store.UpdateAsync(v, ct);
    }

    public async Task CheckOutAsync(Visit v, CancellationToken ct)
    {
        if (v.CheckedInAt is null) throw new ValidationException("Gość nie został zarejestrowany jako obecny.");
        v.CheckedOutAt ??= clock.GetUtcNow();
        await store.UpdateAsync(v, ct);
        if (v.Status is VisitStatus.Scheduled or VisitStatus.Active) await DeprovisionAsync(v, VisitStatus.Expired, ct);
    }

    /// <summary>Jeden cykl workera: aktywuje wizyty, które weszły w okno, i usuwa te, które z niego wyszły.</summary>
    public async Task RunCycleAsync(CancellationToken ct)
    {
        foreach (var v in await store.ListByStatusAsync(VisitStatus.Scheduled, ct))
        {
            if (DeactivationDue(v)) { v.Status = VisitStatus.Expired; await store.UpdateAsync(v, ct); continue; }
            if (IsInActivationWindow(v) && v.ProvisionAttempts < _app.MaxProvisionAttempts) await ProvisionAsync(v, ct);
        }
        foreach (var v in await store.ListByStatusAsync(VisitStatus.Active, ct))
        {
            if (!DeactivationDue(v)) continue;
            try { await DeprovisionAsync(v, VisitStatus.Expired, ct); }
            catch (Exception) { /* zalogowane, ponowienie w następnym cyklu */ }
        }
    }

    internal bool IsInActivationWindow(Visit v) =>
        clock.GetUtcNow() >= v.ValidFrom.AddMinutes(-_app.ActivateMinutesBefore) && !DeactivationDue(v);

    internal bool DeactivationDue(Visit v) => clock.GetUtcNow() >= v.ValidTo.AddMinutes(_app.DeactivateMinutesAfter);

    private async Task ProvisionAsync(Visit v, CancellationToken ct)
    {
        v.ProvisionAttempts++;
        try
        {
            var company = await tenancy.GetCompanyAsync(v.CompanyId, ct) ?? throw new InvalidOperationException("Brak firmy");
            var profile = Profiles.First(p => p.Id == v.AccessProfileId);
            var folder = company.Zones.FirstOrDefault(z => z.ProfileId == profile.Id)?.C4PersonFolderId ?? profile.C4PersonFolderId;

            var reference = await c4.ProvisionGuestAsync(new C4GuestRequest(
                v.Id, v.FirstName, v.LastName, v.Company, company.Name, v.AccessCode, folder, profile.Name,
                v.ValidFrom.AddMinutes(-_app.ActivateMinutesBefore), v.ValidTo.AddMinutes(_app.DeactivateMinutesAfter)), ct);
            v.C4PersonId = reference.PersonId;
            v.C4CredentialId = reference.CredentialId;
            v.Status = VisitStatus.Active;
            v.LastError = null;
            log.LogInformation("Visit {Id} provisioned in C4 as person {Person}", v.Id, reference.PersonId);
        }
        catch (Exception ex)
        {
            v.LastError = "C4: " + ex.Message;
            log.LogError(ex, "Provisioning of visit {Id} failed (attempt {N})", v.Id, v.ProvisionAttempts);
        }
        await store.UpdateAsync(v, ct);
    }

    private async Task DeprovisionAsync(Visit v, VisitStatus finalStatus, CancellationToken ct)
    {
        if (v.C4PersonId is { } pid && v.Status == VisitStatus.Active)
        {
            try
            {
                await c4.RemoveGuestAsync(new C4GuestRef(pid, v.C4CredentialId), ct);
            }
            catch (Exception ex)
            {
                // Zostaje Active – worker ponowi. Bezpieczeństwo: nie udajemy, że dostęp został odebrany.
                v.LastError = "C4 remove: " + ex.Message;
                await store.UpdateAsync(v, ct);
                log.LogError(ex, "Removing visit {Id} from C4 failed", v.Id);
                throw;
            }
        }
        v.Status = finalStatus;
        v.LastError = null;
        await store.UpdateAsync(v, ct);
        log.LogInformation("Visit {Id} -> {Status}", v.Id, finalStatus);
    }

    private void Validate(CreateVisitRequest r, Company company)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.FirstName)) errors.Add("Imię jest wymagane.");
        if (string.IsNullOrWhiteSpace(r.LastName)) errors.Add("Nazwisko jest wymagane.");
        if (string.IsNullOrWhiteSpace(r.Email) || !MailAddress.TryCreate(r.Email.Trim(), out _)) errors.Add("Nieprawidłowy e-mail.");
        if (!ZonesFor(company).Any(p => p.Id == r.AccessProfileId)) errors.Add("Firma nie ma uprawnień do nadawania tej strefy.");
        if (r.ValidTo <= r.ValidFrom) errors.Add("Koniec wizyty musi być po początku.");
        if (r.ValidTo - r.ValidFrom > TimeSpan.FromHours(_app.MaxVisitHours)) errors.Add($"Wizyta może trwać maks. {_app.MaxVisitHours} h.");
        if (r.ValidTo <= clock.GetUtcNow()) errors.Add("Wizyta jest już w przeszłości.");
        if (errors.Count > 0) throw new ValidationException(string.Join(" ", errors));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class ProvisioningWorker(IServiceProvider sp, IOptions<GuestPassOptions> options, ILogger<ProvisioningWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.WorkerIntervalSeconds));
        do
        {
            try
            {
                using var scope = sp.CreateScope();
                await scope.ServiceProvider.GetRequiredService<VisitService>().RunCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Worker cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
