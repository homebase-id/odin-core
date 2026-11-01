# Selective Share PR 3: shares as index rows, and members

**Status:** plan, not built. Part of [selective-share-plan.md](selective-share-plan.md) ("Later PRs", items 3 and 4,
merged into one PR on 2026-11-01: they aren't released apart), which has the model. How a file's access works today, and the terms used here (owner callers, outside callers, required
group, index row, share row, live row, withdrawn share, the read rule), are in [file-access.md](file-access.md); read
it first. Code facts were read on `selective-share-circle-keys` (PR 2, which carries PR 1) on 2026-11-01. Markers:
**(V)** read in the code (file named), **(I)** inferred.

## What PR 3 does

Owner callers write a file's shares: one share row per circle, with its permission bits, expiry and the file key
locked with the circle key. A new file's upload can carry its shares. On a Selective Share drive a header write leaves
the share rows alone, and both the query and the read rule let an outside caller in only through a live row of one of
their circles; a file with no rows is the owner callers' alone. Members then read a shared file with their row's
locked file key, write where their row has `Write`, react and comment where it has `React` or `Comment`, sync with the
query batch like every client, and hear of changes on their sockets (sections 6 to 10, what was planned as PR 4).

**Changed from the first draft** (2026-11-01, after file-access.md):
- **The read rule belongs to the share half, not the member half.** Without it, the query and the read rule disagree on a Selective Share
  drive: a file the query hides would still be readable by id. That matters for any file `VisibleTo` doesn't block: an
  unencrypted file, which a zero-knowledge drive doesn't refuse (I: no check found). The app's circles grant drive
  `Read` (selective-share-plan.md, decision 1) **(V)**, so their members pass steps 1 to 4 of a read by id.
- **The required group keeps its meaning** (decision A, decided 2026-11-01), instead of "always `Connected`, set by
  the server". Filesync's drives are already Selective Share (`filesync` `hbsync-homebase/src/app.rs:70`) **(V)**, in
  production since v0.1.974, and Filesync uploads every file with a required group of `Owner` (`drive/wire.rs:601`)
  **(V)**: setting `Connected` only on new files would have left every existing one unshareable. Now every file starts
  owner-only, and sharing it lifts it to `Connected` (section 1).

## 1. The header on a Selective Share drive

- **The ACL names no one:** named circles and named identities are refused on upload and update
  (`FileSystemStreamWriterBase.ValidateUploadCoreAsync`, `FileSystemUpdateWriterBase.ValidateUploadCore`) **(V for the
  places)**: the share rows say who. Filesync names none today **(V, `wire.rs:601`)**.
- **The required group** (decision A): two of the ladder's rungs mean something here, and the others are refused on
  upload and update:
  - **`Owner`**, what Filesync sends and the default for an upload that names no ACL (`UploadFileMetadata.cs:19`)
    **(V)**: owner callers only, whatever share rows the file has;
  - **`Connected`**: an outside caller also needs a live share row (section 2). With no live row the file is still
    the owner callers' alone.
  - **Sharing lifts the file:** a share write on an `Owner` file sets it to `Connected` in the same transaction
    (section 3). No migration, and no client change: existing files are shared one by one, when the owner shares them.
  - **Withdrawing the last share** leaves `Connected`: with no live row the file is owner callers' only anyway. An
    owner caller who writes the header with `Owner` again makes the file owner-only at once, whatever its rows say.
  - `Anonymous`, `Authenticated` and `AutoConnected` are refused for now: a zero-knowledge drive is never public
    (`CreateDriveRequest.AssertSharingModel`) **(V)**, an encrypted file can't take the two lowest anyway
    (`FileSystemStreamWriterBase.cs:565-576`) **(V)**, and below `Connected` an unreviewed connection could read an
    unencrypted shared file, against "a share requires `Connected`" (decided 2026-10-31). The ladder stays, so they can
    be allowed later (links).
- **A header write leaves the share rows alone:** `SaveFileHeaderAsync` tells `BaseUpsertEntryZapZapAsync` not to
  reconcile on a Selective Share drive (a `reconcileAcl` flag, so a null list can't come to mean two things). A soft
  delete keeps the rows, so a member syncing sees the tombstone; a hard delete and the drive purge still delete them
  (`MainIndexMeta.cs:50`, `DeleteFilesAsync`) **(V)**.
- **No `IsCollaborativeChannel` attribute and no `OwnerOnly` option on a Selective Share drive:** both refused at
  creation and when changed. The attribute's edit rule is what a share row's `Write` bit says, and its other effects
  move copies, which a zero-knowledge drive never sends (file-access.md, "Collaborative channels"). `OwnerOnly`
  requires a required group of `Owner` on every file (`FileSystemStreamWriterBase.cs:599`) **(V)**, so such a drive
  could share nothing. A zero-knowledge drive is already never public, CDN-enabled or subscribable
  (`CreateDriveRequest.AssertSharingModel`) **(V)**.

## 2. The read rule and the query on a Selective Share drive

**One rule, in both places:** an owner caller or `System` passes, as today; an outside caller passes only with a
**live row** (`Read` set, not expired) for one of their circles or for their OdinId's hash, **and** a level that
reaches the file's required group, as everywhere: so never for an `Owner` file, and for a `Connected` one only as a
reviewed connection (an unreviewed one counts as `Authenticated`) **(V for the level rule, `ReviewedSecurityTier`)**.
A file with no live row for the caller is refused. On every other drive nothing changes.

**The read rule** (`DriveAclAuthorizationService.CallerMayReadAsync`): on a Selective Share drive its ACL part reads the
caller's share rows instead of the ACL's names. It is synchronous on the header today **(V)**; it becomes async and
reads the rows. For a page of query results the rows are read once for the page (`… WHERE fileId IN (page) AND
aclMemberId IN (caller's circles)`), not per file. `VisibleTo` is unchanged in PR 3, so a client-locked file still
reaches no member.

**The query's ACL filter stays as it is on every other drive** (built 2026-11-01, instead of the first draft's
`EXISTS` rewrite of every drive's filter): its index rows are all live, so the live-row condition would change no
answer there, and leaving the SQL alone keeps every other drive's query plan as it is. On a Selective Share drive an
outside caller's query takes its own branch (`QueryBatch`, `aclRowsRequired`), below.
- **Expiry and the query cache:** a cached page can outlive a share's expiry by up to 2 hours (`QueryBatchCached`,
  `DefaultTtl`) **(V)**. On a Selective Share drive an outside caller's query isn't cached.
- **One read per page:** the read rule's live rows for a page are read once (`FilesSharedWithCallerAsync`,
  `TableDriveAclIndex.GetLiveRowsAsync`), and a read by id reads them for its one file.

