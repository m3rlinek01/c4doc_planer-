using System.Collections.Concurrent;
using C4GuestPass.Domain;

namespace C4GuestPass.C4;

/// <summary>Symulacja C4 – do demo, testów i pracy bez licencji SDK.</summary>
public sealed class MockC4Gateway(ILogger<MockC4Gateway> log) : IC4Gateway
{
    public ConcurrentDictionary<Guid, C4GuestRequest> Persons { get; } = new();

    /// <summary>Do testów: wymusza błąd przy następnym wywołaniu.</summary>
    public bool FailNext { get; set; }

    public Task<C4GuestRef> ProvisionGuestAsync(C4GuestRequest request, CancellationToken ct)
    {
        if (FailNext) { FailNext = false; throw new InvalidOperationException("Mock: C4 unavailable"); }
        if (Persons.Values.Any(p => p.AccessCode == request.AccessCode))
            throw new InvalidOperationException("Mock: credential already assigned");
        var id = Guid.NewGuid();
        Persons[id] = request;
        log.LogInformation("[MOCK C4] Person {Name} ({Company}) created in zone {Zone} with credential {Code}",
            $"{request.FirstName} {request.LastName}", request.HostCompany, request.ZoneName, request.AccessCode);
        return Task.FromResult(new C4GuestRef(id, Guid.NewGuid()));
    }

    public Task RemoveGuestAsync(C4GuestRef guest, CancellationToken ct)
    {
        Persons.TryRemove(guest.PersonId, out _);
        log.LogInformation("[MOCK C4] Person {Id} removed", guest.PersonId);
        return Task.CompletedTask;
    }

    public Task<C4Health> CheckAsync(IReadOnlyList<Zone> zones, IReadOnlyList<Guid> accessLevelIds, CancellationToken ct) =>
        Task.FromResult(new C4Health(true, "Mock", $"Symulacja C4 – {Persons.Count} aktywnych gości"));

    /// <summary>Przykładowe drzewo C4 – foldery zgodne z domyślnymi strefami z appsettings.json.</summary>
    public Task<C4Catalog> GetCatalogAsync(CancellationToken ct) => Task.FromResult(new C4Catalog(
        [
            new(new Guid("00000000-0000-0000-0000-000000000001"), "Goście / Hol"),
            new(new Guid("00000000-0000-0000-0000-000000000002"), "Goście / Piętro 2"),
            new(new Guid("00000000-0000-0000-0000-000000000003"), "Goście / Piętro 3"),
            new(new Guid("00000000-0000-0000-0000-000000000004"), "Goście / Parking"),
        ],
        [
            new(new Guid("00000000-0000-0000-0000-0000000000a1"), "visitor"),
            new(new Guid("00000000-0000-0000-0000-0000000000a2"), "Parking"),
        ],
        [new(new Guid("00000000-0000-0000-0000-0000000000c1"), "CARD 48")]));
}
