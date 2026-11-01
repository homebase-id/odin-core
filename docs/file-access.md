# File access: who may see and change a file

How a file's access control works today, and how Selective Share changes it. Read it before touching a drive
permission check, a file's ACL, the `DriveAclIndex` rows or the query's ACL filter. Its neighbours:
[security-model.md](security-model.md) (every caller and permission, in short), [owner-keys.md](owner-keys.md) (the
names of the keys), [zero-knowledge-drives.md](zero-knowledge-drives.md) (the drive sharing models) and
[selective-share-plan.md](selective-share-plan.md) (how shares are built, PR by PR).

**Status:** "Today" describes `main` plus Selective Share PR 1 and PR 2 (the share columns and circle keys), read in
the code on 2026-11-01, partly by subagents. "With Selective Share" is the plan for PR 3 and PR 4, not built.
Markers follow security-model.md: **(V)** read in the code (file named), **(I)** inferred.

## Terms

Each term below means one thing everywhere in this document.

**Callers**

| Term | Means |
|---|---|
| **owner callers** | the **owner console** (signed in with the master key) and the **owner's apps** (an app's client token). Both act at the `Owner` level (`CallerContext.IsOwner`) **(V)** |
| **outside callers** | everyone else: peers, YouAuth domains, signed-in identities that aren't connected, and anonymous visitors |
| **peer** | another identity, calling this one through the Peer API (`/api/peer/…`, `/api/v2/peer/…`) |
| **`System`** | the server's own work (outbox, jobs, the CDN): a caller level, not a person |

**Levels and groups**

| Term | Means |
|---|---|
| **level** | where a caller stands on the ladder `Anonymous` 111 < `Authenticated` 444 < `AutoConnected` 555 < `Connected` 777 < `Owner` 999 (`CallerContext.SecurityLevel`) **(V)**. An unreviewed connection's level counts as `Authenticated` wherever content is judged (`ReviewedSecurityTier.EffectiveLevel`) **(V)** |
| **required group** | the rung a file asks for: the ACL's `RequiredSecurityGroup`, on the same ladder **(V)** |

**Drives**

