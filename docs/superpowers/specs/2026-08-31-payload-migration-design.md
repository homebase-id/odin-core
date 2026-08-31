# Payload Migration Design

Date: 2026-08-31
Repo touched: `odin-core`

Follows `docs/superpowers/specs/2026-08-19-identity-json-export-design.md`, which
covers the database half of moving an identity. This spec covers the payload half.

## Problem

`identity-export` and `identity-import` move an identity's database rows. They do
not move the payload bytes those rows point at. An imported identity has file
headers whose payloads are absent, which the export design listed as its first
deferred item.

The obvious approach does not work. The target host cannot read the source's S3
bucket, because the buckets are private and the two hosts hold different
credentials. Nor can the source simply push into the target's bucket, for the same
reason.

The bytes therefore have to travel over HTTP between the two hosts. That collides
with the existing cutover model: `identity-export` requires the source host to be
stopped (`IdentityJsonTransfer.HostIsStopped`, committed in 1fc6d4587), so a host
that is stopped cannot serve payloads. Resolving that collision is most of this
design.

Related, and fixed by the same work: `IIdentityRegistry.CopyRegistration` throws
outright when S3 payloads are enabled (`FileSystemIdentityRegistry.cs:244-248`,
"SEB:TODO update for S3 payloads").

## Requirements

Decided up front, not open:

1. **Two phases, and the second one runs in the background.** Phase 1 is the
   existing database export and import with both hosts stopped. Phase 2 drains the
   payloads while both hosts are running again. Phase 1 is the cutover; after it,
   the identity is live on the target and nowhere else.
2. **Partial availability is acceptable during the drain.** A payload that has not
   arrived yet returns 404. The identity is otherwise fully functional. A large
   identity is expected to take a long time to become whole.
3. **Newest content first.** The drain transfers the most recently created files
   before older ones, so the content users are most likely to reach is restored
   first.
4. **S3 on both ends.** Disk-backed hosts are refused outright, already enforced by
   `IdentityJsonTransfer.PayloadsAreOnS3`.
5. **The export file stays self contained.** Everything the target needs to start
   the drain travels in the file. No out of band secret for the operator to
   mishandle.
6. **The source cannot lose the payloads while they are still needed.** Deleting a
   source registration purges its entire payload prefix, so that has to be blocked
   until the drain completes.

## Non-goals

- **Zero downtime.** Phase 1 still stops both hosts. This spec shortens the window
  by taking payloads out of it, not by removing it. Removing it is a stated goal for
  later, and section 12 constrains this design so that work is additive rather than a
  rewrite.
- **Parallel transfer.** One object at a time, accepted deliberately. See
  **Open follow-ups** for what a per-drive cursor would buy.
- **Moving orphaned objects.** Objects in the source bucket that no imported row
  references never transfer. Those are `Defragment`'s problem, not this one.
- **Cross-provider migration.** Both hosts speak HTTP to each other, so their S3
  providers need not match, but neither is required to reach the other's provider
  directly and no attempt is made to optimise for the case where they could.
- **Presigned URL transfer.** Considered and deferred. See **Open follow-ups**.

## Verified groundwork

Read from the code, not assumed:

- **The S3 key layout is derivable from database rows.** A long-term payload lives
  at `<store root>/<tenantId>/drives/<driveId:N>/files/<hi>/<lo>/<fileId:N>-<payloadKey>-<uid>.payload`,
  built by `TenantPathManager.GetPayloadDirectoryAndFileName` from
  `PayloadsDrivesPath`, `GetPayloadDirectoryFromGuid` (the last two nibbles of the
  fileId), and `GetPayloadFileName`. Thumbnails share the directory and the
  `<payloadKey>-<uid>` stem, adding `-<w>x<h>.thumb`
  (`GetThumbnailFileNameAndExtension`). When S3 is enabled `RootPayloadsPath` is
  empty and `PayloadsDrivesPath` is `<tenantId>/drives`
  (`TenantPathManager.cs:82-86`); the store's own `rootPath`, from
  `S3Payload:RootPath`, is applied separately by `IS3Storage.GetFullKey`.
- **There is no payloads table.** Payload descriptors live inside `hdrFileMetaData`,
  a serialized column on `DriveMainIndex`. Payload uid is therefore not orderable in
  SQL, and any enumeration has to go through file headers.
