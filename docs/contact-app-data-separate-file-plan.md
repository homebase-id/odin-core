# Plan: per-app contact data as separate files

> **Third option, alternative to [`contact-app-payload-plan-refinement.md`](./contact-app-payload-plan-refinement.md)
> and the original [`contact-app-payload-plan.md`](./contact-app-payload-plan.md).**
> Instead of storing app data *inside* the contact file (a per-app payload, or a shared `ext_data`
> overflow payload), an app stores its data in its **own file** on the ContactDrive, keyed so the
> file links back to its contact and its owning app. This keeps app data fully orthogonal to the
> contact record — no chopping/preview split, no shared overflow payload, no contention with
> enrichment or core writes.

## Context

Both prior plans hang app data off the contact file (fileType `100`):

- The **original** gives each app its own payload, keyed by a derived hash of the appId, client-side
  encrypted, read via a new dedicated endpoint.
- The **refinement** replaces that with one shared, server-encrypted `ext_data` payload holding a
  `appData[appId → json]` map plus full values of "chopped" core fields, read via the generic
  payload endpoint.

Both inherit the same structural cost: **app writes mutate the contact file**, so they bump the
contact's version tag and race with background enrichment and core updates; and both must work
around the 10 KB `AppData.Content` cap (`AppFileMetaData.MaxAppDataContentLength`) and the
`FileMetadata.MaxPayloadsCount = 25` ceiling.

App data is just *more contact data namespaced by app*. It does not need to live in the contact
record to be that. Giving each app's per-contact data its **own file** removes the contention and the
overflow machinery entirely, at the cost of one extra client query (which fits the existing
"contacts are read client-side" model) and a contact-delete cascade.

---

## Design

### One file per (app, contact)

A new file on the **ContactDrive** (apps already hold a read grant there, so reads need no new
permission):

