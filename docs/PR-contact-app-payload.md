# Per-app contact data (app-data) + supporting contact work

Adds a way for an app to store its own per-contact data on a contact record, in two
size-based tiers, plus the peer-sourced contact enrichment and temporal-read work that
landed on this branch.

**Scope:** 26 files changed, ~1,948 insertions / ~107 deletions. All contact suites green
on SQLite (`Odin.Hosting.Tests.V2` `ContactTests`).

---

## The model (see `docs/contact-app-payload-plan-refinement.md`)

Governing rule: **list/display reads never touch payloads; opening a single contact may.**

- **Inline / header tier** — small (≤ 200 B), stored in the contact content JSON
  (`appData[appId]`), so it rides in the contacts `QueryBatch` for free.
- **Bulk / payload tier** — larger (≤ 256 KB), stored as the `appextdata` payload, fetched
  on demand when a single contact is opened.

A value lives in exactly one tier. The server stamps the `appId` from the caller's token, so
apps never send it; each app sees/writes only its own slot. Writes are plaintext over the
shared-secret transport (server encrypts at rest).

---

## Changes

### Contact app-data — inline tier
- `ContactContent.cs` — add `appData` (`appId → opaque string`); owner/app-owned, never carried by peer sync; `Merge` preserves it across core and peer writes.
- `ContactService.cs` — `SetAppDataAsync` / `DeleteAppDataAsync`: namespaced read-modify-write with retry (concurrent core/enrichment/other-app edits absorbed, not conflicted); per-field + per-blob (200 B) size caps.
- `ContactRequests.cs` — `SetContactAppDataRequest { Content, VersionTag }`.
- `V2ContactsController.cs` — `PUT` / `DELETE /api/v2/contacts/{uniqueId}/app-data`.
- `AppRegistrationService.cs` / `IAppRegistrationService.cs` — `GetCallingAppIdAsync` (access-reg → appId; null for non-app callers).

### Contact app-data — bulk tier
- `ContactAppData.cs` — new `appextdata` payload type (`appId → opaque string`), server-encrypted, per-app merged. Explicitly **not** `ext_data` (which is peer-owned and replaced wholesale); this is carried forward untouched on peer/core writes.
- `ContactService.cs` — `SetAppExtDataAsync` / `DeleteAppExtDataAsync`: 256 KB cap, emptied payload dropped, retry on version race.
- `ContactRequests.cs` — `SetContactAppExtDataRequest`.
- `V2ContactsController.cs` — `PUT` / `DELETE /api/v2/contacts/{uniqueId}/app-ext-data`.

### Peer-sourced contact fields + ext_data (earlier on branch)
- `ContactContent.cs` / `PeerContactContent` — add shortBio, nickname, status, link, social, etc.
- `ContactExtData.cs` — peer-owned `ext_data` payload (rich bio attributes, replaced wholesale).
- `ContactEnrichmentService.cs`, `ContactMergeLog.cs`, `ContactProfileAttributes.cs`, `ContactRequestData.cs`, `CircleNetworkRequestService.cs` — enrichment wires peer profile → contact + ext_data.

### Temporal read (merged with main)
- `TableDriveMainIndex.cs`, `TableDriveMainIndexCached.cs`, `DriveQuery.cs`, `IDriveDatabaseManager.cs`, `DriveQueryServiceBase.cs`, `TemporalAccessStatus.cs`, `PeerTemporalDriveQueryService.cs` — temporal-verify returns the newest-file modified timestamp.

### Tests + API clients
- `ContactTests.cs` — contact CRUD, merge log, image, enrichment, emergency-contact flag, + 12 new app-data / app-ext-data tests (inline-vs-payload placement, per-app isolation, carry-forward across core updates, delete semantics, over-cap rejection, owner-token rejection).
- `IContactsHttpClientApiV2.cs` / `V2ContactsClient.cs` — Refit client methods for app-data + app-ext-data.
- `TemporalReadTests.cs`, `QueryBatchModifiedAfterTests.cs` — temporal-read coverage.

---

## API summary

| Action | Endpoint | Body | Cap |
|---|---|---|---|
| Set inline blob | `PUT /api/v2/contacts/{uniqueId}/app-data` | `{ content, versionTag }` | 200 B |
| Delete inline blob | `DELETE /api/v2/contacts/{uniqueId}/app-data?versionTag=` | — | — |
| Set bulk blob | `PUT /api/v2/contacts/{uniqueId}/app-ext-data` | `{ content, versionTag }` | 256 KB |
| Delete bulk blob | `DELETE /api/v2/contacts/{uniqueId}/app-ext-data?versionTag=` | — | — |

Requires an app token with `ManageContacts` + Read on the contact drive (owner/guest → `400`).
`content` is opaque (stored verbatim); the merge is namespaced and self-retrying, so unrelated
concurrent writes don't conflict (last-write-wins per app slot).

---

## Caveats (documented for app developers)
- **Not per-app isolated / not zero-knowledge:** data is encrypted under the *file* key, readable by any app with read access to the contact drive. Genuinely sensitive values should be app-encrypted before sending (server stores bytes verbatim).
- **Opaque string = double-encoded:** structured data is a JSON string inside the contact JSON / payload — serialize on write, parse on read.

## Follow-ups (not in this PR)
- Client (chat-kmp) read/write wiring for both tiers.
- PostgreSQL test pass (verified on SQLite only).
