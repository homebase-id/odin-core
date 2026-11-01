# Selective Share: sharing files on a zero-knowledge drive

**Status:** plan. PR 1 (the database) is built and in review; the rest is not built. The design is in
[zero-knowledge-drives.md](zero-knowledge-drives.md#selective-share); this plan says how to build it.
Markers follow [security-model.md](security-model.md): **(V)** verified in the code (file named), **(I)**
inferred.

## The model in short

- **Apps own circles.** "`AppId` on a circle is the owning app; null = owner circle. An app may change only its
  own circles and can't enrol anyone into an owner circle" (security-model.md) **(V)**. Filesync shares a
  folder with Filesync's Family circle, and only Filesync adds or removes its members.
- **Each circle gets a circle key** (random, AES-256), made on a device of the owning app the first time the app
  shares with the circle.
- **The circle row keeps the circle key locked with the owning app's key** (`AppZkK`, one per app,
  zero knowledge: the server keeps it only locked with the zero-knowledge master key) **(V)** (owner-keys.md).
  Every device of the app can open it, so any of them can share new files and add members. The owner console
  reaches it too, through `AppZkK`. The master key is never handed to an app.
- **Each member row keeps the circle key sealed to that member** (decision 1 says to which key). A member who
  leaves takes their copy with them: the row is deleted.
- **A share is the file's ACL index row.** Today `DriveAclIndex` has one row per circle or person a file's ACL
  names, so a query can find "files Family may read" **(V)**. On a Selective Share drive that row is the share:
  it says who may do what with the file (`permissions`), until when (`expiresAt`) and how to decrypt it
  (`lockedKeyHeader`, the file's key header locked with that circle's key), and it keeps its own `created` and
  `modified`. The file's content, its own key header (locked with the drive key) and its payloads are not
  touched.
- **A member reads** by opening their sealed copy of the circle key on their own device, then their circle's
  share row, then the file. Neither server opens anything.

Example: three files, two shared with Family (two members). The two files each have a Family row in
`DriveAclIndex`, with the file's key header locked with Family's key. The Family circle row has the circle key
locked with Filesync's key. Each of the 2 member rows has the circle key sealed to that member. No new table.

### Why the ACL index rows, not the ACL in the header and not a table of its own

- **The rows already exist.** The ACL lives in the file's header, in the server data
  (`ServerMetadata.AccessControlList`, stored in `DriveMainIndex.hdrServerData`) **(V)**, and `DriveAclIndex` is
  the index built from it, one row per named circle or person **(V)**. A separate share table would be a copy of
  `DriveAclIndex` with more columns, so the shares go into `DriveAclIndex` itself.
- **Not in the header's ACL as well.** Two copies could disagree, and keeping them equal would make every share
  change a write of the file's header (see "Changing a share"). On a Selective Share drive the header's ACL keeps
  only `RequiredSecurityGroup` (Connected for a shared file); the rows are the record of who the file is shared
  with.
- **What it buys:**
  - Two devices sharing the same file with different circles at the same moment don't conflict.
  - No size limit: the header's server data is at most 16 KB **(V)**; rows have none.
  - A member asks "which of my shares changed since X" by the rows' own `modified`.
- **Cleanup comes free:** a hard delete already clears the file's `DriveAclIndex` rows (`MainIndexMeta`) **(V)**.
- **A header write keeps the rows it doesn't change** (PR 1, below).
- **Moves with the identity:** the identity move's export and import carry `DriveAclIndex` rows column by column,
  `created` and `modified` included (`IdentityDatabase.Export.Generated.cs`, `TableDriveAclIndexCRUD.ImportRowAsync`)
  **(V)**.

## The share row

`DriveAclIndex`, one row per file and circle (later per person or link, decisions 3 and 4):

