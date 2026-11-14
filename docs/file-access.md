# File access: who may see and change a file

How a file's access control works, on every drive and on Private Share drives. Read it before touching a drive
permission check, a file's ACL, the `DriveAclIndex` rows or the query's ACL filter. Its neighbours:
[security-model.md](security-model.md) (every caller and permission, in short), [owner-keys.md](owner-keys.md) (the
names of the keys), [zero-knowledge-drives.md](zero-knowledge-drives.md) (the drive sharing models) and
[selective-share-plan.md](selective-share-plan.md) (how shares are built, PR by PR).

**Status:** describes `main` plus Private Share's three open PRs (#2053, #2056, #2059). What they don't build yet is
marked **(to build)**. Markers follow security-model.md: **(V)** read in the code (file named), **(I)** inferred.

## Terms

The terms used here are in [terminology.md](terminology.md): callers, levels and groups, drives and their sharing
models (Server Share, Private, Private Share), the community channel attribute, files and key headers, and shares.

## Who each check applies to

**Owner callers skip every file's ACL; the owner console also holds every drive permission, the owner's apps only
their own; outside callers are held to both.**

| Caller | Drive permissions | The file's ACL |
|---|---|---|
| owner console | `All` on every drive (`OwnerAuthenticationService.cs:160-171`) **(V)** | skipped **(V)** |
| owner's apps | the drives in the app's registration, plus `Read` on public drives (`AppRegistrationService.cs:303-317`) **(V)** | skipped **(V)** |
| connected identities (as peers) | what their enabled circles grant, plus the public-drive grants (`TenantSettings`) **(V)** | applies, at `Connected`, or `Authenticated` until reviewed |
| YouAuth domains | what their circles grant, plus the public-drive grants (`YouAuthDomainRegistrationService.cs:505-552`) **(V)** | applies, at `Authenticated` (`Connected` when the domain is a connected identity) |
| a connected identity's device, reading directly with its peer-notification token | `Read` on the zero-knowledge drives its circles (app circles included) grant, nothing else, and only on the drive reads marked `[PeerDirectRead]`; elsewhere anonymous (`CircleNetworkService.TryCreatePeerSubscriberReadContextAsync`, `PeerSubscriberAuthPathHandler`) **(V, tested)** | applies, at `Connected`, or `Authenticated` until reviewed |
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
| 3a | on a Server Share drive, a deposit the caller's request can open becomes the server's own file, so it is no longer client-locked at 4 (`DriveStorageServiceBase.ConvertDepositsAsync`; a query does the same, `FilterVisibleAsync`) | never refuses: a deposit it can't open stays sealed | the soft-delete batch, V1 and hard deletes and the TTL jobs (loading with `includeExpired`), which 4 lets through anyway |
| 4 | the read rule, part 1: a client-locked file only for a caller holding the drive key (`ZkDriveFiles.VisibleTo`) | 403, or no file when the path asks for that (`aclDeniedAsMissing`) | `System`; files on the transient drive; on a Server Share drive, a sealed file for a caller holding its storage key, which the server opens for them; a tombstone's sealed key header goes out empty |
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
rows and the group column are made from the ACL on each header write (below); the SQL keeps pages full, and security
doesn't depend on it **(I)**. Where they disagree, as for a file stored with a group of 0 before it meant `Owner`,
the page comes back short.

### Writing

- **Upload:** drive `Write` (`StandardFileDriveStorageService.AssertCanWriteToDrive`) **(V)**, then the new ACL's checks
  below.
- **Update, overwrite, delete:** the drive isn't archived, drive `Write`, then **the existing header is loaded through
  steps 3 to 5**, so the writer must also pass the read rule (`GetServerFileHeaderForWriting`,
  `SoftDeleteLongTermFile`) **(V)**. There is no per-file write permission: drive `Write` plus passing the read rule is
  the whole rule **(V)**. Private Share drives add one ("With Private Share").
- **The new ACL's checks** (`AccessControlList.Validate`, through upload and update validation and
  `ServerFileHeader.Validate`) **(V)**:
  - a required group of `Anonymous` or `Owner` with named circles or identities is refused;
  - named identities are made canonical and de-duplicated;
  - named circles are allowed from `Authenticated` up, since an `Authenticated` caller can hold circles (a YouAuth
    domain) (`AccessControlList.cs:97-104`);
  - an encrypted file can't have a required group of `Anonymous` or `Authenticated` (`FileSystemStreamWriterBase.cs:565-576`);
  - on an `OwnerOnly` drive every file's required group must be `Owner` (`:599`; `FileSystemUpdateWriterBase.cs:485`);
  - an upload that names no ACL gets a required group of `Owner` (`UploadFileMetadata.cs:19`); so does one whose ACL
    leaves the group out (`AccessControlList.RequiredSecurityGroup`). An upload or update that sends an undefined
    value, such as 0 or 778, is refused with a 400 (`AssertGivenGroupIsDefined`), and one that leaves the group out but
    names circles or identities is refused saying so: `Owner` can't name them. A stored header isn't refused for an
    undefined value: it reads as `Owner`. The
    rule lives on the ACL itself, so every reader of the header sees `Owner`, a header already stored with 0
    included. The query's group column keeps 0 for such a file until its next header write; the SQL then still
    returns it to every caller, and the per-result read rule leaves it out ("A query").
