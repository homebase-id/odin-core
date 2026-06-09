# Server-side Contact API + full connection/introduction lifecycle

## Scenarios (at a glance)

**Contact CRUD & storage**
- When a contact is created **with** an odinId, it's stored keyed on `ToGuidId(odinId)` so every later
  relationship event lands on that same file.
- When a contact is created **without** an odinId, it's stored under a random uniqueId and can only be tied
  to an identity later by an explicit action.
- When a contact is upserted and a file with the same uniqueId already exists, it's updated in place and the
  new content is merged over it without clobbering user-entered fields.
- When a contact is fetched, it's returned as a `SharedSecretEncryptedFileHeader` (keyHeader re-encrypted to
  the caller's shared secret, content left encrypted) and the client decrypts it, with the live relationship
  composed and attached server-side — the server never returns plaintext.
- When the contact list is requested, it's cursor-paged and relationship-annotated, with filters for
  `source`, `hasOdinId`, `search`, and `relationship` (e.g. connections-only) — relationship filters seed
  from the connection registry so pages stay full.
- When a contact is deleted, it's soft-deleted, but a still-connected/introduced/pending contact is recreated
  by the reconcile pass — to hide an active contact the user archives it (`archivalStatus`) instead.
- When a contact image is set or read, it's the `prfl_pic` payload, shared-secret-encrypted like content
  (client decrypts; enrichment re-encrypts the fetched photo under the file keyHeader).
- When a client writes with a stale `versionTag`, it gets **409 Conflict** with the current versionTag/header
  to re-fetch and retry; server-internal write races retry once, then defer to the idempotent job/reconcile.

**Relationship**
- When any contact is read, its relationship (status, origin, via-introduction, introducer, timestamps) is
  composed live with priority connection-registry > pending request > introduction.

**Lifecycle (connections & introductions)**
- When an introduction is received, a stub contact (`source=public`) is upserted for each introduced identity.
- When a connection request is received, a stub contact (`source=public`) is upserted for the sender.
- When a connection request is sent, a stub contact is upserted for the recipient.
- When a connection request is accepted/finalized, the same contact file flips to `source=contact` and a
  background enrichment job is scheduled.
- When the enrichment job runs, it peer-queries the connection's profile and fills name/phone/email/location/
  birthday/photo, merging into the contact.
- When a connection is disconnected or blocked, the contact file is kept but `source` flips `contact`→`public`.
- When reconcile (or `POST /sync`) runs, a contact file is ensured for every current relationship, backfilling
  any that are missing.

**Manual contact ↔ identity**
- When a contact is created and the user already knows the Homebase ID, the odinId is captured up front so the
  match-later problem never arises.
- When two contacts look like the same person, the duplicate detector surfaces candidate pairs (exact
  email/phone or fuzzy name) but never auto-merges.
- When `POST /merge` is called, the two contacts are unioned into the odinId-keyed file and the other is
  soft-deleted.
- When `POST /link` is called, the odinId is attached and the file is re-keyed to `ToGuidId(odinId)` (routing
  into merge if a file already exists under that key).
- When a hand-typed no-odinId contact later connects without having been linked, a separate odinId-keyed file
  is created and the duplicate is resolved only via detection/merge/link.

**Write lockdown (Part D)**
- When a client or app attempts a direct write to the ContactDrive with the lockdown on, it's rejected with
  HTTP 403 `DirectContactDriveWriteNotAllowed`.
- When a write goes through the Contact API, it's allowed via the scoped `ContactService` bypass.
- When anyone reads the ContactDrive, it still works (read is unchanged for backwards compatibility).
- When the lockdown flag is off, direct writes still work, so old and new clients coexist during migration.
- When a peer attempts to write the ContactDrive, it's already impossible (owner-only, non-distributed).

---

## Context

Contacts live as files on `SystemDriveConstants.ContactDrive` (owner-only). Today all contact logic
is client-side; the server never touches the contact drive, and the owner-app creates contacts after
accepting a connection by peer-querying the new connection's profile. We want the **server** to own
contacts: full CRUD over `/api/v2/contacts`, automatic create/update across the entire relationship
lifecycle (introduced → request pending → connected → disconnected/blocked), and a single API that
returns **all** information about a contact, including its relationship provenance (is it via an
introduction, who introduced them, connection status, pending state).

