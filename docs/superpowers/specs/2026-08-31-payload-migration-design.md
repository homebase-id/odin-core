# Payload Move Design

Date: 2026-08-31 (Sebastian), revised 2026-09-29 for the pause model, disk-or-S3 hosts, and a
resumable parallel transfer.
Repo touched: `odin-core`

Follows `docs/superpowers/specs/2026-08-19-identity-json-export-design.md`, which covers the database
half of moving an identity. This spec covers the payload half. The operator procedure is
`agents/identity-move/README.md`.

## Problem

`identity-export` and `identity-import` move an identity's database rows. They do not move the payload
bytes those rows point at, so an imported identity has file headers whose payloads are absent.

The two hosts do not share a payload store, and either may keep payloads on local disk or in S3, in any
combination. Neither can read the other's store: the buckets are private, the hosts hold different
credentials, and a disk is local to its host. The bytes therefore travel over HTTP between the two hosts.

## Decisions

1. **The target pulls.** The source serves a read-only endpoint for the one identity being moved; the
   target fetches from it. Push was rejected: it needs a *write* endpoint into an identity that is live
   on the target after cutover (anyone holding its credential could plant or replace files, including
   unencrypted public content the CDN serves), and a credential the target mints would have to be
   carried back to the source by hand. With pull, the credential travels one way, inside the export file
   that already has to be handled as the identity itself.
2. **The endpoint is on the source's provisioning domain**, on the normal public port, behind its own
   feature flag and host check. The admin API port was rejected: it is normally firewalled.
3. **Cutover first, payloads after.** Export and import move the rows while the identity is paused. The
   transfer then runs in the background, starting at import, before DNS moves, and keeps running after
   the target is resumed.
4. **Partial availability is acceptable during the transfer.** A payload that has not arrived yet returns
   404 with `Cache-Control: no-store` and a `Retry-After`. The identity is otherwise fully functional.
5. **Newest content first**, so what users are most likely to reach arrives first.
6. **Disk or S3 on either side.** Objects are addressed by what they are, not by where they are stored;
   each host builds its own path.
7. **Resumable.** The transfer checkpoints as it goes. A stop, a crash or a restart on either side
   resumes from the last checkpoint, and objects already on the target are skipped.
8. **N objects in parallel**, 5 by default (`PayloadMove:Parallelism`).
9. **The export file stays self-contained.** Everything the target needs to start the transfer travels in
   the file. No second secret for the operator to carry.
10. **The source keeps its payloads until they have all arrived, and after.** Deleting the source is
    refused until the target reports the transfer complete. Purging the source is a separate, explicit
    step (a follow-up).

## Revisions since the first version