- **`DriveMainIndex.rowId` is a usable cursor key on the target.**
  `ExportRowsAsync` reads `ORDER BY rowId ASC` and `ImportRowAsync` does not carry
  `rowId` across, so the target assigns its own in import order, which preserves
  source order. `rowId` is immutable, unlike `modified`.
- **Payload paths are built from `(fileId, Key, Uid)`** and `Uid` is "a sequential
  guid used for each instance of this payload" (`PayloadDescriptor.cs:49,71`), so an
  updated payload is written under a new key rather than over the old one.
- **Deleting a registration purges the payload prefix.** `DeleteRegistration` calls
  `DeletePayloads`, which on S3 does `DeleteDirectoryAsync(id + "/")`
  (`FileSystemIdentityRegistry.cs:213-233,594-607`).
- **`Disabled` is not sufficient as a migration state.** It closes the HTTP front
  door but does not stop `DeleteRegistration`, and it already means "an admin
  suspended this tenant". The withdrawn Task 10 of the export plan reached the same
  conclusion about freeze from a different direction.
- **Not all payloads are encrypted.** `IsEncrypted` is a per-file flag on
  `FileMetadata` (`FileMetadata.cs:86`). Public drive content, which the CDN serves,
  is not ciphertext. Encryption at rest is therefore not an authorization model for
  the transfer endpoint.
- **The admin API is unsuitable as the transport.** `AdminApiRestrictedAttribute`
  requires a specific domain and a specific local port, and that port is normally
  not exposed through the firewall.
- **There is a pattern for a public, domain-gated controller.**
  `Controllers/Registration` is served on the provisioning domain over the normal
  public port, blocked by domain in `Startup.cs` and gated a second time by
  `RegistrationRestrictedAttribute` reading `Registry:ProvisioningEnabled`.
- **Tenant background services are registered and started per tenant** in
  `BackgroundServiceExtensions.cs:76-106`, alongside `PeerOutboxProcessorBackgroundService`
  and others.
- **`IS3Storage` exposes no enumeration.** `ListObjectsV2Async` is used internally
  by `S3AwsStorage` but is not on the interface. This design needs none.

## Design

### 1. Source lifecycle state

A tenant lifecycle state on the registration, distinct from `Disabled`. This work
needs one value, `MigratedAway`, recording that the identity has been exported away
and its payloads have not finished draining.

Build it as a state machine with one inhabited value, not as a boolean. Section 12
explains why: live export needs more values on this same machine, and a boolean here
means rewriting rather than extending. The cost of the general shape now is small and
the states it will need are already known.

While it is set:

- The domain does not resolve to a tenant on that host. The identity is served
  by the target and nowhere else.
- `DeleteRegistration` refuses. This is the point of the state. Without it, an
  operator tidying up the source after cutover silently destroys every payload that
  has not yet transferred, and the target 404s forever with nothing to recover from.
- The payload prefix is retained untouched.
- The migration endpoint will serve this identity's objects. It serves no others.

`identity-export` sets it as part of a successful export. It clears when the target
reports the drain complete, at which point the registration becomes deletable.

`Disabled` is left alone and keeps its current meaning. An identity can be both
disabled and migrated away; they are independent facts.

If the source runs more than one host, this state has to propagate. That is the
tenant lifecycle model the withdrawn Task 10 described, and the Redis pub/sub
already used for `OdinContextCache` invalidation is the carrier. A single-host
source needs no propagation, and the design works without it.

### 2. Migration endpoint on the source

A new controller beside `Controllers/Registration`, served on the provisioning
domain over the normal public port. It copies the registration controller's shape:
blocked by domain in `Startup.cs`, gated a second time by its own restricted
attribute reading a new feature flag.

Four routes:

- `POST /api/migration/v1/redeem/{identityId}` exchanges a handoff token for a
  drain credential. Single use, see section 3.
- `HEAD /api/migration/v1/payloads/{identityId}/{*key}` returns size and existence.
- `GET  /api/migration/v1/payloads/{identityId}/{*key}` streams the object.
- `POST /api/migration/v1/complete/{identityId}` reports the drain finished, which
  clears `MigratedAway` and revokes the drain credential. See section 7.