| Term | Means |
|---|---|
| **drive permission** | what a caller may do on a whole drive, from their grants: `Read` 1, `Write` 2, `React` 4, `Comment` 8, `ConditionalTemporalRead` 16 (`DrivePermission`, `PermissionContext`) **(V)** |
| **public drive** | a drive with the option `AllowAnonymousReads` |
| **drive option** | a typed setting on the drive the drive API validates: `OwnerOnly`, `AllowAnonymousReads`, `AllowSubscriptions`, `AllowCdn`, `IsArchived`, `IsReadonly`, `RequirePayloadHashes` (`StorageDriveDetails`) **(V)** |
| **drive attribute** | a free key/value string an app sets on a drive; the server gives meaning only to `IsCollaborativeChannel` (`StorageDriveDetails.Attributes`) **(V)**. An option and an attribute do the same job and differ only in how they're stored |
| **sharing model** | who holds the drive key: `Collaborative` (the default: the server holds it, and members of the drive's circles get it), `Private` or `SelectiveShare` (zero knowledge) (`Drives.SharingModel`) **(V)** |
| **drive key** | the key every file's key is locked with on that drive (DrvK, [owner-keys.md](owner-keys.md)) |

"Collaborative" names two different things: the **sharing model** `Collaborative`, and the **attribute**
`IsCollaborativeChannel` (V1 community and feed channels, where members may edit each other's files). This document
writes "collaborative channel" for the second.

**Files**

| Term | Means |
|---|---|
| **header** | the file's server-side record (`ServerFileHeader`): metadata, key header and server metadata, stored in `driveMainIndex` **(V)** |
| **ACL** | the file's access control list, in the header's server metadata (`ServerMetadata.AccessControlList`): a **required group**, plus optional **named circles** (`CircleIdList`) and **named identities** (`OdinIdList`) **(V)** |
| **client-locked file** | a file whose key header a client locked (types `sealed` and `zkDrive`); the server can't open it **(V)** |
| **the read rule** | the one check that decides whether a caller may see a stored file: `IDriveAclAuthorizationService.CallerMayReadAsync` **(V)** |
| **version tag**, **`modified`** | the header's version (a new one on every header write, used to refuse a stale update) and its last-change time (what clients sync by) **(V)** |

**Index rows and shares**

| Term | Means |
|---|---|
| **index row** | a `DriveAclIndex` row: one per named circle or named identity of a file (an identity as its OdinId's hash), so the query can find "files my circles may see" **(V)** |
| **share row** | on a Selective Share drive, the index row *is* the share: it adds permission bits, an expiry, the file key locked with the circle key, and its own `created` and `modified` (columns from PR 1 **(V)**; written from PR 3) |
| **live row** | a row with `Read` that hasn't expired |
| **withdrawn share** | a share row kept with no bits and no locked key, so members syncing see it go |
| **member** | an identity in a circle |

## Who each check applies to

**Owner callers skip every file's ACL; the owner console also holds every drive permission, the owner's apps only
their own; outside callers are held to both.**

| Caller | Drive permissions | The file's ACL |
|---|---|---|
| owner console | `All` on every drive (`OwnerAuthenticationService.cs:160-171`) **(V)** | skipped **(V)** |
| owner's apps | the drives in the app's registration, plus `Read` on public drives (`AppRegistrationService.cs:303-317`) **(V)** | skipped **(V)** |
| connected identities (as peers) | what their enabled circles grant, plus the public-drive grants (`TenantSettings`) **(V)** | applies, at `Connected`, or `Authenticated` until reviewed |
| YouAuth domains | what their circles grant, plus the public-drive grants (`YouAuthDomainRegistrationService.cs:505-552`) **(V)** | applies, at `Authenticated` (`Connected` when the domain is a connected identity) |
| signed-in identities, not connected | the public-drive grants only **(V)** | applies, at `Authenticated` |
| anonymous visitors | `Read` on public drives only **(V)** | applies, at `Anonymous` |
| `System` | every drive (`PermissionContext.cs:110`) **(V)** | skipped **(V)** |

So a drive permission means two things: for an owner's app, the part of the identity the owner gave it; for an
outside caller, what a circle (or the drive being public) lets them do. A file's ACL only narrows access for outside
callers.

## How a request is decided

**Every check must pass, in order; the first refusal ends the request.** A later check never grants what an earlier
one refused. The only ways round a check are the bypasses named with it.

### Reading a file by id

Header, payload or thumbnail (`DriveStorageServiceBase.GetServerFileHeader` → `GetServerFileHeaderInternal`) **(V)**:

| # | Check | Refused as | Bypassed by |
|---|---|---|---|
| 1 | the drive isn't archived | 400 `InvalidDrive` | the owner console (master key) |
| 2 | drive `Read` | 403 | `System`; not asked on a public drive (`StandardFileDriveStorageService`) |
| 3 | the file exists and its TTL hasn't passed | no file (I: a 404 to the client) | a delete, which must still see an expired file |
| 4 | the read rule, part 1: a client-locked file only for a caller holding the drive key (`ZkDriveFiles.VisibleTo`) | 403, or no file when the path asks for that (`aclDeniedAsMissing`) | `System`; files on the transient drive |
| 5 | the read rule, part 2: the file's ACL (`CallerPassesAcl`) | as 4 | owner callers; `System` |

**The ACL check (step 5)**, in order (`DriveAclAuthorizationService.Allows`, `:101-123`) **(V)**:
1. A missing ACL refuses.
2. A required group of `System` refuses: it is a caller level, never a file's.
3. The caller's level must reach the required group.
4. If the ACL names no circles and no identities, the caller passes.
5. Otherwise the caller must be in a named circle **or** be a named identity.

**The required group is a floor, and names only narrow it:** naming a circle never lets in a caller below the group.

### A query

(`DriveQueryServiceBase`, `QueryBatch`) **(V)**:
1. **Once, for the drive:** it exists and isn't archived (bypassed by the owner console), and the caller has drive
   `Read`.
2. **In SQL:** the file's required group is at or below the caller's level; for an outside caller, also "no index
   row for the file, or an index row for one of my circles or for my OdinId's hash" (`QueryBatch.cs:75-95`, the list
   from `DriveQuery.GetAcl`). An anonymous caller has neither circles nor an OdinId, so for them the SQL has no row
   condition.
3. **For each result** (`FilterVisibleAsync`): an expired file is left out; then the drive is checked for archiving
   again and the read rule runs. A client-locked file the caller may not see is left out silently; any other refusal
   is left out as an ACL failure. An encrypted file that isn't client-locked is left out when the caller holds no
   storage key for the drive.

**The SQL may only leave files out; the per-result read rule is the authority.** The two agree because the index
rows are made from the ACL (below); the SQL keeps pages full, and security doesn't depend on it **(I)**.

### Writing

- **Upload:** drive `Write` (`StandardFileDriveStorageService.AssertCanWriteToDrive`) **(V)**, then the new ACL's checks
  below.
- **Update, overwrite, delete:** the drive isn't archived, drive `Write`, then **the existing header is loaded through
  steps 3 to 5**, so the writer must also pass the read rule (`GetServerFileHeaderForWriting`,
  `SoftDeleteLongTermFile`) **(V)**. There is no per-file write permission today: drive `Write` plus passing the read
  rule is the whole rule **(V)**.
- **The new ACL's checks** (`AccessControlList.Validate`, through upload and update validation and
  `ServerFileHeader.Validate`) **(V)**:
  - a required group of `Anonymous` or `Owner` with named circles or identities is refused;
  - named identities are made canonical and de-duplicated;
  - named circles are allowed from `Authenticated` up, since an `Authenticated` caller can hold circles (a YouAuth
    domain) (`AccessControlList.cs:85-92`);
  - an encrypted file can't have a required group of `Anonymous` or `Authenticated` (`FileSystemStreamWriterBase.cs:565-576`);
  - on an `OwnerOnly` drive every file's required group must be `Owner` (`:599`; `FileSystemUpdateWriterBase.cs:485`);
  - an upload that names no ACL gets a required group of `Owner` (`UploadFileMetadata.cs:19`).
- Comments, reactions and read receipts have rules of their own ("Special cases").

### What else widens access, on purpose

`UpgradeToByPassAclCheck` makes a caller an owner caller for one job; the CDN reads CDN-enabled drives as `System`;
three callers read headers without the read rule; a public drive skips the drive-permission check, not the read
rule. All in "Special cases".

## Index rows today

**They follow the header.** Every header write (about 50 call sites, soft delete included) ends in
`DriveQuery.SaveFileHeaderAsync`. It turns the ACL into a member list (the named circles, plus each named identity's
OdinId hash) and reconciles the file's index rows in the header's transaction: rows of members no longer named are
deleted, new ones inserted, and a row that stays isn't touched (`MainIndexMeta.BaseUpsertEntryZapZapAsync`,
`TableDriveAclIndex.ReconcileRowsAsync`) **(V)**. A new row means read: `permissions` 1, no expiry, the columns'
defaults **(V)**. A hard delete and a drive purge delete the rows **(V)**. Nothing else writes them **(V)**.

