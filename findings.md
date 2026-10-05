# Findings

## Source access
- wiki.gamanet.com, gamanet.com, web.archive.org, nuget search: BLOCKED by egress proxy. Only WebSearch snippets work.
  Findings below are reconstructed from search-engine snippets of wiki pages -> mark confidence.

## Doc structure (from search hits)
- SDK reference (Sandcastle): https://wiki.gamanet.com/sdkdoc/html/R_Project.htm — namespaces Gamanet.C4, .Attendance, .DriverFramework(.AccessControl.Biometric, .Alarms, .Communication), .Logging, .Security
- C4 SDK 2015: "3. First Steps" (/a/1622), "4. Access Control Management" (/a/1625)
- C4 SDK 2016 generation / Driver Framework 3.0 / Gamanet.C4.Commands namespace (/a/32118)
- **C4 SDK Transformers / Simple Client / Simple Client SDK 2026 / Gamanet.C4.SimpleInterfaces namespace** (/a/45734 LicenseAssignmentV1 class) <- likely the client-side API for our use case
- NuGet feed: nugets.C4portal.com (free for devs)
- c4portal.com PDFs: How_to_Start_Development_EN.pdf, Initial_Steps_for_Driver_Development_EN.pdf, C4_Quick Guide_EN.pdf; C4 Transformers product page

## Access control model (driver side, SDK 2015 snippet)
- AC management = independent block for drivers supporting person management (ACS, some alarm systems)
- C4 uploads ONLY allowed credentials to the device (reduces device memory / upload time)
- DeviceEvent.AccessGranted(IIngressPoint, IIdentityReader, PersonHandle, [IdentifierHandle]) -> audit trail
- PersonHandle = unique person id in C4; IdentifierHandle = credential (card/PIN/...) used

## Simple Client SDK (C4 Transformers) — THE client API  [confidence: high, from wiki URLs/snippets]
- "Simple Client is a new generation of SDK ... entrance gate to the information about the System C4"; Gamanet's own devs use it.
- Versions: 2023.0, 2024.0, 2025, 2026. Package = docs + Developer License + SDK support (license needed!).
- Assembly Gamanet.C4.SimpleClient.dll (v2026.0.0.733), namespace Gamanet.C4.SDK, class SimpleClient:
  - Connect(Uri, String user, String pwd, ConnectionInfoV1)
  - Connect(Uri, String user, SecureString pwd, ConnectionInfoV1)
  - Connect(Uri, Boolean, Func<Uri, IDictionary<string,object>, Task<SecureString>>, ConnectionInfoV1)  (callback/token style auth)
  - SimpleClient.Profiles property (/a/45066)
- Assembly Gamanet.C4.SimpleInterfaces (v2025.0.0.245): namespace Gamanet.C4.SimpleInterfaces + sub-ns .Errors, .Plugins, .Repositories, .Scheduler
  - classes: SimplePersonV1, EntityDefinitionV1/V2, EntityRoot, LicenseAssignmentV1/Category/Type, LicensedMacAddressesV3,
    WorkflowCombinedConditionV1/V2, WorkflowConditionNodeV1/V2, WorkflowSimpleConditionV1/V2
  - Repositories: ISimpleClientPersonRepositoryV3 (person CRUD); PersonHandle = person id / C4 user handle
- It's .NET -> our web backend must be .NET (ASP.NET Core) hosting SimpleClient; browser never talks to C4 directly.

## QR / visitors in C4 ecosystem [confidence: high, marketing pages]
- C4 supports "mobile & QR codes"; visitor: "put an assigned QR code or an access card to the reader".
- Module **Smart Reception** (C4 Smart Office): visitor gets QR, rights assigned dynamically to selected access points,
  valid only for the visit window (before/after meeting). => QR-as-credential is natively supported by C4 ACS.
- Integrations: **2N** intercoms/readers (2N integration hub: gamanet_c4) — 2N devices have QR-capable cameras;
  **Sharry** (sharry.tech/integrations/c4) — mobile credentials/visitor mgmt integrated with C4.
- Key insight: QR code = just another identifier (card number). Reader with QR scanner outputs code via Wiegand/OSDP/
  or IP API as if it was a card number -> C4 sees "card". So: create person + identifier(value = QR payload number) + access rights + validity.

## 2N QR credential format [confidence: high, 2N docs]
- 2N Access Unit QR / IP Style: QR = static code with 4-15 digits (decimal or hex); device treats it as PIN (10-15 digits).
- 2N integration with C4: 2N fw >= 2.33, C4 >= 2019; card learning on any reader; readers on C4 maps.
- => our QR payload: random 12-15 digit decimal number (CSPRNG). Stored in C4 as credential of the person.

## Sharry: commercial C4 integration (C4 2019 SP1 .. 2022 SP1) with QR guest passes, wallet passes — proves the concept; alternative "buy" option.
## CredentialV1 class exists in Simple Client SDK 2023.0/2024.0/2025 docs. ISimpleClientPersonRepositoryV3 exists (2025+ at least).

## USER REQUIREMENT (mid-task): must be compatible with Gamanet C4 2024 too.
- Simple Client SDK 2024.0 exists -> build with SDK matching server version; abstract via adapter; version switch.

## REAL SimpleClient usage (public repo github.com/lava-dev/TreeList, SDK 2023.0.0.692-beta) [confidence: very high — real code]
- NuGet packages (Gamanet feed nugets.c4portal.com): Gamanet.C4.SimpleClient, Gamanet.C4.SimpleInterfaces, Gamanet.Common(.DataProtection)
  - target **netstandard2.0** -> usable from .NET 8 / ASP.NET Core, also on Linux (Http connector).
  - SimpleClient package ships connectors: DataCacheConnector, PipeClientConnector, SapiClientConnector, TcpClientConnector (+Http)
- Code:
  var sc = new SimpleClient(ConnectorConfiguration.HttpClient);          // ISimpleClient, ISimpleClientV2
  var r = sc.Connect(new Uri("https://c4server"), "user", "pwd", out var connectionInfo);
  if (r != ConnectionResult.Successful) throw ...
  sc.Persons.GetAll(EntityRoot.PersonSuperRoot, Properties.None, false)  // -> SimplePersonV1 (: SimpleEntity: Id, ParentId, Name)
  sc.Permissions.Create/Update/Delete(PermissionV1), .Filter(filterGuid, Dictionary<string,object>)
  sc.Calendars.GetAll() -> CalendarV1 (time plans)
  sc.EntityDefinitions.GetAll() -> EntityDefinitionV2
  EntityRoot.PersonSuperRoot / Person / RegionSuperRoot / DeviceSuperRoot ; EntityType.Persons/Devices/Regions/...
  errors: ValidationException
- Pattern: repositories (Persons, Permissions, Calendars, ...) each with GetAll/Create/Update/Delete.
- 2026 docs: Connect(Uri, string, string, ConnectionInfoV1) -> same signature family (out param).
