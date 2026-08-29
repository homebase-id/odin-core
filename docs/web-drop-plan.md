# WebDrop, and a generic file TTL

## Status

| Piece | State |
|---|---|
| **Part 1 — generic file TTL** (`FileMetadata.Ttl`, expiry/reap jobs, peer transit, cache clamp) | **Implemented** — PR #1692 |
| Part 2 — `BlockAnonymousEnumeration` (non-enumerable drives) | **Not implemented** |
| Part 2 — `WebDropDrive` system drive + migration | **Not implemented** |
| Viewer web app | UX prototype only, in odin-js (`packages/apps/web-drop-app`) — no decryption, no drive; see *Viewer UX* |
| Writer — chat-kmp WebDrop add-on (Home-tile app, no toolbar icon) | **Implemented** on chat-kmp branch `webdrop-app`: multi-file drops, owner-encrypted receipts, Waiting/Opened/Removed list, revoke. Drive bootstraps via extend-permissions `allowAnonymousRead` |

Two things, in this order: a **generic per-file TTL** that must serve chat retention and
Snapchat-style messages as well as web drops, and **WebDrop** as its first consumer.

## Context

You need to send a credit-card number to a hotel, or a passport scan to someone who asks for it. The
recipient does not have Homebase and never will, so today it goes by email — it lands in a random
employee's mailbox, on a machine nobody has audited, and it stays there forever. Splitting the card
number and the CVC across two mails is better and still hopeless.

So the thing we hand them has to be a plain URL that opens in a plain browser, and it must not leave
a durable readable copy anywhere — including on the sender's own identity server.

The expiry half of that turns out to be a platform capability we want anyway, so it leads.

---

# Part 1 — Generic file TTL

## Requirements

| Use case | TTL |
|---|---|
| WebDrop, burn | a duration — on first read, `Ttl = now() - Ttl` (20 min) |
| WebDrop, plain | an absolute time, ≤ 30 days out |
| Chat group retention | an absolute time — `now + 90d` resolved at send, so every copy dies together |
| Snapchat-style message | a duration — each recipient's copy resolves `Ttl = now() - Ttl` on *its own* first read |

Two consequences: it must **cross peer**, and the **client must set it at item create**.

## The encoding — one field, three behaviours

`FileMetadata.Ttl` — a raw `long` in **milliseconds** (absent → `0`). Not typed `UnixTimeUtc`: the
negative branch is a duration, not a point in time. A 20-minute burn is `-1_200_000`. It lives on
`FileMetadata` because that crosses peer and `ServerMetadata` does not — see the appendix.

| Value | Meaning |
|---|---|
| `0` / absent | never expires — today's behaviour for every existing file |
| `> 0` | absolute `UnixTimeUtc` at which the file dies |
| `< 0` | a **duration**: on this copy's first payload read, `Ttl = now() - Ttl` (negative, so that is `now() + \|Ttl\|`), and the file dies then |

The `< 0` branch is a one-way door: the field starts as a duration and rewrites itself into an
absolute timestamp the first time anyone reads the payload. After that it is indistinguishable from a
`> 0` file, so every later read and the delete job take exactly one code path.

Give writers helpers — `Ttl.After(TimeSpan)`, `Ttl.AfterFirstRead(TimeSpan)` — so no caller types a
raw number.

`< 0` must resolve on the first **payload** read, not the header read. Mail clients, chat apps and
security scanners prefetch links; the payload URL is not in the message and needs the app's JS to
construct, so ordinary scanners will not start the clock. A header prefetch would.

**Starting the clock is opt-in, and only the caller-facing payload endpoint opts in.** Plenty of
internal callers stream a payload for their own reasons - packaging it for a peer transfer, publishing
static content, reading a contact photo - and none of those is a reader opening the file. Left
implicit, sending a file to a recipient burns the *sender's* own copy. A CDN edge read does count,
and needs no special handling: the CDN fetches through the same `/api/v2/drives/.../payload/...`
endpoint, so reading via the CDN means exactly what reading directly means.

Two consequences of that, both load-bearing:

- **Cache for the life the file has left, including a pending (`< 0`) one.** Serving that payload is
  what starts its clock, so at the moment the response goes out the remaining life is `|Ttl|` - not
  the unread backstop. Cache for exactly that and the edge copy dies when the file does. (Watch the
  ordering: the controller fetched its header *before* the payload read resolved the Ttl, so the
  value it holds is still negative.)
