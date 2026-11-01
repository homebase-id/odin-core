# Selective Share PR 3: shares as ACL index rows

**Status:** plan, not built. Part of [selective-share-plan.md](selective-share-plan.md) ("Later PRs", item 3),
which has the model; this says how PR 3 builds it. Code facts were read on `selective-share-circle-keys`
(PR 2, which carries PR 1) on 2026-11-01. Markers: **(V)** read in the code (file named), **(I)** inferred.

## What PR 3 does, in one paragraph

The owner, or the drive's owning app, writes a file's shares: one `DriveAclIndex` row per circle, with its
permission bits, expiry and the file key locked with the circle key. Sharing a new file can ride in its upload. A
header write on a Selective Share drive leaves the rows alone, and the query honours the rows' Read bit and
expiry and treats a file with no rows as the owner's. Nothing a member does changes: members still can't open a
client-locked file (`ZkDriveFiles.VisibleTo`) **(V)**, so their reads, their "shares changed since" and their
socket events are PR 4.

## Where things stand

- **A drive knows it is Selective Share** by `StorageDrive.SharingModel == DriveSharingModel.SelectiveShare`
  (`DriveSharingModel.cs:20-23`, stored in `Drives.SharingModel`) **(V)**. No code tests that value yet; zero
  knowledge is tested by `StorageDrive.IsZeroKnowledge` (no stored drive key) **(V)**. PR 3 adds
  `StorageDrive.IsSelectiveShare`.
