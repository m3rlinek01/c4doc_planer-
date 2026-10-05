namespace C4GuestPass;

public enum C4GatewayMode { Mock, SimpleClient }

public sealed class C4Options
{
    public const string Section = "C4";

    /// <summary>Mock = bez serwera C4 (demo). SimpleClient = prawdziwe połączenie przez Gamanet Simple Client SDK.</summary>
    public C4GatewayMode Mode { get; set; } = C4GatewayMode.Mock;

    /// <summary>Adres serwera C4 dla Simple Client, np. https://c4server.firma.local</summary>
    public string? ServerUri { get; set; }
    /// <summary>Techniczny operator C4 z prawem tworzenia osób w folderach gości (zasada minimalnych uprawnień).</summary>
    public string? User { get; set; }
    public string? Password { get; set; }
    /// <summary>Connector Simple Client: Http (domyślny, działa też na Linux) lub Tcp.</summary>
    public string Connector { get; set; } = "Http";

    /// <summary>Typ identyfikatora w C4, pod którym zapisujemy kod QR (zwykle "Card" – czytnik QR wysyła numer jak kartę; dla 2N "PIN").</summary>
    public string CredentialType { get; set; } = "Card";

    /// <summary>
    /// Profile dostępu widoczne w UI. Każdy profil = folder osób w drzewie C4, na którym administrator
    /// zdefiniował uprawnienia do drzwi (dziedziczone przez osoby w folderze).
    /// </summary>
    public List<AccessProfileOption> AccessProfiles { get; set; } = new();
}

public sealed class AccessProfileOption
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Id folderu (ParentId) osób w C4 – dla trybu SimpleClient.</summary>
    public Guid C4PersonFolderId { get; set; }
    public string? Description { get; set; }
}

public sealed class GuestPassOptions
{
    public const string Section = "GuestPass";

    public string SiteName { get; set; } = "Recepcja";
    public string DatabasePath { get; set; } = "data/guestpass.db";
    /// <summary>Ile minut przed początkiem wizyty uprawnienie staje się aktywne w C4.</summary>
    public int ActivateMinutesBefore { get; set; } = 30;
    /// <summary>Ile minut po końcu wizyty uprawnienie jest jeszcze aktywne.</summary>
    public int DeactivateMinutesAfter { get; set; } = 30;
    /// <summary>Maksymalna długość wizyty (bezpiecznik).</summary>
    public int MaxVisitHours { get; set; } = 72;
    /// <summary>Długość kodu (cyfry). 2N akceptuje 4–15 cyfr, czytniki Wiegand 26/34 bit – patrz ANALIZA.md.</summary>
    public int CodeDigits { get; set; } = 12;
    /// <summary>Opcjonalny prefiks w treści QR (np. dla czytników wymagających formatu). Pusty = sama liczba.</summary>
    public string QrPayloadPrefix { get; set; } = "";
    public int WorkerIntervalSeconds { get; set; } = 30;
    public int MaxProvisionAttempts { get; set; } = 10;
}

public sealed class MailOptions
{
    public const string Section = "Mail";

    /// <summary>Smtp = wysyłka; Pickup = zapis .eml do katalogu (dev/test).</summary>
    public string Mode { get; set; } = "Pickup";
    public string PickupDirectory { get; set; } = "data/mail";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "recepcja@example.com";
    public string FromName { get; set; } = "Recepcja";
}

/// <summary>Pierwsze uruchomienie: gdy w bazie nie ma kont, tworzone jest konto administratora budynku.</summary>
public sealed class BootstrapOptions
{
    public const string Section = "Bootstrap";
    public string AdminLogin { get; set; } = "admin";
    /// <summary>Puste = losowe hasło wypisane w logu przy pierwszym starcie.</summary>
    public string? AdminPassword { get; set; }
    /// <summary>Firmy demonstracyjne (tylko gdy baza jest pusta) – wygodne do testów, w produkcji wyłączyć.</summary>
    public bool SeedDemoData { get; set; }
}