- Comments, reactions and read receipts have rules of their own ("Special cases").

### Changing a file you sent

**A file's original sender changes it by sending it again or updating it, as long as no one else has written it since
their last write; anyone else, and the sender once someone else has, names the version tag the file has**, on every
drive **(V, tested)**. Anything else is refused with `VersionTagMismatch` (`SentFileChanges.AlreadyLandedOrAssertMayChange`), and the SQL
write that replaces the header checks the tag again (`TableDriveMainIndex`, `WHERE hdrVersionTag = @hdrVersionTag`)
**(V)**, so of two writes from the same tag only the first lands. Both writers ask who may change the file and what it
names in one place (`SentFileChanges`, from `PeerFileWriter` and `PeerFileUpdateWriter`) **(V)**.
- **Who may:** the file's original sender, or on a drive marked as a community channel any member who may read it,
  holding drive `Write` **(V, tested)**; on a Private Share drive also what "With Private Share" asks (below). On
  the inbox path the community channel's read check is the drainer's (the owner or an app), not the sender's, as
  before this rule **(V)**.
- **The recipient records the last write by a peer.** Each new file, file sent again and update from a peer stores, in
  the header, who sent it, the version tag their copy had and the one the write gave the recipient's copy
  (`ServerMetadata.SendersWrite`) **(V, tested)**. The recipient's copy keeps version tags of its own server's making, as
  for every file; only the update flow takes the sender's new tag, as it always has (`UpdateRemoteFileRequest.NewVersionTag`).
  The record is in the server's own metadata, which never travels between identities, so a sender can't supply it **(V)**. A save that changes none of the file's
  content carries the record over to the new tag: the expiry of an expire-after-read file starting or hastened, and the
  owner's change of its archival status (`DriveStorageServiceBase.CarrySendersWrite`) **(V, tested for the archival status)**.
- **A sender who can only write the file names no tag.** While the file still has the tag their last write gave it,
  no one else has changed it since, so their change goes on: a deposit, a Filesync marker, a chat message on a drive
  they hold only `Write` on, an owner-only file on a drive marked as a community channel **(V, tested)**. Nothing the
  sender's server or client keeps has to stay in step with the recipient: a lost response, or an inbox item the
  recipient dropped, leaves the sender free to send their next change. Every device of the sender's sends the same way.
- **Anyone else's write ends it:** the owner's edit, or another member's, gives the file a new tag, after which the
  sender's change must name the file's tag like anyone's, which a sender who can only write it never learns **(V,
  tested)**. A deposit's `sealed` key header then no longer replaces the owner's ([zero-knowledge-drives.md](zero-knowledge-drives.md))
  **(V, tested)**. Converting a deposit's key header, by the server on a Server Share drive or a key holder's transform
  on a zero-knowledge drive, isn't such a write: it keeps the file's version tag, so the sender may still change it with
  a `sealed` key header, which is converted again, or delete it (`ZkDriveFiles.AssertMayReplace`) **(V, tested)**.
- **A sender or member who can read the file** names its tag, as for any update, and gets a version tag error with a
  stale one: the ordinary case of people editing together **(V, tested)**. Only a change that has no copy on the sender's own drive
  names one: an update of the recipient's copy only (`UpdateLocale.Peer`) or a send from the transient drive, where the
  client names it as the `versionTag` of the metadata it uploads, carried as `ExpectedVersionTag` **(V)**. One named tag
  goes to every recipient, so a change naming one is for one recipient. A change sent from the sender's own drive
  (`UpdateLocale.Local`, or an upload with recipients) names none, so once someone else has changed a recipient's copy,
  it is refused there for good (I, from the code).
