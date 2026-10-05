using C4GuestPass.Domain;

namespace C4GuestPass.C4;

/// <summary>Dane potrzebne do założenia gościa w C4.</summary>
public sealed record C4GuestRequest(
    Guid VisitId,
    string FirstName,
    string LastName,
    string? GuestCompany,
    /// <summary>Firma-najemca zapraszająca gościa (do opisu osoby w C4 i audytu).</summary>
    string HostCompany,
    string AccessCode,
    /// <summary>Folder osób w C4, na którym zdefiniowano uprawnienia strefy (domyślny profilu lub nadpisany dla firmy).</summary>
    Guid C4PersonFolderId,
    string ZoneName,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    /// <summary>Poziomy dostępu C4 nadawane gościowi (wspólne + strefy).</summary>
    IReadOnlyList<Guid> AccessLevelIds,
    /// <summary>"Card" albo "PIN".</summary>
    string CredentialType,
    /// <summary>Typ karty dla CredentialType=Card; null = pierwszy włączony w C4.</summary>
    Guid? CardTypeId);

/// <summary>Uchwyty obiektów utworzonych w C4 (do późniejszego usunięcia).</summary>
public sealed record C4GuestRef(Guid PersonId, Guid? CredentialId);

public sealed record C4Health(bool Ok, string Mode, string Message);

/// <summary>Element do wyboru w konfiguracji (folder osób ze ścieżką, poziom dostępu, typ karty).</summary>
public sealed record C4CatalogItem(Guid Id, string Name);

/// <summary>To, co administrator może wybrać w aplikacji zamiast przepisywać identyfikatory GUID z C4.</summary>
public sealed record C4Catalog(
    IReadOnlyList<C4CatalogItem> Folders, IReadOnlyList<C4CatalogItem> AccessLevels, IReadOnlyList<C4CatalogItem> CardTypes);

/// <summary>
/// Jedyny punkt styku aplikacji z Gamanet C4. Cała logika biznesowa (wizyty, QR, mail, wygaszanie)
/// jest niezależna od wersji C4 – zmienia się tylko implementacja tego interfejsu.
/// </summary>
public interface IC4Gateway
{
    /// <summary>Zakłada osobę-gościa w folderze profilu dostępu i przypisuje jej identyfikator = kod QR.</summary>
    Task<C4GuestRef> ProvisionGuestAsync(C4GuestRequest request, CancellationToken ct);

    /// <summary>Usuwa identyfikator i osobę z C4 (idempotentnie – brak obiektu to nie błąd).</summary>
    Task RemoveGuestAsync(C4GuestRef guest, CancellationToken ct);

    /// <summary>Połączenie oraz dostępność folderów stref i poziomów dostępu z konfiguracji.</summary>
    Task<C4Health> CheckAsync(IReadOnlyList<Zone> zones, IReadOnlyList<Guid> accessLevelIds, CancellationToken ct);

    Task<C4Catalog> GetCatalogAsync(CancellationToken ct);
}