**On a Selective Share drive an outside caller's query starts from their share rows** (decided 2026-11-01). A
Selective Share drive shares a few of many files; a drive whose files are mostly shared is a `Collaborative`
(community) drive. So the query doesn't walk the drive's files checking each for a row: it takes the files with a row
for one of the caller's circles, then pages them as any query batch does:

```sql
SELECT … FROM driveMainIndex m
WHERE m.identityId = @identityId AND m.driveId = @driveId AND <the query's other filters>
  AND m.fileId IN (SELECT r.fileId FROM driveAclIndex r
                   WHERE r.identityId = @identityId AND r.driveId = @driveId AND r.aclMemberId IN (@circles…))
  AND (m.modified, m.rowId) > @cursor
ORDER BY m.modified, m.rowId LIMIT @n
```

- **The cost follows the caller's shares, not the drive:** the PR 1 index finds the circles' rows by its
  `(identityId, driveId, aclMemberId)` prefix, each file is found by its key, and only those files are sorted (I:
  `EXPLAIN` on seeded data, both databases, is a test step).
- **Every row counts here, live or not.** A file with a live row for the caller comes back whole; a file with only a
  withdrawn or expired row, or one an owner caller set back to `Owner`, comes back as "no longer shared" (section 8).
- **The query batch is unchanged for clients:** same request, same modified cursor, same pages.

## 3. Writing shares

**API** (V2):

| Method and route | Does |
|---|---|
| `GET /api/v2/drives/{driveId}/files/{fileId}/shares` | the file's share rows, withdrawn shares included |
| `PUT /api/v2/drives/{driveId}/shares` | a batch of `{fileId, circleId, permissions, expiresAt, lockedFileKey}`, all or none, up to 100 |
| `DELETE /api/v2/drives/{driveId}/files/{fileId}/shares/{circleId}` | withdraws one: no bits, no locked file key, `modified` moved |

