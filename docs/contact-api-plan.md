# Server-side Contact API (backend) — write CRUD + connection/introduction lifecycle

> **Scope.** This is a **backend-only** plan. It covers the server-side Contact write API, the
> connection/introduction lifecycle that keeps contact files in step, and profile enrichment. The
> odin-js / front-end migration (moving clients off direct drive writes, dropping the stored `source`
> field on the client) is a **separate plan** and is intentionally out of scope here.
>
> **Deferred (not in this plan).** Duplicate detection (hashed email/phone tags, fuzzy name matching),
> `GET /duplicates`, and the `merge` / `link` endpoints are explicitly **deferred** — too much for one
> shot. They can be layered on later without changing the storage format below.

## Scenarios (at a glance)

**Contact CRUD & storage**
- When a contact is created **with** an odinId, it's stored with `uniqueId = ToGuidId(odinId)` in its
  AppData metadata, so every later relationship event lands on that same file.
- When a contact is created **without** an odinId, it's stored under a random `uniqueId` and can only be
  tied to an identity later by an explicit (future, deferred) action.
- When a contact is upserted and a file with the same `uniqueId` already exists, it's updated in place and
  the new content is merged over it without clobbering user-entered fields; the overwritten values are
  appended to the file's `merge_log`.
- When an odinId is supplied on create, **any syntactically valid domain name is accepted** as the odinId —
  there is **no liveness check** (a live identity can go offline permanently anyway).
- When a contact is **read or listed**, the client reads it **directly from the contact drive** as a plain
  drive file (`QueryBatch` on `fileType=100`) — there is **no server-side contact read/list API**.
- When a contact image is set, it's stored as the `prfl_pic` payload (enrichment re-encrypts the fetched
  photo under the file keyHeader); clients read it as a normal drive payload.
- When a client writes with a stale `versionTag`, it gets **409 Conflict** with the current versionTag/header
  to re-fetch and retry; server-internal write races retry once, then defer to the idempotent job/reconcile.

**Lifecycle (connections & introductions) — server-internal, kept**
- When an introduction is received, a data-only contact is upserted for each introduced identity.
- When a connection request is received, a data-only contact is upserted for the sender.
- When a connection request is sent, a contact is upserted for the recipient.
- When a connection is accepted/finalized, the contact file is ensured and enrichment is scheduled to re-pull
  the now-available **peer** data — no status is written to the file.
- When the enrichment job runs, it loads data from the source dictated by **live** status — the **peer**
  profile if connected, the **public** profile (`pub/profile`) if not — and merges name/phone/email/location/
  birthday/photo into the contact (overwritten values go to `merge_log`).
- When a connection is disconnected or blocked (by us or by them), **nothing is written to the file** —
  status is never stored; clients derive it from `CircleNetworkService` themselves.
- When a contact blocks or disconnects **us from their side**, nothing is pushed; our ICR stays stale until a
  peer call (enrichment / `sync`) hits `403 + X-Remote-Server-Icr-Issue`, which revokes the ICR.
- When reconcile (or `POST /sync`) runs, a contact file is ensured for every current relationship, backfilling
  any that are missing.

**Write lockdown (via permission)**
- When an app without `ManageContacts` attempts a write, it's rejected — apps hold only a **Read** grant on
  the contact drive and all app writes go through the Contact API.
- When a write goes through the Contact API, `ContactService` writes the drive on the caller's behalf via the
  existing `OdinContextUpgrades.UpgradeToByPassAclCheck(...)` upgrade.
- When anyone reads the contact drive, it still works (read is unchanged).

---

## Context

Contacts live as files on `SystemDriveConstants.ContactDrive` (owner-only). Today all contact logic is
client-side; the server never touches the contact drive, and the owner-app creates contacts after accepting
a connection by peer-querying the new connection's profile. We want the **server** to own contact **writes**:
a write API over `/api/v2/contacts`, plus automatic create/update across the relationship lifecycle
(introduced → request pending → connected → disconnected/blocked) and profile enrichment.

**Reads stay client-side.** Clients continue to read contacts as plain files from the contact drive
(`QueryBatch` on `fileType=100`). There is **no server-side read/list API**, no server-composed relationship
object, and no server-side `SharedSecretEncryptedFileHeader` composition — clients already do all of that for
every other drive.

Constraints / decisions locked with the user:
- **A non-connected person is still a contact and MUST be stored as a file.** Introduced, pending, connected,
  and manually-added people all get a contact file. No "virtual" contacts.