- **A file with a Ttl must never be published into a static blob**
  (`StaticFileContentService.Filter`). The blob is permanent, so publishing one copies the content
  somewhere expiry cannot reach.

Open question: **thumbnail reads do not start the clock**, deliberately - `thumb.jpg` exists for
link-preview support in Signal/WhatsApp, so counting it would burn a file on a preview fetch. The
consequence is that a thumbnail is a preview no clock governs, which is why a WebDrop carries none.
Whether a pending-Ttl file should instead *refuse* thumbnails is undecided.

## Where the job is scheduled — one choke point covers peer for free

`DriveStorageServiceBase.CommitNewFile:734` is where **every** write converges: local upload
(`StandardFileStreamWriter:62`, `CommentStreamWriter:73`, `FileSystemUpdateWriterBase:285`), peer
receive (`PeerFileWriter:263`, `PeerFileUpdateWriter:76`) and profile writes (`ProfileAttributeService:251`).

Schedule a `SoftDeleteExpiredFileJob : AbstractJob` there whenever `Ttl != 0`. Because a peer-received
file lands through the same call, **each identity schedules the deletion of its own copy
automatically** — no retention message, no extra transport, no fan-out. That is what makes 90-day
group retention work across a group of independent servers.

Job plumbing is all existing: `JobSchedule { RunAt = … }` (pattern:
`ScheduledNotificationService.cs:98`), registered in `JobManagement/JobTypeRegistry.cs`, moved by
`JobManager.RescheduleJobAsync` when a `< 0` TTL resolves.

## Storage — no column, no migration

`FileMetadata` is persisted as a `FileMetadataDto` into the existing `hdrFileMetaData` TEXT column
(`ServerFileHeader.cs:103`, read back at `:47`; `AppData` goes separately to `hdrAppData` at `:81`).
So `Ttl` is added to `FileMetadata` **and** `FileMetadataDto`, and needs **no ALTER TABLE**. Absent in
existing rows it deserializes to `0` = never expires — the status quo.

**No index, deliberately.** Nothing scans for expired files: the job row *is* the schedule. `Ttl` is
read only on paths that already load the header, to refuse a file whose job has not fired yet — jobs
lag, and a file must never outlive its stated life. The cost is that "list everything that expires
and when" becomes a full scan, needing its own index if an admin view is ever wanted; still a far
better trade than an indexed column on the hottest table in the system.

## Rules

- **Client sets it at create** — `UploadFileMetadata.Ttl` through `MapUploadToMetadata` in
  `FileSystemStreamWriterBase`.
- **On update, `Ttl` may only shorten.** Mirrors the `IsEncrypted` immutability guard at
  `DriveStorageServiceBase.cs:1955`, and stops a routine file update from silently resurrecting a
  message.
- **First-read resolution must be set-only-if-still-negative**, so two parallel GETs cannot race.
- **The resolving write advances `hdrVersionTag`, and cannot avoid it**: `TableDriveMainIndex.cs:71`
  rejects an upsert that reuses the existing tag outright. That is defensible - the file really did
  change state - but it means a client holding the pre-read version tag loses an optimistic-concurrency
  update it attempts afterwards.

## Deletion is a soft delete, and tombstones need reaping

The job **soft-deletes**. A hard delete removes the row outright, so a client polling
`query-modified` never learns the file is gone and goes on showing a stale copy — which defeats
retention. `WriteDeletedFileHeader:1795` instead writes a tombstone: `FileState.Deleted`, the payload
files removed, and `Content`, `PreviewThumbnail`, `Payloads` and **`UniqueId`** all cleared, while
`GlobalTransitId`, `SenderOdinId`, timestamps, tags/fileType/groupId and `ServerMetadata` survive —
enough for a chat client to reconcile.

Nulling `UniqueId` is a free win for WebDrop: a drop is addressed *by* its uniqueId, so the by-uid
lookup 404s the moment it expires, with no special-casing anywhere.

**But tombstones are permanent, and nothing reaps them today** — there is no cleanup of
`FileState.Deleted` rows anywhere in the codebase. For 90-day retention on a busy group that is one
index row per message, forever. So `Ttl` should schedule **two** jobs: soft delete at `Ttl`, then hard
delete at `Ttl + grace`, with grace long enough that every client has certainly synced the tombstone
(30 days, configurable). WebDrop's volume makes this irrelevant; chat's makes it the dominant
long-run cost.

