# File access: who may see and change a file

How a file's access control works today, and how Selective Share changes it. The reference to read before touching a
drive permission check, a file's ACL, the ACL index or the query's ACL filter. Its neighbours:
[security-model.md](security-model.md) (callers, keys and every permission, in short),
[owner-keys.md](owner-keys.md) (the names of the keys), [zero-knowledge-drives.md](zero-knowledge-drives.md) (the
drive key models) and [selective-share-plan.md](selective-share-plan.md) (how shares are built, PR by PR).

**Status:** "Today" describes `main` plus Selective Share PR 1 and PR 2 (the share columns and circle keys), read in
the code on 2026-11-01, partly by subagents. "With Selective Share" is the plan for PR 3 and PR 4, not built.
Markers follow security-model.md: **(V)** read in the code (file named), **(I)** inferred.

## The pieces

| Name | What it is | Where it lives |
|---|---|---|
| **Drive permission** | what a caller may do on a whole drive: `Read` 1, `Write` 2, `React` 4, `Comment` 8, `ConditionalTemporalRead` 16 | the caller's grants, from an app registration, a circle, a YouAuth domain or the anonymous drives (`PermissionContext`) **(V)** |
| **Security level** | who the caller is, on a ladder: `Anonymous` 111 < `Authenticated` 444 < `AutoConnected` 555 < `Connected` 777 < `Owner` 999, and `System` 1 for the server's own work | the caller's context (`CallerContext.SecurityLevel`) **(V)** |
| **The file's ACL** | `RequiredSecurityGroup` (a rung of that ladder) plus optional `CircleIdList` and `OdinIdList` | the header's server metadata (`ServerMetadata.AccessControlList`), stored as JSON in `driveMainIndex.hdrServerData` **(V)** |
| **The group column** | a copy of the ACL's group, so the query can filter by it | `driveMainIndex.requiredSecurityGroup` **(V)** |
| **ACL index rows** | one row per circle or person a file's ACL names (a person as their OdinId's hash), so the query can find "files my circles may see" | `DriveAclIndex` **(V)** |
| **Share row** (Selective Share) | an ACL index row that is also the share: permission bits, expiry, the file key locked with the circle key, its own `created` and `modified` | `DriveAclIndex`, the columns PR 1 added **(V)**; written from PR 3 |
| **Drive options** | `OwnerOnly`, `AllowAnonymousReads`, `AllowSubscriptions`, `AllowCdn`, `IsArchived`, … | typed fields on the drive (`StorageDriveDetails`) **(V)** |
| **Drive attributes** | free key/value strings an app sets; the server reads one, `IsCollaborativeChannel` | `StorageDriveDetails.Attributes` **(V)** |
| **Sharing model** | who holds the drive key: `Collaborative` (the default), `Private`, `SelectiveShare` | `Drives.SharingModel` (`DriveSharingModel.cs`) **(V)** |

An **option** and an **attribute** do the same job, a setting on a drive. The difference is only how they're
stored: an option is a typed field the drive API validates; an attribute is any string pair, and the server gives
meaning to the ones it knows. `IsCollaborativeChannel` is an attribute because the community and feed apps added it
without a server change **(I)**.