- **Same file across the lifecycle.** Any contact that carries an odinId is keyed deterministically
  (`uniqueId = ToGuidId(odinId)`), so introduced → pending → connected resolve to the *same* file, updated in
  place — no duplicate. A contact typed in by hand with **no** odinId cannot be matched automatically (no
  shared key); resolving that is deferred.
- **Status is never stored on the contact file.** Connected/blocked/introduced/pending is derived live from
  `CircleNetworkService` (+ requests/introductions) **by the client at read time**, not composed by the server.
- **No `source` field going forward.** The record holds only contact *data*; whether that data came from the
  peer (connected) or the public profile (not connected) follows from live status. The legacy `source` field
  gets an explicit rule: **readers tolerate it, writers never emit it** (existing files keep theirs and stay
  readable).
- **Data origin follows live status**: connected → the peer's data; not connected → the identity's public
  profile (`pub/profile`). `ICR.OriginalContactData` is being removed.
- **Disconnect/Block**: keep the file, write nothing. **Auth**: `OwnerOrApp`. **Encrypted at rest**: yes.
  **Sync endpoint**: yes.

### The identity-keying insight (why "same file" works)
`uniqueId = ToGuidId(odinId)` (MD5→hex→Guid, identical to odin-js `toGuidId`), stored in the file's **AppData
metadata**. Every relationship-derived contact carries an odinId (introductions carry `Identity`; requests
carry the sender/recipient; connections carry the OdinId), so introduced → pending → connected all resolve to
the **same uniqueId → same file**, updated in place. A contact created with **no** odinId uses a random
`uniqueId`.

---

## Part A — Contact write foundation

### Existing odin-js contract — MUST match byte-for-byte (backwards compatibility)
This is the authoritative shape already written to the drive by odin-js
(`packages/libs/js-lib/src/network/contact/ContactTypes.ts` + `provider/contact/ContactProvider.ts`). It is a
**homegrown, schema.org-inspired** shape (not from an RFC), and that is fine. The C# DTOs and write path must
reproduce it exactly so existing files stay readable by both sides.

**Drive & file identity** (`ContactConfig`):
- ContactDrive: `alias = 2612429d1c3f037282b8d42fb2cc0499`, `type = 70e92f0f94d05f5c7dcd36466094f3a5`
  (= `SystemDriveConstants.ContactDrive`).
- `fileType = 100` (`ContactConfig.ContactFileType`). `dataType` / `groupId`: unset.
- **`uniqueId` (in AppData metadata)** `= ToGuidId(odinId) = md5(odinId)`-as-Guid when an odinId exists;
  otherwise a random Guid.
- `tags = [ ToGuidId(odinId) ]` when an odinId exists; otherwise empty.
- `isEncrypted = true`; `allowDistribution = false`.

**Normative JSON Schema for `AppData.Content`.** `AppData.Content` is the contact JSON, **camelCase**,
AES-encrypted with the per-file KeyHeader then base64 (or spilled to the default payload when it exceeds the
header content limit). The `image` field is **never** in this JSON — it is the `prfl_pic` payload. Writers
MUST conform to the following; readers MUST tolerate the legacy `source` field and any unknown fields.

```jsonc
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "ContactContent",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "odinId": { "type": "string", "description": "optional; a syntactically valid domain. No liveness check." },

    // LEGACY. Readers MUST tolerate it; writers MUST NOT emit it. Connection status is derived
    // live by the client from CircleNetworkService, and data origin follows from that status.
    "source": { "type": "string", "enum": ["contact", "public", "user"] },

    "name": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "displayName":    { "type": "string" },
        "givenName":      { "type": "string" },
        "additionalName": { "type": "string" },
        "surname":        { "type": "string" }
      }
    },
    "location": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "city":    { "type": "string" },
        "country": { "type": "string" }
      }
    },

    // phone and email are SINGLE-VALUED (one object each, not arrays), matching odin-js today.
    "phone": {
      "type": "object",
      "additionalProperties": false,
      "properties": { "number": { "type": "string" } }
    },
    "email": {
      "type": "object",
      "additionalProperties": false,
      "properties": { "email": { "type": "string" } }
    },

    // birthday.date is a FREE-FORM string, kept verbatim (no fixed format).
    "birthday": {
      "type": "object",
      "additionalProperties": false,
      "properties": { "date": { "type": "string", "description": "free-form, stored verbatim" } }
    }
  }
}
```