*(Implementation note: `SoftDeleteLongTermFile:695` calls `AssertCanWriteToDrive`, so the job needs a
context with write on the drive — `OdinContextUpgrades.UpgradeToByPassAclCheck` is the existing
mechanism.)*

## Cooperative retention

Once a file crosses to another identity, **retention is cooperative**. The recipient owns their copy
and their server; nothing stops them updating it to clear the TTL, any more than Snapchat stops a
screenshot. The guarantee is "every honest server deletes on time", not "the content is
unrecoverable".

## A generic bug this fixes on the way

`OdinControllerBase.AddGuestApiCacheHeader:76` stamps `Cache-Control: max-age=31536000` — a year — on
anonymous payload responses. Any expiring or revocable file inherits it, so a dead file survives in
browser and edge caches long after deletion. Clamp the max-age to the remaining lifetime when
`Ttl != 0`.

**And send it to the CDN too.** The header was gated to YouAuth/App callers only, so a CDN response
carried no `Cache-Control` at all — the edge that caches on behalf of every downstream reader was the
one caller getting no instruction. `ClientTokenType.Cdn` now gets the same clamped max-age, which is
what makes "reading via the CDN means the same as reading directly" true of the *cached* copy and not
just the first fetch.

*(Implementation note: the v2 anonymous ticket is issued under `YouAuthConstants.YouAuthScheme`, so
`isYouAuthV1` appears to be true for v2 anonymous callers too — confirm which branch actually fires
before changing it.)*

---

# Part 2 — WebDrop

## What it is

The file sits on a new `web-drop` drive, unencrypted as far as odin-core is concerned, but its
payload bytes are encrypted client-side under a random key carried in the URL fragment. The server
never sees the key. Every drop expires.

## No new WebDrop API

The reader uses the **existing generic V2 endpoints**, unchanged:

```
GET /api/v2/drives/{driveId}/files/by-uid/{dropId}/header
GET /api/v2/drives/{driveId}/files/by-uid/{dropId}/payload/wdr_data
```

Both are already `[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]` with no token required
(`V2DriveFileReadonlyByUidController.cs:21`), already `[NoSharedSecretOnRequest]` /
`[NoSharedSecretOnResponse]`, already ranged, and already set `Access-Control-Allow-Origin: *` for
anonymous callers (`:122`). The header call returns `appData.content` (`IncludeHeaderContent = true`,
`:105`) and the payload descriptors — everything the viewer needs. `driveId` is a constant the viewer
hardcodes, exactly as every app hardcodes its `TargetDrive`.

**No client write either** — the `< 0` TTL resolves server-side. The reader is strictly read-only,
which is what keeps it on existing endpoints.

## Capability needed: drives that are readable but not enumerable *(not yet implemented)*

Today `AllowAnonymousReads = true` means three things at once: a stranger may **list** the drive,
**query** its files, and **read** a file. Capability-URL sharing wants only the third.

Verified: an anonymous `POST /api/v2/drives/{driveId}/files/query-batch` returns every file on any
anonymous drive, and `CircleNetworkService.HandleDriveAdded:1442` additionally auto-grants Read on it
to both system circles, so every connected identity can browse it too.

### Storage — no table change

Drive flags live in `StorageDriveDetails`, serialized whole into `DrivesRecord.detailsJson`, a
`TEXT NOT NULL` column (`TableDrivesCRUD.cs:33`). Written at `DriveManager.cs:138`, read back at
`:534`. No migration, no ALTER TABLE.

### Invert the flag, or every existing drive breaks

Confirmed by an existing test: `AllowCdnTests.DriveStoredBeforeTheFlagExistedIsNotCdnEnabled`
deserializes a pre-flag `detailsJson` and asserts the new bool comes back `false`. A row written
before a flag existed simply has no such key.

So an `AllowAnonymousEnumeration` defaulting to `false` would silently make **every existing drive
non-enumerable** — public posts, profile, homepage — with no migration touching them.

**Invert it: `BlockAnonymousEnumeration`, absent → `false` → today's behaviour.** Only the web-drop
drive sets it `true`. The general rule: *a flag added to `detailsJson` must be polarised so absent
means status quo* — `AllowCdn` is opt-in-to-grant for that reason, this one opt-in-to-restrict.

### Enforcement

