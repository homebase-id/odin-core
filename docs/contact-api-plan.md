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
- **Same file across the lifecycle.** When I already have a contact and they later connect, the
  *same* file is updated with the odinId — not a duplicate.
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

### Data structures — new `src/services/Odin.Services/Contacts/`
On-drive content (serialized **camelCase**, matches odin-js `ContactFile`):
`ContactContent { OdinId?, Source, Name?, Location?, Phone?, Email?, Birthday? }` + sub-objects;
`enum ContactSource { Contact, Public, User }` (lowercase).

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
| POST | `/{uniqueId:guid}/link` `{ odinId }` | `Contact` — attach odinId + re-key/merge (Part C) |

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

`POST /api/v2/contacts/{uniqueId}/link { odinId }` (and PUT setting `content.odinId` routes here):
- Set `content.odinId`, then **re-key** the file's `uniqueId` to `ToContactUniqueId(odinId)` (overwrite
  metadata on the same `fileId`) so future lifecycle events update this same file.
- If a contact already exists under `ToContactUniqueId(odinId)` (e.g. a stub from a prior introduction),
  **merge**: prefer the manual file's user-entered fields, copy over the stub's odinId/source, delete
  the duplicate. Trigger enrichment if connected.
This is the only path requiring explicit linking — every relationship-originated contact is already
odinId-keyed and merges automatically.

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
  - Manual no-odinId contact + `POST /link` → re-keyed to `ToGuidId(odinId)`, merges any stub.
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