**Profile image**: separate payload, key `prfl_pic` (`CONTACT_PROFILE_IMAGE_KEY`), with a generated
`previewThumbnail` on the header. Never embedded in `Content`.

### Data structures — new `src/services/Odin.Services/Contacts/`
`ContactContent` is the contact **data** only (serialized **camelCase**, pinned explicitly on the DTOs — don't
trust the serializer default): `ContactContent { OdinId?, Name?, Location?, Phone?, Email?, Birthday? }` with
`ContactName { DisplayName?, GivenName?, AdditionalName?, Surname? }`, `ContactLocation { City?, Country? }`,
`ContactPhone { Number }`, `ContactEmail { Email }`, `ContactBirthday { Date }`. **No `source`** — readers
tolerate it on legacy files, writers never emit it. **No relationship/status object** — that's derived live by
the client.

Request/response DTOs:
- `UpsertContactRequest { ContactContent Content, Guid? VersionTag }` — `VersionTag` carried on update.
- `UpsertContactResponse { Guid UniqueId, Guid VersionTag }` — the keyed uniqueId and the new versionTag.
- `ContactWriteConflict { Guid VersionTag, SharedSecretEncryptedFileHeader Current }` — 409 body so the client
  can re-fetch, re-apply, and retry.

### `ContactService` (`Odin.Services/Contacts/ContactService.cs`)
- `const int ContactFileType = 100`; drive = `ContactDrive`, `FileSystemType.Standard`.
- `static Guid ToContactUniqueId(OdinId)` — **promote** the private `ToGuidId` from
  `HomebaseChannelContentService.cs:423` into a shared util; repoint both call sites.
- Write surface: `UpsertAsync`, `DeleteByUniqueIdAsync` / `DeleteByOdinIdAsync` (`SoftDeleteLongTermFile`),
  `SetImageAsync` (`prfl_pic` payload). Server-internal helpers used by the lifecycle/enrichment:
  `EnsureContactAsync(odinId)` and `GetByUniqueIdForWriting` (for read-modify-write merges).
- **No read/list/get methods exposed to clients.** Clients read the drive directly. The service reads files
  only internally, for read-modify-write merges, via `GetFileByClientUniqueIdForWriting`.
- **Permission: `PermissionKeys.ManageContacts`.** New permission key, following the **`ManageFeed` pattern
  exactly**: add the constant in `PermissionKeys`, add it to `PermissionKeyAllowance.Apps`, and assert it in
  `ContactService` on every write. Owner master key is implicitly allowed (it always holds the relevant
  permission set).
- **Write-on-behalf via context upgrade.** `ContactService` writes the contact drive using the existing
  `OdinContextUpgrades.UpgradeToByPassAclCheck(...)` pattern (precedent: `ShamirBaseService`). No new security
  mechanism, no `IsServiceManaged` drive flag, no `AssertCanWriteToDrive` chokepoint, no new error code.
- **Write path.** Client sends **plaintext contact JSON over the normal shared-secret transport** (same as any
  shared-secret request body); the server encrypts at rest with the per-file `keyHeader`. **No client-side
  transfer-keyHeader ceremony** — the server already handles contact plaintext during enrichment, so it owns
  the encryption: generate/keep a keyHeader → `keyHeader.EncryptDataAes(json)` base64, `IsEncrypted=true`,
  `CreateServerFileHeader` (wraps keyHeader with the drive storage key), `WriteNewFileHeader` /
  `UpdateActiveFileHeader`. `ServerMetadata = OwnerOnly`; `tags = [uniqueId]` when present. Default-payload
  spill fallback for oversized content.