| Where | Change |
|---|---|
| `Management/StorageDriveDetails.cs`, `Drives/StorageDrive.cs`, `Management/CreateDriveRequest.cs` | the new bool + the `DriveManager.cs:138`/`:534` round trip |
| `FileSystem/Base/DriveQueryServiceBase.cs` | new `AssertCanEnumerateDriveAsync`; call from `GetBatch:100`, `GetModified:75`, `GetSmartBatch:127`, `GetTemporalBatch:109`. `GetBatchCollection:245` gates on `HasDrivePermission` then calls `GetBatch`, so it inherits it |
| — direct-address reads untouched | `GetFileByClientUniqueId:186`, by-gtid, and header/payload by fileId keep only `AssertCanReadDriveAsync` |
| `Management/DriveManager.cs` | anonymous listing predicates at `:375`, `:390` exclude blocked drives, so the drive itself is not listed to strangers |
| `Membership/Connections/CircleNetworkService.cs:1442` | `HandleDriveAdded` skips blocked drives — browsing access is exactly what they must not grant |

Rule: `if (caller.IsAnonymous && drive.BlockAnonymousEnumeration) throw OdinSecurityException`.

Placing it in the query service rather than a controller is deliberate: it covers the V1 guest routes
and V2 at once, and cannot be bypassed by a future endpoint.

## URL contract

```
https://michael.id/apps/web-drop/d/<dropId>#<key>
                                     22 chars  22 chars
```

- `dropId` — base64url of `appData.uniqueId` (random 16 bytes). Path; the server sees it.
- `key` — base64url of the AES-128 key (random 16 bytes). **Fragment; never transmitted.**

IVs are not secret and stay out of the URL — they live in the file's cleartext `appData.content`
along with the format version, so the fragment is pure key material.

## File layout

One file per drop, on the web-drop drive.

