# Selective Share: sharing files on a zero-knowledge drive

**Status:** PR 1 (columns) and PR 2 (circle keys) built and in review; PR 3 onwards planned. The design is in
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
- **The file's ACL says who may read, who may write, until when, and how to decrypt.** Today the ACL names the
  circles that may read a file **(V)** (`AccessControlList.CircleIdList`). On a Selective Share drive each named
  circle also carries the file's key header locked with that circle's key, an optional expiry and a write
  flag. The file's content, its own key header (locked with the drive key) and its payloads are not touched.
- **A member reads** by opening their sealed copy of the circle key on their own device, then their circle's
  entry in the file's ACL, then the file. Neither server opens anything.

Example: three files, two shared with Family (two members). The two files' ACLs each name Family, with the
file's key header locked with Family's key. The Family circle row has the circle key locked with Filesync's
key. Each of the 2 member rows has the circle key sealed to that member. No new table.

### Why the ACL and not a table of its own

- **The ACL already lives in the file's header**, in the server data (`ServerMetadata.AccessControlList`,
  stored in `DriveMainIndex.hdrServerData`, at most 16 KB) **(V)**, and `DriveAclIndex` is the index built from
  it, one row per named circle or person, so a query can find "files Family may read" **(V)**. A separate share
  table would have been a copy of `DriveAclIndex` with one more column.
- **A reader gets their key with the header**, with no second lookup.
- **Cleanup comes free:** a hard delete already clears the file's `DriveAclIndex` rows (`MainIndexMeta`) **(V)**,
  and the entries go with the header.
- **Costs:**
  - Changing a file's shares rewrites its header (see "Changing the ACL").
  - Two devices changing the same file's shares at the same moment conflict, and one retries.
  - Size: one entry is about 180 bytes of JSON **(I)**, so about 90 shares per file fit in the 16 KB.

## The ACL entry

A new list on `AccessControlList`, used only on a Selective Share drive:

