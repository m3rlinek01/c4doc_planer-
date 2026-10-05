using C4GuestPass.Domain;
using Microsoft.Data.Sqlite;
using static C4GuestPass.Data.Database;

namespace C4GuestPass.Data;

public interface IVisitStore
{
    Task InsertAsync(Visit visit, CancellationToken ct = default);
    Task UpdateAsync(Visit visit, CancellationToken ct = default);
    Task<Visit?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Wizyty od <paramref name="since"/> (plus wszystkie trwające). companyId = null -> wszystkie firmy.</summary>
    Task<IReadOnlyList<Visit>> ListAsync(DateTimeOffset since, Guid? companyId, CancellationToken ct = default);
    Task<IReadOnlyList<Visit>> ListByStatusAsync(VisitStatus status, CancellationToken ct = default);
    /// <summary>Liczba niezakończonych zaproszeń firmy nakładających się na okno [from, to).</summary>
    Task<int> CountOverlappingAsync(Guid companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    /// <summary>Czy kod jest używany przez wizytę, która nie została jeszcze zakończona (unikalność w C4).</summary>
    Task<bool> IsCodeInUseAsync(string code, CancellationToken ct = default);
    /// <summary>RODO: usuwa zakończone wizyty, których koniec był przed <paramref name="before"/>. Zwraca liczbę usuniętych.</summary>
    Task<int> PurgeFinishedAsync(DateTimeOffset before, CancellationToken ct = default);
}

public sealed class SqliteVisitStore(Database db) : IVisitStore
{
    public async Task InsertAsync(Visit v, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO visits (id,company_id,first_name,last_name,email,phone,company,host_name,access_profile_id,valid_from,valid_to,
              access_code,status,c4_person_id,c4_credential_id,provision_attempts,last_error,email_sent_at,checked_in_at,checked_out_at,created_by,created_at,updated_at)
            VALUES ($id,$cid,$fn,$ln,$em,$ph,$co,$host,$prof,$from,$to,$code,$st,$pid,$crid,$att,$err,$sent,$cin,$cout,$by,$cat,$uat)
            """;
        Bind(cmd, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Visit v, CancellationToken ct = default)
    {
        v.UpdatedAt = DateTimeOffset.UtcNow;
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE visits SET company_id=$cid,first_name=$fn,last_name=$ln,email=$em,phone=$ph,company=$co,host_name=$host,
              access_profile_id=$prof,valid_from=$from,valid_to=$to,access_code=$code,status=$st,
              c4_person_id=$pid,c4_credential_id=$crid,provision_attempts=$att,last_error=$err,
              email_sent_at=$sent,checked_in_at=$cin,checked_out_at=$cout,created_by=$by,created_at=$cat,updated_at=$uat WHERE id=$id
            """;
        Bind(cmd, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Visit?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryAsync("SELECT * FROM visits WHERE id=$id", ct, ("$id", id.ToString()))).FirstOrDefault();

    public Task<IReadOnlyList<Visit>> ListAsync(DateTimeOffset since, Guid? companyId, CancellationToken ct = default) =>
        companyId is { } cid
            ? QueryAsync("SELECT * FROM visits WHERE company_id=$cid AND (valid_to >= $since OR status IN (0,1)) ORDER BY valid_from DESC LIMIT 1000",
                         ct, ("$since", D(since)), ("$cid", cid.ToString()))
            : QueryAsync("SELECT * FROM visits WHERE valid_to >= $since OR status IN (0,1) ORDER BY valid_from DESC LIMIT 1000",
                         ct, ("$since", D(since)));

    public Task<IReadOnlyList<Visit>> ListByStatusAsync(VisitStatus status, CancellationToken ct = default) =>
        QueryAsync("SELECT * FROM visits WHERE status=$st", ct, ("$st", (int)status));

    public async Task<int> CountOverlappingAsync(Guid companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        (await QueryAsync("SELECT * FROM visits WHERE company_id=$cid AND status IN (0,1) AND valid_from < $to AND valid_to > $from",
            ct, ("$cid", companyId.ToString()), ("$from", D(from)), ("$to", D(to)))).Count;

    public async Task<bool> IsCodeInUseAsync(string code, CancellationToken ct = default) =>
        (await QueryAsync("SELECT * FROM visits WHERE access_code=$code AND status IN (0,1)", ct, ("$code", code))).Count > 0;

    public async Task<int> PurgeFinishedAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM visits WHERE status IN (2,3) AND valid_to < $before";
        cmd.Parameters.AddWithValue("$before", D(before));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<Visit>> QueryAsync(string sql, CancellationToken ct, params (string, object)[] ps)
    {
        await using var c = db.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, val) in ps) cmd.Parameters.AddWithValue(n, val);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Visit>();
        while (await r.ReadAsync(ct)) list.Add(Read(r));
        return list;
    }