**Who gets the ACL back:** owner callers only. Outside callers get the header without its server metadata
(`DriveFileUtility.CreateClientFileHeader`, `:113-116`) **(V)**.

**Peers' contexts are cached** for up to an hour (`OdinContextCache`): their level, enabled circles and whether they
are reviewed (`TransitAuthenticationService`) **(V)**. The cache is reset when a connection is finalized, blocked or
deleted, not when a circle is disabled **(V)**, so a check that must see a disable asks at the time (as the circle
share key read does, `CircleShareKeyService`) **(V)**.

## Copies sent to others

- **Feed:** a file goes to the followers the ACL lets in, judged from each follower's connection
  (`IdentityHasPermissionAsync`); an encrypted post only to those who can also decrypt the drive
  (`FeedDriveDistributionRouter`) **(V)**.
- **Direct sends** (`TransitOptions.Recipients`) aren't checked against the ACL: the sender names the recipients
  (`PeerOutgoingTransferService.CreateOutboxItems`) **(V)**.
- **A sent copy carries the ACL without its named identities**, so a recipient never learns who else was named
  (`SendFileOutboxWorkerAsync`, `UpdateRemoteFileOutboxWorker`) **(V)**.
- **The recipient's copy gets a required group of `Owner`**, except: a comment takes the ACL of the file it comments
  on (`IncomingCommentAcl`), and a collaborative channel keeps the sender's ACL (`PeerFileWriter.cs:76-100`) **(V)**.
- **A zero-knowledge drive sends nothing** (`ZkDriveFiles.AssertNotSent`) **(V)**.

## Special cases

