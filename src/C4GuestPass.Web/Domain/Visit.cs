namespace C4GuestPass.Domain;

/// <summary>Stan wizyty w cyklu życia: kod wysłany -> uprawnienie w C4 aktywne -> usunięte.</summary>
public enum VisitStatus
{
    /// <summary>Kod QR wysłany gościowi, osoba jeszcze nie istnieje w C4 (czeka na okno ważności).</summary>
    Scheduled,
    /// <summary>Osoba + identyfikator (kod QR) założone w C4 – drzwi się otworzą.</summary>
    Active,
    /// <summary>Okno ważności minęło, osoba/identyfikator usunięte z C4.</summary>
    Expired,
    /// <summary>Operator ręcznie cofnął dostęp.</summary>
    Revoked,
}

public sealed class Visit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Firma (najemca), która zaprosiła gościa i odpowiada za jego uprawnienia.</summary>
    public Guid CompanyId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? HostName { get; set; }
    public required string AccessProfileId { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }

    /// <summary>Wartość zakodowana w QR = numer identyfikatora w C4 (traktowany jak numer karty / PIN).</summary>
    public required string AccessCode { get; set; }

    public VisitStatus Status { get; set; } = VisitStatus.Scheduled;

    /// <summary>Id osoby w C4 (PersonHandle / SimplePersonV1.Id) – wypełnione po provisioningu.</summary>
    public Guid? C4PersonId { get; set; }
    /// <summary>Id identyfikatora w C4 (jeśli SDK go zwraca).</summary>
    public Guid? C4CredentialId { get; set; }

    /// <summary>Recepcja potwierdziła przybycie / wyjście gościa (rejestr obecności, ewakuacja).</summary>
    public DateTimeOffset? CheckedInAt { get; set; }
    public DateTimeOffset? CheckedOutAt { get; set; }

    public int ProvisionAttempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string FullName => $"{FirstName} {LastName}";
}