The two payload routes address a single object by store-relative key.

The key on the wire is store-relative, without either host's `S3Payload:RootPath`.
Each side applies its own root through `IS3Storage.GetFullKey`, so the two hosts may
configure different roots and different buckets.

Every payload request is checked three ways, independently:

1. The bearer credential is valid and bound to this `identityId`.
2. The identity is in the `MigratedAway` state on this host.
3. The key resolves under that identity's own prefix, after normalization.

Any failure returns 404, matching the existing convention in
`AdminApiRestrictedAttribute` of not confirming that something interesting is there.
Check 3 is what stops the endpoint from becoming a general read primitive over the
bucket, and it must reject traversal (`..`) and absolute keys before use rather than
relying on the key looking well formed.

### 3. Tokens

Two credentials, deliberately split.

**The handoff token** is minted by `identity-export`, which stores its hash, its
expiry and the identity id on the source and writes the token itself into the export
file. It is single use and short lived, sized to the gap between export and import
rather than to the drain.

**The drain credential** is what the source returns when the handoff token is
redeemed. It is bound to the redeeming target, lives as long as the drain needs
(days, potentially), and is stored only in the target's system database. It never
appears in a file.

The split matters. A token that had to survive a multi-day drain would have to be
long lived, and it sits in a file that already grants identity takeover. Redeeming
it at the start of the drain means a file that leaks afterwards is no better for
payload access than the file is today. Single use also means a second import of the
same file fails loudly, so two targets cannot both drain the same identity.

The export file already carries password data, private keys, the TLS certificate
private key and DKIM signing keys, and `identity-export` already warns about exactly
that. The handoff token does not change the sensitivity class of the file. It does
add payload read access to what a file holder can reach, which is the reason for the
single-use redemption rather than a bare long-lived secret.

### 4. Drain state on the target

`identity-import` writes one drain record into the system database, next to the
`Registrations`, `Certificates` and `DkimKeys` rows it already writes there:

- source base URL,
- the handoff token, replaced by the drain credential once redeemed,
- `StartRowId`, the highest `DriveMainIndex.rowId` present at import,
- `CursorRowId`, initially `StartRowId`,
- a failure list,
- status: `Pending`, `Draining`, `Complete`, or `Failed`.

The import performs no network calls. It only records what the drain will need.
Redemption happens on the worker's first pass, so a source that is unreachable at
import time does not fail an import that has already written rows, and the retry
lives where retries already are.

`StartRowId` is the boundary that makes the cursor correct. Files created on the
target after cutover receive rowIds above it and are excluded automatically; their
payloads were written locally and were never on the source.

### 5. The drain worker

`PayloadDrainBackgroundService`, registered in `AddTenantBackgroundServices` and
started in `StartTenantBackgroundServices` beside the existing tenant workers. Its
first act on startup is to read the drain record; absent, `Complete` or `Failed`, it
exits immediately, so it costs nothing for the identities that are not migrating.

Each pass:

1. Take the highest `rowId` at or below `CursorRowId` from `DriveMainIndex` for this
   identity.
2. Deserialize its `hdrFileMetaData` and read the payload descriptors, each with its
   key, uid and thumbnail list.
3. For each payload and each thumbnail, build the store-relative key through
   `TenantPathManager`.
4. Skip any key already present in the target bucket. This makes the pass
   idempotent and makes a resumed drain cheap.
5. Fetch the rest from the source endpoint and write them into the target bucket.
6. Advance `CursorRowId` past that row and commit.

Because step 2 re-reads the header at transfer time rather than working from a
manifest built at import, files deleted on the target after cutover simply are not
there, and no stale entry has to be reconciled. This is why the design has no
manifest table.

The cursor is committed per file, so a crash re-transfers at most one file's objects,
and step 4 makes that re-transfer nearly free.

Completion is `CursorRowId` exhausted and the failure list empty.

### 6. Reads during the drain

A payload read that misses in the target bucket, while a drain is active for that
identity, returns 404 with `Cache-Control: no-store` and a `Retry-After`.