- **Public drives:** the file services don't ask for drive `Read`, nor for `Comment` when writing a comment
  (`CommentFileStorageService`) **(V)**; the read rule still applies. The tenant's settings give signed-in and
  connected callers `Read`, and by flag `Comment` and `React`, on public drives (`TenantSettings`) **(V)**.
- **Comments** are a separate file system on the same drive: reading needs drive `Read` or `Comment`, writing needs
  `Comment` (security-model.md, section 6) **(V)**.
- **Reactions** need drive `React` and never pass the read rule, locally or from a peer: a peer's reaction finds the
  file by global transit id with a drive check only (`ResolveFileId`) **(V)**. Reactions keep rules of their own by an
  earlier decision (#1977); whether the drive check is enough is asked in #2058.
- **Read receipts** need a drive check; the inbox then applies them through the read rule
  (`PeerFileWriter.MarkFileAsRead`) **(V)**.
- **The server's own work skips the read rule's ACL part:** `System` passes it, and `UpgradeToByPassAclCheck` makes a
  caller an owner caller for one job: profile attributes, contacts, Shamir, zero-knowledge keys
  (`OdinContextUpgrades.cs:34-65`) **(V)**. `GetServerFileHeaderBy…SkippingFileAcl` skips the read rule for three
  callers: the peer file-exists probes, the email key writer and Shamir (`DriveQueryServiceBase.cs:206-235`) **(V)**.
- **The CDN** reads payloads and thumbnails of CDN-enabled drives as `System`, so it passes every file's ACL there
  (`CdnAuthPathHandler`) **(V)**: `AllowCdn` means "the CDN may read this drive". A zero-knowledge drive is never
  CDN-enabled (`DriveManager.GetCdnEnabledDrivesAsync`) **(V)**.
- **Static and profile publishing** pick public files themselves: active, unencrypted, a required group of
  `Anonymous`, no TTL (`StaticFileContentService.Filter`) **(V)**.

## Collaborative channels

**Status:** kept on today's drives; refused on Selective Share drives from PR 3; to retire when community and feed
channels move to Selective Share drives (not planned yet).

The attribute `IsCollaborativeChannel` is set by the community app, by collaborative feed channels and on the built-in
Lists drive (`BuiltinDrives.cs:146`) **(V)**. On such a drive **(V)**:
1. a peer may update a file another identity sent; elsewhere only the original sender may (`PeerFileWriter.cs:285`);
2. a peer's delete takes the direct soft-delete path (`PeerDriveIncomingTransferService.cs:226`);
3. an incoming file keeps the sender's ACL instead of `Owner` (`PeerFileWriter.cs:94-100`);
4. files received from others are distributed on to the members (`AllowDistribution`,
   `FeedDriveDistributionRouter.ShouldDistribute`);
5. an upload may name recipients for app notifications (`FileSystemStreamWriterBase.cs:431`).

The community app's per-message flag `appData.content.isCollaborative` is inside the encrypted content, so only
clients read it, to decide whether to offer Edit **(V, odin-js)**. The server goes by the drive's attribute.

## With Selective Share

On a Selective Share drive only owner callers hold the drive key, and each share has a key of its own
([zero-knowledge-drives.md](zero-knowledge-drives.md#selective-share)). **A share is the file's index row**: there
the index rows stop being a copy of the ACL and become the record (decided 2026-10-31).

| | Other drives | Selective Share drive |
|---|---|---|
| The ACL | the record: required group, named circles, named identities | required group always `Connected`, set by the server; named circles and identities refused (PR 3) |
| The index rows | made from the ACL on every header write; `Read`, no expiry | share rows, written on their own by the share API; a header write leaves them alone (PR 3) |
| A file with no rows | open to every caller who reaches the required group | owner callers only (PR 3) |
| Which rows count in a query | every row | live rows (PR 3; every row on other drives is live, so nothing changes there) |
| The read rule's ACL part | the ACL's names | the caller's live row (PR 4) |
| Who may edit a file | drive `Write` plus passing the read rule | owner callers, and a member whose live row has `Write` (PR 4) |
| Changing who may see a file | a header write: new version tag, `modified` moves | a share row write: `modified` moves, the version tag doesn't (decision 6, amended 2026-11-01) |
| How others learn of it | by `modified` | owner callers' other devices by `modified`; members by the share rows' own `modified`, which also shows a withdrawn share (PR 4) |

**PR 3** ([selective-share-pr3-plan.md](selective-share-pr3-plan.md)):
- **The share API**, for the owner console or the app that owns both the drive and the circle: list a file's share
  rows, write a batch (all or none; `Read` required, an expiry in the future, the file key locked with the circle
  key: 45 bytes, format 2), withdraw a share.
- **Shares in a new file's upload**, written with its header.
- **The query's ACL filter as `EXISTS`:** only live rows count, and a file with no rows is the owner callers' on these
  drives.
- **`includeShares`** on the query and on the read by id: owner callers get each file's live rows (circle, bits,
  expiry, `modified`; no locked keys), so a client can show what is shared without one call per file.

**PR 4, members:** the read rule's ACL part reads the caller's live row; `ZkDriveFiles.VisibleTo` lets a member with a
live row see the client-locked file and get their row's locked key; a member's write needs `Write` on their live row;
members sync their shares by the share rows' `modified`; socket events for shares.

**Is the collaborative channel attribute still needed?** On a Selective Share drive, no. Its edit rules (1 and 2
above) are what a share row's `Write` bit says, per file; its other effects (3 to 5) move copies, and a
zero-knowledge drive sends none. PR 3 refuses the attribute on Selective Share drives so the two can't mix. On today's
community and feed drives, which work by copies, it stays.

## Kept, retiring, retired

| What | Status |
|---|---|
| The `IsCollaborativeChannel` attribute | **kept** on today's drives; **refused** on Selective Share drives (PR 3); **retires** when community and feed channels move to Selective Share drives, not planned yet |
| The ACL's named circles and identities on a Selective Share drive | **not used**: refused from PR 3, the share rows say who. Kept on every other drive |
| Index rows made from the ACL | **kept** on every other drive; **replaced** by share rows on Selective Share drives (PR 3) |
| `QueryModifiedAsync` (V1's change query) | **obsolete**, V1 only; the query batch does the same. Left as it is (it ignores its `MaxDate`) until V1 goes |
| `FileUpdateInstructionSetV2` | **unused**; removed in PR 3 |
| The drive option `IsReadonly` | documented but **not enforced**: no write path reads it (security-model.md, section 3) **(V)**. Open: enforce or remove |
| Key header version 1 (AES-CBC; the IV cancels out) | **to be replaced** by version 2 (#2024); share rows use version 2 from the start |

## Open

- **Withdrawn shares:** when the rows are removed.
- **A deleted circle's share rows** let no one in (a reader must be a member), and **a reset circle key** leaves rows
  nobody can open: what to do with both is decided in PR 4.
- **Sharing with one identity, and links** (selective-share-plan.md, decisions 3 and 4).
- **Reactions and the read rule** (#2058).

## Where in the code

| What | Where (under `src/`) |
|---|---|
| The read rule and the ACL check | `services/Odin.Services/Authorization/Acl/DriveAclAuthorizationService.cs` |
| The ACL and its checks | `services/Odin.Services/Authorization/Acl/AccessControlList.cs` |
| Unreviewed connections | `services/Odin.Services/Authorization/Acl/ReviewedSecurityTier.cs` |
| Client-locked files | `services/Odin.Services/Drives/ZkDriveFiles.cs` (`VisibleTo`) |
| Drive permissions | `services/Odin.Services/Base/PermissionContext.cs`, `services/Odin.Services/Drives/DrivePermission.cs` |
| Reading by id, writing | `services/Odin.Services/Drives/FileSystem/Base/DriveStorageServiceBase.cs` |
| Queries, the per-result check | `services/Odin.Services/Drives/FileSystem/Base/DriveQueryServiceBase.cs` (`FilterVisibleAsync`) |
| The query's SQL | `core/Odin.Core.Storage/Database/Identity/Abstractions/QueryBatch.cs` |
| The caller's member list for the SQL | `services/Odin.Services/Drives/DriveCore/Query/DriveQuery.cs` (`GetAcl`) |
| Index rows on a header write | `MainIndexMeta.BaseUpsertEntryZapZapAsync`, `TableDriveAclIndex.ReconcileRowsAsync` |
| What a recipient's copy gets | `services/Odin.Services/Peer/Incoming/Drive/Transfer/PeerFileWriter.cs` |
| Feed distribution | `services/Odin.Services/DataSubscription/FeedDriveDistributionRouter.cs` |