- **The sender's client hears how it went.** A client's change with no copy on the sender's own drive
  (`UpdateLocale.Peer`, or an upload sent from the transient drive) leaves no transfer history it could read: the
  transient copy is deleted once sent. So each outcome goes to the sockets of the app that sent it, or the owner's own
  client's when no app did, never another app's or a guest's (the server's own sends from the transient drive, such as
  Shamir shards, tell no one): a `PeerSendResult` event (`PeerSendResultNotification`) **(V, tested)**. A delete
  sent by global transit id is told the same way when it names the version it is meant for (`expectedVersionTag`,
  below), and not otherwise **(V, tested)**. It names:
  - the recipient, the drive id and the global transit id the send named; a file sent again that the recipient
    matched by its unique id keeps its own (I, from the code);
  - the version tag the client's upload or update gave back, or the one its delete named, which with whether it was an
    update or a delete tells two sends of one file apart **(V, tested)**; `send-status` has no kind to name, so an update
    and a delete naming the version it gave the file answer as one, whichever is due first (I, from the code);
  - whether it was an update or a delete (`isUpdate`, `isDelete`), the transfer status (delivered, or why not) and
    whether the outbox tries again **(V, tested)**;
  - on delivery, the version tag of the recipient's copy, for the next change to name: for a file sent, the one the
    recipient's server returns when it writes the file at once (`PeerTransferResponse.VersionTag`); for an update, the
    new version tag the update gave it; none for a delete **(V, tested)**.

  It is told before the outbox records the outcome, so a failure recording it can repeat it: the latest counts (I,
  from the code). On a drive with an inbox, a change the recipient queued there is told as delivered with no version
  tag; the inbox may still refuse it, and nothing tells the sender then (I, from the code; a Private Share drive has no
  inbox). Nothing is kept for a client not connected at the time: it asks whether its send is still waiting in the
  outbox, and once not, reads the recipient's copy.
- **A client asks whether its send is still waiting.** `GET /api/v2/peer/{odinId}/drives/{driveId}/files/by-gtid/{gtid}/send-status?sentVersionTag=…`
  answers for the caller's own sends alone, an app's or the owner's own client's, as the event is matched, and asks
  `UseTransitWrite`, as the send did (`PeerOutbox.GetTransientSendStatusAsync`) **(V, tested)**:
  - pending while the outbox holds it, with how often it has taken it up and when it next does, or that it is being
    sent right now;
  - not pending once the outbox is done with it, delivered or not, or for a send it doesn't know.

  Naming no `sentVersionTag`, for a client that lost its upload's answer, it asks about any of the caller's sends of
  the file, the one due soonest first **(V, tested)**; that client must know the global transit id, so it picked it
  (a deposit, or a Private Share drive). It reads the outbox's own rows, so it keeps nothing new; each lookup reads every
  queued send of the transient drive to that recipient (I, from the code).
- **The same change sent again,** the outbox retrying one that landed, is that peer's tag already recorded: done,
  nothing written; an inbox item is marked complete and the payloads it streamed in are deleted
  (`SentFileChanges.AlreadyDeliveredAsync`) **(V)**.
- **What doesn't change the tag:** a share row write ("With Private Share") **(V)**, a reaction (its own column) and the
  owner's local app data, which has a tag of its own (`hdrLocalVersionTag`) **(V)**. Taking a deposit over today is the
  owner's update, which does **(V)**.
- **The sender changes it without reading it.** The server finds the file without the read rule, by global transit id
  then unique id, checks the sender is the stored file's, and, while they may change it unread (on a Private Share
  drive, while it has no share), writes it in a context marked for that file and sender
  (`OdinContextUpgrades.ForSendersOwnFile`), whose header loads skip the read rule for that file alone and ask no drive
  `Read` (`DriveStorageServiceBase.IsSendersOwnFileAsync`) **(V, tested)**. It holds on the inbox path too, whoever
  drains it. Once it has a share, its sender changes it as any member does.
- **On a Private Share drive, a share freezes it.** A standard file with no share is changed by the rule above. Once
  it has one, the shares decide: the sender also needs a live share with `Write` on it, so a share without `Write`, or a
  withdrawn one, keeps them from changing it until the owner changes that share **(V, tested)**. **Any share freezes
  it**, not only one for a circle the sender is in: a file shared with a group the sender isn't in stays as the group
  reads it. The rule may be loosened later; it can't be tightened without surprising senders. A comment there goes by
  the shares of the file it comments on.
- **What a send again keeps,** as an update does: encrypted or not, its data source, and for a client-locked file a
  client-locked key header with a new IV (`DriveStorageServiceBase.AssertSameEncryption`, `AssertSameDataSource`,
  `ZkDriveFiles.AssertFitsUpdate`) **(V)**. The file key stays the same; the server can't check a client-locked file's
  key, which is the sender's to keep.
- **A delete follows it too, on a zero-knowledge drive.** A peer's delete of a standard file there is written at
  once, the file found without the read rule (`GetServerFileHeaderByGlobalTransitIdSkippingFileAcl`): its original
  sender's, or on a Private Share drive a member's through a live share with `Write`
  (`PeerDriveIncomingTransferService.AcceptDeleteLinkedFileRequestAsync`, `SentFileChanges.ContextForDeleteAsync`,
  `SentFileChanges.ContextForChangeAsync`) **(V, tested)**. A sender deleting their own file is refused unless the
  file's record of their last write (`ServerMetadata.SendersWrite`) names them and still matches the file's version
  tag, so a file stored before that record was kept is refused too; on a drive still marked as a community channel
  only the original sender deletes **(V)**. The delete is of the version judged (`expectedVersionTag`): a write that
  lands in between is judged again, once, rather than lost (`PeerDriveIncomingTransferService.DeleteAtOnceAsync`)
  **(V)**. A delete may also name the version it is meant for (`expectedVersionTag` on `senddeleterequest`, one
  recipient only): it is refused (`VersionTagMismatch`) once the file has changed since, so a member's delete can't
  discard a co-editor's newer version unseen, and is not judged again **(V, tested)**. One sent again after it landed is
  done, not refused by its own tombstone's version (I, from the code). The same holds for a comment **(V, tested)** and
  for a community channel's post **(V)**; a drive with an inbox refuses a delete naming a version, as the inbox writes
  it later and checks nothing **(V, tested)**. So on a Private Share drive a member's own new file with no share is theirs to delete as its sender
  **(V, tested)**. A delete of a file already deleted does nothing **(V)**. On a Server Share drive a peer's delete goes
  as before: through the inbox, or on a drive marked as a community channel the original sender's alone, at once
  (below).
