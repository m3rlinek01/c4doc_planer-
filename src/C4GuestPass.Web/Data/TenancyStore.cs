using System.Text.Json;
using C4GuestPass.Domain;
using Microsoft.Data.Sqlite;
using static C4GuestPass.Data.Database;

namespace C4GuestPass.Data;

public interface ITenancyStore
{
    Task<IReadOnlyList<Company>> ListCompaniesAsync(CancellationToken ct = default);
    Task<Company?> GetCompanyAsync(Guid id, CancellationToken ct = default);
    Task SaveCompanyAsync(Company company, CancellationToken ct = default);

    Task<IReadOnlyList<AppUser>> ListUsersAsync(Guid? companyId, CancellationToken ct = default);
    Task<AppUser?> GetUserAsync(Guid id, CancellationToken ct = default);
    Task<AppUser?> FindUserByLoginAsync(string login, CancellationToken ct = default);
    Task SaveUserAsync(AppUser user, CancellationToken ct = default);
    Task<int> CountUsersAsync(CancellationToken ct = default);
}

public sealed class SqliteTenancyStore(Database db) : ITenancyStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<Company>> ListCompaniesAsync(CancellationToken ct = default) =>
        Query("SELECT * FROM companies ORDER BY name", ReadCompany, ct);

    public async Task<Company?> GetCompanyAsync(Guid id, CancellationToken ct = default) =>
        (await Query("SELECT * FROM companies WHERE id=$id", ReadCompany, ct, ("$id", id.ToString()))).FirstOrDefault();

    public async Task SaveCompanyAsync(Company co, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO companies (id,name,active,max_concurrent_guests,zones_json,created_at)
            VALUES ($id,$name,$active,$max,$zones,$cat)
            ON CONFLICT(id) DO UPDATE SET name=$name, active=$active, max_concurrent_guests=$max, zones_json=$zones
            """;
        cmd.Parameters.AddWithValue("$id", co.Id.ToString());
        cmd.Parameters.AddWithValue("$name", co.Name);
        cmd.Parameters.AddWithValue("$active", co.Active);
        cmd.Parameters.AddWithValue("$max", co.MaxConcurrentGuests);
        cmd.Parameters.AddWithValue("$zones", JsonSerializer.Serialize(co.Zones, Json));
        cmd.Parameters.AddWithValue("$cat", D(co.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task<IReadOnlyList<AppUser>> ListUsersAsync(Guid? companyId, CancellationToken ct = default) =>
        companyId is { } cid
            ? Query("SELECT * FROM users WHERE company_id=$cid ORDER BY display_name", ReadUser, ct, ("$cid", cid.ToString()))
            : Query("SELECT * FROM users ORDER BY display_name", ReadUser, ct);

    public async Task<AppUser?> GetUserAsync(Guid id, CancellationToken ct = default) =>
        (await Query("SELECT * FROM users WHERE id=$id", ReadUser, ct, ("$id", id.ToString()))).FirstOrDefault();

    public async Task<AppUser?> FindUserByLoginAsync(string login, CancellationToken ct = default) =>
        (await Query("SELECT * FROM users WHERE login=$l", ReadUser, ct, ("$l", login.Trim()))).FirstOrDefault();

    public async Task<int> CountUsersAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task SaveUserAsync(AppUser u, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (id,login,display_name,email,password_hash,role,company_id,active,must_change_password,security_stamp,created_at,last_login_at)
            VALUES ($id,$login,$dn,$em,$ph,$role,$cid,$active,$mcp,$stamp,$cat,$lla)
            ON CONFLICT(id) DO UPDATE SET login=$login, display_name=$dn, email=$em, password_hash=$ph, role=$role,
              company_id=$cid, active=$active, must_change_password=$mcp, security_stamp=$stamp, last_login_at=$lla
            """;
        var p = cmd.Parameters;
        p.AddWithValue("$id", u.Id.ToString()); p.AddWithValue("$login", u.Login.Trim()); p.AddWithValue("$dn", u.DisplayName);
        p.AddWithValue("$em", N(u.Email)); p.AddWithValue("$ph", u.PasswordHash); p.AddWithValue("$role", (int)u.Role);
        p.AddWithValue("$cid", N(u.CompanyId?.ToString())); p.AddWithValue("$active", u.Active);
        p.AddWithValue("$mcp", u.MustChangePassword); p.AddWithValue("$stamp", u.SecurityStamp);
        p.AddWithValue("$cat", D(u.CreatedAt)); p.AddWithValue("$lla", N(u.LastLoginAt is { } l ? D(l) : null));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Company ReadCompany(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(S(r, "id")!), Name = S(r, "name")!,
        Active = r.GetBoolean(r.GetOrdinal("active")),
        MaxConcurrentGuests = r.GetInt32(r.GetOrdinal("max_concurrent_guests")),
        Zones = JsonSerializer.Deserialize<List<CompanyZone>>(S(r, "zones_json")!, Json) ?? new(),
        CreatedAt = DateTimeOffset.Parse(S(r, "created_at")!),
    };

    private static AppUser ReadUser(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(S(r, "id")!), Login = S(r, "login")!, DisplayName = S(r, "display_name")!,
        Email = S(r, "email"), PasswordHash = S(r, "password_hash")!,
        Role = (UserRole)r.GetInt32(r.GetOrdinal("role")),
        CompanyId = S(r, "company_id") is { } c ? Guid.Parse(c) : null,
        Active = r.GetBoolean(r.GetOrdinal("active")),
        MustChangePassword = r.GetBoolean(r.GetOrdinal("must_change_password")),
        SecurityStamp = S(r, "security_stamp")!,
        CreatedAt = DateTimeOffset.Parse(S(r, "created_at")!),
        LastLoginAt = S(r, "last_login_at") is { } l ? DateTimeOffset.Parse(l) : null,
    };

    private async Task<IReadOnlyList<T>> Query<T>(string sql, Func<SqliteDataReader, T> map, CancellationToken ct, params (string, object)[] ps)
    {
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<T>();
        while (await r.ReadAsync(ct)) list.Add(map(r));
        return list;
    }
}