| Field | Value |
|---|---|
| `appData.uniqueId` | random 16-byte GUID = dropId |
| `appData.content` | `{"v":1,"ivs":{"wdr_meta":"<b64>","wdr_data":"<b64>"}}` |
| `appData.fileType` | one constant for every drop |
| `ACL` | `SecurityGroupType.Anonymous` |
| `IsEncrypted` | `false` |
| `FileMetadata.Ttl` | absolute, ≤ 30 days out — or `-1_200_000` (20 min after first read) to burn |
| payload `wdr_meta` | `E_k(JSON [{key,name,contentType,size},…])` — a manifest **array**: drops are multi-file |
| payloads `wdr_dat1..N` | `E_k(bytes)` each, own IV; N ≤ 24 (manifest takes the 25th slot of the server's per-file payload cap) |
| thumbnails, preview thumbnail | **none** |

The countdown UI reads `Ttl` straight off the client header — one value, one truth.

Constraints, all verified:

- Payload keys must match `^[a-z0-9_]{8,10}$` (`TenantPathManager.cs:40`); `wdr_meta` / `wdr_data` fit.
- Every manifest payload IV must be all-zero when `IsEncrypted = false`
  (`FileSystemStreamWriterBase.cs:547`) — which is why the real IVs live in `appData.content`.
- A preview thumbnail is stored in the clear. An unencrypted preview of a passport scan is a readable
  passport scan. Set none.
- `appData.content` is cleartext but reader-untamperable — strangers have no write access.

## Drive definition

**Decided: not a system drive.** The chat-kmp app requests the drive through the extend-permissions
flow with `allowAnonymousRead = true` (the YouAuth drive-request `r` param — WebDrop is its first
user), and the owner console creates it on consent, exactly as `WellKnownAppDrives.cs` documents for
the Email drive. Alias `6d1711af-8b93-43ef-b798-b84d51f25828`, type
`edee430a-73d4-49ae-a9ae-2d3091957702` (chat-kmp `AppConfig.webDropLabeledDrive`); mirror them in
`WellKnownAppDrives.cs` when server-side authorization needs to name the drive.

Still owed here *(not yet implemented)*: `BlockAnonymousEnumeration = true` on this drive — until
that capability exists, drops are enumerable by anyone ("boring bit last"). When it lands, the
extend-permissions drive request needs to carry it too.

**The owner's own list is free.** The owner has full read on the drive, so drive sync returns the
drop file with its resolved `Ttl` and — because expiry soft-deletes — its tombstone. The writer
derives per-drop status from that alone: pending negative `Ttl` → *Waiting*; positive on a drop the
receipt says started as burn → *Opened, dies at Ttl*; positive on a fixed-lifetime drop →
*Expiring*; tombstone or gone → *Removed*. An owner-only **encrypted receipt** file (same drive,
`groupId` = dropId) carries what the anonymous drop must not: filenames and the full link,
fragment key included, so the owner can re-copy it. Revoke deletes the drop file only — the receipt
stays to show *Removed* until the user clears it.

## On giving the viewer an app token

**Don't.** A token in a public JS bundle is not a secret, so it buys nothing against the reader it
would guard against — and granting a publicly-extractable token *Write* on the drive would be worse
than anonymous read-only, since anyone could then write files to it. The only thing a token was
needed for was a client-side write, and the `< 0` TTL encoding removes that. Keep the viewer
anonymous and read-only.

## Viewer UX

The page a stranger lands on when they click a WebDrop URL. Tone: Mission Impossible — dark ground,
signal red, monospace, scanlines. It is a cool feature and should feel like one.

1. **Intro.** Homebase logo, "WEBDROP" header, and the sender (the URL host):

   > *biggus.dickus has sent you a WebDrop. It will self-destruct when you open it.*

   Below it, a required consent checkbox — *"This drop is for me alone. I agree to respect the
   confidentiality and privacy of the sender and will not share its contents."* — and a large
   **OPEN DROP** button, disabled until checked. The intro fetches only the **header**: a header
   read deliberately does not start the TTL clock, so the page can show payload count and sizes
   without burning anything. Prefetching mail scanners land here and cost nothing.

2. **Open.** The click fetches the payloads — the first payload fetch is what starts the
   server-side clock — then re-reads the header for the resolved absolute `Ttl` and starts the
   countdown against it: big mono digits, a burning-fuse bar, green→amber→red,
   *"This drop will self-destruct in 19:42"*. Each payload gets a download icon, name, type and
   size; downloads are object URLs from the bytes already fetched, no second round trip.

3. **Destructed.** At zero — or on a 404 at any point (expired, burned, never existed; the server
   deliberately does not say which) — a glitch screen: **THIS DROP HAS SELF-DESTRUCTED.** Object
   URLs revoked, nothing left clickable.

The prototype in odin-js implements exactly this against the V2 by-uid endpoints with a
`/d/{driveId}/{dropId}` URL (drive id in the URL until the system drive exists) and a `/d/demo`
mode that runs on mocked data. Decryption is not in the prototype; when it lands, only the
data-source layer changes — the fragment key decrypts `wdr_meta`/`wdr_data` after fetch, and the
screens stay as they are.

## The viewer app

A standalone Vite app, so nothing from `common-app` and no auth code loads for a stranger.

**odin-js** — `packages/apps/web-drop-app/`, modelled on `feed-app` (the smallest React app that
ships to odin-core; `login-app` is smaller but deploys to Netlify).

- `vite.config.ts` with `base: '/apps/web-drop'`, dev port **3007** (3000–3006 are taken)
- `index.html`: `<base href="/apps/web-drop/" />`, a static `og:title`, no description, and
  `<meta name="robots" content="noindex,nofollow">`
- one route, `/d/:id`; the key is read from `window.location.hash` and **never** put into router
  state, a query string, or any log
- plain `fetch` — these endpoints are not shared-secret wrapped, so no `DotYouClient` is needed;
  import `cbcDecrypt` / `base64ToUint8Array` from `@homebase-id/js-lib`
- fetch the header first; for a burn drop render a **Reveal** button and fetch the payload only on
  the click — the payload fetch is what starts the clock
- decrypt `wdr_meta` → filename/MIME, then `wdr_data`; render images, PDFs and text inline, otherwise
  offer a download from an object URL; JS countdown driven by `Ttl`
- register in root `package.json` (`build:web-drop`, `start:web-drop`) and root `tsconfig.json`

js-lib ships **AES-CBC only** (`helpers/AesEncrypt.ts`: `cbcEncrypt`, `cbcDecrypt`,
`streamEncryptWithCbc` / `streamDecryptWithCbc`). There is no AES-GCM in js-lib. Use CBC, and the
streaming variants above a few MB.

**odin-core** — mount it:

- `src/apps/Odin.Hosting/client/apps/web-drop/.gitkeep` — the `client\**\*` glob at
  `Odin.Hosting.csproj:45` copies it to output, so no csproj change is needed
- `Startup.cs`: a `MapWhen` on `/apps/web-drop` in **both** the dev branch (proxy to
  `https://dev.dotyou.cloud:3007/`) and the prod branch, registered **before** the non-`/api/`
  catch-all at `:377` or it will be swallowed
- use `SpaFallback.ServeShellOrNotFound`, not the always-`index.html` `Run` handler that `/apps/mail`
  uses (`:351`) — `SpaFallback.cs:8-28` records the multi-day debugging incident that pattern caused
- `.github/actions/host/build-frontend/action.yml`: add the build + `mv dist` step alongside the
  existing apps

---

## Change list

**TTL (Part 1)**

| File | Change |
|---|---|
| `DriveCore/Storage/FileMetadata.cs` + `FileMetadataDto.cs`, `Upload/UploadFileMetadata.cs`, `Upload/FileSystemStreamWriterBase.cs` | `Ttl` field + `MapUploadToMetadata`; shorten-only on update |
| `Apps/ClientFileMetadata.cs`, `FileSystem/Base/DriveFileUtility.cs` | add `Ttl` to the client DTO and to `RedactFileMetadata:131` |
| `FileSystem/Base/DriveStorageServiceBase.cs` | schedule the job in `CommitNewFile:734`; refuse an expired file on read; resolve a negative `Ttl` on first payload read and reschedule |
| `JobManagement/` | `SoftDeleteExpiredFileJob` + `ReapDeletedFileJob` + `JobTypeRegistry` entries |
| `Controllers/Base/OdinControllerBase.cs` | cache max-age clamped to remaining lifetime |

**WebDrop (Part 2)**

| File | Change |
|---|---|
| `Management/StorageDriveDetails.cs`, `Drives/StorageDrive.cs`, `Management/CreateDriveRequest.cs`, `Management/DriveManager.cs` | `BlockAnonymousEnumeration` + anonymous listing predicates |
| `FileSystem/Base/DriveQueryServiceBase.cs` | `AssertCanEnumerateDriveAsync` on the four enumerating entry points |
| `Membership/Connections/CircleNetworkService.cs` | `HandleDriveAdded` skips blocked drives |
| `Drives/SystemDriveConstants.cs` | `WebDropDrive` (new alias+type GUIDs), add to `SystemDrives`, `CreateWebDropDriveRequest` |
| `Configuration/TenantConfigService.cs` | append to `EnsureSystemDrivesExist:248`, after `ListsDrive` per the ordering note at `:256` |
| `Version.cs`, `VersionUpgrade/Version12tov13/`, `TenantServices.cs`, `VersionUpgrade/VersionUpgradeService.cs` | 12 → 13; the migration only bumps the version so the ladder re-enters — the drive is created free by the `EnsureSystemDrivesExist` pre-pass (`VersionUpgradeService.cs:114`) |
| tests touching `SystemDrives` | only `SystemInitializeConfigTests.cs:130` asserts a count; `:306` concatenates the list, and `DriveManagementTests.cs:461` / `DriveManagementArchivalTests.cs:164` iterate it — those three pick a new drive up on their own, but will now exercise it, so check they tolerate a non-enumerable one |

## Verification

1. `dotnet build ./odin-core.sln`
2. **TTL platform tests** — these matter more than the WebDrop ones:
   - a pre-`Ttl` `hdrFileMetaData` reads `Ttl == 0` and never expires, and a pre-flag `detailsJson`
     still enumerates — the two backwards-compatibility tests
   - `Ttl > 0`: refused at read before the job fires; soft-deleted after it, with the tombstone
     visible to `query-modified` and the by-uid lookup 404ing; hard-gone after `Ttl + grace`
   - `Ttl < 0`: a header read does **not** start the clock; the first payload read rewrites `Ttl` to
     `now() - Ttl`; a second read does not move it; concurrent first reads resolve to one timestamp
   - **peer**: send a file with `Ttl > 0` to a recipient; the recipient's copy carries the same `Ttl`
     and its own scheduled job, and dies on time — the chat-retention case
   - **peer, Snapchat**: `Ttl < 0` sent to two recipients resolves independently per copy
   - update may shorten `Ttl`, never extend
   - `Cache-Control` clamped to the remaining lifetime
3. **WebDrop tests** under `tests/apps/Odin.Hosting.Tests.V2/`, per the `_V2` conventions in
   CLAUDE.md (`OwnerTestCase`, `await callerContext.Initialize(...)` before `GetFactory()`): owner
   uploads a drop and an unauthenticated client reads the header and both payloads by uid; deleting
   the file 404s it; anonymous `query-batch` on the drive is rejected while `by-uid` succeeds; the
   drive is absent from the anonymous drive list and from both system circles' grants.
4. `dotnet test ./odin-core.sln` — `SystemInitializeConfigTests.cs:130` needs its count updated; the
   three other `SystemDrives` sites iterate the list and should be checked against the new drive.
5. Locally: `dotnet run --project src/apps/Odin.Hosting` plus the odin-js dev server on 3007; open a
   drop URL in a private window, hit Reveal, confirm the countdown and that the file is gone 20 minutes
   later.

---

# Appendix

## Why the file must be unencrypted to odin-core

Not a workaround — the only legal shape. `FileSystemStreamWriterBase.cs:526` rejects
`ACL = Anonymous && IsEncrypted = true` outright, its comment giving the reason: *"we wont have a
client shared secret to secure the key header"*. `PermissionGroup.cs:114` spells it out — an
anonymous caller has `sharedSecretKey: null`, so there is no channel over which to hand them a key
header. The URL fragment is precisely that missing channel. Mirrored on update at
`FileSystemUpdateWriterBase.cs:432`.

Consequently the server stores `EncryptedKeyHeader.Empty()` (`DriveStorageServiceBase.cs:1915`), and
`IsEncrypted` is immutable across updates (`:1955`).

## Why `Ttl` is on `FileMetadata`

`ServerMetadata` is the tempting home, being server-owned, but it fails both chat requirements:
`PeerFileWriter.cs:114` builds a **fresh** one on receipt (only `FileSystemType`,
`AllowDistribution`, rewritten ACL), and `DriveFileUtility.CreateClientFileHeader:89` hands it only
to the owner. `FileMetadata` is the mirror image: `PeerFileWriter.cs:72` deserializes it whole from
the transfer, and `RedactFileMetadata:131` copies it into `ClientFileMetadata` through an explicit
allow-list — so it crosses peer for free, and reaching the client is one deliberate line.

## What leaks

- To a holder of the link: existence, size, timing. Unavoidable.
- To a stranger without the link: nothing — no listing, no count, and the drive does not appear in
  the anonymous drive list.
- The identity's domain, from the URL itself. Intended.
- To the server: ciphertext, size, IVs, expiry, access times. Not the content, the filename, or the
  MIME type.
- The recipient's browser history, and any TLS-terminating corporate proxy, see the full URL
  including the fragment — fragments are not sent over the wire, but they are in the address bar.
  Burn bounds the window.
- Rate limiting is effectively absent: a single global fixed-window limiter partitioned by IP,
  default 1000 req/s (`OdinConfiguration.cs:330`), enabled only in Production (`Startup.cs:81`). A
  128-bit dropId makes brute force irrelevant; a scraper is a DoS concern, not a confidentiality one.

## Writer sketch

Owner or App client — `uploadFile` calls `assertIfDotYouClientIsOwnerOrApp`, so a Guest client cannot
write:

1. `dropId`, `key`, and one IV per payload — all `getRandom16ByteArray()`
2. `wdr_meta = cbcEncrypt(JSON({fileName, contentType, size, note}), iv_meta, key)`
3. `wdr_data = cbcEncrypt(bytes, iv_data, key)`
4. `uploadFile(...)` with `encrypt = false`, ACL anonymous, `appData.uniqueId = dropId`,
   `appData.content` as above, `Ttl` set, both payloads, **no thumbnails and no preview thumbnail**,
   and all manifest payload IVs zero
5. `https://<identity>/apps/web-drop/d/<b64url(dropId)>#<b64url(key)>`

Open question for the writer document: whether a drop is always a fresh copy on the web-drop drive
(simple, costs storage) or can reference a file on another drive — probably not, since the source is
encrypted under the drive key and would have to be re-encrypted under the drop key anyway.

## Rejected: a URL shortener

The only part of the link worth shortening is the key — and a `slug → long URL` table would store
that key on the identity server, next to the ciphertext, handing the server plaintext. That is the
one thing this feature exists to prevent.

There is also nothing left to shorten: addressing the file by its own `uniqueId` already gives a
22-character id and a 22-character key, so a lookup table adds a round trip, a table and a migration
to save nothing. If a shorter link is ever wanted, shorten the *path* (`/d/` on the identity root
instead of `/apps/web-drop/d/`), never the fragment.