| Field | Value | Purpose |
|---|---|---|
| `FileType` | `389` (`AppContactDataFileType`) | distinguishes app-data files from contact files (`100`) |
| `UniqueId` | `md5(appId + odinId)` | deterministic addressing — upsert via `GetFileByClientUniqueId` (exactly how `ContactService` locates a contact) |
| `GroupId` | `md5(odinId)` (= the contact's own `UniqueId`) | links the file to its contact; query "all app data for contact X" across every app |
| `Tags` | `[appId]` | query "all of this app's data" across every contact |
| `AppData.Content` | app JSON, server-encrypted under the file AES key | the app's data for this contact |

`GroupId` equals `ContactService.ToContactUniqueId(odinId)`, so the app-data file's group **is** the
contact file's unique id — the relationship is explicit and derivable from the odinId alone (no need
to know the contact's random `FileId`). All four fields above are first-class indexed query fields
(`FileQueryParams`: `FileType`, `ClientUniqueIdAtLeastOne`, `GroupId`, `TagsMatchAtLeastOne`).

> **Keying note:** `md5(appId + odinId)` — *not* appId alone. Using appId as the uniqueId would mean
> one file per app holding all contacts' data, which reintroduces the same 10 KB cap + overflow
> problem this plan exists to avoid. Per-(app, contact) keeps each file small and independent.

### Three access patterns, all indexed queries

1. **Upsert this app's data for this contact** — `ClientUniqueIdAtLeastOne = [md5(appId+odinId)]`.
2. **Contact-delete cascade** — `GroupId = [md5(odinId)]` → delete every app's data file for that
   contact in one query.
3. **App-uninstall cleanup** — `TagsMatchAtLeastOne = [appId]` → delete that app's files across every
   contact in one query.

### Encryption — server-side (consistent with the contact file)

The client sends **plaintext** JSON over the existing shared-secret transport; the server encrypts
it at rest under the file's AES key with a per-file content IV — identical to `ContactService`'s
content handling (`EncryptContent` / `DecryptContent`), and unlike the original plan's client-side
image-style crypto. The content IV rotates on every update (the same rule the contact writes
enforce).

Each app-data file gets its own random `KeyHeader.NewRandom16()` (AES key + IV), stored as
`EncryptedKeyHeader` wrapped under the **ContactDrive storage key** (`CreateServerFileHeader` /
`EncryptKeyHeader`); `IsEncrypted = true`, ACL `OwnerOnly`, `AllowDistribution = false` (local, never
sent to peers) — the same server metadata as a contact file. The `chng_log` payload is encrypted with
the same file AES key under its own per-payload IV (the `merge_log` pattern). **Decided:** lift
`EncryptContent` / `DecryptContent` out of `ContactService` into a shared helper, since
`AppContactDataService` uses the identical logic (pairs with the shared change-log helper).

On reads, the standard `QueryBatch` response carries `SharedSecretEncryptedKeyHeader` (the file key
re-encrypted to the caller's shared secret); the client decrypts content with `KeyHeader(iv, aesKey)`
— the same flow that already reads contact files (fileType 100). No app-specific read crypto.

This is **not** zero-knowledge: the owner's server holds the drive storage key and can decrypt. See
*Isolation & sensitive-field guidance* for what that means and the app-developer guidance.

### Reads — the existing client-side QueryBatch

Nothing new server-side. The app reads its data with a normal `QueryBatch` on the ContactDrive
filtered by `FileType=389` and `TagsMatchAtLeastOne=[appId]` (all its data) or
`ClientUniqueIdAtLeastOne=[md5(appId+odinId)]` (one contact) — the same way clients already read
contacts (fileType `100`) client-side. No dedicated read endpoint, no key derivation, no resolver.

### Writes — a small dedicated service + two endpoints

The appId is resolved from the **token** on writes (never from the body): the existing
`AccessRegistrationId → AppClientRegistration.AppId` lookup (mirror
`AppRegistrationService.DeleteCurrentAppClientAsync`, which already reads
`Caller.OdinClientContext.AccessRegistrationId` and the `AppClientRegistration`). This rejects
owner/guest tokens, enforcing app-only writes.

- **`PUT /api/v2/contacts/{odinId}/app-data`** — create-or-update the calling app's data file for the
  contact. Resolve `appId` from the token, derive `uniqueId = md5(appId+odinId)` and
  `groupId = md5(odinId)`. **Create** if absent (`CreateInternalFileId` + `WriteNewFileHeader`;
  `versionTag` not required for the first write). **Update** otherwise: gate on the **app-data file's
  own `versionTag`** — stale → `409` carrying the current header, then rewrite content under a fresh
  content IV via `UpdateActiveFileHeader`. This is exactly `ContactService.UpdateAsync` /
  `SetImageAsync`, scoped to the 389 file.
- **`DELETE /api/v2/contacts/{odinId}/app-data?versionTag=…`** — version-gated soft-delete of the
  calling app's data file (`SoftDeleteLongTermFile`); `404` if absent, `409` on stale tag (mirrors
  `DeleteImageAsync`).

Reads route addressed by `odinId` (not the file uniqueId) because the app knows the odinId and the
keying is deterministic — the app never has to know the derived uniqueId or the contact's FileId.

**Permission floor (decided):** the write endpoints require a valid **App token** (the resolver
rejects owner/guest) and **ContactDrive access** — the latter is enforced implicitly because
`GetWriteContext` needs the drive storage key, which only a granted caller has. They do **not**
additionally assert `ManageContacts`: a contact-*consuming* app storing its own sidecar data should
not need full contact-management rights (this is looser than `ContactService`'s core writes, which do
assert `ManageContacts`, and that's intentional — app data is the app's own namespaced file, not the
shared contact record).

### Concurrency

Writes are **optimistic-concurrency gated on the app-data file's own `versionTag`** (the fileType-389
file, *not* the contact file): the client sends the tag it last read, a stale tag yields `409` with
the current header, and the client re-reads and retries. This is the exact model
`ContactService.UpdateAsync` / `SetImageAsync` / `DeleteImageAsync` already use, scoped to the app's
file.

Because each app-data file is written only by its owning app, the gate never trips on enrichment,
core contact updates, or other apps — there is no shared mutable surface, so the only conflicts are an
app racing *itself* across two of its own devices, which is precisely what the version tag protects
against (lost-update protection rather than last-writer-wins).

There is **no field-merge** on a write: the file's `AppData.Content` *is* the app's opaque blob (the
server doesn't know its shape), so an accepted write is a **wholesale replace** of the content (under
a fresh content IV), with the prior content captured into `chng_log` (see below).

### Change history (merge_log pattern, whole-blob snapshots)

