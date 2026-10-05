#if C4SDK
using System.Runtime.Loader;
using C4GuestPass.Domain;
using Gamanet.C4.SDK;
using Gamanet.C4.SimpleInterfaces;

namespace C4GuestPass.C4;

/// <summary>
/// Implementacja na Gamanet C4 Simple Client SDK 21.x (C4 2024) – pakiet Gamanet.C4.SimpleClient
/// (SimpleClient + SimpleInterfaces, net461 bez kodu natywnego – działa w .NET 8 na Windows i na Linuksie;
/// konektor RestClient łączy się z serwerem C4 przez HTTPS). API sprawdzone na bibliotekach SDK
/// 21.0.10657 i połączeniem z serwerem C4 21.0:
///   new SimpleClient(ConnectorConfiguration.RestClient, katalogKonektorów)
///   client.Connect(Uri serwera (bez /c4 – SDK dokleja ścieżkę sam), user, password, out token)
///   client.Persons / client.Credentials – Create/Update/Delete(encja), Get/GetBasic/GetById
///   new SimplePerson(id, name, parentId, categoryId), new Credential(id, categoryId) { Code, HolderId, ... }
/// Cały kod zależny od wersji SDK jest w tym jednym pliku.
/// </summary>
public sealed class SimpleClientC4Gateway : IC4Gateway, IDisposable
{
    // Identyfikatory z pakietu Gamanet.C4.SDK.Enums (Enums.cs: PersonCategory, CredentialCategory, CredentialStatus).
    private static readonly Guid PersonCategoryPerson = new("5fefa0a9-70ee-46aa-bbbd-bed5888c445c");
    private static readonly Guid PersonCategoryGroup = new("063422e9-3488-42a1-a521-48c9eaca1be3");
    /// <summary>Kategorie węzłów drzewa osób, w których można zakładać osoby (Company, Department, Division, Center).</summary>
    private static readonly HashSet<Guid> FolderCategories =
    [
        new("47336057-7ecb-4d0b-9e82-48be62556d95"), new("8289478c-d06d-46d4-adde-729fd7a96798"),
        new("f9823ea2-d234-444c-b32d-7d45dbb5ab03"), new("1346e412-213e-4553-807a-7ee8c0944fd5"),
    ];
    private static readonly Guid PersonCategoryPersonRef = new("71d86917-2681-441b-aa6c-88c34129520a");
    private static readonly Guid CredentialCategoryCard = new("f373e087-d762-42e8-81f8-339e77092938");
    private static readonly Guid CredentialCategoryPin = new("bcfa4527-f231-41db-a46f-9d77935d308f");
    private static readonly Guid CredentialStatusEnabled = new("a8fc4638-db35-40a0-9a8b-01aadbaf8d3d");

    /// <summary>Konektory SDK (SapiClientConnector itd.) są ładowane z bin/Connectors, nie z katalogu aplikacji.</summary>
    private static readonly string ConnectorsDirectory = Path.Combine(AppContext.BaseDirectory, "Connectors");

