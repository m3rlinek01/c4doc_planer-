# Integracja z Gamanet C4 2024 (SDK 21) – opis techniczny

Dokument dla osoby wdrażającej i utrzymującej GuestPass. Opisuje, jak aplikacja rozmawia z C4 2024,
co zostało sprawdzone na działającym serwerze i na co uważać. Instrukcja obsługi dla administratora
budynku: [INSTRUKCJA-ADMINISTRATORA.md](INSTRUKCJA-ADMINISTRATORA.md).

**Stan:** cały cykl gościa (założenie osoby → poziom dostępu → karta → usunięcie) sprawdzony na serwerze
C4 **21.0.10658** z SDK **21.0.10657.17457**. Wszystkie punkty `VERIFY` z [ANALIZA.md](ANALIZA.md) rozdz. 7.2
są rozstrzygnięte poniżej.

---

## 1. Budowanie

| | |
|---|---|
| Polecenie | `dotnet publish src/C4GuestPass.Web -c Release -p:C4SdkVersion=2024 -o <katalog>` |
| Pakiet SDK | `Gamanet.C4.SimpleClient` **21.0.10657.17457** (zawiera `SimpleClient` i `SimpleInterfaces`; osobnego pakietu `SimpleInterfaces` nie ma) |
| Źródło pakietu | lokalna instalacja C4 SDK: `C:\Program Files (x86)\Gamanet\C4 SDK` (katalog w układzie `id/wersja/*.nupkg`); inny katalog: `-p:C4SdkLocalFeed=...`, inna wersja: `-p:C4SdkPackageVersion=...` |
| Platforma | SDK jest skompilowane pod **.NET Framework 4.6.1** i działa w .NET 8 **tylko na Windows** (IIS albo usługa Windows). Obraz Docker/Linux z SDK 2024 nie zadziała. |
| Konektory | `content/Connectors/*.dll` z pakietu kopiowane są do `bin\Connectors\`. .NET 8 nie czyta `<probing privatePath>` z `app.config`, więc brakujące zestawy (np. `Gamanet.C4.ConnectorsConfiguration`) dociąga `AssemblyLoadContext.Resolving` w `SimpleClientC4Gateway`. |

Build bez `C4SdkVersion` (tryb Mock) działa jak dotąd – do demo i testów.

## 2. Połączenie

```csharp
var client = new SimpleClient(ConnectorConfiguration.RestClient, "<katalog aplikacji>\\Connectors");
client.Connect(new Uri("https://serwer-c4"), user, password, out _);   // ConnectionResult.Successful
```

* `C4:ServerUri` to adres **bez** `/c4` – SDK samo dokleja `/c4/sapi/...`. Z `/c4` serwer zwraca
  `NotSupportedServerVersion`.
* W SDK 21 jedynym konektorem jest **REST** (`ConnectorConfiguration.RestClient`); `C4:Connector` jest ignorowane.
* Wyniki `Connect`: `Successful`, `InvalidCredentials` (złe konto), `UnreachableServer`, `InvalidCertificate`,
  `NotSupportedServerVersion` (zły adres lub wersja SDK).

## 3. Model danych C4 widziany przez SDK

Całe drzewo osób przychodzi z jednego wywołania `Persons.GetAllBasic(EntityRoot.PersonSuperRoot)`:

```
PersonSuperRoot (11000000-…)
├── Root (EntityRoot.Person, 21000000-…)          ← osoby i foldery
│   ├── PIR            (Company)
│   │   └── asd        (Division)                 ← folder gości
│   └── Support        (Person)
├── RoleRoot (23000000-…)                          ← role operatorów
└── GroupRoot (22000000-…)                         ← POZIOMY DOSTĘPU
    └── visitor        (Group)
        └── <PersonRef → osoba>                    ← przypisanie osoby do poziomu dostępu