- **Upsert/merge.** Lookup via `GetFileByClientUniqueIdForWriting(ToContactUniqueId(odinId))`; if present
  preserve `fileId` and **merge** new content over existing (don't clobber user-entered fields); else create.
  Every merge that overwrites an existing field value appends the old value(s) to the **`merge_log`** payload
  (see below).
- **Concurrency (optimistic, `versionTag`).** A client upsert carries the `versionTag` it last read; the write
  asserts it still matches the stored file.
  - *Client write with a stale versionTag* → **409 Conflict** whose body is `ContactWriteConflict` (current
    `versionTag` + current `SharedSecretEncryptedFileHeader`), so the client re-fetches, re-applies, retries.
    We never silently overwrite.
  - *Server-internal races* (a lifecycle handler and the enrichment job touching the same odinId-keyed file):
    re-read + re-merge and retry once; if it still conflicts, drop it — the enrichment job is idempotent and
    reconcile converges the file later.
- **Delete.** `DELETE /contacts/{uniqueId}` soft-deletes the file (`SoftDeleteLongTermFile`). Because the
  server-internal lifecycle/reconcile still runs, a connected contact would be re-ensured by reconcile; so for
  a contact with a **live relationship**, delete also cascades the teardown first (disconnect if connected /
  cancel outgoing request / reject incoming request / drop introduction) so reconcile won't resurrect it. A
  contact with no live relationship is a plain soft-delete. The disconnect is outward-facing, so `DELETE`
  requires explicit client confirmation.
- **Image (`prfl_pic`).** Stored as the AES-encrypted payload under the file keyHeader. There is no image read
  endpoint — clients read it as a normal drive payload. Enrichment fetches the peer's `prfl_pic`, decrypts it
  internally, re-encrypts with the contact file's keyHeader, and stores it as payload + preview thumbnail.

### Merge log (`merge_log` payload) — in scope
Whenever a merge overwrites existing field values — both an **API upsert over an existing file** and
**enrichment merging peer/public data over it** — the overwritten values are appended to an append-only log
attached to the contact file. (Same idea as Outlook dumping conflicts into the long-description field.)
- **Mechanism.** A dedicated **`merge_log` payload** on the contact file (separate from `Content` and
  `prfl_pic`), encrypted at rest like content.
- **Trigger.** Only fields whose prior value was non-empty and is being *replaced* by a different value;
  no-op writes and first-time fills do not log.
- **Entry format.** A JSON array; each entry `{ "at": <UnixTimeUtc>, "by": "api" | "enrichment",
  "changes": { "<jsonPath>": <oldValue> } }` (old values only — the new value is in `Content`).
- **Growth bound.** Cap the log (e.g. last N=100 entries or a byte ceiling); oldest entries are dropped when
  the cap is exceeded, so the payload size stays bounded.

### Controller — `src/apps/Odin.Hosting/UnifiedV2/Connections/V2ContactsController.cs`
Modeled on `V2ConnectionRequestsController` (`[Route(UnifiedApiRouteConstants.Contacts)]`,
`[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]`, `[ApiExplorerSettings(GroupName="v2")]`,
`: OdinControllerBase`, `[SwaggerOperation(Tags=[SwaggerInfo.Contacts])]`):

| Verb | Route | Body | Returns |
|---|---|---|---|
| POST | `/api/v2/contacts` | `UpsertContactRequest` | `UpsertContactResponse`; stale `versionTag` → **409** `ContactWriteConflict` |
| DELETE | `/api/v2/contacts/{uniqueId:guid}` | — | 204 — soft-delete (cascades relationship teardown first if a live relationship exists) |
| POST | `/api/v2/contacts/sync/{odinId}` *(optional)* | — | 204/accepted — trigger re-enrichment from the peer/public profile (Part B) |

`POST /contacts` is the **upsert**: it accepts an optional `content.odinId`; when present the file is keyed on
`ToContactUniqueId(odinId)`, so a later connection lands on the same file. The client sends plaintext content
over the shared-secret transport; the server encrypts at rest.

**No GET/list/by-odin-id/PUT/archive/image/link/merge/duplicates endpoints** — reads are direct-from-drive, and
those operations are dropped or deferred.

---

## Part B — Lifecycle: events keep contact files in step with relationships

### `ContactLifecycleService` (`Odin.Services/Contacts/`) — MediatR handlers
These notifications are **awaited synchronously** on the connection hot path → handlers must be **fast &
local** (ensure the file exists, schedule enrichment; never peer I/O inline). Handlers write **only contact
data**, never status:
- `INotificationHandler<IntroductionsReceivedNotification>` → for each introduced `Identity`, ensure a
  data-only `{ odinId }` contact and schedule enrichment (uses the **public** profile — not connected yet).
- `INotificationHandler<ConnectionRequestReceivedNotification>` → ensure a data-only `{ odinId }` contact for
  `Sender` and schedule enrichment (public profile).
- `INotificationHandler<ConnectionFinalizedNotification>` → ensure the file and schedule enrichment to re-pull
  the now-available **peer** data (`IJobManager.ScheduleJobAsync(ContactEnrichmentJob{ ... })`).
- `INotificationHandler<ConnectionDeletedNotification|ConnectionBlockedNotification>` → **no file write**.
  (Optionally schedule a re-enrich from the public profile to refresh data no longer peer-available.)
- **Remote-initiated block/disconnect is silent** (no push, no background reconcile): our ICR stays stale
  until a peer call (enrichment / `sync`) hits `403 + X-Remote-Server-Icr-Issue`, which auto-revokes the ICR.
  A bare 403 / other failure does **not** revoke; the job exits gracefully without mutating the contact.
- **Outgoing request**: confirm whether a "request sent" notification exists; if not, ensure the contact in
  the V2 send path (`V2ConnectionRequestsController` / send service) — see risk below.

Since there is no server-composed relationship object, these handlers only persist contact **data**; clients
read live status from `CircleNetworkService` themselves.

### Reconcile (backfill + self-heal) — `ContactReconcileJob` / `POST /sync`
A reconcile pass ensures a contact file exists for every current relationship: scan connected ICRs + pending +
sent requests + received introductions, upsert any missing data-only contact (no status written). Backfills
contacts for relationships that predate this feature and heals missed events.

### Profile enrichment — `ContactEnrichmentService` + `ContactEnrichmentJob`
`EnrichAsync(OdinId, IOdinContext)` chooses the data source from **live** status (`CircleNetworkService`):
- **Connected** → peer-query the identity's `ProfileDrive` via `PeerDriveQueryService.GetBatchAsync`
  (`FileType=[77]`, `GroupId=[BuiltInAttribute GUIDs]`, `IncludeHeaderContent=true`; needs `UseTransitRead` +
  an ICR) and fetch the `prfl_pic` payload (`GetPayloadStreamAsync(...,"prfl_pic")`).
- **Not connected** → read the identity's **public profile** (`pub/profile`, anonymous) for name/photo/etc.

Then map the result → `ContactContent` (Name/Phone/Email/Address→Location/Birthday; port the mapping from
odin-js `queryRemoteAttributes`; add C# `BuiltInAttributes` GUID constants), store the image as the `prfl_pic`
payload + preview thumbnail, and `ContactService.UpsertAsync` (merge, **data only**, overwritten values →
`merge_log`).

`ContactEnrichmentJob : AbstractJob` (template: `ExportTenantJob`; register in `JobExtensions.cs`) reconstructs
an owner/system `IOdinContext` w/ `UseTransitRead` (mirror
`PeerOutboxProcessorBackgroundService.ProcessItemThread`), resolves the service from its own job scope (safe
for DB), and calls `EnrichAsync`; idempotent via uniqueId upsert. `POST /sync/{odinId}` calls the same method
inline with the request's owner context.

On a peer **403** the platform may revoke the ICR (→ live status flips to severed; **no file write**); on any
other peer failure the job returns `Fail` **without mutating** the contact. `sync` doubles as the "verify +
refresh" path — optionally call `CircleNetworkVerificationService.VerifyConnectionAsync` to detect a peer-side
block proactively.

---

## Wiring (edits)
- `UnifiedV2/UnifiedApiRouteConstants.cs` → `Contacts = BasePath + "/contacts"`.
- `UnifiedV2/SwaggerInfo.cs` → `Contacts = "Contact Operations"`.
- `PermissionKeys.cs` → add `ManageContacts` constant (mirror `ManageFeed`); add it to
  `PermissionKeyAllowance.Apps`.
- `SystemAppConstants` → ChatApp/MailApp contact-drive grant `ReadWrite` → **`Read`** (app writes go through
  the API; reads stay direct).
- `TenantServices.cs` (~L277): register `ContactService` and `ContactEnrichmentService`
  (`InstancePerLifetimeScope`), and `ContactLifecycleService`
  `.As<INotificationHandler<IntroductionsReceivedNotification>>().As<…ConnectionRequestReceived…>()
  .As<…ConnectionFinalized…>().As<…ConnectionDeleted…>().As<…ConnectionBlocked…>()`.
- `JobExtensions.cs` → register `ContactEnrichmentJob` (+ `ContactReconcileJob` if used).
- Promote `ToGuidId` → shared util; repoint `HomebaseChannelContentService.cs:423`.

## Reused existing code
`OdinContextUpgrades.UpgradeToByPassAclCheck` (write-on-behalf; precedent `ShamirBaseService`);
`PermissionKeys` / `PermissionKeyAllowance` (the `ManageFeed` pattern); `PeerDriveQueryService.GetBatchAsync` /
`GetPayloadStreamAsync`; `HomebaseProfileContentService` (attribute query shape, `AttributeFileType=77`,
`ProfileBlock` parse); `CircleNetworkService.GetIcrAsync` (live status for the enrichment source choice);
`CircleNetworkService.DisconnectAsync` (delete cascade); `CircleNetworkRequestService` pending/sent getters +
cancel/reject (delete cascade); `CircleNetworkIntroductionService.GetReceivedIntroductionsAsync` + delete
(cascade); `IJobManager` / `AbstractJob` (`ExportTenantJob`); `PeerOutboxProcessorBackgroundService` (detached
owner context); `KeyHeader` / `DriveStorageServiceBase` encrypt pattern (`FeedWriter`,
`ShardRequestApprovalCollector`).

## Verification
- **Unit**: `ToContactUniqueId` parity vector vs odin-js `toGuidId`; a contact written via the API is stored
  encrypted (`IsEncrypted=true`) with `fileType=100`, `uniqueId=ToGuidId(odinId)`, `tags=[uniqueId]`, and
  decodes (camelCase) back to `ContactContent`; an odin-js-written encrypted contact is readable unchanged
  (and a legacy `source` field is tolerated, never re-emitted); merge over an existing file appends overwritten
  values to `merge_log` and respects the growth bound; first-time fills do **not** log.
- **Integration** (`_Universal`, two identities, peer flow per CLAUDE.md):
  - Frodo introduced to Sam → a contact file appears (`fileType=100`, keyed on `ToGuidId(sam)`); enrichment
    fills name/photo from Sam's **public** profile (`pub/profile`); **no status field on the file**.
  - Request pending → same file (no new file).
  - Connect → **same file**; enrichment re-pulls name/photo from **peer** data; assert no duplicate file and
    no status written to the file; overwritten fields land in `merge_log`.
  - Disconnect → file kept, no file write.
  - Create with `content.odinId` set (any valid domain, no liveness check) → keyed on `ToGuidId(odinId)`; a
    later connect updates the same file.
  - Concurrent client POSTs with a stale `versionTag` → the loser gets **409** carrying the current
    `versionTag`/header, re-fetches and succeeds; assert no lost update.
  - `DELETE` a connected contact → it disconnects (peer access revoked) and stays deleted (reconcile does not
    resurrect it); `DELETE` a contact with a pending outgoing/incoming request → the request is
    cancelled/rejected.
  - **Permission**: an app **without** `ManageContacts` cannot write via the API; an app with only a `Read`
    drive grant **can still read** the contact drive directly; an app with `ManageContacts` writes succeed via
    the API (which upgrades the context to bypass the ACL check).
- SQLite + PostgreSQL: `dotnet test ./odin-core.sln`.

## Open implementation risks (resolve during build)
- Outgoing-request materialization: confirm a "request sent" notification exists; if not, upsert the contact
  in the send path or rely on `ContactReconcileJob` / `/sync`.
- Enrichment job must reconstruct an owner context with `UseTransitRead` / ICR access detached from a request
  — mirror `PeerOutboxProcessorBackgroundService`.
- Enrichment branches on live status: **connected** → peer profile (transit); **not connected** → public
  profile (`pub/profile`, anonymous). Confirm the exact server-side `pub/profile` fetch + field mapping.
- `OdinContextUpgrades.UpgradeToByPassAclCheck` for contact writes: confirm it cleanly bypasses the drive ACL
  for an app caller holding only a `Read` grant, exactly as `ShamirBaseService` uses it.
- `ManageContacts` permission: confirm the `ManageFeed` pattern (constant + `PermissionKeyAllowance.Apps` +
  service-side assert) is the complete set of touch points.
- `BuiltInAttributes` GUIDs only in TS today → port to C#. Pin DTO camelCase explicitly.
- `merge_log` payload: confirm payloads can be added/overwritten on the contact file alongside `Content` and
  `prfl_pic` without disturbing the header content-vs-payload spill logic; settle the growth-bound policy.
- Delete cascade: confirm the exact request cancel/reject + introduction-delete methods, run the teardown
  **before** the file soft-delete, and require explicit client confirmation since disconnect is outward-facing.