- **A deleted file stays deleted:** its change is refused ("File is deleted") **(V)**.
- **A file received before this rule has no record of its sender's write,** so a sender who can only write it can't
  change it. There is no fallback: chat edits are short-lived, and clients update.
- **A refused change isn't retried** (`OutboxWorkerBase.cs:86`) **(V)**.
- **Feed copies** a channel distributes to its followers are written by their own path (`PeerFileWriter`'s feed
  files), which this rule doesn't cover (I, not changed).

### What else widens access, on purpose

`UpgradeToByPassAclCheck` makes a caller an owner caller for one job; the CDN reads CDN-enabled drives as `System`;
some callers find headers without the read rule, and a file's original sender changes it without reading it ("Changing
a file you sent"); a public drive skips the drive-permission check, not the read rule. All in "Special cases".

## Index rows on other drives

**They follow the header.** Every header write (about 50 call sites, soft delete included) ends in
`DriveQuery.SaveFileHeaderAsync`. It turns the ACL into a member list (the named circles, plus each named identity's
OdinId hash) and reconciles the file's index rows in the header's transaction: rows of members no longer named are
deleted, new ones inserted, and a row that stays isn't touched, so a share row's columns survive it
(`MainIndexMeta.BaseUpsertEntryZapZapAsync`, `TableDriveAclIndex.ReconcileRowsAsync`) **(V)**. A new row means read:
`permissions` 1, no expiry, the columns' defaults **(V)**. A hard delete and a drive purge delete the rows **(V)**.
Nothing else writes them **(V)**. On a Private Share drive the share API writes them and a header write leaves them
alone ("With Private Share").

**Who gets the ACL back:** owner callers only. Outside callers get the header without its server metadata
(`DriveFileUtility.CreateClientFileHeader`, `:113-116`) **(V)**.

**Peers' contexts are cached** for up to an hour (`OdinContextCache`): their level, enabled circles and whether they
are reviewed (`TransitAuthenticationService`) **(V)**. The cache is reset when a connection is finalized, blocked or
deleted, and when a member is removed from a circle (`CircleNetworkService.RevokeCircleAccessAsync`), not when a circle
is disabled **(V)**, so a check that must see a disable asks at the time (as the circle share key read does,
`CircleShareKeyService`) **(V)**.

## Copies sent to others

- **Feed:** a file goes to the followers the ACL lets in, judged from each follower's connection
  (`IdentityHasPermissionAsync`); an encrypted post only to those who can also decrypt the drive
  (`FeedDriveDistributionRouter`) **(V)**.
- **Direct sends** (`TransitOptions.Recipients`) aren't checked against the ACL: the sender names the recipients
  (`PeerOutgoingTransferService.CreateOutboxItems`) **(V)**.
- **A sent copy carries the ACL without its named identities**, so a recipient never learns who else was named
  (`SendFileOutboxWorkerAsync`, `UpdateRemoteFileOutboxWorker`) **(V)**.
- **The recipient's copy gets a required group of `Owner`**, except: a comment, new or updated, takes the ACL of the
  file it comments on, on every drive (`IncomingFileAcl`, from `PeerFileWriter` and `PeerFileUpdateWriter`), a
  drive marked as a community channel keeps the sender's ACL (`PeerFileWriter`), and a member's update of a standard
  file on a Private Share drive keeps the file's own ACL (`PeerFileUpdateWriter`) **(V)**. On a Private Share drive a
  member's update never creates a file: one with no stored file to update, standard or comment, is refused
  (`PeerFileUpdateWriter`) **(V)**.
- **A zero-knowledge drive sends nothing** (`ZkDriveFiles.AssertNotSent`) **(V)**.

## Special cases

- **Public drives:** the file services don't ask for drive `Read`, nor for `Comment` when writing a comment
  (`CommentFileStorageService`) **(V)**; the read rule still applies. The tenant's settings give signed-in and
  connected callers `Read`, and by flag `Comment` and `React`, on public drives (`TenantSettings`) **(V)**. On a
  drive that blocks enumeration that `Read` reaches a file only by its address: only the owner, the owner's apps and
  the server may list it (`StorageDriveDetails.BlockEnumeration`) **(V, tested)**.