One `PUT` for one share or a hundred: the job that shares a folder's existing files makes the same call.

**Who may:** the owner console, or the owner's app whose app id is both the drive's `AppId` and the circle's `AppId`
**(V for both columns)**, as PR 2's circle-key check does (`CircleMembershipService.GetCircleCallerMayManageAsync`)
**(V)**. The circle is always the drive's app's own, for the owner console too: its members' copies of the circle key
are sealed to their matching drive of that app (built 2026-11-01). The caller also goes through a write's usual drive
checks (file-access.md, "Writing"): the drive isn't archived, and the caller holds drive `Write`; listing a file's shares
needs drive `Read`. Members write files (section 7), not shares, except on their own new files.

**Checked for each entry, before anything is written:**
- The drive is Selective Share; the file exists on it, isn't deleted and is a file, not a comment (a comment follows its
  file's shares, section 10).
- The circle belongs to the app, is enabled, grants the drive `Read` and `Write` (decision 1 of the plan), and holds its
  share key, since the file key is locked with it (`CircleShareKeyNotSet`).
- `permissions` isn't 0 (0 is a withdrawn share, written only by `DELETE`), holds only `Read`, `Write`, `React` and
  `Comment`, and includes `Read`: no upload-only rows yet (decided 2026-11-01). The index rows V1 makes from an ACL on
  other drives always mean `Read`, as today; V1 gets no share API.
- `expiresAt` is null or later than now.
- `lockedFileKey` is exactly 45 bytes and starts with format byte 2 (decision 7), when the file is encrypted; absent
  when it isn't.

**Stored** with one upsert per row, in one transaction (insert, or update bits, expiry and locked file key; the generated
`modified = MAX(modified + 1, now)`) **(V for the generated rule)**. The file's existence is checked in the same
transaction, with its row locked on Postgres as `TableDriveMainIndex` does (`:391`) **(V)**, so a hard delete running
alongside can't leave a share behind.

**The file's `modified` moves, its version tag doesn't** (decision 6, amended 2026-11-01): a targeted update of
`driveMainIndex.modified` (`MAX(modified + 1, now)`), once per file the batch touches, in the same transaction.
**For an `Owner` file the same update lifts it to `Connected`** (decision A): the `requiredSecurityGroup` column and
the ACL in the header's JSON (`hdrServerData`), read and rewritten in the transaction with the file's row locked.
That is a header update that keeps the version tag, which doesn't exist today (`UpsertAllButReactionsAndTransferAsync`
always sets a new one) **(V)**: a new targeted method beside it. Keeping the tag is safe here: an `Owner` file had no
member who could be editing it. Owner
callers' other devices see the file in their ordinary sync and read its shares with `includeShares`. Then the drive's
caches are cleared after commit, through a new `MainIndexMetaCached` method, as every header write does
(`MainIndexMetaCached.cs:24-84`) **(V)**. No `DriveFileChangedNotification`: a share raises its own event (section 9).

## 4. Shares in the upload

- A new file's upload may carry `shares`, the same entries as the `PUT`, on `UploadFileDescriptor` next to
  `EncryptedKeyHeader` (key material together, encrypted with the shared secret) **(V for the descriptor)**, not in
  `UploadFileMetadata`, which belongs to the header.
- Checked with the upload's other checks, before any payload is moved, and written in the header's transaction, so a
  file is never visible half shared.
