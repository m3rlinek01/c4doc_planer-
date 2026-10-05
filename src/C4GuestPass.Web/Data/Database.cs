using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace C4GuestPass.Data;

/// <summary>Lokalna baza aplikacji (SQLite). C4 pozostaje źródłem prawdy o dostępie; tu: firmy, konta, wizyty.</summary>
public sealed class Database
{
    private readonly string _cs;

    public Database(IOptions<GuestPassOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _cs = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        EnsureSchema();
    }

    internal Database(string connectionString)
    {
        _cs = connectionString;
        EnsureSchema();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private void EnsureSchema()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS companies (
                id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE COLLATE NOCASE, active INTEGER NOT NULL,
                max_concurrent_guests INTEGER NOT NULL, zones_json TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY, login TEXT NOT NULL UNIQUE COLLATE NOCASE, display_name TEXT NOT NULL,
                email TEXT, password_hash TEXT NOT NULL, role INTEGER NOT NULL,
                company_id TEXT REFERENCES companies(id), active INTEGER NOT NULL, must_change_password INTEGER NOT NULL,
                security_stamp TEXT NOT NULL, created_at TEXT NOT NULL, last_login_at TEXT);
            CREATE TABLE IF NOT EXISTS visits (
                id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
                first_name TEXT NOT NULL, last_name TEXT NOT NULL, email TEXT NOT NULL,
                phone TEXT, company TEXT, host_name TEXT,
                access_profile_id TEXT NOT NULL,
                valid_from TEXT NOT NULL, valid_to TEXT NOT NULL,
                access_code TEXT NOT NULL,
                status INTEGER NOT NULL,
                c4_person_id TEXT, c4_credential_id TEXT,
                provision_attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT, email_sent_at TEXT, checked_in_at TEXT, checked_out_at TEXT,
                created_by TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_visits_status ON visits(status);
            CREATE INDEX IF NOT EXISTS ix_visits_code ON visits(access_code);
            CREATE INDEX IF NOT EXISTS ix_visits_company ON visits(company_id, valid_from);
            """;
        cmd.ExecuteNonQuery();
    }

    internal static string D(DateTimeOffset d) => d.UtcDateTime.ToString("O");
    internal static object N(object? o) => o ?? DBNull.Value;
    internal static string? S(SqliteDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }
}
