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
    DateTimeOffset ValidTo);

/// <summary>Uchwyty obiektów utworzonych w C4 (do późniejszego usunięcia).</summary>
public sealed record C4GuestRef(Guid PersonId, Guid? CredentialId);

public sealed record C4Health(bool Ok, string Mode, string Message);

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

    Task<C4Health> CheckAsync(CancellationToken ct);
}