| Column | Type | Meaning |
|---|---|---|
| `aclMemberId` | Guid | the circle (and later a link's id, decision 4) |
| `permissions` | Int32, not null, default 1 | what the circle may do with this file, in `DrivePermission` bits (Read 1, Write 2, React 4, Comment 8), narrowing the drive grant per file: Read, and Write to update it or, on a folder, add files to it (see "Who may write"). No bits = a withdrawn share. Every row before Selective Share means read, hence the default |
| `expiresAt` | UnixTimeUtc (ms), null | when this share of this file ends, so queries skip expired shares. NULL = never |
| `lockedKeyHeader` | ByteArray (≤ 128), null | the file's key header locked with the circle key: key header version 2, the file key only, 45 bytes (see Blob formats). NULL on every other drive, and on a withdrawn share |
| `created`, `modified` | UnixTimeUtc (ms), not null | the share's own times, apart from the file's (the generator's `FinallyAddCreatedModified`): the cursor a member syncs their shares by. `modified` moves on every change of the row, `MAX(modified + 1, now)` like every generated table **(V)** |

- **Each reader sees only their own circle's row.** When a file's ACL is returned, the server builds its circle
  list from the rows: all of them for the owner, only the caller's own for a member. The server already clears
  `OdinIdList` before a file goes to a peer, so a recipient never learns who else was named **(V)**; the rows are
  cut the same way.
- **What `expiresAt` can and can't do:** the server enforces it, so after it a member can no longer fetch the
  file through that row, and a query skips it (filtered in SQL), so a query never lists what a read refuses. A
  member who already read the file keeps what they read, and their device may have kept the file key: expiry
  stops access, it doesn't take back what was opened.
- **Withdrawing a share leaves the row:** no key header, no permission bits, `modified` moved. A member syncing by
  `modified` sees it go; a deleted row would vanish without a trace. A row with no bits grants nothing on any drive,
  once every read and query checks the Read bit, which must happen before such a row is written (PR 1, below).
  Something still has to remove withdrawn rows in the end, and a member offline longer than they are kept needs a
  full resync (open, PR 3).
- **A file with no rows is open to its whole security group:** a query lists a file with no `DriveAclIndex` rows
  to every caller in its `RequiredSecurityGroup` (`QueryBatch.SharedWhereAnd`, `cir.fileId IS NULL`) **(V)**. So
  when a file's last share is withdrawn and its row later removed, a shared file (Connected) would open to every
  connection. PR 3 must close that: on a Selective Share drive, either no rows means owner only, or removing the
  last row sets the file back to Owner.

## Changing a share: neither the version tag nor `modified`

**Decided (2026-10-31, decision 6):** sharing or unsharing a file writes only its share row. It changes who may
read the file, not the file, so the file's version tag stays (a member editing the file at that moment must not get
a conflict, the same reason a `sealed` → `zkDrive` transform keeps it) and so does its `modified` (the owner's own
devices gain nothing from fetching the file again, and a bulk share of a folder would make every one of them
re-fetch it). Members learn of shares and unshares from the rows' own `modified`: a file shared with Family
today, last modified a year ago, sits behind every member's file cursor, but its new row doesn't.

**What today's code does:** every ACL change gives the file a new version tag and moves `modified`. There
is no way to change only the ACL.

- Every path that changes a file's ACL writes the whole header through one method,
  `TableDriveMainIndex.UpsertAllButReactionsAndTransferAsync`, which always sets a new `hdrVersionTag` (it
  refuses to keep the old one) and moves `modified` **(V)**. `MainIndexMeta.BaseUpsertEntryZapZapAsync`
  rewrites the `DriveAclIndex` rows in the same transaction **(V)**.
- No endpoint or service method changes only the ACL. The cheapest today is a metadata-only update, which
  still needs the full metadata and, for an encrypted file, a new IV and key header (read by a subagent, not
  re-checked).

**PR 3 adds the share writes:** a row's insert, update and withdrawal, each on its own, with the drive cache
cleared as `MainIndexMetaCached` does for a header write. The file's header isn't written at all. Not yet checked:
whether anything that listens for a changed file needs to hear about a share.

## PR 1: the database

All made by the SQL generator (`../Odin-SQLite-Generator`), with the generated CRUD and migrations in odin-core.
The generator's migration copies every existing row, as #38 added the drive columns. Every new column is
nullable or has a value for existing rows: `permissions` its default, `created` and `modified` the migration's
time (none of the existing rows is a share yet).

| Table | New column | Type | Meaning |
|---|---|---|---|
| `Circle` | `appKeyLockedShareKey` | ByteArray (≤ 512), null | the circle key locked with the owning app's `AppZkK`. NULL until the app first shares with the circle |
| `CircleMember` | `sealedShareKey` | ByteArray (≤ 2048), null | the circle key sealed to this member. NULL until a device of the owning app seals it (a new member waits for the app's next sign-in, like the app-key backfill) |
| `DriveAclIndex` | `permissions`, `expiresAt`, `lockedKeyHeader`, `created`, `modified` | | see "The share row" |

**The query batch names `driveMainIndex` on every column it uses.** It joins `DriveAclIndex` when the caller's
circles are filtered (`QueryBatch.SharedWhereAnd`) **(V)**, and with `created` and `modified` in both tables an
unnamed one is ambiguous.

**A header write reconciles the file's rows** instead of deleting all of them and inserting them again from the
ACL, in the same transaction as the header (`MainIndexMeta.BaseUpsertEntryZapZapAsync`,
`TableDriveAclIndex.ReconcileRowsAsync`) **(V)**: rows of circles and identities no longer named go, new ones are
added, and a row that stays is not touched, so it keeps its times (and would keep its key header, bits and
expiry). A member named twice gets one row; before, the write failed on the unique key. On a Selective Share drive
the header's ACL names no circles, so reconciling against it would delete every share: there a header write must
leave the rows alone entirely (PR 3).

**Before any row is written without Read** (PR 3, e.g. an upload-only folder), every query that reads through
`DriveAclIndex` must check the Read bit, or a write-only row would let its circle list the files.

**Who owns a row's bits and expiry depends on the drive** (PR 3). The reconcile leaves a staying row's
`permissions` and `expiresAt` alone. On a Selective Share drive that is the point: the row is the share. On any
other drive the header's ACL is the record, and the delete-and-insert used to reset both on every header write; so
if anything ever narrows a row there, the header write must reset it, or the header and the index disagree.
Nothing writes them yet **(V)**.

## Blob formats: key header version 2 from the start

The share rows' key headers are new, so they use key header version 2 (#2024) for the locking from day one; there is no
version 1 to stay compatible with.

- **`lockedKeyHeader`:** AES-GCM under the circle key, a random 12-byte nonce, and the plaintext
  `FileKey` alone, no IV. Laid out as `version (1) ‖ nonce (12) ‖ ciphertext (16) ‖ tag (16)`, 45 bytes.
  - **The IV is the file's own:** a member decrypts `appData.content` with the visible `Iv` of the file's own
    key header, which a zero-knowledge drive returns as stored **(V)**. So a row stays valid across every update
    that keeps the file key (decision 7). The IV was never secret: in version 1 it cancels out of the key header, so the visible `Iv`
    is what every reader has always used **(V, #2024)**. `localAppData` and payloads carry IVs of their own
    already **(V)**.
  - **Fixes the equality problem of version 1 for these rows:** the random nonce means two rows with the
    same file key don't look alike.
  - **AAD binds the key header to its row:** `version ‖ driveId ‖ fileId ‖ circleId`. The server can't move it
    to another file or another circle without the tag failing.
- **`appKeyLockedShareKey`:** the same AES-GCM layout under `AppZkK`, AAD `version ‖ circleId`.
- **`sealedShareKey`:** the `sealed` key header's ECDH and HKDF (P-384) with AES-GCM, AAD
  `version ‖ circleId ‖ memberId`, plus the hint of the drive key it was sealed to.
- **Not fixed by this:** the second half of #2024, integrity of the file's content (`appData.content`,
  payloads), which stay AES-CBC with no MAC. The file's own key header (locked with the drive key) also
  stays version 1 until #2024 is built.
- One golden vector for all three, checked in odin-core, odin-js, chat-kmp and filesync, as
  `ClientLockedKeyHeaderVectorTests` **(V)**.

## Later PRs

2. **Circle keys.** The owning app's devices set the circle key (once) and seal it for members. Only the
   circle's owning app; the circle must grant the drive. A member row's sealed copy goes when the member leaves.
3. **Shares as ACL index rows.** The share writes (see "Changing a share") and their validation (no share
   without bits from a client; only a circle that grants the drive); a header write on a Selective Share drive
   leaving the rows alone, and its ACL holding no circles; shares carried in an upload so sharing a new file is
   one request; the ACL returned built from the rows, a reader seeing only their own; the Read bit and expiry
   checked by every read and query; a member's "shares changed since" query by the rows' `modified`; when
   withdrawn rows are removed; and a file with no rows left staying owner-only (see "The share row").
   - **The query's ACL filter as `EXISTS`:** today a `LEFT JOIN` with `cir.fileId IS NULL OR cir.aclMemberId IN
     (…)`, under a `SELECT DISTINCT` that exists only for the join **(V)**. Once the Read bit and expiry join the
     filter, `NOT EXISTS (any row) OR EXISTS (a live row for my circles)` says plainly that a file whose rows are
     all expired is not "rowless", and drops the `DISTINCT`. Check the plan on SQLite and Postgres (`EXPLAIN`).
4. **Member reads and writes.** The read rule lets a member read a client-locked file with a share row for one
   of their circles; the write rules in "Who may write". The Peer API passes it all through as stored.
5. **Clients.** odin-js, chat-kmp and filesync crypto against the golden vector; Filesync's share UI.
6. **Links**, once decision 4 is made.

## Who may write

The app's circles have Read and Write on the whole drive (decision 1). The drive grant is generic; the ACL
narrows it per file, as it already does for reading **(V)**. Today nothing narrows writing per file **(V)**
(`AccessControlList`: security group, circles, identities), so the Write bit in a share row's `permissions` is new:

- **Updating or deleting a file** needs a share row for one of the writer's circles with Write.
- **Adding a file to a folder** needs that on the folder (Filesync's folders are files, and a file names its
  folder as its group **(I)**, from the Filesync design).
- **A writer may only add shares for circles they belong to**, only on files they write in that request.
  Every other share is the owning app's.

## A member adds a file to a shared folder

Michael shares a folder with Family, with read and write. John, a Family member, adds a file. John's Filesync
app does all of it in the upload, with no help from Michael's devices:

1. **The file**, encrypted with a new file key.
2. **The file's own key header, `sealed` to Michael's drive's write-only public key.** John has no DrvK, so
   he deposits, as senders do today **(V)** (design doc, "One write rule"). Michael's devices may later
   transform it to `zkDrive`.
3. **Family's share:** the key header locked with the Family circle key, which John's device opened
   from his own sealed copy. Every Family member can read the file at once.

**When the folder is shared with more than one circle** (Family and Friends, John only in Family), John
can't make the Friends share. A device of Michael's Filesync adds it later: it opens John's `sealed` header
with DrvK and adds the share. That is the same catch-up job as sharing existing files. Until then Friends
don't see John's file.

**John edits an existing file.** He keeps the file key (he knows it from the Family share), encrypts the new
content with it and leaves the key headers alone. Two things are needed:

- **A rule to update content without the key header.** Today a `sealed` header replaces only a `sealed` one
  **(V)** (design doc), so once Michael's device has transformed John's file, John can't send a new key
  header. Updating content while keeping the stored key header is new, and a security decision (decision 5).
- **The member's own server must be able to find the file** (design doc, "Known limit" **(V)**): when John
  updates or deletes, the lookup on Michael's server runs as John, and today's read rule lets only the owner
  read a client-locked file, so John's update finds nothing. PR 4 widens the read rule to a member whose
  circle has a share row for the file; that should make the same lookup find it **(I)**, to be checked when PR 4
  is built.

## Decisions for the user

1. ~~What a member's copy is sealed to~~ **Decided (2026-10-30):** the member's matching drive of the
   sharing app: the drive of the same app and the same drive slug on the member's identity, its write-only
   public key. Filesync shares seal to the member's Filesync drive, Photos to their Photos drive. If an app
   shares two of its drives with one circle, it picks one of them as the drive the circle key is sealed to.
   Not chosen: a key pair per app on every identity (cleaner, but new key machinery on every identity).
   The drive is browser-made on a zero-knowledge drive. Fetched by slug, never by drive id:
   `GET /api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/public-key`
   (`V2PeerAppDrivePublicKeyController`) **(V)**, which goes through the peer drive key cache from #2045.
   That endpoint requires Write on the target drive (`PeerDriveQueryService.cs`,
   `GetDriveWriteOnlyPublicKeyAsync`) **(V)**. **Decided (2026-10-30):** the app's circles grant Read and Write
   on its drive, so the endpoint works as it is; whether a share is read-only is the Write bit of its row.
2. **Owner circles** (no owning app): not shareable this way, or locked with the master key so only the owner
   console can share with them.
3. **Sharing with one person** outside any circle. The ACL already names people (`OdinIdList`) **(V)**, so a
   person's row could carry the key header sealed straight to their matching drive, with no circle at all;
   or the app makes a small circle of its own for that person, so every share is a circle's.
4. **Links**, for someone not on Homebase (design doc, "Links"): a row with the link's id and the key
   header locked with the link's key. Still open: where the link's own record lives (its expiry, the app's
   copy of its key), a small table of its own or a hidden app circle with no members.
5. **Members editing existing files:** whether a writer may update a file's content while keeping its stored
   key header, and on which drives (see "A member adds a file").
6. ~~Does a share change move `modified`?~~ **Decided (2026-10-31):** no; a share is its `DriveAclIndex` row,
   with times of its own (see "Changing a share").
7. ~~A share row's IV after the file changes.~~ **Decided (2026-11-01):** the row locks the file key only, and
   a member takes the IV from the file (Blob formats). Every update of an encrypted file must carry a new IV: a
   metadata-only update is refused without one (`FileSystemStreamWriterBase`, "You must specify a new IV") **(V)**.
   A row that locked `IV ‖ FileKey` would go stale on the file's next update.
   - **The rule that comes with it:** on a Selective Share drive an update keeps the file key and changes only the
     IV. A client that changes the key must re-lock every share row. The server can't check it: on a
     zero-knowledge drive the client-locked key header is stored as sent **(V)**. Inferred, not read in the
     clients: they already keep the key on update.
   - **Not chosen:** re-locking every share row on each update (a member writer can only re-lock their own
     circles' rows); keeping the IV inside the content blob (a new content format in every client, for nothing
     over the visible `Iv` once tampering by whoever stores the data is out of scope).

## Out of scope

- Key rotation when a member leaves (design doc: not in the first version).
- Collaborative drives: unchanged.