Plain 404 was rejected. The CDN sits in front of public payloads
(`Cdn:PayloadBaseUrl`), and a cacheable negative answer can keep a payload invisible
for the TTL after it has actually landed. Peers fetching over transit may likewise
record the file as gone rather than retrying. A distinct retryable status such as
503 was also rejected: older clients and peers may treat 5xx as a host fault and
back off from the identity entirely rather than from the one object.

When no drain is active, the existing missing-object behaviour is unchanged.

### 7. Completion and purge

On completion the target calls the source once to report it. The source clears
`MigratedAway`, revokes the drain credential, and the registration becomes
deletable.

The source payloads are then deleted by an explicit operator action, not
automatically. `DeleteRegistration` wipes the whole prefix and cannot be undone, and
a migration that reported complete against a subtly wrong cursor would be
unrecoverable if the purge fired on its own.

### 8. Operator flow

1. Stop the source host. Run `identity-export`. It writes the file, sets
   `MigratedAway`, and embeds a handoff token.
2. Move the file. Stop the target host, run `identity-import`, start the target.
3. Repoint DNS. Start the source host. It serves every identity except this one.
4. The target drains in the background, newest first. Missing payloads 404
   uncacheably until they land.
5. On completion the operator deletes the source registration, which purges the
   prefix.

### 9. Error handling

- **Source unreachable.** The worker backs off and retries. The drain record
  persists, so this survives restarts on either side.
- **A single object fails.** It goes on the failure list and the cursor advances.
  One bad object must not block the thousands of older ones behind it. A drain that
  reaches the end with a non-empty list is `Failed`, not `Complete`, and does not
  release the source.
- **An object is missing on the source.** Treated as a failure, not as success.
  Silently completing a drain that skipped objects would release the source
  registration and destroy the only remaining copy of whatever was actually there.
- **Handoff token already redeemed.** The source refuses, and the target records
  `Failed` with a message naming the cause. This is the second-import case and
  should be loud.
- **Target restarted mid-drain.** Resumes from the committed cursor.

### 10. Testing

- Key derivation: a payload descriptor plus its file and drive ids produce exactly
  the key the S3 store reads today. Pin payload and thumbnail forms both.
- Cursor: descends, is committed per file, resumes from the committed value, and
  excludes rows above `StartRowId`.
- Cursor immutability: a file edited on the target after cutover, whose payloads
  have not drained, still transfers. This is the property that ruled out `modified`
  as the ordering key and it should have a test that fails if someone switches to it.
- `DeleteRegistration` refuses while `MigratedAway`.
- Endpoint: rejects a wrong identity id, a key outside the identity's prefix, a
  traversal attempt, an unknown credential, and an identity not in `MigratedAway`.
  All five return 404.
- Handoff token: redeems once, refuses the second time.
- Failure handling: a failing object does not stall the cursor, and a drain ending
  with a non-empty failure list does not release the source.
- Missing-payload read returns 404 with `no-store` while draining, and normal
  behaviour when not.
- End to end: two hosts, export, import, drain, verify every referenced object
  arrives and the target reads them back.

### 11. Security

- The endpoint is internet facing, so the three independent checks in section 2 are
  the security boundary, not the obscurity of the route.
- Prefix confinement must be enforced on the normalized key. This is the check that
  stands between one identity's migration and a read primitive over the bucket.
- The drain credential is stored in the target's system database. It is not written
  to any file, and it must not appear in logs.
- The handoff token is in the export file, which was already sensitive enough to
  warrant the warning `identity-export` prints. Single-use redemption bounds its
  usefulness to the export-to-import window.
- Rate limiting belongs on the endpoint. It serves whole identities one object at a
  time to a caller that is by definition automated.

### 12. Path to live export

Running export and import without stopping the hosts is a stated goal. It is not in
this spec, but this spec must not make it harder, because it is already building part
of the machinery.

The withdrawn Task 10 of the export plan listed four things live export needs. This
design delivers the first two as a side effect of what it needs for itself:

1. **An explicit lifecycle state, distinct from `Disabled`.** Built here, section 1.
   One bit cannot mean both "an admin suspended this tenant" and "this tenant is
   being migrated", which is the conflation that made the withdrawn
   `UnfreezeIdentityAsync` need a `restoreDisabledTo` argument.
2. **One source of truth for that state, propagated across hosts.** Needed here the
   moment a source runs more than one host, and carried by the Redis pub/sub already
   used for `OdinContextCache` invalidation.