- **Every header write knows the drive:** `DriveQuery.SaveFileHeaderAsync(StorageDrive drive, …)` builds the
  rows' member list from the header's ACL (circles plus the named identities' hash ids) and calls
  `MainIndexMeta.BaseUpsertEntryZapZapAsync`, which reconciles the rows in the header's transaction
  (`DriveQuery.cs:221-246`, `MainIndexMeta.cs:205-229`) **(V)**. All about 50 header-writing call sites go through
  it, soft delete included **(V)**.
- **The query and the read rule read different things.** The query filters by rows
  (`cir.fileId IS NULL OR cir.aclMemberId IN (…)`, `QueryBatch.cs:83-95`); the read rule reads the header's
  ACL (`DriveAclAuthorizationService.Allows`, `:101-123`) **(V)**. They agree today because the rows are made from
  the header. On a Selective Share drive they won't: the header names no circles.
- **The owner and apps get the header's ACL; nobody else does:** `DriveFileUtility.CreateClientFileHeader` adds
  `ServerMetadata` only for the owner (`:113-116`) **(V)**.
- **Query results are cached** per `aclAnyOf` for up to 2 hours, cleared by the drive's invalidation tag
  (`QueryBatchCached.cs`, `TableDriveMainIndexCacheKeys.InvalidateDriveAsync`) **(V)**.

## 1. The header on a Selective Share drive

- **Its security group is always `Connected`, set by the server** whatever the client sends (decided
  2026-11-01). The rows say who; the header says only "a reviewed connection, at least" (decided 2026-10-31). A
  share change never touches the header, so the group can't depend on whether a file is shared. It is safe for a
  file nobody shares: with no rows it is the owner's alone (section 4). Clients change nothing: an upload that
  names no ACL gets `Owner` today (`UploadFileMetadata.cs:19`) **(V)**, and is stored as `Connected` here.
- **Refused on upload and update:** any circle or identity in the ACL, since the rows say who
  (`FileSystemStreamWriterBase.ValidateUploadCoreAsync`, `FileSystemUpdateWriterBase.ValidateUploadCore`) **(V
  for the places)**. Only on Selective Share drives (I: Filesync's drive is one; to check that its uploads name
  none).
- **No `IsCollaborativeChannel` attribute on a Selective Share drive:** refused at creation and when attributes are
  set. Its edit rule is what a row's `Write` bit says per file (PR 4), and its other effects move copies, which a
  zero-knowledge drive never sends ([file-access.md](file-access.md), "Is the collaborative attribute still
  needed?").
- **The header write leaves the rows alone:** `SaveFileHeaderAsync` tells `BaseUpsertEntryZapZapAsync` not to
  reconcile on a Selective Share drive (a `reconcileAcl` flag rather than a null list, so null can't come to mean
  two things). A soft delete keeps the rows, so a member syncing sees the tombstone; a hard delete still clears
  them (`MainIndexMeta.cs:50`) **(V)**, and so does the drive purge **(V)**.

## 2. Writing shares

**API** (V2, owner console or the drive's owning app):

| Method and route | Does |
|---|---|
| `GET /api/v2/drives/{driveId}/files/{fileId}/shares` | the file's rows, withdrawn ones included |
| `PUT /api/v2/drives/{driveId}/shares` | a batch of `{fileId, circleId, permissions, expiresAt, lockedKeyHeader}`, all or none, up to 100 |
| `DELETE /api/v2/drives/{driveId}/files/{fileId}/shares/{circleId}` | withdraws one: bits 0, no key header, `modified` moved |

One `PUT` for one share or a hundred: the bulk job that shares a folder's existing files is the same call.

**Who may:** the master key (owner console), or an app whose client token's app id is the drive's `AppId` and
the circle's `AppId` **(V for both columns)**. That mirrors PR 2's "owning app or owner console" check
(`CircleMembershipService.GetCircleCallerMayManageAsync`, `:650-688`) **(V)**, but judged by the drive: a share is
about the drive's file. A member writer is PR 4.

**Checked for each entry, before anything is written:**
- The drive is Selective Share; the file exists on it and isn't deleted.
- The circle is the app's, enabled, grants the drive Read and Write (decision 1 of the plan), and holds its share
  key: the locked key header is locked with that key, so a circle without one can't have shares
  (`CircleShareKeyNotSet`).
- `permissions` is not 0 (0 is a withdrawal, written only by `DELETE`), holds only Read, Write, React and
  Comment, and includes Read: no upload-only rows yet (decided 2026-11-01). The rows V1 makes from a header's ACL
  on other drives always mean Read, as today; V1 gets no share API.
- `expiresAt` is null or later than now.
- `lockedKeyHeader` is exactly 45 bytes and starts with format byte 2 (decision 7), when the file is encrypted;
  absent when it isn't.

**Stored** with one upsert per row in one transaction (insert, or update bits, expiry and key header; the
generated `modified = MAX(modified + 1, now)`) **(V for the generated rule)**. The file's existence is checked in
the same transaction, with the file's row locked on Postgres as `TableDriveMainIndex` already does (`:391`) **(V)**,
so a hard delete running alongside can't leave a share behind. Then the drive's caches are cleared, after commit,
through a new `MainIndexMetaCached` method, as every header write does (`MainIndexMetaCached.cs:24-84`) **(V)**.

**The file's `modified` moves, its version tag doesn't** (decision 6, amended 2026-11-01): a targeted update of
`driveMainIndex.modified` (`MAX(modified + 1, now)`, as the generated tables do) in the same transaction, for each
file the batch touches, once. The owner's other devices see the file in their ordinary sync and read the shares
with `includeShares`. No `DriveFileChangedNotification` in PR 3: a socket event for a share is PR 4's, with the
members' (I: the owner's devices then catch up at their next sync, not at once).

## 3. Shares in the upload

- A new file's upload may carry `shares`, the same entries as the `PUT`, on `UploadFileDescriptor` next to
  `EncryptedKeyHeader` (key material travels together, encrypted with the shared secret) **(V for the
  descriptor)**, not in `UploadFileMetadata`, which the header owns.
- Checked with the upload's other checks, before any file is moved, and written in the transaction that writes
  the header, so a file is never visible half-shared.
- Updates and overwrites carry no shares: they keep the file key (decision 7), so the rows stay valid.

## 4. The query honours the rows

The ACL filter, used whenever the caller isn't the owner (`DriveQuery.GetAcl`) **(V)**, becomes:

```sql
-- rowless: only on drives where no rows means "the whole security group"
( (@rowlessIsOpen AND NOT EXISTS (SELECT 1 FROM driveAclIndex r WHERE <same file>))
  OR EXISTS (SELECT 1 FROM driveAclIndex r WHERE <same file>
             AND r.aclMemberId IN (…) AND (r.permissions & 1) = 1
             AND (r.expiresAt IS NULL OR r.expiresAt > @now)) )
```

- `@rowlessIsOpen` is false on a Selective Share drive, true elsewhere: the drive's model is known in `DriveQuery`
  (I: passed down like the other query parameters).
- On every other drive the answer is the same as today's: their rows all have bit 1 and no expiry, written by
  the reconcile with the record's defaults **(V)**.
- The `LEFT JOIN` and the `SELECT DISTINCT` that exists only for it go (`QueryBatch.cs:369`) **(V)**. `EXPLAIN` on
  SQLite and Postgres, before and after, for a drive of 100,000 files: the `UNIQUE(identityId, driveId, fileId,
  aclMemberId)` prefix serves both subqueries **(V for the index)**.
- **Expiry and the query cache:** a cached page can outlive a share's expiry by up to 2 hours. On Selective Share
  drives a non-owner query isn't cached (members can't query them before PR 4 anyway, so nothing is lost now).
- **The read rule doesn't change in PR 3.** On a Selective Share drive a member is still refused by
  `ZkDriveFiles.VisibleTo` **(V)**; PR 4 makes the read rule read the row.

## 5. What the owner sees

**Asked for, not sent by default** (proposed 2026-11-01): a query (`QueryBatch`, smart batch, the change feed)
and a header read by id take `includeShares`. With it, each returned file carries `shares`: its live rows as
`{circleId, permissions, expiresAt, modified}`, without the locked key headers, which only a member needs. One
grouped read per page (`… WHERE fileId IN (page)`), only when asked, only for the owner and the owning app, only on
a Selective Share drive. A client draws whatever it wants from that (shared, editable by others, expiring), and
nothing is stored that an expiry could make stale. Without the flag nothing changes and nothing is read.

The file header's ACL still says `Connected` and names no one. The `GET` above lists one file's rows, withdrawn
ones included.

## Tests

- **Storage, both databases:** the new filter (rowless hidden on Selective Share, shown elsewhere; an expired row
  and a row without Read hidden; another circle's row hidden); the header write on a Selective Share drive keeps
  the rows (update, overwrite, soft delete) and a hard delete clears them; the share upsert moves `modified` and
  keeps `created`; the earlier `AnAclFilterWorksWithEverySortAndFilter` still passes unchanged.
- **V2 integration:** who may write (owner, owning app; another app, an app's circle on another app's drive, an
  owner circle, a circle without a key, a non-Selective-Share drive refused); each validation; a batch with one
  bad entry stores nothing; an upload with shares stores the file and its rows, an upload with a bad share stores
  neither; a share is seen by the next owner query (cache cleared); a withdrawal keeps the row.

## Decided (2026-11-01)

- **The group** on a Selective Share drive is always `Connected`, set by the server (section 1).
- **No rows without Read** from the V2 share API; V1's rows always mean Read.
- **Shares are in query results only when asked** (`includeShares`, section 5); the `GET` lists one file's rows.
- **The index for a member's "shares changed since"** (`identityId, driveId, aclMemberId, modified, rowId`) goes
  into PR 1's unreleased migration, before PR 1 merges; PR 4 uses it.
- **One PR.**
- **Dead code goes in PR 3:** `FileUpdateInstructionSetV2`, never used by the V2 update
  (it reads `FileUpdateInstructionSet`) **(V, read by a subagent)**.

## Not in PR 3

- Member reads, writes, queries and socket events: PR 4.
- Cleaning up withdrawn rows (still open), and what deleting a circle or resetting its key does to its rows: a
  deleted circle's rows grant no one (a reader must be in the circle), and a reset key's rows can't be opened.
  Listed so PR 4 decides them.
- Sharing with one person or a link: decisions 3 and 4.

## Found on the way, not PR 3's (for the user to decide)

- **An anonymous caller's query has no SQL ACL filter:** `GetAcl` returns null when the list is empty, so the
  query lists files that name circles and only the post-filter drops them (`DriveQuery.cs:157-173`,
  `DriveQueryServiceBase.FilterVisibleAsync`) **(V)**. Nothing leaks (I: the post-filter runs on every result), but
  a page can come back short.
- **`QueryModifiedAsync` ignores its `MaxDate`:** it sets `nextBoundaryCursor`, which `QueryBatchAsync` never reads
  (`QueryBatch.cs:610-647`, `:322-346`) **(V, read by a subagent)**. The user: obsolete, V1 only, the query batch
  does the same; left alone.
- **A peer's reaction skips the file's ACL:** the peer path finds the file with `ResolveFileId`, which skips it
  (`PeerServiceBase.cs:149-163`, `FileSystemResolver.cs:56-72`), and checks only the drive's React permission
  **(V, read by a subagent)**. Security-relevant; not filed. The user: may be deliberate; to find out before
  fixing.