- **Comments** are a separate file system on the same drive: reading needs drive `Read` or `Comment`, writing needs
  `Comment` (security-model.md, section 6) **(V)**. Who may comment follows the sharing model: on a Server Share
  drive the members who may read; on a Private Share drive through shares ("With Private Share"); on a Private
  drive only owner callers: any other caller's comment, new or an update, is refused, a local upload (a guest or
  YouAuth caller) as well as a peer's (`CommentFileStorageService.AssertCanWriteToDrive` / `CanWriteToDrive`) **(V,
  tested for a peer who may read the file and holds `Comment`)**.
- **Reactions** need drive `React` and never pass the read rule, locally or from a peer: a peer's reaction finds the
  file by global transit id with a drive check only (`ResolveFileId`) **(V)**. On a zero-knowledge drive it is applied
  at once, the file, a standard file or a comment, found in the request's file system without the read rule by drive
  `React` alone, not `Read` or `Write` (`GetServerFileHeaderByGlobalTransitIdSkippingFileAcl(…, permission)`), and on
  Private Share asks the share's `React` bit too
  (`PeerIncomingGroupReactionInboxRouterService.TryApplyToZeroKnowledgeDriveAsync`) **(V, tested; I: this route has no
  test of its own for a peer holding `React` alone)**; the reaction summary is changed in one read inside storage,
  without the read rule (`DriveStorageServiceBase.ChangeReactionSummary`, from all three `ReactionPreviewCalculator`
  reaction handlers) **(V)**. Reactions keep rules of their own
  (#1977); whether the drive check is enough is asked in #2058. They stay allowed on Private drives: Chat needs them.
- **Read receipts** need a drive check and go through the inbox on every drive, zero knowledge too
  (`PeerDriveIncomingTransferService.MarkFileAsReadAsync`); the inbox then applies them through the read rule
  (`PeerFileWriter.MarkFileAsRead`) **(V)**.
- **The server's own work skips the read rule's ACL part:** `System` passes it, and `UpgradeToByPassAclCheck` makes a
  caller an owner caller for one job: profile attributes, contacts, Shamir, zero-knowledge keys
  (`OdinContextUpgrades.UpgradeToByPassAclCheck`) **(V)**. `GetServerFileHeaderBy…SkippingFileAcl` finds a header without
  the read rule for the peer file-exists probes, the email key writer, Shamir, and the peer writers, which find a file
  sent again or updated and then decide who may change it (`DriveQueryServiceBase.cs:206-235`; "Changing a file you
  sent") **(V)**. A context marked for one file and its original sender (`OdinContextUpgrades.ForSendersOwnFile`) loads
  that file's header without the read rule or drive `Read`, while the sender may change it unread
  (`DriveStorageServiceBase.IsSendersOwnFileAsync`) **(V)**.
- **The CDN** reads payloads and thumbnails of CDN-enabled drives as `System`, so it passes every file's ACL there
  (`CdnAuthPathHandler`) **(V)**: `AllowCdn` means "the CDN may read this drive". The CDN never reads a zero-knowledge
  drive (`DriveManager.GetCdnEnabledDrivesAsync`) **(V)**, which is created refusing `AllowCdn`, anonymous reads and
  subscriptions: every setting at creation (`DriveSettings.AssertFit`), turned off by a conversion
  (`DriveSettings.FitForConversion`), and in each setter the one it changes, as it will be
  (`DriveSettings.AssertChangeFits`) **(V, tested)**
  (security-model.md, section 3).
- **Static and profile publishing** pick public files themselves: active, unencrypted, a required group of
  `Anonymous`, no TTL (`StaticFileContentService.Filter`) **(V)**.

## Drives marked as a community channel

**Status:** a drive attribute, not a kind of drive. Only a Server Share drive may carry it, since it needs the server
to hold the drive key: a zero-knowledge drive, Private or Private Share, refuses it (`DriveSettings.AssertFit`,
`FitForConversion`) **(V, tested)**. It could go once community and feed channels move to Private Share
drives, which isn't planned.

The attribute `IsCollaborativeChannel` (read through `StorageDrive.IsCollaborationDrive()`) is set by the community
app, by the feed app for channels members post to, and on the built-in Lists drive (`BuiltinDrives`) **(V)**. On such a drive
**(V)**:
1. a member who may read a post may change it, naming its version tag; elsewhere only the original sender may, and
   on a Private Share drive a member through a share (`SentFileChanges.ContextForChangeAsync`; "Changing a file you
   sent");
2. a peer's delete takes the direct soft-delete path, and only the post's original sender may delete it
   (`PeerDriveIncomingTransferService.AcceptDeleteLinkedFileRequestAsync`). Whether only the original sender is meant
   is Not decided (#2067);
3. an incoming file keeps the sender's ACL instead of `Owner` (`PeerFileWriter`, `PeerFileUpdateWriter`);
4. files received from others are distributed on to the members (`AllowDistribution`,
   `FeedDriveDistributionRouter.ShouldDistribute`);
5. an upload may name recipients for app notifications (`FileSystemStreamWriterBase`).

The community app's per-message flag `appData.content.isCollaborative` is inside the encrypted content, so only
clients read it, to decide whether to offer Edit **(V, odin-js)**. The server goes by the drive's attribute.

## With Private Share

On a Private Share drive only owner callers hold the drive key, and each share has a key of its own
([zero-knowledge-drives.md](zero-knowledge-drives.md#private-share)). **A share is the file's index row**: there
the index rows aren't a copy of the ACL but the record.

| | Other drives | Private Share drive |
|---|---|---|
| The ACL | the record: required group, named circles, named identities | names no one: named circles and identities refused. Required group `Owner` (owner callers only; the default) or `Connected` (shareable); the first share lifts `Owner` to `Connected`, keeping the version tag, and a later write keeps the stored group whatever it sends |
| The index rows | made from the ACL on every header write; each means read | share rows, written on their own by the share API; a header write leaves them alone |
| A file with no rows | open to every caller who reaches the required group | owner callers only, whatever its group |
| Which rows count in a query | a row naming the caller, or no row at all | a row naming the caller, live or not, so a withdrawn share reaches their sync; the read rule then lets through only a file with a live row, and turns the rest into tombstones. A file no row names the caller on is missing, by query and by id. A comment goes by the rows of the file its group names |
| The read rule's ACL part | the ACL's names | the caller's live row, and the caller reaching the required group, as everywhere |
| Who may add a file | drive `Write` | the same: drive `Write`, nothing else asked. It lands owner-only unless it carries rows: for circles the writer is in, their individual circle included ("Sharing with one person"). A deposit takes payloads through the payload-upload route too **(V, tested)** |
| Who may edit a file | drive `Write` plus passing the read rule | owner callers, and a member with drive `Write` whose live row has `Write`. The author of a deposit edits it through the row for their individual circle it carries |
| Sending a file again | the original sender's send again updates it while no one else has written it since; anyone else's (on a drive marked as a community channel, a member who may read it) names the file's version tag: "Changing a file you sent" **(V, tested)** | the same, as its original sender: a file with no share by that rule alone, one with a share through a live row with `Write` too ("Changing a file you sent") **(V, tested)**. It keeps its rows and its required group: shares come only with a new file, and a send again carrying any is refused. The same delivery again, the outbox retrying one that already landed, is taken as done (`PeerFileWriter.StoreNormalFileLongTermAsync`) **(V, tested)** |
| Who may react or comment | drive `React` or `Comment` (a reaction skips the read rule, #2058) | owner callers, and a member with drive `React` or `Comment` whose live row has the same bit. A member sees a file's reactions while they may read it. A comment is read by whoever may read its file, and is encrypted with its file's key (`fileKey`, its IV only); only its author changes or deletes it |
| Changing who may see a file | a header write: new version tag, `modified` moves | a share row write: `modified` moves, the version tag doesn't |
| How others learn of it | by `modified` | owner callers' other devices by `modified`; members by their query batch, where a withdrawn share comes back as "no longer shared" |

### A share row narrows its circle's grant

**The circles set the ceiling, and the share row picks under it, for every bit.** To use a
shared file a member needs the bit twice: in their drive grant, which is what their circles grant on the drive,
together, and in their live share row on the file:

| To | Drive grant | Live share row |
|---|---|---|
| read the file, its comments and its reactions | `Read` | any (a live row has `Read`) |
| edit or delete it | `Write` | `Write` |
| add a new file | `Write` | none: there is no file yet |
| react to it | `React` | `React` |
| comment on it | `Comment`, and they may read the file | `Comment` |

This is the rule people know from file servers: a share's permissions and the folder's permissions both apply, each
added up over the person's groups, and the stricter wins. It is also how other drives work: there a file's ACL only
narrows what a circle grants on the drive, never widens it, and each drive check reads the grant of all the caller's
circles together.

- **The drive grant is all the member's circles together, not the row's circle alone,** as every drive check reads
  it, and as file servers add up a person's groups: a member whose other circle grants the drive `Write` may edit a
  file whose row has `Write`, whatever the row's own circle grants. Each row's bits stay within its own circle's grant
  when written (below).
- **Changing a circle's grant changes all of its shares at once, both ways.** Take `Read` away and its members stop
  reading every file shared through it, unless another of their circles grants `Read`; give it back and every share
  works again as it was. The rows aren't touched,
  so nothing has to go through them. "At once" means when the member's context is next built: a change of a circle's
  grants doesn't reset the cached peer contexts (`CircleNetworkService.UpdateCircleDefinitionAsync`) **(V)**, so it
  takes up to an hour to reach a peer, on every drive, as a disable does (#2054, "Peers' contexts are cached" above).
- **A share starts within its ceiling:** the share API refuses a row with a bit its circle doesn't grant on the drive
  (`DriveShareService.cs:296-302`) **(V)**; for an individual circle, which grants nothing, a bit the member's other
  circles don't. A later change of a grant caps the row instead.
- **Where a file sits is the client's business.** Filesync names a file's folder by putting the folder's unique id in
  `GroupId` **(V, filesync)**; the server reads nothing into it. Which of a member's files a client shows in which folder is the client's choice.
- **A reaction asks `React` of both,** as a reaction on other drives asks drive `React` (#2058); finding the file
  first asks drive `Read` or `Write`, as there (`DriveQueryServiceBase.ResolveFileId`) **(V)**. A comment also needs
  the file readable, drive `Read` included, as a peer's comment does on other drives (`IncomingFileAcl`).
- **Owner callers aren't capped:** they hold the drive.

### Sharing with one person: individual circles

**A person is a circle with one member** (`CircleDesignation.Individual`). Shares with one person
are then share rows like any other: the circle's id, the file key locked with the circle's key. The circle key works as
for every circle: the owner's app holds it locked (`Circle.appKeyLockedShareKey`), the member a copy sealed to their
own drive (`CircleMember.sealedShareKey`) **(V)**.
- **One member at most:** the server refuses a second, wherever a member is added
  (`CircleMembershipService.AssertCircleTakesMemberAsync`) **(V)**. A client bug that added someone to "Frodo" would
  otherwise share everything shared with Frodo with them too.
- **Individual from its creation to its deletion:** no circle becomes individual or stops being one, so none can carry
  one person's shares to a group, and a refused change is refused before any member is re-granted
  (`CircleDefinitionService.AssertIndividualRulesKeptAsync`) **(V)**.
- **It grants nothing of its own.** The member's drive grant comes from their other circles (Family, Colleagues, a
  project's circle: an app may have many), and the individual circle's rows narrow it, as every row does (above). So
  removing Frodo from Family stops his individual shares at once; nothing has to follow. Every other circle must grant at
  least one drive or permission; an individual circle must grant none, and enrols no one by itself, on every write of
  it (`CircleDefinitionService.AssertGrantRulesAsync`) **(V)**. Nor may an app be authorized through it, which would
  give its member the app's grant (`AppRegistrationService.AssertCirclesExistAsync`) **(V)**. A share with it asks
  what the member holds through their other enabled circles, their apps' grants through them included: what their
  permission context holds (`DriveShareService.MemberHoldsAsync`, `CircleNetworkService.DrivePermissionAsync`) **(V)**.
- **The owner's client makes it**, not the server, which never sees a circle key: when it adds members to any of the
  app's circles that grants a Private Share drive, at the moment it seals that circle's key to them. It first looks
  for the member's existing one (app, designation, member). The app creates it like any of its circles, naming the
  designation (`CreateAppCircleRequest.Designation`: an app makes `Personal` and `Individual` circles; the other kinds
  change how the owner reviews a connection, so they stay the owner's). A slug of its own per member makes the create
  return the same circle to every device (#1939), so two devices racing make one; a slug naming a circle of another
  kind is refused rather than returned **(V)**.
- **It counts as the member's circle** in their context and in the query's member list, though it grants nothing: a
  member finds and opens a file through its row **(V, tested)**.
- **Removing the member from their other circles leaves them in it:** with no grant it gives nothing, and their sealed
  key copy is kept for when they are added back. A disconnect removes them, as from every circle (I).
- **Clients' circle lists hide individual circles;** the server lists them like any circle **(V)**. No access check
  reads the designation; the share API reads it to set a share's ceiling, and the circle rules above to keep its shape
  (`CircleEnrollment.cs`) **(V)**.

**The author keeps their deposit.** A member's new file lands owner-only, so its author couldn't see or change it.
Their client locks its file key with their individual circle's key (their copy is theirs to open), bound to the global
transit id the client picked for the file, as the file id is the receiving server's to pick (selective-share-plan.md,
"Blob formats"), and sends the row with the file, as a member already may for circles they are in (`DriveShareService.AssertMemberMayShareAsync`)
**(V)**. That row gives them what a drive marked as a community channel gives a file's original sender, through the one rule above: their
sync finds it, their other devices can open it, and they delete it with a `Write` share, as they may a new file of
theirs with no share, as its sender **(V, tested)**; their edit
goes through the same check, sending a new `sealed` key header of the same file key as a deposit's edit does, its
payloads included **(V, tested)**, and so does sending it again **(V, tested)**. The owner can withdraw it like any share.

### Sharing with a group

**Sharing with several people is sharing with a circle:** the app makes the group a circle of its own (`Personal`),
granting the drive, and a file shared with the group has one row, for that circle. Every member reads through it
**(V, tested)**; one row per file, however many members.
- **A member's new file goes back to the group:** their client sends it with the group's row, as for any circle
  they are in, and every member then reads it **(V, tested)**. Only its original sender sends it again **(V, tested)**;
  the others change it through an update, with the row's `Write` **(V)**.
- **A new member reads everything already shared with the group,** once the owner's client seals them the circle's key.
- **A member who leaves stops reading at once** on the server **(V, tested)**, but keeps the copy of the circle key
  they had, and whatever they opened with it. Nothing is re-locked: the "no rotation on removal" risk of
  [zero-knowledge-drives.md](zero-knowledge-drives.md).
- **The group's name:** a member can't read the owner's circle definitions, so how their client names the group is
  the clients' (Not decided).

### Who a file is shared with

**The owner's client finds the files shared with a person or a group** by the query batch's `SharedWith`: circle ids,
and the files with a live row for at least one of them come back, paged as any query, without walking the drive's
files **(V, tested)**. A person is their individual circle, a group its circle. Withdrawn and expired rows don't
count; `includeShares` gives each file's live rows. Only the callers who manage the drive's shares may ask: the owner
console and the app that owns the drive; anyone else is refused, and on any other drive it is a client error
**(V, tested)**. Such a query is never cached, as a row can expire (`QueryBatchCached`).

### Telling someone you shared with them: markers

**The marker is the clients' to build; the server part is built.** **Frodo's client leaves a marker on Sam's Private Share drive of the same app**: a file of the
app's marker file type, saying only "Frodo has files for you" (Frodo's identity and drive), sealed to Sam's drive as
any deposit. Sam's client keeps markers out of its file tree, shows Frodo as someone sharing with him, and when Sam
opens that, pages Frodo's drive with the query batch over the Peer API. Nothing of Frodo's files is copied to Sam.
- **Frodo needs drive `Write` on Sam's drive**, as on a drive marked as a community channel, before Sam has done anything for him. Sam's
  app makes a circle every connection joins, granting `Write` on the drive and nothing else
  (`CreateAppCircleRequest.GrantOn = Connect`, held to the deposit-only rule as every such circle is), with a fixed
  slug so every device finds the same one. The app needs `ReadConnections` for it, since it may list its own circle's
  members and so would learn every connection. New connections join it as they connect; the app adds the ones made before
  it (`enrollment-candidates`, which needs `ReadConnections`, then `circles/add-many`) **(V, tested)**. Every
  connection can then leave files on Sam's drive; they land owner-only, and Sam's client shows the markers alone.
- **One marker per sharer:** Frodo's client always sends it with the same unique id. A second send updates it, for a
  group's new name or to show a new share, naming no tag, until Sam changes it or shares it ("Changing a file you
  sent") **(V, tested)**. Sam dismisses it by deleting it; Frodo's next share makes it
  again after a hard delete, while a soft delete's tombstone refuses it, as a deleted file stays deleted (I, not tested). Sam's client trusts a
  marker only when its sender is the identity it names: another connection could take Frodo's marker id first, and
  then Frodo's own is refused until Sam deletes theirs.
- **It stays when Frodo withdraws his shares;** Sam's client stops showing him when the query finds nothing.

**What builds it** (the share API, shares in the upload, the query and the read rule, `includeShares`, members'
reads, writes, sync and socket events): [selective-share-pr3-plan.md](selective-share-pr3-plan.md).

**Is the community channel attribute still needed?** On a Private Share drive, no. Its edit rules (1 and 2
above) are what a share row's `Write` bit says, per file; its other effects (3 to 5) move copies, and a
zero-knowledge drive sends none. Private Share drives refuse the attribute so the two can't mix; the community
app's and feed channels' drives, which work by copies, keep it.

## Kept, retiring, retired

| What | Status |
|---|---|
| The community channel attribute (`IsCollaborativeChannel`) | **kept** on Server Share drives; **refused** on Private and Private Share drives; can go when community and feed channels move to Private Share drives, not planned |
| The ACL's named circles and identities on a Private Share drive | **refused**: the share rows say who. Kept on every other drive |
| Index rows made from the ACL | **kept** on every other drive; share rows instead on Private Share drives |
| `QueryModifiedAsync` (V1's change query) | **obsolete**, V1 only; the query batch does the same. Left as it is (it ignores its `MaxDate`) until V1 goes |
| `FileUpdateInstructionSetV2` | **unused** by any route, referenced only by a V2 test **(V)**; to remove |
| The drive option `IsReadonly` | documented but **not enforced**: no write path reads it (security-model.md, section 3) **(V)**. Open: enforce or remove |
| Key header version 1 (AES-CBC; the IV cancels out) | **to be replaced** by version 2 (#2024); share rows use AES-GCM, format 2, from the start |

## Open

Not decided:
- reactions and the read rule on other drives (#2058);
- whether only a post's original sender may delete it on a drive marked as a community channel (#2067);
- **how a member's client names a group** it reads files through;
- **marker ids another connection can't take first**, for example ones only Frodo and Sam can derive.

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