App data keeps an optional append-only change history, reusing the **storage mechanics** of the
contact `merge_log` — a separate payload (`chng_log`) on the app's own file, encrypted under the file
AES key with its own IV, written in the **same `UpdateBatchAsync` transaction** as the content, and
preserved on every other write via `CarryForwardPayloads`. On overwrite, the prior content is
appended before the new content is written; the create path logs nothing (no prior value), mirroring
`merge_log`'s first-fill no-op.

What differs from `merge_log` is the **entry content**, because the app blob is opaque to the server:

- **Entries are whole-blob snapshots**, not field-level diffs — `{ at, priorContent }`. The
  contact-specific `Flatten` / `ComputeOverwrites` (hardcoded to `ContactContent`'s fields) does
  **not** apply; the server cannot and does not parse the app JSON.
- **The cap is small** — **decided: keep the last 10 snapshots** (count-based; oldest dropped past
  the cap). Each snapshot can be up to the full ~10 KB content budget, so 10 bounds the `chng_log`
  payload at ~100 KB worst case — predictable and far below `merge_log`'s `MaxEntries = 100`.
  Count-based over byte-bound for simplicity; revisit only if a real app needs deeper history.
- **`by` is single-source** — recorded as `"app"` (or the appId), since only the owning app writes the
  file (no `Api`/`Enrichment` distinction).

**Shared-helper refactor:** extract the reusable change-log mechanics (payload stage/write/read +
append/trim + the `UpdateBatchAsync` wiring) into a shared helper so contacts (field-diff entries) and
app-data (snapshot entries) ride one code path, each supplying only its own entry builder. The
contact `merge_log` keeps its field-diff behavior; app-data plugs in the snapshot builder.

### Lifecycle / cleanup

> **Note (verified):** contacts are **not** deleted on disconnect/revoke. The contact is data-only
> (status is derived live), so `DisconnectAsync` / `RevokeConnectionAsync` only publish
> `ConnectionDeletedNotification` (handled by auth-cache resets + introduction cleanup) and leave the
> contact file intact. The **only** contact deletion is the explicit `DELETE /api/v2/contacts/{uniqueId}`
> → `ContactService.DeleteByUniqueIdAsync` (`DeleteByOdinIdAsync` has no callers). So there is no
> lifecycle delete-cascade to attach to — the app-data cascade hooks directly into the contact delete.

- **Contact deleted** — hook the sweep **synchronously into `ContactService.DeleteByUniqueIdAsync`**:
  after soft-deleting the contact, query `FileType=389, GroupId=[uniqueId]` and soft-delete those
  files. This is clean because the contact's `uniqueId` *is* the app-data files' `groupId` (both
  `md5(odinId)`), so the delete already has the exact key — no extra derivation, no MediatR handler,
  same request scope. (Disconnect/revoke deliberately does **not** trigger this — app data persists
  alongside the surviving contact.)
- **App uninstalled** — in the app delete/revoke path, query `TagsMatchAtLeastOne=[appId]` on
  `FileType=389` and soft-delete. Far simpler than the refinement, which would have to rewrite every
  contact's `ext_data` map to drop the uninstalled app's key.

Both sweeps delete N files; respect the `ScopedConnectionFactory` "no parallel DB work on one scope"
rule (CLAUDE.md) — iterate **sequentially** (cleanup is not hot-path). A periodic reconcile sweep
(orphan 389 files whose `groupId` matches no contact, or whose `appId` is uninstalled) is a
low-priority backstop, not v1-blocking.

### Isolation & sensitive-field guidance

**What's protected.** App data is encrypted at rest (file AES key, under the ContactDrive storage
key) and in transit (shared-secret transport + TLS). This protects against on-disk compromise and
network interception — the same posture as the contact record itself.

**What's *not* protected — and is intentional.** All fileType-389 files live on the **ContactDrive**,
which apps hold a **shared read grant** to. So:

- **App-from-app:** any app with ContactDrive read access can read **another** app's data files. The
  `Tags=[appId]` stamp is a **convenience boundary (find-your-own / don't-clobber), not a security
  boundary** — it's set from the token on writes, but it does not restrict reads.
- **App-from-server:** the owner's server holds the drive storage key, so it *can* decrypt the
  content. This is **not zero-knowledge** — the same explicit stance the refinement takes.

This is a deliberate trade for simplicity and consistency with the rest of the contact file (where
core data is likewise readable by any contact-drive-granted app). True per-app read isolation would
require a **separate drive per app** or **per-file ACLs** — both add provisioning/grant plumbing and
are **out of scope** here, independent of the storage shape. Flag for a future increment if a concrete
requirement appears.

**Guidance to document for app developers:**

1. App contact data is **not private to your app** — assume any installed app with contacts access can
   read it. Do not store another party's secrets there.
2. If a value is genuinely sensitive, **either don't store it on the contact record at all, or
   apply your own app-level encryption** to that value before sending it. The server stores whatever
   bytes you send **verbatim** inside the JSON, so app-level encryption is transparent to this design
   (it just becomes opaque ciphertext in your blob).
3. The `versionTag` you read is required to write — **read-before-write**, and on `409` re-read and
   retry (another instance of your app advanced the file).

---

## Files to change

- **`src/services/Odin.Services/Contacts/`** — new `AppContactDataService` (mirrors `ContactService`'s
  create/overwrite/soft-delete helpers, pointed at `FileType=389`; reuses `GetWriteContext`,
  `GetForWritingAsync`, `EncryptContent`/`DecryptContent`, `CreateInternalFileId`,
  `WriteNewFileHeader`, `UpdateActiveFileHeader`, `SoftDeleteLongTermFile`). Add
  `AppContactDataFileType = 389`. Could also live as new methods on `ContactService`; a separate
  service keeps it orthogonal and unit-testable.
- **Change-log helper** — extract the reusable `merge_log` mechanics (payload stage/write/read,
  append/trim, `UpdateBatchAsync` wiring from `WriteContentWithMergeLogAsync`/`ReadMergeLogAsync`)
  into a shared helper. `ContactService` keeps its field-diff entry builder (`ContactMergeLog`);
  `AppContactDataService` supplies a snapshot entry builder (`chng_log` payload, `{ at, priorContent }`,
  cap ~10 entries).
- **`src/services/Odin.Services/Contacts/ContactRequests.cs`** —
  `SetAppContactDataRequest { string Content; Guid VersionTag }` (plaintext JSON over shared secret;
  `VersionTag` for the optimistic-concurrency gate, empty on first write). Reuse `ContactWriteResponse`
  / `ContactWriteConflict` for responses.
- **`src/apps/Odin.Hosting/UnifiedV2/Connections/V2ContactsController.cs`** — add
  `PUT`/`DELETE /{odinId}/app-data`; inject `AppRegistrationService` and resolve the appId via the
  existing `AccessRegistrationId → AppClientRegistration.AppId` lookup (a thin helper, not a new
  abstraction). No read endpoint (clients use the existing QueryBatch).
- **`ContactService.DeleteByUniqueIdAsync`** — after the contact soft-delete, sweep
  `FileType=389, GroupId=[uniqueId]` and soft-delete (the contact's `uniqueId` is the app-data
  `groupId`). This is the only contact-delete path; disconnect/revoke does not delete contacts.
- **App delete/revoke path** (`AppRegistrationService`) — sweep `FileType=389, Tags=[appId]` and
  soft-delete the uninstalled app's files across all contacts.

## Reuse (do not reinvent)

`ContactService.GetWriteContext` (bypass-ACL write upgrade), `GetForWritingAsync`,
`EncryptContent`/`DecryptContent`, `ToContactUniqueId` / `ContactGuid.ToGuidId`,
`fileSystem.Storage` `CreateInternalFileId` / `WriteNewFileHeader` / `UpdateActiveFileHeader` /
`SoftDeleteLongTermFile`, `fileSystem.Query.GetFileByClientUniqueId`, the `MergeAsync` retry pattern,
the `merge_log` payload mechanics (`WriteContentWithMergeLogAsync` / `ReadMergeLogAsync`,
`UpdateBatchAsync` + `PayloadInstruction`), the `AppRegistrationService` access-reg → appId lookup.

---

## Comparison to the prior plans

| Dimension | Original (per-app payload) | Refinement (shared `ext_data`) | This plan (separate file) |
|---|---|---|---|
| Where app data lives | payload on the contact file | `ext_data` payload on the contact file | own file (`FileType=389`) |
| Write contention with enrichment/core | yes (whole-file gate, 409s) | yes (merge-retry to dodge 409s) | **none** — app owns its file |
| 10 KB content cap / overflow logic | bumps into it | needs chopped-preview + overflow split | **n/a** — each file has its own budget |
| `MaxPayloadsCount=25` ceiling | ~23 apps | avoided via one shared payload | **n/a** — no contact-file payloads added |
| Read | new dedicated GET + key derivation | generic payload GET (fixed key) | existing client-side `QueryBatch` |
| Encryption | client-side | server-side | server-side |
| Inline preview rides with contact query | n/a | **yes** (the one advantage) | no (separate query) |
| App-uninstall cleanup | per-contact descriptor purge | rewrite every `ext_data` map | **one query by appId tag** |
| Contact-delete cleanup | free (inside contact file) | free (inside contact file) | cascade query by `GroupId` |
| Touches core contact write path | no | **yes** | **no** (fully orthogonal) |

The refinement's sole advantage is that a preview returns "for free" with the normal contact query —
and that convenience is exactly what pulls in its chopping / overflow / inline-cap complexity. This
plan trades that for one extra (cheap, idiomatic) query and gains zero write contention, no overflow
machinery, and trivial app-uninstall cleanup.

---

## Open decisions (recommendations)

- **Separate service vs. methods on `ContactService`** — recommend a separate `AppContactDataService`
  (orthogonal, unit-testable; `ContactService` stays focused on `FileType=100`).
- **Address writes by `odinId` vs. by derived `uniqueId`** — recommend `odinId` in the route; the
  server derives the keys, so the client never handles the derived id.
- **Per-file content cap** — the standard 10 KB `AppData.Content` applies per app-data file. If an app
  needs more, it adds its own payloads to *its own* file (it has the full 25-payload budget there) —
  no shared-overflow design needed. (The refined plan's ~200 B per-app inline cap does not apply — it
  existed only because apps shared the contact header.)
- **Concurrency** — **decided: optimistic-concurrency gate on the app-data file's own `versionTag`**
  (the 389 file, not the contact file); stale → `409`, client re-reads + retries — exactly like
  `UpdateAsync` / `SetImageAsync`. (Not last-writer-wins.)
- **Change history** — **decided: keep it, as whole-blob snapshots** (`chng_log` payload, reusing the
  `merge_log` mechanics; entries `{ at, priorContent }`). Not field-diff (app JSON is opaque). **Cap
  decided: keep the last 10** (count-based, oldest dropped).
- **Permission floor** — **decided: App token + ContactDrive access only; do NOT require
  `ManageContacts`** (the storage-key requirement in `GetWriteContext` is the implicit gate).
- **Shared `EncryptContent`/`DecryptContent`** — **decided: lift out of `ContactService` into a shared
  helper** (`AppContactDataService` needs the identical logic; pairs with the shared change-log
  helper).
- **Drive choice** — recommend ContactDrive (apps already have read access). A dedicated drive would
  buy real per-app read isolation but adds drive-provisioning and grant plumbing; defer unless
  isolation becomes a requirement.

---

## Verification

- **Unit** (`Odin.Services.Tests`): key derivation is deterministic (`md5(appId+odinId)`,
  `groupId = md5(odinId)`); upsert round-trips content; content IV rotates on update.
- **Integration** (`Odin.Hosting.Tests`, app-token client; mirror the existing V2 contacts setup):
  - App writes its data for a contact → file exists with `FileType=389`, `UniqueId=md5(appId+odinId)`,
    `GroupId=md5(odinId)`, `Tags=[appId]`; content decrypts.
  - **Isolation:** a different app's QueryBatch (by its own appId tag) never returns app A's file; an
    app writing its data does **not** bump the contact file's version tag or touch `merge_log` /
    `prfl_pic`.
  - **Contact-delete cascade:** deleting/disconnecting the contact soft-deletes all `FileType=389,
    GroupId=md5(odinId)` files.
  - **App-uninstall cleanup:** deleting by `Tags=[appId]` removes that app's files across contacts.
  - **Concurrency:** a write with a stale `versionTag` → `409` carrying the current header; a
    successful write advances the file's tag; a fresh read + retry succeeds.
  - Owner/guest tokens are rejected on the write endpoints.
  - **Change history:** an overwrite appends the prior content to the `chng_log` payload (create logs
    nothing); the log is bounded to the cap (oldest dropped); `chng_log` survives subsequent writes
    (carry-forward) and is absent from a default app-data `QueryBatch` unless explicitly requested.
- **Build/regression:** `dotnet build ./odin-core.sln`; `dotnet test` the contacts + V2 contacts
  suites on SQLite (and PostgreSQL via `docker/start-dev-servers.sh`).
