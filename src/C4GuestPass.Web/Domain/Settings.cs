namespace C4GuestPass.Domain;

/// <summary>
/// Strefa (profil dostępu), którą firma może nadawać gościom: folder osób w C4, w którym zakładany jest gość,
/// oraz poziomy dostępu C4 nadawane mu dodatkowo do poziomów wspólnych (<see cref="C4Settings.AccessLevelIds"/>).
/// </summary>
public sealed class Zone
{
    /// <summary>Stały klucz zapisywany w wizytach – nie zmienia się przy edycji nazwy.</summary>
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid C4PersonFolderId { get; set; }
    public List<Guid> AccessLevelIds { get; set; } = new();
}

/// <summary>Ustawienia integracji z C4 edytowane przez administratora budynku w aplikacji.</summary>
public sealed class C4Settings
{
    /// <summary>"Card" – kod z QR jako numer karty; "PIN" – jako PIN.</summary>
    public string CredentialType { get; set; } = "Card";
    /// <summary>Typ karty w C4. Puste = pierwszy włączony typ karty.</summary>
    public Guid? CardTypeId { get; set; }
    /// <summary>Poziomy dostępu C4 nadawane każdemu gościowi (np. "visitor").</summary>
    public List<Guid> AccessLevelIds { get; set; } = new();

    public bool IsPin => string.Equals(CredentialType, "PIN", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Połączenie z serwerem C4 ustawione w aplikacji (Konfiguracja C4 → Połączenie). Hasło zapisane w bazie
/// zaszyfrowane kluczem ASP.NET Data Protection. Dopóki administrator go nie zapisze, obowiązują wartości C4:ServerUri/User/Password.
/// </summary>
public sealed class C4ConnectionSettings
{
    public string? ServerUri { get; set; }
    public string? User { get; set; }
    public string? ProtectedPassword { get; set; }
}