Constraints / decisions locked with the user:
- **A non-connected person is still a contact and MUST be stored as a file.** Introduced, pending,
  connected, and manually-added people all get a contact file. No "virtual" contacts.
- **Same file across the lifecycle.** Any contact that carries an odinId is keyed deterministically,
  so introduced→pending→connected resolve to the *same* file, updated in place — no duplicate. A
  contact typed in by hand with **no** odinId cannot be matched automatically (no shared key); it is
  handled by capture-at-creation, duplicate *detection* (suggestions), explicit link, and a merge
  operation — **never a silent auto-merge** (see Part C).
- **List always includes** non-connected (introduced/pending) identities.
- **Relationship is composed live** from the connection registry/requests/introductions at read time
  (authoritative, never stale). Stored content stays backwards-compatible with odin-js.
- `ICR.OriginalContactData` is being removed → profile data comes from peer-querying the profile.
- **Disconnect/Block**: keep file, flip `source` `contact`→`public`. **Auth**: `OwnerOrApp`.
  **Encrypted at rest**: yes. **Sync endpoint**: yes.

### The identity-keying insight (why "same file" works)
`uniqueId = ToGuidId(odinId)` (MD5→hex→Guid, identical to odin-js `toGuidId`). Every
relationship-derived contact carries an odinId (introductions carry `Identity`; requests carry the
sender/recipient; connections carry the OdinId), so introduced→pending→connected all resolve to the
**same uniqueId → same file**, updated in place. A contact created with **no** odinId uses a random
uniqueId; linking it to an identity later is an explicit re-key (see Part C).

---

## Part A — Contact CRUD foundation

### Existing odin-js contract — MUST match byte-for-byte (backwards compatibility)
This is the authoritative shape already written to the drive by odin-js
(`packages/libs/js-lib/src/network/contact/ContactTypes.ts` + `provider/contact/ContactProvider.ts`).
The C# DTOs and write path must reproduce it exactly so existing files stay readable by both sides.

**Drive & file identity** (`ContactConfig`):
- ContactDrive: `alias = 2612429d1c3f037282b8d42fb2cc0499`, `type = 70e92f0f94d05f5c7dcd36466094f3a5`
  (= `SystemDriveConstants.ContactDrive`).
- `fileType = 100` (`ContactConfig.ContactFileType`). `dataType`/`groupId`: unset.
- `uniqueId = toGuidId(odinId) = md5(odinId)`-as-Guid when an odinId exists; otherwise a random Guid.
- `tags = [ toGuidId(odinId) ]` when an odinId exists; otherwise empty.
- `isEncrypted = true`; `allowDistribution = false`.

**`AppData.Content`** = the contact JSON, **camelCase**, AES-encrypted with the per-file KeyHeader then
base64 (or spilled to the default payload when it exceeds the header content limit). The `image` field
is **removed** from this JSON and stored as a payload instead. Exact shape:
```jsonc
{
  "odinId": "frodo.dotyou.cloud",            // optional
  "source": "contact" | "public" | "user",   // required
  "name":     { "displayName": "…", "givenName": "…", "additionalName": "…", "surname": "…" },
  "location": { "city": "…", "country": "…" },
  "phone":    { "number": "…" },
  "email":    { "email": "…" },
  "birthday": { "date": "…" }                 // string, kept verbatim
}
```
**Profile image**: separate payload, key `prfl_pic` (`CONTACT_PROFILE_IMAGE_KEY`), with a generated
`previewThumbnail` on the header. Never embedded in `Content`.