- Updates and overwrites carry no shares: they keep the file key (decision 7), so the share rows stay valid.
- **Built (2026-11-01):** the entries name no file (they are the upload's), and the same checks as the `PUT` run
  (`DriveShareService.AssertUploadMayShareAsync`), so only the owner console or the drive's app uploads with shares. A
  file sent as `Owner` with shares is stored `Connected`, as a share lifts it. The rows are written through
  `CommitNewFile`'s second-row hook, inside the header's transaction, which also deletes the copied payloads if it fails.
  Only standard files take shares; a comment follows its file's (section 10).

## 5. What owner callers see

**`includeShares`, asked for, never sent by default:** a query (`QueryBatch`, smart batch, the change feed) and a read
by id take it. With it, each returned file carries `shares`: its live rows as `{circleId, permissions, expiresAt,
modified}`, without the locked file keys, which only a member needs. One grouped read per page (`… WHERE fileId IN
(page)`), only when asked, only for owner callers, only on a Selective Share drive; without it nothing changes and
nothing is read. A client draws what it wants from it (shared, editable by others, expiring), and nothing is stored
that an expiry could make stale.

The header's ACL names no one on these drives. The `GET` lists one file's share rows, withdrawn shares included.

## 6. Members read

- **`VisibleTo` lets a member in** (`ZkDriveFiles.VisibleTo`): on a Selective Share drive a client-locked file is
  visible to a caller holding the drive key, as today, **or** to an outside caller with a live row (section 2).
- **What a member gets:** the header with their live row's locked file key in place of the key header: they can't open the
  file's own `sealed` or `zkDrive` key header, and the locked file key opens with their circle key. With live rows in more
  than one of their circles, the row of the lowest circle id, so every read picks the same one. Payload and thumbnail
  reads carry no key header for a client-locked file **(V, zero-knowledge-drives.md)**: the client uses the file key and the
  payload's IV, unchanged.
- **Nothing about other shares:** outside callers never get the header's server metadata (`CreateClientFileHeader`)
  **(V)**, so a member sees only their own row: its bits and expiry come with the header, for the client to show.
- **Through the Peer API:** a member reads via their own server, which asks the owner's. A client-locked key header is
  passed through as stored today (`PeerDriveQueryService`, zero-knowledge-drives.md) **(V)**; the row's locked file key goes
  the same way **(I, to check when built)**.

**Built (2026-11-01):**
- The member's key header is a new type, `share` (`EncryptionType.Share`): the row's locked file key with the file's
  own IV. It is only ever sent: every write refuses it (`ZkDriveFiles.AssertDriveTakes`), and the member's own server
  passes it on as it does a client-locked one (`PeerDriveQueryService.ReEncrypt`).
- The member's header carries their row in `shares` (circle, bits, expiry), so their client knows which circle key
  opens it. The read rule hands each file's chosen row to the header (`FilesSharedWithCallerAsync`), one read a page.
- A payload or thumbnail of a client-locked file has no key header, and now crosses the Peer API without one: the
  owner's server sends none, the member's server reads none and forwards none. Before, the member's server expected
  one and failed (I: not tested before; tested now).
- Sockets: a client-locked file still reaches only a socket holding the drive's key; members hear of shares in
  section 9.

## 7. Members write

The rules from selective-share-plan.md, "Who may write" and "A member adds a file to a shared folder":
- **Updating or deleting a file** needs, besides drive `Write` and passing the read rule, a live row with `Write` for
  one of the writer's circles. Owner callers need none.
- **Adding a file to a shared folder** needs that on the folder: Filesync's folders are files, and a file names its
  folder as its `GroupId` **(I, Filesync's design)**. The new file's own key header is `sealed` to the drive's
  write-only key, as deposits are today **(V)**; its shares are for circles the writer is in, only on files written in
  that request. Shares for the folder's other circles are added later by an owner caller's device, the same job as
  sharing existing files.
- **Editing an existing file** keeps the file key and the stored key header (decision C, decided 2026-11-01). The
  member's update sends a new IV instead of a key header; the server keeps the stored key header and stores the new IV
  with it. Today an update of a client-locked file must send a whole client-locked key header with a new IV, and the
  server compares the two (`ZkDriveFiles.AssertFitsUpdate`) **(V)**; a member can send neither kind (no drive key, no
  write-only private key). The server can't check that the member kept the file key, as it can't for any client-locked
  update **(V, the same comment)**: a member with `Write` can write garbage, and that follows from zero knowledge.
- **The member's own server must find the file** when it updates or deletes on their behalf: the lookup runs as the
  member, and today's read rule refused them (zero-knowledge-drives.md, "Known limit") **(V)**. The read rule of section
  2 plus section 6 should let it find the file **(I, test it)**.

## 8. Members sync with the query batch

**A member syncs a shared drive the way every client syncs a drive: the query batch with a modified cursor**, over
the Peer API (decided 2026-11-01). A member with access to 7 files on the owner's Filesync drive pages through those 7
and sees them appear in their own Filesync; nothing else is built for them.
- **A new share** reaches them because the share write moves the file's `modified` (section 3).
- **A withdrawn share is its own signal** (decision E): what a soft delete's tombstone is to a file, a withdrawn row
  is to a share. The row stays with no bits and no locked file key, and the withdrawal moves the file's `modified`, so the member's next page holds that file as **"no longer shared"**: its ids
  and `modified`, no content, no key. Proposed form: the tombstone a deleted file already gets (`FileState.Deleted`),
  which sync clients already remove locally (I: to check against Filesync and odin-js). The same answer when an owner
  caller sets the file back to `Owner`.
- **Expiry needs no signal:** the member's client holds the share's `expiresAt` (it comes with their row) and drops the
  file when it passes; the server refuses it from then on (section 2).
- **No separate "my shares changed since" query** until a client needs one (decided 2026-11-01). The PR 1 index serves
  the query batch above.

## 9. Socket events

- **Today** a header write raises `DriveFileChangedNotification`, which device and peer sockets filter by the read rule
  (`ClientDriveNotification.VisibleToAsync`, `PeerAppNotificationDispatcher`) **(V)**.
- **A share write** raises a new `DriveShareChangedNotification` per file: owner callers' devices hear every one; a
  member's peer socket hears those for their circles' rows, a withdrawal included, judged by the row rather than the
  read rule (which a withdrawn member no longer passes) **(I, the design; to settle when built)**.

## 10. Members react and comment

Decision B (2026-11-01): a share row's `React` bit lets its members react to the file, its `Comment` bit lets them
comment on it (a photo, a document).
- **The row alone grants it:** on a Selective Share drive a caller needs no drive `React` or `Comment`; the app's
  circle still grants the drive `Read` and `Write` only (section 3). Today a reaction needs drive `React` and a comment
  drive `Comment` (file-access.md, "Special cases") **(V)**; on these drives both checks become "a live row with that
  bit for one of the caller's circles", and owner callers pass as today. Other drives keep their checks, and #2058
  stays Todd's.
- **A reaction** is checked against the reacted file's rows, locally and from a peer: today a peer's reaction finds the
  file with a drive check only (`ResolveFileId`) **(V)**, so on these drives that path asks for the row too. Who may see
  a file's reactions is who may read the file.
- **A comment is readable exactly when its file is:** owner callers, and anyone with a live row with `Read` on the file
  it comments on. A comment has no share rows of its own: the read rule and the query ask the commented file's rows, so
  a withdrawal or an expiry hides the comments with the file **(I: how the query reaches the commented file's rows, by
  `ReferencedFile` or the comment's group, is settled when built)**. Today a peer's comment takes the ACL of the file
  it comments on, looked up as the commenter (`IncomingCommentAcl`) **(V)**; on these drives the comment's ACL names
  no one, as every file's there.
- **A comment is encrypted with the commented file's file key** and an IV of its own: everyone who may read the file
  already holds the key, so no row is written for it. Its key header says so and holds only the IV (a new form, named
  when built); the server checks only that a comment on an encrypted file uses it. An unencrypted file's comments are
  unencrypted, as today (`IncomingCommentAcl`: encryption must match) **(V)**.
- **Writing a comment** needs a live row with `Comment` on the commented file, in place of drive `Comment` **(I: the
  rules for editing and deleting a comment are read when built, and keep their meaning)**.

## Tests

- **Storage, both databases:** the filter (no rows: hidden on Selective Share, shown elsewhere; an expired row, a row
  without `Read` and another circle's row: hidden); a header write on a Selective Share drive keeps the share rows
  (update, overwrite, soft delete) and a hard delete deletes them; the share upsert moves `modified` and keeps
  `created`; the file's `modified` moves and its version tag doesn't; `AnAclFilterWorksWithEverySortAndFilter` passes
  unchanged.
- **The read rule:** on a Selective Share drive, an unencrypted file read by id by a member of a granting circle: refused
  with no row, with a row without `Read`, with an expired row and when unreviewed; allowed with a live row. On another
  drive, the same reads answer as today.
- **The required group:** a share on an `Owner` file lifts it to `Connected` with the version tag unchanged; an
  `Owner` file with live rows is refused to members; `Anonymous`, `Authenticated` and `AutoConnected` are refused on
  upload to a Selective Share drive; the last withdrawal leaves `Connected` and the file owner-only.
- **Members (V2 and Peer API):** a member reads a client-locked file with their row's locked file key, and is refused with no
  live row; with two circles' rows they always get the same one; a member updates a file with a `Write` row and is
  refused without; adds a file to a folder with a `Write` row; their own server finds the file for an update; the
  member's query batch pages exactly their shared files, and a withdrawn share comes back as "no longer shared"; a share write reaches the owner's devices and the
  member's peer socket.
- **V2 integration:** who may write shares (the owner console and the owning app; refused: another app, an app's circle
  on another app's drive, an owner circle, a circle without a key, an archived drive, a drive that isn't Selective
  Share); each entry check; a batch with one bad entry stores nothing; an upload with shares stores the file and its
  rows, one with a bad share stores neither; the next owner query sees a share (cache cleared); a withdrawn share keeps
  its row; `includeShares` returns the live rows and nothing without it.
- **Reactions and comments:** on a Selective Share drive a member reacts and comments with a row's `React` and
  `Comment` bits, and is refused without them, whatever the drive grants; a comment is read by a member with a live
  `Read` row on its file and by no one else outside, and is hidden once that row is withdrawn or expired; a comment on
  an encrypted file must use the file's key form. On other drives reactions and comments answer as today.

## Decided (2026-11-01)

- **B. A share row's `React` and `Comment` bits** let its members react to and comment on the file; the row alone
  grants it; a comment is read by whoever may read its file and is encrypted with the file's key; in PR 3 (section 10).
- **C. A member editing a file keeps the file key and the stored key header** (decision 5 of the plan): the update
  sends a new IV only (section 7). The server can't check the member kept the file key; a member with `Write` can
  write garbage either way, which follows from zero knowledge.
- **D. Withdrawn shares, a deleted circle's share rows and a reset circle key's rows stay as they are** in PR 3. None
  lets anyone in: a withdrawn row has no bits, no one is in a deleted circle, and nobody can open a row locked with a
  reset key (the client shares the file again). Removing them is a later PR's.

- **A. The required group keeps its meaning:** files start `Owner` (owner-only), and the first share lifts a file to
  `Connected`, keeping its version tag (sections 1 and 3).
- **No rows without `Read`** from the V2 share API; V1's index rows always mean `Read`.
- **Shares in results only when asked** (`includeShares`); the `GET` lists one file's share rows.
- **An index on a circle's rows** (`identityId, driveId, aclMemberId, modified, rowId`), in PR 1's unreleased
  migration: a member's query batch starts from those rows (section 2).
- **E. Members sync with the query batch; a withdrawn share row is the withdrawal signal**, as a tombstone is a
  file's (section 8). The member's client holds `expiresAt`. No separate share feed until a client needs one.
- **A share write moves the file's `modified`, not its version tag** (decision 6, amended).
- **One PR.**
- **Dead code goes:** `FileUpdateInstructionSetV2`, never used by the V2 update (it reads `FileUpdateInstructionSet`)
  **(V, read by a subagent)**.

## To decide

Nothing (2026-11-01): A to E are decided. Points marked **(I)** above are settled as they are built.

## Not in PR 3
- Removing withdrawn shares, and the rows of deleted circles and reset keys (decision D).
- Sharing with one identity, and links: decisions 3 and 4 of the plan.
- The clients: odin-js, chat-kmp and filesync (the plan's item 5).

## Found on the way, not PR 3's

- **Peer reactions skip the read rule:** asked in #2058 (may be deliberate, #1977).
- **The anonymous home-page cache** runs as its first caller: hardening asked in #2060.
- **`QueryModifiedAsync` ignores its `MaxDate`:** obsolete, V1 only; left alone (file-access.md, "Kept, retiring,
  retired").
- **An anonymous caller's query has no SQL row condition** (`DriveQuery.GetAcl` returns null for an empty list); the
  per-result read rule drops what they may not see, so only a page can come back short **(V)**.