Two different things are called collaborative: the **sharing model** `Collaborative` (every member holds the drive
key; every drive that isn't zero knowledge counts as one) and the **attribute** `IsCollaborativeChannel` (V1
community and feed channels: members may edit each other's files, below).

## Today: reading a file

A read needs both: the **drive permission** (`Read`), and the **file's ACL** (`DriveAclAuthorizationService`)
**(V)**.

**The ACL rule** (`Allows`, `DriveAclAuthorizationService.cs:101-123`) **(V)**:
1. The owner and `System` callers pass (`CallerPassesAcl`, `:67-87`).
2. A `System` group on a file admits no one; it is a caller level, never a file's.
3. The caller's level must reach the file's group. An unreviewed connection counts as `Authenticated`, not
   `Connected`, wherever content is judged (`ReviewedSecurityTier.EffectiveLevel`) **(V)**.
4. If the ACL names no circles and no people, that's enough.
5. Otherwise the caller must be in one of the named circles **or** be one of the named people.

**One rule for every read** (`CallerMayReadAsync`): the ACL above, and a client-locked file (sealed or `zkDrive`)
only for a caller holding the drive key (`ZkDriveFiles.VisibleTo`) **(V)**. It is asked by:
- every read by id: header, payload, thumbnail, and the header a write loads first (`GetServerFileHeaderInternal`,
  `DriveStorageServiceBase.cs:2209-2235`) **(V)**;
- every query, on each result (`DriveQueryServiceBase.FilterVisibleAsync`) **(V)**;
- every socket event, per connected device (`ClientDriveNotification.VisibleToAsync`) **(V)**.

**Queries filter in SQL first, then by the rule.** The SQL (`QueryBatch.cs:75-95`) **(V)**:
- `requiredSecurityGroup BETWEEN 0 AND <caller's level>`;
- when the caller isn't the owner, `LEFT JOIN driveAclIndex` and `(no row for the file) OR (a row for one of my
  circles or my own OdinId hash)`; the list comes from `DriveQuery.GetAcl` (`:157-173`) **(V)**.

So the query reads the **rows** and the rule reads the **header's ACL**. They agree because the rows are made from the
ACL on every header write (below). An anonymous caller has no circles and no OdinId, so `GetAcl` returns nothing and
the SQL has no row filter; the per-result rule then drops what they may not see **(V)**.

**Who gets the ACL back:** only the owner and apps acting for the owner. Everyone else gets the header without its
server metadata (`DriveFileUtility.CreateClientFileHeader`, `:113-116`) **(V)**.

**Peers** read through the Peer API with the same services and their own context: `Connected`, their enabled circles,
and `IsReviewed` (`TransitAuthenticationService`) **(V)**. That context is cached for up to an hour and reset when a
connection is finalized, blocked or deleted, not when a circle is disabled (`OdinContextCache`) **(V)**; the peer path
for circle share keys asks whether the circle is enabled at the time for that reason (`CircleShareKeyService`) **(V)**.

## Today: writing a file

- **Upload and update need drive `Write`** (`StandardFileDriveStorageService.AssertCanWriteToDrive`) **(V)**.
- **An update or a delete loads the existing header through the read rule** (`GetServerFileHeaderForWriting`,
  `SoftDeleteLongTermFile`) **(V)**, so it also needs the file's ACL to admit the caller. There is no per-file write
  permission: drive `Write` plus "may see it" is the whole rule **(V)**.
- **The ACL is checked on every write** (`AccessControlList.Validate`, via upload and update validation and
  `ServerFileHeader.Validate`) **(V)**:
  - `Anonymous` or `Owner` with circles or people is refused;
  - people are canonicalised and de-duplicated;
  - circles are allowed from `Authenticated` up: an `Authenticated` caller can hold circles (a YouAuth domain), so
    "Authenticated plus circles" means something (`AccessControlList.cs:85-92`) **(V)**;
  - an encrypted file can't be `Anonymous` or `Authenticated` (`FileSystemStreamWriterBase.cs:565-576`) **(V)**;
  - on an `OwnerOnly` drive every file must be `Owner` (`:599`; `FileSystemUpdateWriterBase.cs:485`) **(V)**.
- **An upload that names no ACL gets `Owner`** (`UploadFileMetadata.cs:19`) **(V)**.

**The rows follow the header.** Every header write (some 50 call sites, soft delete included) ends in
`DriveQuery.SaveFileHeaderAsync`, which turns the ACL into a member list (circles, plus each person's OdinId hash)
and reconciles the file's rows in the header's transaction: rows of members no longer named go, new ones are added,
a row that stays is untouched (`MainIndexMeta.BaseUpsertEntryZapZapAsync`, `TableDriveAclIndex.ReconcileRowsAsync`)
**(V)**. A new row means read (`permissions` 1, no expiry), the column's default **(V)**. A hard delete and a drive
purge delete the rows **(V)**. Nothing else writes them **(V)**.

## Today: copies sent to others

- **Feed:** a file is distributed to followers whose access the ACL allows (`IdentityHasPermissionAsync`, the same
  rule judged from the follower's connection); encrypted posts only to those who can also decrypt the drive
  (`FeedDriveDistributionRouter`) **(V)**.
- **Direct sends** (`TransitOptions.Recipients`) are not checked against the ACL; the sender names the recipients
  (`PeerOutgoingTransferService.CreateOutboxItems`) **(V)**.
- **A sent copy carries the ACL without its `OdinIdList`**, so a recipient never learns who else was named
  (`SendFileOutboxWorkerAsync`, `UpdateRemoteFileOutboxWorker`) **(V)**.
- **The recipient's copy gets `Owner`**, except: a comment takes the ACL of the file it comments on
  (`IncomingCommentAcl`), and a collaborative channel keeps the sender's ACL (`PeerFileWriter.cs:76-100`) **(V)**.
- **A zero-knowledge drive sends nothing** (`ZkDriveFiles.AssertNotSent`) **(V)**.

## Today: the special cases

- **Public drives (`AllowAnonymousReads`):** the file services skip the drive-permission checks for read, and a
  comment's write check (`CommentFileStorageService`) **(V)**; the file's ACL still applies to reads. The tenant's
  settings give authenticated and connected callers read (and, by flag, comment and react) on these drives
  (`TenantSettings`) **(V)**.
- **Comments** are a separate file system on the same drive; read needs `Read` or `Comment`, write needs `Comment`
  (security-model.md, section 6) **(V)**.
- **Reactions** need drive `React` and never pass the file's ACL, locally or from a peer: the peer path finds the
  file by global transit id with a drive check only (`ResolveFileId`) **(V)**. Their rules are their own by an earlier
  decision (#1977); whether the drive check is enough is asked in #2058.
- **Read receipts** check the drive; the inbox applies them through the read rule (`PeerFileWriter.MarkFileAsRead`)
  **(V)**.
- **System work skips the ACL:** a `System` caller passes it, and `UpgradeToByPassAclCheck` makes a caller the owner
  for one job (profile attributes, contacts, Shamir, zero-knowledge keys) (`OdinContextUpgrades.cs:34-65`) **(V)**.
  `GetServerFileHeaderBy…SkippingFileAcl` has three callers: the peer file-exists probes, the email key writer and
  Shamir (`DriveQueryServiceBase.cs:206-235`) **(V)**.
- **The CDN** reads payloads and thumbnails of CDN-enabled drives as `System`, so it passes every file's ACL there
  (`CdnAuthPathHandler`) **(V)**: `AllowCdn` means "the CDN may read this drive". Zero-knowledge drives are never
  CDN-enabled (`DriveManager.GetCdnEnabledDrivesAsync`) **(V)**.
- **Static and profile publishing** pick public files themselves: active, unencrypted, `Anonymous`, no TTL
  (`StaticFileContentService.Filter`) **(V)**.

## Today: collaborative channels (`IsCollaborativeChannel`)

Set by the community app, by collaborative feed channels and on the built-in Lists drive (`BuiltinDrives.cs:146`)
**(V)**. On such a drive **(V)**:
1. a peer may update a file someone else sent; elsewhere only the original sender may
   (`PeerFileWriter.cs:285`);
2. a peer's delete takes the direct soft-delete path (`PeerDriveIncomingTransferService.cs:226`);
3. an incoming file keeps the sender's ACL instead of `Owner` (`PeerFileWriter.cs:94-100`);
4. files received from others are distributed on to the members (`AllowDistribution`,
   `FeedDriveDistributionRouter.ShouldDistribute`);
5. an upload may name recipients for app notifications (`FileSystemStreamWriterBase.cs:431`).

The community app's per-message "collaborative" (`appData.content.isCollaborative`) is inside the encrypted
content, so only clients read it: it decides whether to offer Edit **(V, odin-js)**. The server's rule is the drive's.

## With Selective Share

Selective Share is a drive sharing model: only the owner's devices and apps hold the drive key, and each share has a
key of its own ([zero-knowledge-drives.md](zero-knowledge-drives.md#selective-share)). **A share is the file's
`DriveAclIndex` row**: on these drives the rows stop being a copy of the header's ACL and become the record
(decided 2026-10-31).

| | Other drives | Selective Share drive |
|---|---|---|
| The header's ACL | the record: group, circles, people | always `Connected`, set by the server; circles and people refused (PR 3) |
| The rows | made from the header's ACL on every write; bits 1, no expiry | written on their own by the share API; a header write leaves them alone (PR 3) |
| A file with no rows | open to everyone who meets the group | the owner's alone (PR 3) |
| A row counts when | it exists | it has `Read` and hasn't expired (PR 3; on other drives every row qualifies, so nothing changes) |
| Who may edit a file | anyone with drive `Write` who may see it | a member whose row has `Write` (PR 4), the owner and the drive's app |
| Changing who may see it | a header write: new version tag, `modified` moves | a row write: `modified` moves, the version tag doesn't (decision 6, amended 2026-11-01) |
| Who learns of the change | everyone syncing by `modified` | the owner's devices by `modified`; members by the rows' `modified`, which also shows a withdrawal (PR 4) |

**PR 3** ([selective-share-pr3-plan.md](selective-share-pr3-plan.md)):
- the share API: the owner console or the app owning both the drive and the circle writes rows (`Read` required,
  an expiry in the future, the file key locked with the circle key: 45 bytes, format 2), all or none per batch;
  `DELETE` withdraws a share, keeping the row with no bits and no key so members see it go;
- shares carried in a new file's upload, written with its header;
- the query's ACL filter as `EXISTS`: a row counts only with `Read` and before its expiry, and a file with no rows is
  the owner's on these drives;
- `includeShares` on the query and the read by id: the owner gets each file's live rows (circle, bits, expiry,
  `modified`; no key headers), so a client can show what is shared without a call per file.

**PR 4:** members. The read rule reads the caller's row instead of the header's circles; `ZkDriveFiles.VisibleTo`
lets a member with a live row see the client-locked file and get their row's locked key; members' writes are judged
by their row's `Write` bit; members sync their shares by the rows' `modified`; socket events for shares.

**Is the collaborative attribute still needed?** On a Selective Share drive, no. Its edit rule (1 and 2 above) is what
a row's `Write` bit says per file, and more precisely; its other effects (3 to 5) move copies around, and a
zero-knowledge drive sends none. PR 3 refuses the attribute on Selective Share drives so the two can't mix. On the V1
community and feed drives, which work by copies, it stays until they move to Selective Share drives.

## Open

- **Withdrawn rows:** when they are removed (they let a member see a share go).
- **A deleted circle's rows** grant no one (a reader must be in the circle), and **a reset circle key** leaves rows
  nobody can open: what to do with them, decided in PR 4.
- **Sharing with one person, and links** (selective-share-plan.md, decisions 3 and 4).
- **Reactions and the file's ACL** (#2058).

## Where in the code

| What | Where |
|---|---|
| The ACL rule | `Services/Authorization/Acl/DriveAclAuthorizationService.cs` |
| The ACL and its validation | `Services/Authorization/Acl/AccessControlList.cs` |
| Client-locked files | `Services/Drives/ZkDriveFiles.cs` (`VisibleTo`) |
| Unreviewed connections | `Services/Authorization/Acl/ReviewedSecurityTier.cs` |
| Drive permissions | `Services/Base/PermissionContext.cs`, `Services/Drives/DrivePermission.cs` |
| The query's ACL SQL | `Core.Storage/Database/Identity/Abstractions/QueryBatch.cs` |
| The caller's member list for the query | `Services/Drives/DriveCore/Query/DriveQuery.cs` (`GetAcl`) |
| The rows on a header write | `MainIndexMeta.BaseUpsertEntryZapZapAsync`, `TableDriveAclIndex.ReconcileRowsAsync` |
| What a recipient's copy gets | `Services/Peer/Incoming/Drive/Transfer/PeerFileWriter.cs` |
| Feed distribution | `Services/DataSubscription/FeedDriveDistributionRouter.cs` |