### Data structures — new `src/services/Odin.Services/Contacts/`
`ContactContent` mirrors the JSON above 1:1 (serialized **camelCase**; `enum ContactSource { Contact,
Public, User }` serialized as the lowercase strings): `ContactContent { OdinId?, Source, Name?,
Location?, Phone?, Email?, Birthday? }` with `ContactName { DisplayName, GivenName?, AdditionalName?,
Surname? }`, `ContactLocation { City?, Country? }`, `ContactPhone { Number }`, `ContactEmail { Email }`,
`ContactBirthday { Date }`. Pin camelCase explicitly on these DTOs (don't trust the serializer default).

**Live-composed relationship** (NOT stored on the file):
```
enum ContactRelationshipState { None, Introduced, RequestIncoming, RequestOutgoing, Connected, Blocked }
class ContactRelationship {
  ContactRelationshipState State;
  ConnectionStatus ConnectionStatus;     // None|Connected|Blocked  (from ICR)
  ConnectionRequestOrigin Origin;        // None|IdentityOwner|Introduction|IdentityOwnerApp
  bool ViaIntroduction;                  // Origin==Introduction OR State==Introduced
  OdinId? IntroducerOdinId;
  UnixTimeUtc? ConnectedAt, UpdatedAt;   // ICR.Created / LastUpdated
  UnixTimeUtc? IntroducedAt;             // IdentityIntroduction.Received
  UnixTimeUtc? RequestReceivedAt;        // pending/sent request timestamp
}
```
API response uses the **same shared-secret-encrypted file-header mechanism as every other drive read** —
the standard `SharedSecretEncryptedFileHeader` (file metadata, `HasImage`, AES-encrypted `AppData.Content`,
and the `SharedSecretEncryptedKeyHeader`) **plus** the live `ContactRelationship`. The server does **not**
return plaintext; the **client** decrypts `Content` into `ContactContent` exactly as it does any drive file
today. (`ContactContent` is the *decrypted* schema — used by the client after decryption and by the server
internally for enrichment/merge — and is never sent in the clear.)
Inputs: `UpsertContactRequest { Content, VersionTag? }`, `ContactListResult { Results, Cursor }`.

### `ContactService` (`Odin.Services/Contacts/ContactService.cs`)
- `const int ContactFileType = 100`; drive = `ContactDrive`, `FileSystemType.Standard`.
- `static Guid ToContactUniqueId(OdinId)` — **promote** the private `ToGuidId` from
  `HomebaseChannelContentService.cs:423` into a shared util; repoint both call sites.
- CRUD: `UpsertAsync`, `GetByOdinIdAsync`, `GetByUniqueIdAsync`, `GetByFileIdAsync`,
  `GetListAsync`, `DeleteBy{OdinId,UniqueId}Async` (`SoftDeleteLongTermFile`),
  `Get/SetImageAsync` (`prfl_pic` payload).
- **Read = shared-secret-encrypted, never plaintext.** Reuse the standard drive query that returns a
  `SharedSecretEncryptedFileHeader`: the per-file keyHeader is re-encrypted from the drive storage key to
  the caller's shared secret (`DriveFileUtility`), and `AppData.Content` stays AES-encrypted — the **client**
  decrypts, exactly as for any drive file today. The server decrypts content internally **only** when it
  must mutate it (enrichment/merge), using the drive storage key.
- **Write**: client-originated writes use the same shared-secret transfer-keyHeader mechanism as a drive
  upload (client encrypts content and sends the keyHeader encrypted with the shared secret), so the server
  never sees client plaintext. Server-originated writes (lifecycle/enrichment) generate a keyHeader →
  `keyHeader.EncryptDataAes(json)` base64, `IsEncrypted=true`, `CreateServerFileHeader` (wraps keyHeader with
  the drive storage key), `WriteNewFileHeader`/`UpdateActiveFileHeader`.
  `ServerMetadata = OwnerOnly`; `tags=[uniqueId]` (+ normalized email/phone) when present. Default-payload spill fallback.
- **Upsert/merge**: lookup via `GetFileByClientUniqueIdForWriting(ToContactUniqueId(odinId))`; if present
  preserve `fileId` and **merge** new content over existing (don't clobber user-entered fields); else create.
- **Concurrency (optimistic, `versionTag`).** A client upsert/PUT carries the `versionTag` it last read; the
  write asserts it still matches the stored file.
  - *Client write with a stale versionTag* → **409 Conflict** whose body carries the current `versionTag` and
    the current `SharedSecretEncryptedFileHeader`, so the client re-fetches, re-applies its edit, and retries.
    We never silently overwrite — the client's change may be based on stale data.
  - *Server-internal races* (a lifecycle handler and the enrichment job touching the same odinId-keyed file):
    re-read + re-merge and retry once; if it still conflicts, drop it — the enrichment job is idempotent and
    the reconcile pass converges the file later, so nothing is lost and nothing surfaces to a user.
- **Delete = soft-delete** (`SoftDeleteLongTermFile`; recoverable tombstone). **Caveat:** a contact whose
  identity is still connected/introduced/pending will be **re-created** by the lifecycle/reconcile pass (the
  relationship still exists), so a plain delete is meaningful only for contacts with no live relationship. To
  hide an *active* contact the user sets an **archived/hidden** flag (`archivalStatus`) that reconcile honors
  instead of resurrecting (recommended), or must disconnect/block first.
- **Image (`prfl_pic`) — shared-secret-encrypted like content.** The payload is AES-encrypted with the file
  keyHeader; `GetImageAsync` serves the encrypted payload stream with the `SharedSecretEncryptedKeyHeader`
  header (and encrypted preview thumbnail) so the **client decrypts** — same as any drive payload, never
  plaintext. Client `SetImageAsync` uploads the image encrypted via the shared-secret transfer-keyHeader;
  server-originated enrichment fetches the peer's `prfl_pic`, decrypts it internally, re-encrypts with the
  contact file's keyHeader, and stores it as payload + thumbnail.

### Relationship composition — `ContactRelationshipResolver` (`Odin.Services/Contacts/`)
- Single: compose from `CircleNetworkService.GetIcrAsync(odinId)` →
  `CircleNetworkRequestService.GetPendingRequestAsync` / `GetSentRequestAsync` →
  `CircleNetworkIntroductionService` introduction lookup. Priority: ICR(Connected/Blocked) > pending
  request > introduction > None. Origin/introducer/timestamps from whichever applies; note
  `IntroducerOdinId` survives on the ICR after the introduction record is deleted at finalize.
- Bulk (for list): load once — connected/blocked ICRs, `GetPendingRequestsAsync`, sent requests,
  `GetReceivedIntroductionsAsync` — into odinId→info maps, join in memory (avoids N+1).
- Needs `ReadConnections` + `ReadConnectionRequests` perms in context (owner has them; app must be granted).

### Controller — `src/apps/Odin.Hosting/UnifiedV2/Connections/V2ContactsController.cs`
Modeled on `V2ConnectionRequestsController` (`[Route(UnifiedApiRouteConstants.Contacts)]`,
`[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]`, `[ApiExplorerSettings(GroupName="v2")]`,
`: OdinControllerBase`, `[SwaggerOperation(Tags=[SwaggerInfo.Contacts])]`):

| Verb | Route | Returns |
|---|---|---|
| GET | `/` (`pageSize`, `cursor`, filters) | `ContactListResult` (stored contacts, relationship-annotated) |
| GET | `/{uniqueId:guid}` / `/by-odin-id/{odinId}` | `Contact` (content + live Relationship) / 404 |
| POST | `/` ; PUT `/{uniqueId:guid}` | `Contact` |
| DELETE | `/{uniqueId:guid}` ; `/by-odin-id/{odinId}` | 204 |
| GET/POST | `/{uniqueId:guid}/image` | encrypted image payload (shared-secret keyHeader) / 204 |
| POST | `/sync/{odinId}` | `Contact` — enrich from peer profile inline (Part B) |
| POST | `/{uniqueId:guid}/link` `{ odinId }` | `Contact` — attach odinId + re-key (Part C #5) |
| POST | `/merge` `{ sourceUniqueId, targetUniqueId }` | `Contact` — explicit merge, never automatic (Part C #4) |
| GET | `/duplicates` (or `/{uniqueId}/duplicate-suggestions`) | candidate dup pairs (Part C #3) |

Contact creation (`POST /`) accepts an optional `content.odinId`; when present the file is keyed on
`ToContactUniqueId(odinId)` immediately (Part C #1), so a later connection lands on the same file.

**List paging & filtering.** Cursor-paged (`pageSize` + opaque `cursor`), default newest-first. Filters:
`source` (contact/public/user), `hasOdinId`, `relationship` (`connected` | `introduced` | `pending` |
`blocked` | `all`), and optional `search` (name/email/phone prefix). Two strategies, because relationship is
**not** stored on the file:
- **Content filters** (`source`, `hasOdinId`, `search`) are applied in the drive query / on tags, so paging
  is over stored files and pages stay full.
- **`relationship` filters** are served by seeding from the authoritative source — `connected`/`blocked` from
  the connection registry, `introduced` from introductions, `pending` from requests — then joining to the
  contact files. This keeps pages full and avoids composing-then-discarding an entire page. `all` pages
  straight over stored files with the relationship annotated per row.

---

## Part B — Lifecycle: events keep contact files in step with relationships

### `ContactLifecycleService` (`Odin.Services/Contacts/`) — MediatR handlers
All these notifications are **awaited synchronously** on the connection hot path → handlers must be
**fast & local** (upsert a stub, never peer I/O inline). Each ensures the odinId-keyed contact file
exists and sets `source`:
- `INotificationHandler<IntroductionsReceivedNotification>` → for each introduced `Identity`, upsert
  stub `{ odinId, source: Public }`. (Notification carries `IntroducerOdinId` + identities.)
- `INotificationHandler<ConnectionRequestReceivedNotification>` → upsert stub for `Sender`
  `{ odinId, source: Public }`. (Origin/introducer are encrypted in the header but composed live later.)
- `INotificationHandler<ConnectionFinalizedNotification>` → upsert same file → `source: Contact`,
  then `IJobManager.ScheduleJobAsync(ContactEnrichmentJob{ Identity, TargetOdinId })` (`JobSchedule.Now`).
- `INotificationHandler<ConnectionDeletedNotification|ConnectionBlockedNotification>` → load by
  `ToContactUniqueId(odinId)`; if `source==Contact` flip to `Public`. Keep the file.
- **Outgoing request**: confirm whether a "request sent" notification exists; if not, upsert the stub
  in the V2 send path (`V2ConnectionRequestsController`/send service) — see risk below.

### Reconcile (backfill + self-heal) — `ContactReconcileJob` / `POST /sync`
A reconcile pass ensures a contact file exists for every current relationship: scan connected ICRs +
pending + sent requests + received introductions, upsert any missing stub with the right `source`.
Backfills contacts for relationships that predate this feature and heals missed events.

### Profile enrichment — `ContactEnrichmentService` + `ContactEnrichmentJob`
`EnrichFromConnectionAsync(OdinId, IOdinContext)`:
1. Peer-query the connection's `ProfileDrive` via `PeerDriveQueryService.GetBatchAsync`
   (`FileType=[77]`, `GroupId=[BuiltInAttribute GUIDs]`, `IncludeHeaderContent=true`). Needs
   `UseTransitRead` + an ICR (present once connected).
2. Map attribute `Data` → `ContactContent` (Name/Phone/Email/Address→Location/Birthday); port mapping
   from odin-js `queryRemoteAttributes`. Add C# `BuiltInAttributes` GUID constants (not in C# today; port from TS).
3. Fetch `prfl_pic` payload from the Photo attribute (`GetPayloadStreamAsync(...,"prfl_pic")`) → store
   as contact image payload + preview thumbnail.
4. `source=Contact`, odinId set → `ContactService.UpsertAsync` (merge).
`ContactEnrichmentJob : AbstractJob` (template: `ExportTenantJob`; register in `JobExtensions.cs`):
reconstructs an owner/system `IOdinContext` w/ `UseTransitRead` (mirror
`PeerOutboxProcessorBackgroundService.ProcessItemThread`), resolves the service from its own job scope
(safe for DB), calls `EnrichFromConnectionAsync`; idempotent via uniqueId upsert. `POST /sync/{odinId}`
calls the same method inline with the request's owner context.

---

## Part C — Manual contact (no odinId) → later connects

Every relationship-originated contact (introduction/request/connection) carries an odinId, so it is
keyed on `ToContactUniqueId(odinId)` and updates in place automatically. The only unsolved case is a
**hand-typed contact with no odinId** that later connects: there is no shared key, and the server has
no reliable way to know they're the same person (`OriginalContactData` is going away; name/email
matching is heuristic). **Guiding principle: auto-merge is never performed — a wrong merge is far
worse than a duplicate.** We address it in layers:

**#1 — Capture odinId at creation (prevent the problem).** `content.odinId` is a first-class optional
field on create; when the user knows the Homebase ID, the contact is keyed on `ToContactUniqueId(odinId)`
from the start and the whole problem collapses into the automatic path.

**#3 — Detect potential duplicates (suggest, never act).** `ContactDuplicateDetector`
(`Odin.Services/Contacts/`) finds candidate pairs between odinId-keyed contacts and no-odinId contacts
using exact normalized **email/phone** (high confidence) and fuzzy **name** (low confidence). To make
the scan cheap, `ContactService` stores normalized email/phone as additional **tags** on each contact
(tags already exist in `AppFileMetadata`) — used **only** for candidate lookup, never to auto-merge.
The enrichment job (which already fetches the peer's email/phone/name) is the natural place to compute
suggestions for a newly-connected identity. Surfaced via `GET /duplicates`; the user/UI decides.

**#4 — First-class merge operation.** `POST /contacts/merge { sourceUniqueId, targetUniqueId }`:
union content (prefer the user-entered/manual fields, take the odinId + relationship from the
connected one), keep the odinId-keyed file, soft-delete the other, re-point the image payload.
Duplicates are an accepted intermediate state; this is the clean resolution.

**#5 — Explicit link.** `POST /contacts/{uniqueId}/link { odinId }`: set `content.odinId` and **re-key**
the file's `uniqueId` to `ToContactUniqueId(odinId)` on the same `fileId`, so all future lifecycle
events land on it. If a contact already exists under that key, this routes into the `merge` path (#4).

(Explicitly **rejected**: auto-merging on email/phone — too dangerous; and a multi-identity contact
container — breaks odin-js's flat, odinId-keyed `ContactFile` compatibility.)

---

## Part D — Lock down direct writes to the ContactDrive

### The problem
Today the ContactDrive is a plain drive that **any client or app holding a Write grant can write to
directly** — the owner app, ChatApp and MailApp all have `ReadWrite` (`SystemAppConstants`), and the
owner additionally has `DrivePermission.All` on every drive via the master key. Writes go straight to
the drive storage layer with no awareness of what a "contact" is.

Everything this plan builds depends on the server being the single authority over contact data, and
direct writes defeat that:
- **No contract enforcement.** The dedupe key (`uniqueId = ToGuidId(odinId)`), `fileType=100`, the
  camelCase content shape, `source` semantics, and the encrypted-at-rest format are only guaranteed if
  writes go through `ContactService`. A client can write a malformed, mis-keyed, or plaintext file and
  the server cannot stop it.
- **Broken dedupe / duplicates.** Multiple independent writers (each client *plus* the new server-side
  lifecycle handlers and enrichment job) racing on the same contact produce duplicate or divergent
  files — exactly the "same file across the lifecycle" guarantee we just designed, silently undone.
- **Relationship integrity.** The server composes `Relationship` from the connection registry and keeps
  `source` in step via lifecycle events. A client writing `source`/odinId directly can contradict the
  authoritative connection state.
- **The new API is pointless if it can be bypassed.** CRUD, lifecycle, dedupe-detection, merge, and
  enrichment only hold if *all* writes funnel through them.

So the contact drive must become **service-managed**: writable only through the Contact API.

**Goal:** the Contact API becomes the *only* writer of the ContactDrive. Direct drive writes from
clients/apps (V1 + V2 upload, update, delete, payload) are rejected. **Read is left unchanged** for
backwards compatibility (legacy clients keep reading contacts directly).

### Why a permission-grant revoke is insufficient
Every write path funnels through `DriveStorageServiceBase.AssertCanWriteToDrive(driveId, ctx)` →
`PermissionContext.AssertCanWriteToDrive` (checks `HasDrivePermission(driveId, Write)`). But the
**owner holds `DrivePermission.All` on every drive via the master key**
(`OwnerAuthenticationService.GetPermissionContextAsync`), and `ContactService` runs under that same
owner context — so removing grants either fails to stop the owner or also blocks our own service.
The difference between "via Contact API" and "direct write" is the **call site**, not the permission.

### Candidate solution — declarative "service-managed" drive flag, enforced at the single chokepoint
This combines a **drive-level flag** (declarative, reusable) with the **one permission chokepoint** that
every write already passes through, plus a tightly-scoped bypass for `ContactService`.

1. **Declarative drive flag.** Add `IsServiceManaged` (alongside the existing `IsReadonly` /
   `OwnerOnly` flags on `StorageDrive` / `CreateDriveRequest`, or as a `BuiltInDriveAttributes` entry) and
   set it on `SystemDriveConstants.CreateContactDriveRequest`. The intent lives **with the drive
   definition**, so any drive (feed, profile, …) can become service-managed later with no new code —
   not a hardcoded list.
2. **Guard at the one chokepoint.** In `AssertCanWriteToDrive`, if the drive `IsServiceManaged` and the
   call is **not** an authorized managed write → throw a client error (see semantics below). This single
   assert is reached by create (`CommitNewFile`), update (`UpdateActiveFileHeaderInternal`), soft/hard
   delete, and payload writes — so it covers **all V1 and V2 client write entry points** at once.
3. **Authorized bypass for `ContactService` only** — the lone caller permitted to write a managed drive.
   Two implementation variants (recommend the first):
   - **Explicit flag** — add `bool allowManagedDriveWrite = false` to `AssertCanWriteToDrive` and thread
     it through the ~5 storage methods `ContactService` actually calls (`WriteNewFileHeader`,
     `UpdateActiveFileHeader`, `OverwriteMetadata`, `SoftDeleteLongTermFile`, payload set). All other
     callers keep the `false` default. Matches the existing `overrideHack` / `bypassCallerCheck` precedent.
   - **Request-scoped marker** — a scoped `ManagedDriveWriteGuard` that `ContactService` flips on inside a
     `using` block around its write; the assert consults it. Less signature churn, but ambient state.
4. **Read untouched.** Do not modify `AssertCanReadDrive`/read grants; `IsServiceManaged` gates *writes*
   only. Peer writes are already impossible (ContactDrive is `OwnerOnly` + not distributed).

### Secondary (defense-in-depth, not the enforcement)
Change `SystemAppConstants` ChatApp/MailApp ContactDrive grant from `ReadWrite` → `Read` for
**newly registered** apps (least privilege). Existing installed apps keep their persisted `ReadWrite`
grant, but the chokepoint blocks them anyway — so **no grant migration is required** and enforcement
does not depend on it.

### Error semantics
The guard throws `OdinClientException` with a dedicated `OdinClientErrorCode` (e.g.
`DirectContactDriveWriteNotAllowed`) → HTTP **403** with a message pointing clients to
`POST /api/v2/contacts`. (A bare `OdinSecurityException` would read as an auth failure; we want a clear,
actionable code.)

### Rollout — this is a BREAKING change for current odin-js
Existing odin-js writes contacts directly to the drive, so the lockdown must be sequenced:
1. Ship the Contact API (Parts A–C).
2. Migrate odin-js to write via `/api/v2/contacts`.
3. **Gate the lockdown behind a `TenantConfigService` flag** (e.g. `ContactDriveWriteLockdown`),
   default **off**; flip on per-tenant once clients have migrated; make it the default in a later release.
This lets read-compat and the new write path coexist during migration with no hard cutover.

## Wiring (edits)
- `UnifiedV2/UnifiedApiRouteConstants.cs` → `Contacts = BasePath + "/contacts"`.
- `UnifiedV2/SwaggerInfo.cs` → `Contacts = "Contact Operations"`.
- `TenantServices.cs` (~L277): register `ContactService`, `ContactRelationshipResolver`,
  `ContactEnrichmentService` (`InstancePerLifetimeScope`), and `ContactLifecycleService`
  `.As<INotificationHandler<IntroductionsReceivedNotification>>().As<…ConnectionRequestReceived…>()
  .As<…ConnectionFinalized…>().As<…ConnectionDeleted…>().As<…ConnectionBlocked…>()`.
- `JobExtensions.cs` → register `ContactEnrichmentJob` (+ `ContactReconcileJob` if used).
- Promote `ToGuidId` → shared util; repoint `HomebaseChannelContentService.cs:423`.
- Part D: `IsServiceManaged` drive flag on `StorageDrive`/`CreateDriveRequest` (set on
  `CreateContactDriveRequest`); guard in `DriveStorageServiceBase.AssertCanWriteToDrive` (+ bypass plumbing
  on the storage methods `ContactService` uses); new `OdinClientErrorCode`; optional
  `TenantConfigService.ContactDriveWriteLockdown` flag; `SystemAppConstants` ChatApp/MailApp grant `ReadWrite`→`Read`.

## Reused existing code
`PeerDriveQueryService.GetBatchAsync`/`GetPayloadStreamAsync`; `HomebaseProfileContentService`
(attribute query shape, `AttributeFileType=77`, `ProfileBlock` parse); `CircleNetworkService.GetIcrAsync`
+ `RedactedIdentityConnectionRegistration` (status/origin/introducer); `CircleNetworkRequestService`
pending/sent getters; `CircleNetworkIntroductionService.GetReceivedIntroductionsAsync`; `IJobManager`/
`AbstractJob` (`ExportTenantJob`); `PeerOutboxProcessorBackgroundService` (detached owner context);
`KeyHeader`/`DriveStorageServiceBase` encrypt pattern (`FeedWriter`, `ShardRequestApprovalCollector`);
`DriveFileUtility` + `SharedSecretEncryptedFileHeader` (the shared-secret keyHeader re-encryption used
by every drive read — the Contact API returns contacts in this exact shape).

## Verification
- **Unit**: `ToContactUniqueId` parity vector vs odin-js `toGuidId`; a contact written via the API is
  returned as a `SharedSecretEncryptedFileHeader` and **decrypts client-side** with the shared secret
  (assert the server never emits plaintext content); an odin-js-written encrypted contact is returned in
  that same shape and decodes (camelCase); relationship composition priority (ICR > request > introduction).
- **Integration** (`_Universal`, two identities, peer flow per CLAUDE.md):
  - Frodo introduced to Sam → a `source=public`, `Relationship.State=Introduced`,
    `ViaIntroduction=true`, `IntroducerOdinId=<introducer>` contact appears.
  - Request pending → same file, `State=RequestIncoming/Outgoing`.
  - Connect → **same file** flips `source=contact`, `State=Connected`, `Origin=Introduction` retained
    via ICR; enrichment job fills name/photo. Assert no duplicate file.
  - Disconnect → file kept, `source=public`, `State=None/Blocked`.
  - Create with `content.odinId` set → keyed on `ToGuidId(odinId)`; later connect updates same file (#1).
  - Manual no-odinId contact + `POST /link` → re-keyed to `ToGuidId(odinId)` (routes to merge on collision).
  - Two contacts for the same person → `GET /duplicates` surfaces the pair (exact email/phone, fuzzy name);
    `POST /merge` collapses them; assert **no auto-merge** ever fires on its own.
  - List with mixed states returns all, correctly annotated.
  - Filtered list: `relationship=connected` returns only connected contacts (seeded from the registry);
    `source`/`search` filters page over stored files with full pages and a working cursor.
  - Concurrent client PUTs with a stale `versionTag` → the loser gets **409** carrying the current
    `versionTag`/header, re-fetches and succeeds; assert no lost update.
  - Soft-delete a still-connected contact → reconcile/lifecycle **recreates** it; setting `archivalStatus`
    (archive) hides it and reconcile leaves it archived rather than resurrecting.
  - Contact image round-trips encrypted: `GET image` returns an encrypted payload + shared-secret keyHeader
    that decrypts client-side; an enrichment-fetched `prfl_pic` is re-encrypted under the contact's keyHeader.
- **Part D write lockdown** (with the flag on): direct V1 **and** V2 create/update/delete/payload to the
  ContactDrive → 403 `DirectContactDriveWriteNotAllowed`; the same operations via `/api/v2/contacts`
  succeed; **reads of the ContactDrive still succeed** (owner + app with read grant); flag off → direct
  writes still work (compat). Confirm `ContactService` writes pass the guard.
- SQLite + PostgreSQL: `dotnet test ./odin-core.sln`.

## Open implementation risks (resolve during build)
- Outgoing-request materialization: confirm a "request sent" notification exists; if not, upsert the
  stub in the send path or rely on `ContactReconcileJob`/`/sync`.
- Enrichment job must reconstruct an owner context with `UseTransitRead`/ICR access detached from a
  request — mirror `PeerOutboxProcessorBackgroundService`.
- Re-key uniqueId via `OverwriteMetadata` on an existing fileId — confirm clientUniqueId can change and
  handle the collision/merge case.
- `BuiltInAttributes` GUIDs only in TS today → port to C#. Pin DTO camelCase explicitly.
- List relationship composition must use bulk-loaded maps (no per-contact connection queries).
- Archive vs. resurrection: confirm `archivalStatus` (or an equivalent flag) is queryable and that the
  reconcile/lifecycle pass checks it before re-creating a soft-deleted contact for a live relationship.
- Relationship-filtered paging seeds from the registry/introductions/requests and joins to files — confirm a
  stable cursor across that join (vs. the plain drive-query cursor used for content filters).
- Part D: confirm exactly which `DriveStorageServiceBase` methods call `AssertCanWriteToDrive` (e.g.
  `WriteNewFileHeader` may not) so the bypass is plumbed only where `ContactService` actually hits it;
  ensure the guard covers `OverwriteMetadata` used by the Part C re-key.
