# Server-side Contact API + full connection/introduction lifecycle

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
API response: `Contact { FileId, UniqueId, VersionTag, Created, Updated, HasImage, ContactContent Content, ContactRelationship Relationship }`.
Inputs: `UpsertContactRequest { Content, VersionTag? }`, `ContactListResult { Results, Cursor }`.

### `ContactService` (`Odin.Services/Contacts/ContactService.cs`)
- `const int ContactFileType = 100`; drive = `ContactDrive`, `FileSystemType.Standard`.
- `static Guid ToContactUniqueId(OdinId)` — **promote** the private `ToGuidId` from
  `HomebaseChannelContentService.cs:423` into a shared util; repoint both call sites.
- CRUD: `UpsertAsync`, `GetByOdinIdAsync`, `GetByUniqueIdAsync`, `GetByFileIdAsync`,
  `GetListAsync`, `DeleteBy{OdinId,UniqueId}Async` (`SoftDeleteLongTermFile`),
  `Get/SetImageAsync` (`prfl_pic` payload).
- Encrypt-on-write / decrypt-on-read (verified): write → `keyHeader.EncryptDataAes(json)` base64,
  `IsEncrypted=true`, `CreateServerFileHeader` (encrypts keyHeader w/ drive storage key),
  `WriteNewFileHeader`/`UpdateActiveFileHeader`; read → `GetDriveStorageKey` →
  `EncryptedKeyHeader.DecryptAesToKeyHeader` → `keyHeader.Decrypt(Content.FromBase64())`.
  `ServerMetadata = OwnerOnly`; `tags=[uniqueId]` when odinId present. Default-payload spill fallback.
- **Upsert/merge**: lookup via `GetFileByClientUniqueIdForWriting(ToContactUniqueId(odinId))`; if
  present preserve `fileId`/`versionTag` and **merge** new content over existing (don't clobber
  user-entered fields); else create. Retry once on versionTag conflict (client may write same uniqueId).

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
| GET | `/` (`pageSize`,`cursor`) | `ContactListResult` (every stored contact, relationship-annotated) |
| GET | `/{uniqueId:guid}` / `/by-odin-id/{odinId}` | `Contact` (content + live Relationship) / 404 |
| POST | `/` ; PUT `/{uniqueId:guid}` | `Contact` |
| DELETE | `/{uniqueId:guid}` ; `/by-odin-id/{odinId}` | 204 |
| GET/POST | `/{uniqueId:guid}/image` | image / 204 |
| POST | `/sync/{odinId}` | `Contact` — enrich from peer profile inline (Part B) |
| POST | `/{uniqueId:guid}/link` `{ odinId }` | `Contact` — attach odinId + re-key (Part C #5) |
| POST | `/merge` `{ sourceUniqueId, targetUniqueId }` | `Contact` — explicit merge, never automatic (Part C #4) |
| GET | `/duplicates` (or `/{uniqueId}/duplicate-suggestions`) | candidate dup pairs (Part C #3) |

Contact creation (`POST /`) accepts an optional `content.odinId`; when present the file is keyed on
`ToContactUniqueId(odinId)` immediately (Part C #1), so a later connection lands on the same file.

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

## Wiring (edits)
- `UnifiedV2/UnifiedApiRouteConstants.cs` → `Contacts = BasePath + "/contacts"`.
- `UnifiedV2/SwaggerInfo.cs` → `Contacts = "Contact Operations"`.
- `TenantServices.cs` (~L277): register `ContactService`, `ContactRelationshipResolver`,
  `ContactEnrichmentService` (`InstancePerLifetimeScope`), and `ContactLifecycleService`
  `.As<INotificationHandler<IntroductionsReceivedNotification>>().As<…ConnectionRequestReceived…>()
  .As<…ConnectionFinalized…>().As<…ConnectionDeleted…>().As<…ConnectionBlocked…>()`.
- `JobExtensions.cs` → register `ContactEnrichmentJob` (+ `ContactReconcileJob` if used).
- Promote `ToGuidId` → shared util; repoint `HomebaseChannelContentService.cs:423`.

## Reused existing code
`PeerDriveQueryService.GetBatchAsync`/`GetPayloadStreamAsync`; `HomebaseProfileContentService`
(attribute query shape, `AttributeFileType=77`, `ProfileBlock` parse); `CircleNetworkService.GetIcrAsync`
+ `RedactedIdentityConnectionRegistration` (status/origin/introducer); `CircleNetworkRequestService`
pending/sent getters; `CircleNetworkIntroductionService.GetReceivedIntroductionsAsync`; `IJobManager`/
`AbstractJob` (`ExportTenantJob`); `PeerOutboxProcessorBackgroundService` (detached owner context);
`KeyHeader`/`DriveStorageServiceBase` encrypt pattern (`FeedWriter`, `ShardRequestApprovalCollector`).

## Verification
- **Unit**: `ToContactUniqueId` parity vector vs odin-js `toGuidId`; CRUD encrypt/decrypt round-trip;
  odin-js-written encrypted contact decodes (camelCase); relationship composition priority
  (ICR > request > introduction).
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