| Field | Type | Meaning |
|---|---|---|
| `circleId` | Guid | the circle (and later a link's id, decision 4) |
| `lockedKeyHeader` | bytes | the file's key header locked with the circle key: key header version 2 (see Blob formats) |
| `expires` | UnixTimeUtc (ms), optional | when this share of this file ends. Empty = never |
| `canWrite` | bool | members may update this file; on a folder, may add files to it (see "Who may write") |

- **One list, not two:** on a Selective Share drive the server sets `CircleIdList` from the entries, so the
  circles that may read and the circles that have a key can't disagree. `DriveAclIndex` is built from
  `CircleIdList` as today **(V)**, unchanged.
- **Each reader sees only their own circle's entry.** The server already clears `OdinIdList` before a file
  goes to a peer, so a recipient never learns who else was named **(V)**; the entries are cut the same way.
- **What `expires` can and can't do:** the server enforces it, so after it a member can no longer fetch the
  file through that entry. A member who already read the file keeps what they read, and their device may have
  kept the file key: expiry stops access, it doesn't take back what was opened.
  - Reads check it at once. Queries go through `DriveAclIndex`, which has no expiry, so either a nullable
    `expires` column there (filtered in SQL), or an expired file may still be listed until a cleanup job
    removes the entry. Proposed: the column, so a query never lists what a read refuses.

## Changing the ACL: `modified` yes, version tag no

Sharing or unsharing a file changes who may read it, not the file. It keeps the version tag: a member
editing the file at that moment must not get a conflict, the same reason a `sealed` → `zkDrive` transform
keeps it.

**`modified` is open (decision 6).** The server data is the server's, so the owner's own devices gain
nothing from fetching the file again; a bulk share of a folder would make every one of them re-fetch it.
But a member does need to notice: a file shared with Family today, last modified a year ago, sits behind
every member's `modified` cursor, so a member syncing by cursor never sees it, and never sees an unshare
either. The member's entry (`lockedKeyHeader`) is also data their client needs, unlike the rest of the
server data.

**What today's code does:** every ACL change gives the file a new version tag and moves `modified`. There
is no way to change only the ACL.

- Every path that changes a file's ACL writes the whole header through one method,
  `TableDriveMainIndex.UpsertAllButReactionsAndTransferAsync`, which always sets a new `hdrVersionTag` (it
  refuses to keep the old one) and moves `modified` **(V)**. `MainIndexMeta.BaseUpsertEntryZapZapAsync`
  rewrites the `DriveAclIndex` rows in the same transaction **(V)**.
- No endpoint or service method changes only the ACL. The cheapest today is a metadata-only update, which
  still needs the full metadata and, for an encrypted file, a new IV and key header (read by a subagent, not
  re-checked).
- **Narrow writes that keep the version tag already exist**, each its own `UPDATE` of its own column:
  - the reaction summary and the transfer history (they move `modified`);
  - `UpdateEncryptedKeyHeaderAsync`, the `sealed` → `zkDrive` transform (it keeps `modified` too) **(V)**.

**PR 3 adds one more narrow write:** `hdrServerData` and `requiredSecurityGroup`, guarded by the current version
tag, the version tag itself kept, with the `DriveAclIndex` rows rewritten in the same transaction and the drive
cache cleared, as `MainIndexMetaCached` does. Whether it moves `modified` is decision 6. Not yet checked:
whether anything that listens for a changed file assumes a new version tag.

## PR 1: the database

All made by the SQL generator (`../Odin-SQLite-Generator`), with the generated CRUD and migrations in odin-core.
Every new column is nullable, so existing rows need no data: the generator's migration adds them, as #38 added
the drive columns.

| Table | New column | Type | Meaning |
|---|---|---|---|
| `Circle` | `appKeyLockedShareKey` | ByteArray (≤ 512), null | the circle key locked with the owning app's `AppZkK`. NULL until the app first shares with the circle |
| `CircleMember` | `sealedShareKey` | ByteArray (≤ 2048), null | the circle key sealed to this member. NULL until a device of the owning app seals it (a new member waits for the app's next sign-in, like the app-key backfill) |
| `DriveAclIndex` | `expiresAt` | UnixTimeUtc, null | copied from the ACL entry, so queries skip expired shares |
| `DriveAclIndex` | `permissions` | Int32, not null, default 1 | what the row's circle or person may do with the file, in `DrivePermission` bits (Read 1, Write 2, React 4, Comment 8), narrowing the drive grant per file. Every existing row means read, hence the default |

**Before any row is written without Read** (PR 3, e.g. an upload-only folder), every query that reads through
`DriveAclIndex` must check the Read bit, or a write-only row would let its circle list the files.

## Blob formats: key header version 2 from the start

The share entries are new, so they use key header version 2 (#2024) for the locking from day one; there is no
version 1 to stay compatible with.

- **`lockedKeyHeader`:** AES-GCM under the circle key, a random 12-byte nonce, and the plaintext
  `IV ‖ FileKey`. Laid out as `version (1) ‖ nonce (12) ‖ ciphertext (32) ‖ tag (16)`.
  - **Fixes the IV problem of version 1 for these entries:** the random nonce means two entries with the same
    file key don't look alike, and the tag covers the IV inside, so whoever stores the entry can't change it.
    A member must use the IV from the entry, never the file's visible `Iv` field.
  - **AAD binds the entry to its place:** `version ‖ driveId ‖ fileId ‖ circleId`. The server can't move an
    entry to another file or another circle without the tag failing.
- **`appKeyLockedShareKey`:** the same AES-GCM layout under `AppZkK`, AAD `version ‖ circleId`.
- **`sealedShareKey`:** the `sealed` key header's ECDH and HKDF (P-384) with AES-GCM, AAD
  `version ‖ circleId ‖ memberId`, plus the hint of the drive key it was sealed to.
- **Not fixed by this:** the second half of #2024, integrity of the file's content (`appData.content`,
  payloads), which stay AES-CBC with no MAC. The file's own key header (locked with the drive key) also
  stays version 1 until #2024 is built.
- One golden vector for all three, checked in odin-core, odin-js, chat-kmp and filesync, as
  `ClientLockedKeyHeaderVectorTests` **(V)**.

## Later PRs

2. **Circle keys** (built on branch `selective-share-circle-keys`).
   - **API** (`V2CircleShareKeyController`, owner or app): `GET`/`PUT /api/v2/connections/circles/{circleId}/share-key`
     (the key, set once), `GET .../share-key/unsealed-members`, `PUT .../share-key/members` (sealed copies, all or
     none). A member: `GET /api/v2/peer/{odinId}/circles/{circleId}/share-key`, which asks the circle owner's server
     (`PeerCircleShareKeyController`, `GET /api/peer/v1/host/circles/{circleId}/my-share-key`).
   - **Rules:** only the owning app or the owner console, never an owner circle; a copy only for an identity
     member and only once the circle has its key; a member reads only their own copy, while the circle grants
     them and is enabled (asked at the time: a peer's cached context doesn't see a disable).
   - **No copy yet is an empty answer** (`200` with no key), so a `404` always means a server that doesn't serve
     the route.
   - **The keys survive other writes:** the generator's `keepOnUpsert` leaves both columns out of the generated
     upsert's and update's SET, and saving a connection reconciles the member's rows
     (`ReconcileMemberCirclesAsync`) instead of deleting and re-inserting them. Only the set-once update writes
     the key, and only the targeted update writes a copy.
   - **The member's public key** comes from #2045's endpoint and cache (`GET /api/v2/peer/{odinId}/apps/{appSlug}/
     drives/{driveSlug}/public-key`): `PublicKeyJwk`, `PublicKeyCrc32` (the key hint in the sealed copy) and
     `IsZeroKnowledge`. The client must not seal to a drive that isn't zero knowledge, whose server could open it.
   - **Moved to PR 3:** "the circle must grant the drive", which is about sharing files from a drive.
3. **Shares in the ACL.** The entry type, its validation, `CircleIdList` set from the entries, an ACL-only
   update that keeps the version tag (see "Changing the ACL"), entries carried in an upload so sharing a new
   file is one request, readers seeing only their own entry, and expiry.
4. **Member reads and writes.** The read rule lets a member read a client-locked file whose ACL has an entry
   for one of their circles; the write rules in "Who may write". The Peer API passes it all through as stored.
5. **Clients.** odin-js, chat-kmp and filesync crypto against the golden vector; Filesync's share UI.
6. **Links**, once decision 4 is made.

## Who may write

The app's circles have Read and Write on the whole drive (decision 1). The drive grant is generic; the ACL
narrows it per file, as it already does for reading **(V)**. Today nothing narrows writing per file **(V)**
(`AccessControlList`: security group, circles, identities), so the entry's `canWrite` is new:

- **Updating or deleting a file** needs an entry for one of the writer's circles with `canWrite`.
- **Adding a file to a folder** needs that on the folder (Filesync's folders are files, and a file names its
  folder as its group **(I)**, from the Filesync design).
- **A writer may only add entries for circles they belong to**, only on files they write in that request.
  Every other entry is the owning app's.

## A member adds a file to a shared folder

Michael shares a folder with Family, with read and write. John, a Family member, adds a file. John's Filesync
app does all of it in the upload, with no help from Michael's devices:

1. **The file**, encrypted with a new file key.
2. **The file's own key header, `sealed` to Michael's drive's write-only public key.** John has no DrvK, so
   he deposits, as senders do today **(V)** (design doc, "One write rule"). Michael's devices may later
   transform it to `zkDrive`.
3. **An ACL with Family's entry:** the key header locked with the Family circle key, which John's device opened
   from his own sealed copy. Every Family member can read the file at once.

**When the folder is shared with more than one circle** (Family and Friends, John only in Family), John
can't make the Friends entry. A device of Michael's Filesync adds it later: it opens John's `sealed` header
with DrvK and adds the entry. That is the same catch-up job as sharing existing files. Until then Friends
don't see John's file.

**John edits an existing file.** He keeps the file key (he knows it from the Family entry), encrypts the new
content with it and leaves the key headers alone. Two things are needed:

- **A rule to update content without the key header.** Today a `sealed` header replaces only a `sealed` one
  **(V)** (design doc), so once Michael's device has transformed John's file, John can't send a new key
  header. Updating content while keeping the stored key header is new, and a security decision (decision 5).
- **The member's own server must be able to find the file** (design doc, "Known limit" **(V)**): when John
  updates or deletes, the lookup on Michael's server runs as John, and today's read rule lets only the owner
  read a client-locked file, so John's update finds nothing. PR 4 widens the read rule to a member whose
  circle has an entry for the file; that should make the same lookup find it **(I)**, to be checked when PR 4
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
   on its drive, so the endpoint works as it is; whether a share is read-only is the ACL's `canWrite`.
2. **Owner circles** (no owning app): not shareable this way, or locked with the master key so only the owner
   console can share with them.
3. **Sharing with one person** outside any circle. The ACL already names people (`OdinIdList`) **(V)**, so a
   person's entry could carry the key header sealed straight to their matching drive, with no circle at all;
   or the app makes a small circle of its own for that person, so every entry is a circle's.
4. **Links**, for someone not on Homebase (design doc, "Links"): an entry with the link's id and the key
   header locked with the link's key. Still open: where the link's own record lives (its expiry, the app's
   copy of its key), a small table of its own or a hidden app circle with no members.
5. **Members editing existing files:** whether a writer may update a file's content while keeping its stored
   key header, and on which drives (see "A member adds a file").
6. **Does a share change move `modified`?** (see "Changing the ACL"). Moving it is the simple way for members
   to see shares and unshares, at the cost of the owner's devices re-fetching. Keeping it means members need
   another way to hear about shares, e.g. a `created` time on the `DriveAclIndex` row that a member queries
   by, and a signal for unshares.

## Out of scope

- Key rotation when a member leaves (design doc: not in the first version).
- Collaborative drives: unchanged.