```

Kategorie (z pakietu `Gamanet.C4.SDK.Enums`, plik `Enums.cs`):

| Kategoria | GUID | Znaczenie |
|---|---|---|
| `PersonCategory.Person` | `5fefa0a9-70ee-46aa-bbbd-bed5888c445c` | osoba (gość) |
| `PersonCategory.Company / Department / Division / Center` | `47336057…`, `8289478c…`, `f9823ea2…`, `1346e412…` | foldery osób |
| `PersonCategory.Group` | `063422e9-3488-42a1-a521-48c9eaca1be3` | **poziom dostępu** (w bazie `AccessLevel`) |
| `PersonCategory.PersonRef` | `71d86917-2681-441b-aa6c-88c34129520a` | referencja osoby (przypisanie do grupy/roli) |
| `CredentialCategory.Card` / `Pin` | `f373e087…` / `bcfa4527…` | rodzaj identyfikatora |
| `CredentialStatus.Enabled` | `a8fc4638-db35-40a0-9a8b-01aadbaf8d3d` | identyfikator aktywny |

## 4. Zakładanie i usuwanie gościa

Kod: `src/C4GuestPass.Web/C4/SimpleClientC4Gateway.cs`.

### 4.1 Osoba

```csharp
var person = new SimplePerson(Guid.NewGuid(), name, folderId, PersonCategory.Person);
person.Data["Name"] = name;          // wymagane – C4 waliduje klucz Data["Name"], nie właściwość Name
client.Persons.Create(person);
```

Bez `Data["Name"]` serwer zwraca *„…does not contain required data key: 'Name'…”*.

### 4.2 Poziom dostępu

C4 **nie przypisuje poziomów dostępu do folderu** (klient C4: `AssignPersonToAccessLevel … InvalidObject`),
a `SimplePerson` nie ma pola na poziomy dostępu. Przypisanie robi się tak jak w przykładowym projekcie
Gamanetu (`C:\Program Files (x86)\Gamanet\C4\PublicApi`) – referencją `PersonRef` pod grupą:

```csharp
var assignment = new SimplePerson(Guid.NewGuid(), name, accessLevelId, PersonCategory.PersonRef);
assignment.Data["PersonId"] = person.Id;
assignment.Data["Name"] = name;
client.Persons.Create(assignment);   // serwer: rodzic jest Group → AssignPersonToAccessLevel
```

Zapis trafia do tabeli `AccessLevel_Person`. **Usunięcie osoby usuwa też jej przypisania** (sprawdzone),
więc przy wymeldowaniu nie trzeba ich zdejmować osobno.

### 4.3 Identyfikator (karta / PIN)

```csharp
var credential = new Credential(Guid.NewGuid(), CredentialCategory.Card)
{
    Code = accessCode, Name = "GuestPass <strefa>", HolderId = person.Id, Status = CredentialStatus.Enabled,
    ValidFrom = ..., ValidTo = ..., CardTypeId = cardTypeId,      // CardTypeId tylko dla kart
};
credential.Data["Name"] = credential.Name;
client.Credentials.Create(credential);
```

* `CardTypeId` musi wskazywać typ **włączony** w C4 (`Credentials.GetEnabledCardTypes()`); inaczej
  *„…invalid or disabled CardTypeId…”*. Typy kart są w C4 również przed włączeniem, ale wtedy SDK ich nie zwraca.
* Kod karty musi być **unikalny w ramach typu karty** (*„…duplicate card codes…”*).
* 12-cyfrowy kod (`GuestPass:CodeDigits = 12`) wymaga typu karty co najmniej 40-bitowego.
* `ValidFrom/ValidTo` podajemy w strefie czasowej z `client.Config.TimeZone` (u nas *Central European
  Standard Time*); C4 przechowuje je w UTC. To druga linia obrony – osoba i tak istnieje w C4 tylko w oknie wizyty.

Jeśli przypisanie poziomu dostępu albo utworzenie identyfikatora się nie uda, osoba jest od razu
usuwana (żadnych „sierot” w folderze gości).

### 4.4 Usuwanie

```csharp
client.Credentials.Delete(client.Credentials.GetByIdBasic(credentialId));
client.Persons.Delete(client.Persons.GetBasic(personId));
```

`Delete` przyjmuje encję, nie `Guid`. Encję trzeba pobrać przez `GetBasic`/`GetByIdBasic` – encje z
`GetAllBasic` mają daty w UTC i `Delete` odrzuca je błędem *„…DateTime did not have the Kind property set
correctly…”*.

## 5. Konfiguracja w aplikacji

Rodzaj identyfikatora, typ karty, poziomy dostępu dla każdego gościa i strefy (folder + dodatkowe poziomy)
ustawia administrator budynku w **Konfiguracji C4**, wybierając z list pobranych z C4
(`GET /api/c4/catalog`). Ustawienia są w bazie aplikacji (tabela `settings`). Klucze `C4:CredentialType`,
`C4:CardTypeId`, `C4:AccessLevelIds` i `C4:AccessProfiles` z `appsettings` są tylko wartościami startowymi
– obowiązują, dopóki administrator nie zapisze ustawień w aplikacji.

Do C4 przy każdym gościu trafiają: folder strefy (lub folder firmy), poziomy wspólne + poziomy strefy
(bez duplikatów), rodzaj identyfikatora i typ karty.

`GET /api/health` (wskaźnik „C4: połączono / błąd” w aplikacji) sprawdza połączenie oraz to, czy foldery
stref i poziomy dostępu z konfiguracji istnieją w C4 i są widoczne dla konta technicznego.

| Endpoint | Kto | Co |
|---|---|---|
| `GET /api/c4/catalog` | administrator budynku | foldery (ze ścieżką), poziomy dostępu, włączone typy kart |
| `GET/PUT /api/settings/c4` | administrator budynku | rodzaj identyfikatora, typ karty, wspólne poziomy dostępu |
| `GET /api/zones` | zalogowani | strefy |
| `POST /api/zones`, `PUT/DELETE /api/zones/{id}` | administrator budynku | edycja stref (usunięcie zablokowane, gdy strefę ma firma) |

## 6. Konto techniczne w C4

Konto, którym loguje się GuestPass (`C4:User` / `C4__Password`), potrzebuje prawa do:

* odczytu drzewa osób (foldery i grupy – lista w Konfiguracji C4),
* tworzenia i usuwania osób w folderach gości,
* tworzenia referencji pod poziomami dostępu dla gości (przypisanie do `visitor` itd.),
* tworzenia i usuwania identyfikatorów,
* odczytu włączonych typów kart.

Testy wykonano kontem z pełnymi uprawnieniami (`support`). Przed produkcją warto założyć osobne konto
z prawami ograniczonymi do folderów gości i poziomów dostępu dla gości – i powtórzyć test z punktu 7.

## 7. Test po wdrożeniu (ok. 5 minut)

1. *Konfiguracja C4*: wskaźnik **„C4: połączono”**, brak ramki *Do poprawy*.
2. Zaproś gościa testowego na najbliższą godzinę do strefy, której folder znasz.
3. W kliencie C4 w folderze strefy: osoba *„Nazwisko Imię [gość Firma]”*, na liście poziomu dostępu
   (np. `visitor`) ta osoba, identyfikator z typem karty z konfiguracji, status *Enabled*.
4. Przyłóż kod QR do czytnika – drzwi z poziomu dostępu powinny się otworzyć.
5. *Cofnij dostęp* – osoba, przypisanie i identyfikator znikają z C4.

## 8. Poprawki w aplikacji wykryte podczas testów na C4

* **Wyścig API ↔ worker.** Utworzenie wizyty i cykl workera mogły równolegle zakładać tego samego gościa:
  starsza kopia nadpisywała status *Active*, a w C4 zostawała osoba z kartą, której aplikacja nie usunęłaby.
  Teraz wszystkie zmiany stanu wizyty idą przez wspólną blokadę i zaczynają się od odczytu z bazy
  (`VisitService.UnderGateAsync`). Test: `Stale_copy_does_not_overwrite_state_set_by_worker`.
* **Wymagane `Data["Name"]`** dla osoby i identyfikatora (pkt 4.1).
* **Wycofanie osoby** przy błędzie poziomu dostępu / identyfikatora (pkt 4.3).

## 9. Ograniczenia i rzeczy do decyzji

* Typ karty jest jeden dla całego budynku (zależy od czytników), nie per firma.
* Wariant `C4SdkVersion=2026` nie był testowany – wymaga sprawdzenia API SDK 2026 (może różnić się od 21).
* SDK 2024 działa tylko na Windows (pkt 1).
* Aplikacja działa w **jednej instancji** (SQLite + worker); blokada z pkt 8 chroni w obrębie jednego procesu.
