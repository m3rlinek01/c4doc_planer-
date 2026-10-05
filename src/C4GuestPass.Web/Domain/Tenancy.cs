namespace C4GuestPass.Domain;

/// <summary>
/// Role w aplikacji.
///  BuildingAdmin – administrator budynku/recepcji głównej: zarządza firmami, ich strefami i wszystkimi kontami.
///  CompanyAdmin  – administrator firmy-najemcy: zarządza kontami swojej firmy i zaprasza gości.
///  Host          – pracownik firmy: zaprasza gości w imieniu swojej firmy.
/// </summary>
public enum UserRole { BuildingAdmin, CompanyAdmin, Host }

/// <summary>Firma (najemca), która nadaje gościom uprawnienia – tylko do stref, które przydzielił jej administrator budynku.</summary>
public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Limit jednocześnie ważnych zaproszeń (0 = bez limitu).</summary>
    public int MaxConcurrentGuests { get; set; }
    public List<CompanyZone> Zones { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Strefa przydzielona firmie. Opcjonalnie własny folder osób w C4 (np. "Goście/ACME/Biuro 2p").</summary>
public sealed class CompanyZone
{
    public required string ProfileId { get; set; }
    public Guid? C4PersonFolderId { get; set; }
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Login { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    /// <summary>Firma użytkownika. Null tylko dla BuildingAdmin.</summary>
    public Guid? CompanyId { get; set; }
    public bool Active { get; set; } = true;
    public bool MustChangePassword { get; set; }
    /// <summary>Zmieniany przy zmianie hasła/roli/blokadzie – unieważnia aktywne sesje.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>Zalogowany użytkownik (z ciasteczka, zweryfikowany z bazą przy każdym żądaniu).</summary>
public sealed record CurrentUser(Guid Id, string Login, string DisplayName, UserRole Role, Guid? CompanyId)
{
    public bool IsBuildingAdmin => Role == UserRole.BuildingAdmin;
    public bool CanManageUsers => Role is UserRole.BuildingAdmin or UserRole.CompanyAdmin;
    public bool CanSee(Guid companyId) => IsBuildingAdmin || CompanyId == companyId;
}
