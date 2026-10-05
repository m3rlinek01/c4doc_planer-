#if C4SDK
using Gamanet.C4.SDK;
using Gamanet.C4.SimpleInterfaces;
using Microsoft.Extensions.Options;

namespace C4GuestPass.C4;

/// <summary>
/// Implementacja na Gamanet C4 Simple Client SDK (pakiety Gamanet.C4.SimpleClient + Gamanet.C4.SimpleInterfaces,
/// netstandard2.0 – działa w .NET 8 na Windows i Linux).
///
/// Elementy POTWIERDZONE w realnym kodzie (SDK 2023) i dokumentacji (2024/2026):
///   new SimpleClient(ConnectorConfiguration.HttpClient)
///   client.Connect(Uri, user, password, out connectionInfo) == ConnectionResult.Successful
///   client.Persons (repozytorium SimplePersonV1 : SimpleEntity { Id, ParentId, Name }), GetAll/Create/Update/Delete
///   EntityRoot.PersonSuperRoot, ValidationException
///
/// Miejsca oznaczone "VERIFY" trzeba potwierdzić w dokumentacji SDK dla konkretnej wersji
/// (nazwy właściwości osoby / klasa identyfikatora) – patrz ANALIZA.md, rozdział 7.
/// Cały kod zależny od wersji SDK jest w tym jednym pliku.
/// </summary>
public sealed class SimpleClientC4Gateway : IC4Gateway, IDisposable
{
    private readonly C4Options _opt;
    private readonly ILogger<SimpleClientC4Gateway> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ISimpleClientV2? _client;

    public SimpleClientC4Gateway(IOptions<C4Options> opt, ILogger<SimpleClientC4Gateway> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    private ISimpleClientV2 EnsureConnected()
    {
        if (_client is not null) return _client;

        var connector = string.Equals(_opt.Connector, "Tcp", StringComparison.OrdinalIgnoreCase)
            ? ConnectorConfiguration.TcpClient   // VERIFY: nazwa w danej wersji SDK
            : ConnectorConfiguration.HttpClient;

        var client = new SimpleClient(connector);
        var result = client.Connect(new Uri(_opt.ServerUri!), _opt.User!, _opt.Password!, out var info);
        if (result != ConnectionResult.Successful)
            throw new InvalidOperationException($"C4 connect failed: {result}");

        _log.LogInformation("Connected to C4 {Uri} ({Info})", _opt.ServerUri, info);
        _client = client;
        return client;
    }

    /// <summary>SDK jest synchroniczne – serializujemy wywołania i przy błędzie połączenia zrywamy sesję (reconnect przy następnym).</summary>
    private async Task<T> RunAsync<T>(Func<ISimpleClientV2, T> action, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                try { return action(EnsureConnected()); }
                catch (ValidationException) { throw; }       // błąd danych – sesja OK
                catch { _client = null; throw; }             // błąd komunikacji – reconnect
            }, ct);
        }
        finally { _lock.Release(); }
    }

    public Task<C4GuestRef> ProvisionGuestAsync(C4GuestRequest r, CancellationToken ct) => RunAsync(c =>
    {
        // 1) Osoba w folderze gości wybranego profilu. Uprawnienia do drzwi są zdefiniowane przez administratora
        //    na folderze i dziedziczone (model drzewa EntityRoot.PersonSuperRoot -> foldery -> osoby).
        var person = new SimplePersonV1
        {
            Id = Guid.NewGuid(),
            ParentId = r.C4PersonFolderId,
            Name = $"{r.LastName} {r.FirstName} [gość {r.HostCompany}]",
            // VERIFY: osobne pola imienia/nazwiska/firmy/ważności w SimplePersonV1 danej wersji, np.:
            // FirstName = r.FirstName, LastName = r.LastName, Company = r.GuestCompany, Note = $"Gość {r.HostCompany}",
            // ValidFrom = r.ValidFrom.UtcDateTime, ValidTo = r.ValidTo.UtcDateTime,
        };
        c.Persons.Create(person);

        // 2) Identyfikator (credential) = kod z QR. Czytnik QR przekazuje go do C4 jak numer karty / PIN.
        //    VERIFY: klasa CredentialV1 (obecna w dokumentacji SDK 2023–2025) i repozytorium, w którym się ją zapisuje.
        //    Jeśli w danej wersji identyfikatory są kolekcją osoby – dodać do person.Credentials i wywołać Persons.Update.
        var credential = new CredentialV1
        {
            Id = Guid.NewGuid(),
            ParentId = person.Id,
            Name = _opt.CredentialType,
            // VERIFY: Value/Number/Type – np. Value = r.AccessCode, Type = CredentialType.Card
        };
        c.Credentials.Create(credential);   // VERIFY: nazwa repozytorium

        return new C4GuestRef(person.Id, credential.Id);
    }, ct);

    public Task RemoveGuestAsync(C4GuestRef g, CancellationToken ct) => RunAsync(c =>
    {
        // Usunięcie osoby usuwa jej identyfikatory; jawne kasowanie identyfikatora na wypadek innego modelu kaskady.
        TryDelete(() => { if (g.CredentialId is { } cid) c.Credentials.Delete(cid); });  // VERIFY
        TryDelete(() => c.Persons.Delete(g.PersonId));                                     // VERIFY: Delete(Guid) vs Delete(entity)
        return true;
    }, ct);

    private void TryDelete(Action a)
    {
        try { a(); }
        catch (ValidationException ex) { _log.LogWarning(ex, "Delete skipped (already removed?)"); }
    }

    public async Task<C4Health> CheckAsync(CancellationToken ct)
    {
        try
        {
            var n = await RunAsync(c => c.Persons.GetAll(EntityRoot.PersonSuperRoot, Properties.None, false).Count(), ct);
            return new C4Health(true, "SimpleClient", $"Połączono z {_opt.ServerUri}, osób: {n}");
        }
        catch (Exception ex)
        {
            return new C4Health(false, "SimpleClient", ex.Message);
        }
    }

    public void Dispose() => (_client as IDisposable)?.Dispose();
}
#endif