- **No schema changes.** The first version added `Registrations.lifecycleState` and a `PayloadMigration`
  table through the SQL generator. Both are gone. `TenantStatus` (#1743) already distinguishes a paused
  or moved identity; the source's state is one `Settings` row; the target's state is the transfer job
  itself (the jobs table already persists, claims and reschedules work).
- **No stopped hosts.** Export requires the identity paused and settled, not the host stopped (#1665),
  and the imported identity lands paused on a running target.
- **Structured addressing instead of store-relative keys.** The first version sent store-relative keys
  and confined them by normalizing the path. The endpoint now takes `(driveId, fileId, payloadKey, uid[,
  width, height])` and builds the path itself, so there is no path to traverse.
- **Parallel instead of one object at a time**, and resumable by design rather than by a cursor alone.
- **Disk-backed hosts are supported** on both sides. The "S3 on both ends" requirement and #1665's
  `PayloadsAreOnS3` guard are dropped.

## Verified groundwork

Read from the code (2026-09-29), not assumed:

- **One store interface hides disk and S3.** `IDriveFileStore`, implemented by `DiskFileStore` and
  `S3FileStore`, is chosen per tenant from `S3Payload:Enabled` behind `LongTermPayloadStore`
  (`TenantServices.cs`). Writes stream (`WriteStreamAsync`). **Reads do not**: every read loads the whole
  object into memory (`LongTermStorageManager.GetPayloadStreamAsync`), so the endpoint needs a streaming
  read. A disk `WriteStreamAsync` checks the byte count against `stream.Length`, so it needs a stream of
  known length.
- **The layout below the store root is the same on both backends.** A long-term payload is
  `<tenantId>/drives/<driveId:N>/files/<hi>/<lo>/<fileId:N>-<key>-<uid>.payload`, and a thumbnail shares
  the stem with `-<w>x<h>.thumb` (`TenantPathManager.GetPayloadDirectoryAndFileName`,
  `GetThumbnailDirectoryAndFileName`). The root is `<TenantDataRootPath>/payloads/` on disk and the
  bucket's `S3Payload:RootPath` on S3.
- **There is no payloads table.** Descriptors live in `DriveMainIndex.hdrFileMetaData` as
  `FileMetadata.Payloads` (`PayloadDescriptor`: `Key`, `Uid`, `BytesWritten`, up to five `Thumbnails`
  with their own `BytesWritten`). Files with `DataSource.PayloadsAreRemote` hold no local objects;
  soft-deleted rows have no payloads. Inline preview thumbnails and CDN static files are database
  content and move with the export.
- **`DriveMainIndex.rowId` orders files on the target.** Export reads `ORDER BY rowId ASC` and import
  inserts in that order without carrying `rowId`, so the target's rowIds follow the source's order. Rows
  created on the target after import get higher rowIds. There is no descending query yet.
- **Payload objects are write-once per uid**, with one benign exception: a peer that retransmits a file
  rewrites the same path with the same bytes (`PayloadStorage.cs`). Updates always mint a new uid, and the
  replaced version is deleted after the header commits.
- **A missing object is usually a 500.** `LongTermStorageManager.MissingFileOrNullAsync` turns it into
  `OdinFileHeaderHasCorruptPayloadException`, unless the header has moved on to a new uid (then 404). It is
  the one place every long-term read passes through when an object is missing.
- **`OdinRetryLaterException` maps to any status with `Retry-After`** in `ExceptionHandlingMiddleware`,
  but sets no `Cache-Control`.
- **The provisioning branch bypasses the tenant middleware.** Its `MapWhen` runs before
  `UseMultiTenancy` (`Startup.cs`), so a paused or disabled identity's 503/409 does not apply there. It
  exists only when `Registry:ProvisioningEnabled`, and `RegistrationRestrictedAttribute` does not check
  the host.
- **A paused or disabled identity keeps its tenant scope** (`FileSystemIdentityRegistry.LoadRegistrationRecordAsync`);
  only its background services stop. So the source can still resolve the identity's storage.
- **Jobs.** One node claims a job at a time; `jobData` is saved on `Repeat` and `Defer` (a checkpoint
  between runs) and is capped at 64 KB; `jobHash` deduplicates; a run cancelled by shutdown is deferred.
  A job whose node dies while it is `Running` stays `Running`: `LogOrphanedJobsAsync` only logs it. And
  since #1823 a job tagged with an identity is deferred while that identity is paused or disabled, which
  is exactly when the target's transfer must run.
- **Tenant background services cannot carry the transfer.** They stop while the identity is paused and
  run on every node of a cluster.
- **Deleting a registration purges its payload prefix** (`FileSystemIdentityRegistry.DeletePayloads`, on
  disk and S3).

## Design

### 1. Source state and the delete guard

The source keeps one `Settings` row per identity being moved, `payload-move-source:<identityId>`, holding
JSON: the handoff token's hash and expiry, the transfer credential's hash, when it was redeemed, and when the
target reported completion. `Settings` is not exported, so the row stays on the source.

`FileSystemIdentityRegistry.DeleteRegistration` refuses while that row exists without a completion time.
Without the guard, an operator tidying up the source after cutover would destroy every payload not yet
transferred, and the target would 404 forever with nothing to recover from.

The identity's status is not extended for this. The source is Paused from export until the operator
retires it as `Disabled/Moved` (`TenantStatus`, #1743); the endpoint serves either.

### 2. The endpoint on the source

`PayloadMoveController`, served on the provisioning domain over the normal public port, in its own
`MapWhen` branch for `/api/payload-move` that does not depend on `Registry:ProvisioningEnabled`:

- `POST /api/payload-move/v1/{identityId}/redeem` exchanges the handoff token for a transfer credential.
  Single use (section 3).
- `HEAD|GET /api/payload-move/v1/{identityId}/payload/{driveId}/{fileId}/{key}/{uid}` returns one payload.
- `HEAD|GET /api/payload-move/v1/{identityId}/thumb/{driveId}/{fileId}/{key}/{uid}/{width}x{height}`
  returns one thumbnail.
- `POST /api/payload-move/v1/{identityId}/complete` records that the target has everything (section 8).

Objects are streamed with a `Content-Length`, through a streaming read added to `IDriveFileStore`
(`OpenReadAsync`: a `FileStream` on disk, the object's response stream on S3).

Every request is checked, and any failure is a 404, in keeping with `AdminApiRestrictedAttribute`'s
convention of not confirming that anything is there:

1. The feature is enabled on this host (`PayloadMove:SourceEnabled`), and the request came in on the
   provisioning domain (`PayloadMoveRestrictedAttribute`, which checks the host itself).
2. The credential is valid and bound to this `identityId`.
3. The identity is **Paused or Disabled** on this host. An active identity is never served: if someone
   resumed the source, its data is live again and must not leak into a second copy.
4. The ids parse as GUIDs, the payload key matches the payload-key character set, and the dimensions are
   numbers.

The path is built by that identity's own `TenantPathManager` from those values. The client never supplies
a path, so the endpoint cannot read outside the identity's files, and no key normalization is needed.

### 3. Tokens

Two credentials, deliberately split.

**The handoff token** is minted by `identity-export`, which stores its hash and a 7-day expiry on the
source and writes the token into the export file (`ExportHeader.payloadSource`, together with the source's
base URL). It is single use and sized to the gap between export and import, not to the transfer.

**The transfer credential** is what the source returns when the handoff token is redeemed. It is bound to
the identity, lives as long as the transfer needs (days, potentially), and is stored only in the target's
transfer job. It never appears in a file or a log.

A token that had to survive a multi-day transfer would have to be long-lived, in a file that already
grants identity takeover. Redeeming it at the start means a file that leaks afterwards gives no payload
access. Single use also means a second import of the same file fails loudly, so two targets cannot both
transfer the identity.

The export file already carries password data, private keys and the TLS certificate key, and
`identity-export` warns about exactly that. The token does not change the file's sensitivity class; it
adds payload read access to what a holder can reach, which is why it is redeemed once rather than being a
bare long-lived secret. Export refuses unless `PayloadMove:SourceEnabled`, so a file never promises
payloads its source will not serve. The export format version goes to 2, so an older target refuses the
file instead of ignoring the payload source.

### 4. Import

When the export file names a payload source, `identity-import` schedules the transfer job inside its own
transaction (the `beforeCommit` hook), so the job exists if and only if the import committed. It records:

- the source base URL and the handoff token,
- `startRowId`, the highest `DriveMainIndex.rowId` the import created, and
- a cursor starting just above it.

The import makes no network calls. Redemption happens on the job's first run, so a source that is
unreachable at import time does not fail an import that has already written rows; the retry lives where
retries already are.

`startRowId` is the boundary that keeps the transfer honest. Files created on the target after cutover get
higher rowIds and are excluded automatically: their payloads were written locally and were never on the
source.

### 5. The transfer job

`PayloadMoveJob` is a system job, tagged with the identity and deduplicated by `payload-move:<identityId>`.
A job, rather than a background service, because the jobs table already gives exactly one runner per job
across a cluster, a persisted checkpoint between runs, and rescheduling across restarts. Two small
additions to the job framework make it fit:

- **`RunsWhileIdentityStopped`**: the job runs although its identity is paused, which is how the target
  identity lands. Other identity jobs keep deferring while paused (#1823).
- **`RescheduleIfOrphanedAfter`**: a job left in `Running` or `Preflight` by a dead node is moved back to
  `Scheduled` after that long (30 minutes here) by the job clean-up service, instead of only being logged.

Each run is a slice, bounded to a few minutes or a few hundred files, and ends by saving its checkpoint
and asking to run again. One slice:

1. On the first run, redeem the handoff token for the transfer credential and keep the credential in place of
   the token. A token already redeemed is recorded as a loud failure: it means a second import.
2. Take the next files below the cursor, newest first (`DriveMainIndex`, `rowId` descending).
3. From each file's descriptors, list its payload and thumbnail objects. Skip files whose payloads are
   remote, and soft-deleted files.
4. Skip any object the target store already holds with the expected length. This makes a slice idempotent
   and a resumed transfer cheap.
5. Fetch the rest, at most `PayloadMove:Parallelism` at a time: each into a local temporary file, checked
   against the descriptor's byte count, then written to the target store from that file (a stream of known
   length, which both backends accept) and the temporary file deleted.
6. Advance the cursor past the batch and save the checkpoint.

Because each slice re-reads headers at transfer time rather than working from a manifest built at import,
files deleted on the target in the meantime are simply not there, and nothing has to be reconciled.

The checkpoint holds the source URL, the credential, `startRowId`, the cursor, counters (files, objects,
bytes, skipped), the first 200 failures with a total count, the current backoff, and a status:
`Transferring`, `Throttled`, `Complete` or `CompleteWithFailures`. It stays far below the jobs table's
64 KB limit.

The job is complete when the cursor is exhausted. With no failures, the target calls the source's
`complete` and the status becomes `Complete`; otherwise `CompleteWithFailures`, and the source is not
released. Either way the job row is kept for the operator.

### 6. Throttling and backpressure

The transfer reads every object an identity owns, for as long as it takes. That is exactly the traffic
shape a storage provider throttles. Treat it as an expected condition, not an error.

**Throttled is a third outcome, not a failure.** Each object ends as transferred, throttled, or failed.
Only failed goes on the failure list. Conflating them would mean a single throttling episode marks
thousands of objects failed, the cursor races to the end, and the transfer reports failure having moved
almost nothing.

- A **429 or 503** from the source ends the slice without advancing the cursor, and the job is deferred
  by the `Retry-After`, or else by a backoff that doubles up to a ceiling (10 minutes) and resets after a
  successful slice. When the backoff sits at its ceiling the status reads `Throttled`, so an operator can
  see why a transfer is slow instead of inferring it.
- A **network error, timeout or other 5xx** is retried a few times within the slice, then treated as
  throttled. Timeouts are caught inside the job, so they never reach the job runner as a cancellation
  (which it would reschedule after three seconds, forever).
- A **404 from the source, or a size mismatch**, is a failure: the object is recorded and the transfer
  moves on. One bad object must not block the thousands behind it.

The source translates its own storage throttling into a 429 with `Retry-After` rather than a 500, so the
target slows down instead of concluding the source is broken. The global per-IP rate limiter answers the
same way, so the target does not need to tell the two apart.

### 7. Reads during the transfer

On the target, a read of a payload or thumbnail that is missing while the identity's transfer is still
running returns **404 with `Cache-Control: no-store` and `Retry-After`**.
`LongTermStorageManager.MissingFileOrNullAsync` asks a small tenant-scoped check whether an unfinished
transfer job exists (cached for 30 seconds; it only runs when an object is missing) and throws
`OdinRetryLaterException` with 404; `ExceptionHandlingMiddleware` adds `no-store` for that exception.

Plain 404 was rejected: the CDN sits in front of public payloads, and a cacheable negative answer could
keep a payload invisible for the TTL after it has actually landed. A 5xx was rejected too: clients and
peers may treat it as a host fault and back off from the identity entirely rather than from one object.
When no transfer is running, the existing behaviour is unchanged.

A peer that fetches the payload over transit gets a 404 and passes it on as 404 to its client; the
`Retry-After` is not forwarded. That is acceptable: the peer asks again the next time its user opens the
file.

### 8. Completion and purge

On completion the target calls `complete` once. The source records the time, revokes the credential, and
the registration becomes deletable.

The source payloads are then removed only by an explicit operator action, never automatically: a
transfer that reported complete against a subtly wrong cursor would be unrecoverable if the purge fired
on its own. `odin-admin tenant delete` is not that action: it also deletes the identity's DNS in the
shared PowerDNS, which after a move is the target's. A purge command that removes payloads and the
registration but never DNS is a follow-up.

### 9. Operator flow

`agents/identity-move/README.md`, in short: pause the source, wait out the settle time, export (which
mints the handoff token), import on the target (which lands paused and schedules the transfer, which
starts at once), repoint DNS, resume the target, retire the source as `Disabled/Moved`. Watch the transfer
with `odin-admin tenant payload-move <domain>`; `--retry` re-arms a finished or stuck transfer from the
top, which is cheap because objects already present are skipped.

### 10. Error handling

- **Source unreachable.** The job backs off and retries; the checkpoint survives restarts on either side.
- **A single object fails.** It goes on the failure list and the transfer moves on. A transfer that ends
  with failures is `CompleteWithFailures`, and the source is not released.
- **A single object is throttled.** Not a failure (section 6).
- **An object is missing on the source.** A failure, not a success. Silently completing a transfer that
  skipped objects would release the source and destroy the only remaining copy of whatever was there.
- **Handoff token already redeemed.** The source refuses; the target records the cause loudly. This is
  the second-import case.
- **Target restarted mid-transfer.** Resumes from the last checkpoint. A node that died mid-slice leaves
  the job `Running`; the orphan rescue reschedules it.

### 11. Testing

- Streaming read: returns the object's bytes on disk and on S3; a missing object throws as reads do today.
- Jobs: an opted-in job runs while its identity is paused; the orphan rescue reschedules only opted-in
  jobs, and only after their threshold.
- Tokens: a handoff token redeems once, refuses the second time and after expiry; a credential is bound
  to its identity.
- Delete guard: `DeleteRegistration` refuses while the source row is incomplete.
- Endpoint (a real host): streams the right bytes; returns 404 for a wrong identity, an active identity,
  an unknown or wrong credential, an unknown object and a malformed key.
- Transfer job (a fake source, a real disk target): newest first; never more than N transfers in flight;
  resumes after a cancelled slice; a 429 defers without advancing or failing; a source 404 and a size
  mismatch become failures; objects already present are skipped; rows above `startRowId` are excluded;
  completion calls the source and nothing else releases it.
- Reads: a missing payload during a transfer returns 404 with `no-store` and `Retry-After`; without a
  transfer, behaviour is unchanged.
- End to end: two local hosts; move a test identity disk to disk and disk to S3; kill the target
  mid-transfer and restart it; every object arrives and reads back byte-identical.

### 12. Security

- The endpoint is internet facing. Its checks in section 2 are the boundary, not the obscurity of the
  route.
- Structured addressing is what stops the endpoint from being a read primitive over the store: the host
  builds the path from validated ids under one identity's root.
- An active identity is never served, so the endpoint cannot be used against an identity that was not
  handed off.
- The transfer credential is stored only in the target's job data and must not appear in logs.
- The handoff token is in the export file, which is already sensitive enough to warrant the warning
  `identity-export` prints. Single-use redemption bounds its usefulness to the export-to-import window.
- The global per-IP rate limiter covers the endpoint.

## To verify before implementing

1. **The CLI's job manager joins the import's system transaction**, so scheduling the job commits and
   rolls back with the import. If it does not, schedule after commit and rely on `--retry` as the
   fallback.
2. **`OdinRetryLaterException` with 404** passes through `ExceptionHandlingMiddleware` as 404 with
   `Retry-After`.
3. **The CDN honours `Cache-Control: no-store` on a 404.** If it does not, public payloads need a
   different answer during the transfer. (Ops.)
4. **Every host has a provisioning domain with a certificate**, reachable from the other cluster. (Ops.)
5. **Whether the deployed S3 provider throttles with 429 or 503, and sends `Retry-After`.** The design
   handles both; this only tunes the backoff.

## Open follow-ups

- **Purge the source** after completion: payloads and registration, never DNS.
- **Push**, for a source the target cannot reach (for example a disk-only box behind NAT). The target's
  side barely changes.
- **Per-drive cursors**, if one identity-wide cursor ever limits throughput.
- **Presigned URLs**, so an S3 source serves bytes straight from its bucket and the host carries metadata
  only. Nothing in the repo presigns today.
- **`S3FileStore`'s retry predicate and 429.** It retries 5xx and timeouts only; a provider that throttles
  with 429 gets no retry. That fix helps every S3 caller but changes live read latency, so it is its own
  change.
- **Passing `Retry-After` through peers.**
- **`CopyRegistration` on S3.** Still refuses; the streaming read and the addressing built here are most
  of what it needs (and whether to keep it at all is an open question).