Two remain, and both are additions to the machine rather than changes to it:

3. **Workers observing the state at every write boundary** and abandoning the current
   unit of work, rather than being told to stop from outside. `StopBackgroundServices`
   cannot do this: it shuts down the caller's own container, which from the CLI is a
   throwaway container whose workers never started.
4. **A freeze acknowledgement**, so a freeze blocks until every host confirms it is
   idle for that tenant, with a timeout.

Why 4 is not optional: the export already takes `RepeatableRead` snapshots of both
databases, so a live export would not produce an *inconsistent* file. The failure is
lost writes. Anything a worker commits after the snapshot is absent from the file, and
the source is then abandoned, so those writes are gone. Checking a flag alone gives an
eventual freeze, not a confirmed one, and a worker that reads the flag and then writes
for thirty seconds is still writing when the export begins.

Three constraints this design accepts so that work stays additive:

- **The lifecycle state is a state machine from the start**, with `Frozen` and
  `Freezing` named in the type even though nothing sets them yet. Adding a value must
  not mean changing every call site.
- **The host-stopped check stays a single decision point.**
  `IdentityJsonTransfer.HostIsStopped` is one function called from two places, and
  live export replaces it with a freeze-confirmed check at the same seam. It must not
  spread into the exporter or the importer.
- **`IdentityJsonExporter.ExportAsync` keeps `callerHasFrozenIdentity` as a caller
  assertion.** It is already the right shape: today the CLI justifies it with a
  stopped host, later it justifies it with a confirmed freeze, and the exporter does
  not change either way.

The implementation plan should carry these as explicit constraints on the tasks that
touch the lifecycle state, the guards and the exporter signature, so that a later live
export plan starts from a machine with two values rather than from a boolean.

## To verify before implementing

Each of these is an assumption this design rests on that has not been confirmed in
code. The implementation plan should verify them as early tasks, and the design
changes if any is false.

1. **No code path overwrites a payload object in place.** The path construction from
   `(fileId, Key, Uid)` and the per-instance `Uid` strongly imply write-once, and the
   design relies on it: an object rewritten on the source after the cursor passed it
   would leave the target holding a stale copy. Verified: the path construction and
   the `Uid` comment. Not verified: that no writer reuses a uid.
2. **The CDN honours `Cache-Control: no-store` on a 404.** If it does not, section 6
   needs a different answer for public payloads specifically.
3. **What the current read path does on a missing S3 object.** Section 6 assumes
   there is a single place to intercept. Not yet located.
4. **How peers over transit treat a 404 for a payload.** If a peer records the file
   as permanently gone rather than retrying, the drain silently degrades peer copies
   and this needs handling on the peer path too.

## Open follow-ups

- **Per-drive cursors for parallelism.** One cursor per identity means strictly
  serial transfer. A media-heavy identity can hold hundreds of thousands of objects,
  and at a read plus a write each that is days of draining during which the source
  registration is pinned. A cursor per drive would allow drive-level parallelism
  without complicating any single cursor. Deliberately not in this design.
- **Presigned URLs instead of proxying.** The source could mint time-limited GET
  URLs so the target pulls straight from the source bucket and the source host
  carries metadata only. AWSSDK.S3 4.0.17 is referenced and `IAmazonS3` is
  registered (`S3AwsStorage.cs:751`), so it is available in principle. Nothing in
  the repo uses presigning today and it has not been confirmed against the deployed
  provider. Because the target derives its own keys and fetches one object per
  request, swapping the fetch for a presigned redirect would not disturb the cursor
  or the drain record.
- **`CopyRegistration` on S3.** Still throws. The migration endpoint and the key
  derivation built here are most of what it needs.
- **Progress reporting to an operator.** The drain record holds enough to report
  percentage complete and failures. No surface exposes it.
- **Automatic purge on completion.** Deliberately manual. Revisit once the drain has
  proven its completion signal in practice.
- **Live export and import.** Its own spec, building on section 12. It needs worker
  observation at write boundaries and a freeze acknowledgement across hosts, and it is
  the prerequisite for zero-downtime migration and for fixing `CopyRegistration`,
  which also runs while the host is live.
