# Task Plan: Web client for GAMANET C4 — guest onboarding with QR codes instead of cards

## Goal
Deep analysis (based on the C4 SDK docs at wiki.gamanet.com) of how to build a web-UI client that
adds users/visitors to GAMANET C4 and, instead of a physical card, sends the guest a QR code
that opens doors.

## Current Phase
Phase 5

## Phases
### Phase 1: Recon of documentation source
- **Status:** complete (wiki blocked; used search snippets + real code sample)
### Phase 2: Read C4 SDK docs (architecture, auth, API, objects: persons, cards/credentials, access rights, readers)
- **Status:** complete
### Phase 3: Find QR / credential / visitor mechanisms (QR readers, Wiegand, card number formats, visitor module)
- **Status:** complete
### Phase 4: Design solution architecture + alternatives + risks
- **Status:** complete
### Phase 5: Write deliverable (ANALIZA.md, PL) + PoC skeleton if justified, commit & push
- **Status:** complete

### Phase 6: (user req) Multi-tenant: accounts bound to companies; company grants guest permissions
- companies + users in DB, roles BuildingAdmin / CompanyAdmin / Receptionist, per-company zones + quota, visits scoped by company
- **Status:** complete
### Phase 7: (user req) Modern, non-generic UI redesign (sidebar app, drawer form, vendored font)
- **Status:** complete
### Phase 8: ANALIZA.md + README, tests, commit & push
- **Status:** complete

## Next Step
Done. Remaining: push blocked (GitHub App lacks access to repo); on-site verification of SDK VERIFY points (docs/ANALIZA.md §7).

## Decisions Made
| .NET 8 ASP.NET Core backend + static web UI | SimpleClient SDK is .NET; browser cannot host it |
| Adapter IC4Gateway with Mock + SimpleClient impl, MSBuild switch C4SdkVersion=2024|2026 | exact SDK signatures unreadable (wiki blocked), C4 2024 + 2026 support |
| QR payload = random 12-digit decimal | compatible with 2N QR readers (PIN 10-15 digits) and Wiegand-style card numbers |
| Decision | Rationale |
|----------|-----------|

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| git push 403 (Claude GitHub App no access to m3rlinek01/c4doc_planer-) | 1 | Committed locally; user must connect GitHub / install app |
| pkill -f matched own shell (exit 144) | 2 | run pkill in separate call with [C] bracket pattern |
| enum role JSON binding 400 in API test | 1 | JsonStringEnumConverter |
| wiki.gamanet.com CONNECT 403 (curl) + WebFetch EGRESS_BLOCKED | 1-2 | Host denied by env network policy. Pivot: WebSearch snippets, archive.org, GitHub SDK samples, NuGet packages |