    static SimpleClientC4Gateway()
    {
        // SimpleClient.dll odwołuje się statycznie do Gamanet.C4.ConnectorsConfiguration z katalogu Connectors;
        // .NET 8 nie czyta <probing privatePath> z app.config, więc dociągamy brakujące zestawy sami.
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var path = Path.Combine(ConnectorsDirectory, name.Name + ".dll");
            return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
        };
    }

    /// <summary>Limit na próbę połączenia – SDK czeka na nieosiągalny serwer nawet 100 s.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private readonly C4ConnectionProvider _connection;
    private readonly ILogger<SimpleClientC4Gateway> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SimpleClient? _client;
    private C4ConnectionInfo? _connectedWith;
    private Guid? _cardTypeId;

    public SimpleClientC4Gateway(C4ConnectionProvider connection, ILogger<SimpleClientC4Gateway> log)
    {
        _connection = connection;
        _log = log;
    }

    private SimpleClient EnsureConnected()
    {
        // Administrator zmienił połączenie w aplikacji -> nowa sesja z nowymi danymi.
        var conn = _connection.Current;
        if (_client is not null && conn == _connectedWith) return _client;
        DropClient();

        _client = Connect(conn);
        _connectedWith = conn;
        _cardTypeId = null;
        _log.LogInformation("Connected to C4 {Uri} as {User}", conn.ServerUri, conn.User);
        return _client;
    }

    private static SimpleClient Connect(C4ConnectionInfo conn)
    {
        if (!conn.IsComplete)
            throw new InvalidOperationException("Nie ustawiono połączenia z C4 – podaj adres serwera, login i hasło w Konfiguracji C4 → Połączenie z C4.");

        var client = new SimpleClient(ConnectorConfiguration.RestClient, ConnectorsDirectory);
        ConnectionResult result;
        try { result = client.Connect(new Uri(conn.ServerUri!), conn.User!, conn.Password!, out _); }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Brak połączenia z serwerem C4 {conn.ServerUri}: {Root(ex).Message}", ex);
        }
        if (result != ConnectionResult.Successful)
            throw new InvalidOperationException(result switch
            {
                ConnectionResult.InvalidCredentials => $"C4 odrzucił logowanie użytkownika '{conn.User}' – sprawdź login i hasło.",
                ConnectionResult.UnreachableServer => $"Brak połączenia z serwerem C4 {conn.ServerUri} – sprawdź adres i zaporę (port HTTPS serwera C4).",
                ConnectionResult.InvalidCertificate => $"Serwer C4 {conn.ServerUri} przedstawił nieprawidłowy certyfikat HTTPS.",
                ConnectionResult.NotSupportedServerVersion => "Wersja serwera C4 nie pasuje do wersji SDK, z którą zbudowano aplikację (C4 2024 = SDK 21).",
                _ => $"Nie udało się zalogować do C4 {conn.ServerUri}: {result}",
            });
        return client;
    }

    private static Exception Root(Exception ex) => ex.InnerException is { } inner ? Root(inner) : ex;

    /// <summary>SDK jest synchroniczne – serializujemy wywołania i przy błędzie połączenia zrywamy sesję (reconnect przy następnym).</summary>
    private async Task<T> RunAsync<T>(Func<SimpleClient, T> action, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                try { return action(EnsureConnected()); }
                catch (ValidationException) { throw; }       // błąd danych – sesja OK
                catch { DropClient(); throw; }               // błąd komunikacji – reconnect
            }, ct);
        }
        finally { _lock.Release(); }
    }

    private void DropClient()
    {
        try { _client?.Disconnect(); } catch { /* sesja i tak jest nieużywalna */ }
        _client = null;
        _connectedWith = null;
    }

    /// <summary>Typ karty: wybrany w konfiguracji albo pierwszy włączony typ karty w C4.</summary>
    private Guid ResolveCardType(SimpleClient c, Guid? configured)
    {
        if (configured is { } id) return id;
        if (_cardTypeId is { } cached) return cached;

        var types = c.Credentials.GetEnabledCardTypes();
        if (types.Count == 0)
            throw new InvalidOperationException("C4: brak włączonych typów kart – włącz typ karty w C4 lub wybierz PIN w konfiguracji.");
        var first = types.First();
        if (types.Count > 1)
            _log.LogWarning("C4 ma {N} typów kart ({Types}); używam '{Name}'. Inny typ wybierz w Konfiguracji C4.",
                types.Count, string.Join(", ", types.Values), first.Value);
        _cardTypeId = first.Key;
        return first.Key;
    }

    /// <summary>Daty ważności w strefie czasowej serwera C4 (Config.TimeZone), a gdy jej brak – w czasie lokalnym.</summary>
    private static DateTime ToServerTime(SimpleClient c, DateTimeOffset t)
    {
        var tz = c.Config?.TimeZone;
        return tz is null ? t.LocalDateTime : TimeZoneInfo.ConvertTime(t, tz).DateTime;
    }

    public Task<C4GuestRef> ProvisionGuestAsync(C4GuestRequest r, CancellationToken ct) => RunAsync(c =>
    {
        // 1) Osoba w folderze gości wybranego profilu.
        var name = $"{r.LastName} {r.FirstName} [gość {r.HostCompany}]";
        var person = new SimplePerson(Guid.NewGuid(), name, r.C4PersonFolderId, PersonCategoryPerson);
        (person.Data ??= new Dictionary<string, object>())["Name"] = name;   // C4 waliduje klucz Data["Name"], nie właściwość Name
        c.Persons.Create(person);

        try
        {
            // 2) Poziomy dostępu (np. "visitor"). W SDK poziom dostępu jest grupą w drzewie osób, a przypisanie osoby
            //    to referencja PersonRef pod tą grupą (serwer wykonuje wtedy AssignPersonToAccessLevel). Usunięcie
            //    osoby usuwa też jej przypisania, więc RemoveGuestAsync nie musi ich zdejmować osobno.
            foreach (var accessLevelId in r.AccessLevelIds)
            {
                var assignment = new SimplePerson(Guid.NewGuid(), name, accessLevelId, PersonCategoryPersonRef);
                assignment.Data["PersonId"] = person.Id;
                assignment.Data["Name"] = name;
                c.Persons.Create(assignment);
            }

            // 3) Identyfikator = kod z QR. Czytnik QR przekazuje go do C4 jak numer karty / PIN.
            //    Ważność od–do to druga linia obrony – osoba i tak istnieje w C4 tylko w oknie wizyty.
            var isPin = string.Equals(r.CredentialType, "PIN", StringComparison.OrdinalIgnoreCase);
            var credential = new Credential(Guid.NewGuid(), isPin ? CredentialCategoryPin : CredentialCategoryCard)
            {
                Code = r.AccessCode,
                Name = $"GuestPass {r.ZoneName}",
                HolderId = person.Id,
                Status = CredentialStatusEnabled,
                ValidFrom = ToServerTime(c, r.ValidFrom),
                ValidTo = ToServerTime(c, r.ValidTo),
            };
            if (!isPin) credential.CardTypeId = ResolveCardType(c, r.CardTypeId);
            (credential.Data ??= new Dictionary<string, object>())["Name"] = credential.Name;
            c.Credentials.Create(credential);
            return new C4GuestRef(person.Id, credential.Id);
        }
        catch
        {
            // Bez uprawnień i identyfikatora osoba jest bezużyteczna – nie zostawiamy sierot w folderze gości.
            TryDelete(() => c.Persons.Delete(person));
            throw;
        }
    }, ct);

    public Task RemoveGuestAsync(C4GuestRef g, CancellationToken ct) => RunAsync(c =>
    {
        // Brak obiektu (już usunięty ręcznie w C4) to nie błąd – metoda jest idempotentna.
        if (g.CredentialId is { } cid)
            TryDelete(() => { if (c.Credentials.GetByIdBasic(cid) is { } cr) c.Credentials.Delete(cr); });
        TryDelete(() => { if (c.Persons.GetBasic(g.PersonId) is { } p) c.Persons.Delete(p); });
        return true;
    }, ct);

    private void TryDelete(Action a)
    {
        try { a(); }
        catch (ValidationException ex) { _log.LogWarning(ex, "Delete skipped (already removed?)"); }
    }

    private static bool FolderVisible(SimpleClient c, Guid folderId)
    {
        try { return c.Persons.GetBasic(folderId) is not null; }
        catch (ValidationException) { return false; }
    }

    private static bool AccessLevelVisible(SimpleClient c, Guid accessLevelId)
    {
        try { return c.Persons.GetBasic(accessLevelId) is { } g && g.CategoryId == PersonCategoryGroup; }
        catch (ValidationException) { return false; }
    }

    public async Task<C4Health> CheckAsync(IReadOnlyList<Zone> zones, IReadOnlyList<Guid> accessLevelIds, CancellationToken ct)
    {
        try
        {
            // Połączenie + widoczność folderów stref i poziomów dostępu z konfiguracji.
            var levels = accessLevelIds.Concat(zones.SelectMany(z => z.AccessLevelIds)).Distinct().ToList();
            var missing = await RunAsync(c => zones
                .Where(z => !FolderVisible(c, z.C4PersonFolderId)).Select(z => $"folder strefy '{z.Name}'")
                .Concat(levels.Where(id => !AccessLevelVisible(c, id)).Select(id => $"poziom dostępu {id}"))
                .ToList(), ct);
            return missing.Count == 0
                ? new C4Health(true, "SimpleClient", $"Połączono z {_connectedWith?.ServerUri} jako {_connectedWith?.User}, stref: {zones.Count}, poziomów dostępu: {levels.Count}")
                : new C4Health(false, "SimpleClient", $"Połączono, ale brak w C4: {string.Join(", ", missing)}");
        }
        catch (Exception ex)
        {
            return new C4Health(false, "SimpleClient", ex.Message);
        }
    }

    public Task<C4Catalog> GetCatalogAsync(CancellationToken ct) => RunAsync(c =>
    {
        // Całe drzewo osób w jednym wywołaniu: foldery (ze ścieżką od korzenia) i poziomy dostępu (grupy pod GroupRoot).
        var all = c.Persons.GetAllBasic(EntityRoot.PersonSuperRoot);
        var byId = all.ToDictionary(p => p.Id);
        string PathOf(SimplePerson p)
        {
            var parts = new List<string>();
            for (var n = p; n is not null && n.Id != EntityRoot.Person; n = byId.GetValueOrDefault(n.ParentId))
                parts.Insert(0, n.Name);
            return string.Join(" / ", parts);
        }

        var folders = all.Where(p => FolderCategories.Contains(p.CategoryId) && !p.Archived)
            .Select(p => new C4CatalogItem(p.Id, PathOf(p)))
            .Prepend(new C4CatalogItem(EntityRoot.Person, "(główny folder osób)"))
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var levels = all.Where(p => p.CategoryId == PersonCategoryGroup)
            .Select(p => new C4CatalogItem(p.Id, p.Name))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var cards = c.Credentials.GetEnabledCardTypes()
            .Select(t => new C4CatalogItem(t.Key, t.Value))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new C4Catalog(folders, levels, cards);
    }, ct);

    public async Task<C4Health> TestConnectionAsync(C4ConnectionInfo connection, CancellationToken ct)
    {
        // Osobny klient – bieżąca sesja (i praca w tle) działa dalej na starych danych, dopóki administrator nie zapisze nowych.
        var attempt = Task.Run(() =>
        {
            var client = Connect(connection);
            try { return client.Credentials.GetEnabledCardTypes().Count; }
            finally { try { client.Disconnect(); } catch { /* tylko test */ } }
        }, ct);
        try
        {
            var cardTypes = await attempt.WaitAsync(TestTimeout, ct);
            return new C4Health(true, "SimpleClient", $"Połączenie działa: zalogowano do {connection.ServerUri} jako {connection.User} (włączone typy kart: {cardTypes}).");
        }
        catch (TimeoutException)
        {
            return new C4Health(false, "SimpleClient", $"Serwer C4 {connection.ServerUri} nie odpowiada w ciągu {TestTimeout.TotalSeconds:0} s – sprawdź adres i czy serwer GuestPass ma dostęp do portu HTTPS serwera C4 (zapora).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new C4Health(false, "SimpleClient", ex.Message);
        }
    }

    public void Dispose() => DropClient();
}
#endif