    private static Visit Read(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(S(r, "id")!),
        CompanyId = Guid.Parse(S(r, "company_id")!),
        FirstName = S(r, "first_name")!, LastName = S(r, "last_name")!, Email = S(r, "email")!,
        Phone = S(r, "phone"), Company = S(r, "company"), HostName = S(r, "host_name"),
        AccessProfileId = S(r, "access_profile_id")!,
        ValidFrom = DateTimeOffset.Parse(S(r, "valid_from")!), ValidTo = DateTimeOffset.Parse(S(r, "valid_to")!),
        AccessCode = S(r, "access_code")!,
        Status = (VisitStatus)r.GetInt32(r.GetOrdinal("status")),
        C4PersonId = S(r, "c4_person_id") is { } p ? Guid.Parse(p) : null,
        C4CredentialId = S(r, "c4_credential_id") is { } cr ? Guid.Parse(cr) : null,
        ProvisionAttempts = r.GetInt32(r.GetOrdinal("provision_attempts")), LastError = S(r, "last_error"),
        EmailSentAt = S(r, "email_sent_at") is { } s ? DateTimeOffset.Parse(s) : null,
        CheckedInAt = S(r, "checked_in_at") is { } ci ? DateTimeOffset.Parse(ci) : null,
        CheckedOutAt = S(r, "checked_out_at") is { } co ? DateTimeOffset.Parse(co) : null,
        CreatedBy = S(r, "created_by")!,
        CreatedAt = DateTimeOffset.Parse(S(r, "created_at")!), UpdatedAt = DateTimeOffset.Parse(S(r, "updated_at")!),
    };

    private static void Bind(SqliteCommand cmd, Visit v)
    {
        var p = cmd.Parameters;
        p.AddWithValue("$id", v.Id.ToString()); p.AddWithValue("$cid", v.CompanyId.ToString());
        p.AddWithValue("$fn", v.FirstName); p.AddWithValue("$ln", v.LastName); p.AddWithValue("$em", v.Email);
        p.AddWithValue("$ph", N(v.Phone)); p.AddWithValue("$co", N(v.Company)); p.AddWithValue("$host", N(v.HostName));
        p.AddWithValue("$prof", v.AccessProfileId);
        p.AddWithValue("$from", D(v.ValidFrom)); p.AddWithValue("$to", D(v.ValidTo));
        p.AddWithValue("$code", v.AccessCode);
        p.AddWithValue("$st", (int)v.Status);
        p.AddWithValue("$pid", N(v.C4PersonId?.ToString())); p.AddWithValue("$crid", N(v.C4CredentialId?.ToString()));
        p.AddWithValue("$att", v.ProvisionAttempts); p.AddWithValue("$err", N(v.LastError));
        p.AddWithValue("$sent", N(v.EmailSentAt is { } s ? D(s) : null));
        p.AddWithValue("$cin", N(v.CheckedInAt is { } ci ? D(ci) : null)); p.AddWithValue("$cout", N(v.CheckedOutAt is { } co ? D(co) : null));
        p.AddWithValue("$by", v.CreatedBy); p.AddWithValue("$cat", D(v.CreatedAt)); p.AddWithValue("$uat", D(v.UpdatedAt));
    }
}
