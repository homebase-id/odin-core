# Terminology

The words the reference docs use, each with one meaning. Where the code's name differs, it follows in backticks.
Read it before writing about drives, keys or files. The docs it serves: [security-model.md](security-model.md) (the root),
[file-access.md](file-access.md), [owner-keys.md](owner-keys.md) (every key's shorthand), and
[zero-knowledge-drives.md](zero-knowledge-drives.md).

Markers follow security-model.md: **(V)** read in the code (file named), **(I)** inferred. **(to build)** marks a term
whose meaning the code doesn't give yet.

## Zero knowledge

| Term | Means |
|---|---|
| **zero knowledge** | the server never holds a key that opens the data, not even in memory. A zero-knowledge drive's key is made in the owner's browser and locked by the zero-knowledge master key, which only the browser opens (`ZKMK`, [owner-keys.md](owner-keys.md)) |
| **zero knowledge at rest** | the server stores the key locked, so it can't open it while nobody is signed in, but opens it in memory while the owner, one of the owner's apps or a member is online and calling the API. Every Server Share drive is zero knowledge at rest **(I: not traced for every key path)** |
| **zero-knowledge drive** | a drive whose sharing model is Private or Private Share. One definition, by the model: `StorageDrive.IsZeroKnowledge` asks `DriveSharingModel.IsZeroKnowledgeDrive()`, and the drive key's copies follow from it: a save refuses a zero-knowledge model with a master-key copy, a Server Share model without one, and a drive with neither (`DriveManager.AssertKeyCopies`) **(V)**. Creation sets the model, and so does conversion, since the model existed (`DriveManager.ApplyZeroKnowledgeKeysAsync`) **(V)** |
| **client-locked file** | a file whose key header a client locked or made (drive-locked, sealed or file-key, below): the server can't open it, on any drive (`EncryptedKeyHeader.IsLockedByClient`) **(V)** |

## Sharing models

A drive's **sharing model** says who holds its drive key. An app chooses it when it creates the drive; an existing
drive changes it only by conversion (`Drives.SharingModel`) **(V)**.

| Model | Code | Who holds the drive key | Others |
|---|---|---|---|
| **Server Share** | `DriveSharingModel.Collaborative` | the server, locked at rest; it opens the key for the owner, the owner's apps and the members of the drive's circles, whose grants carry it. Zero knowledge at rest, not zero knowledge | members read and write through their circles' grants |
| **Private** | `DriveSharingModel.Private` | the owner's devices and apps only. Zero knowledge | others may only add **deposits** |
| **Private Share** | `DriveSharingModel.SelectiveShare` | the owner's devices and apps only. Zero knowledge | others add deposits, and read the files shared with them through **shares** |

- **Server Share is the default**: a drive is Server Share unless its app asked for another model **(V)**.
- **The names pair up**: Server Share and Private Share differ in who shares (the server, by handing out the drive
  key, or the owner privately, by locking a key per file for chosen people); Private and Private Share are the
  zero-knowledge pair.
- **The code still has the older names**: `Collaborative` for Server Share, too close to the community channel below,
  and `SelectiveShare` for Private Share, also in type names such as `SelectiveShareDrives` and in the plan docs'
  file names. Renaming them in the code is **(to build)**; both are part of the API (`sharingModel: "collaborative"` or
  `"selectiveShare"` in a drive's public-key answer), so clients change with it.
- `DriveSharingModel.Unclassified` exists in the code and nothing sets it **(V)**.

## Drives

| Term | Means |
|---|---|
| **drive key** | the key every file key on the drive is locked with (`DrvK`, [owner-keys.md](owner-keys.md)); the code calls it the storage key |
| **write-only key pair**, **the drive's public key** | a drive's P-384 key pair; anyone may seal to the public half, which is how deposits are made (`WO-PK`, [owner-keys.md](owner-keys.md)). The public key is served to any identity, with whether the asker may write to the drive (`DrivePublicKeyResponse.CallerMayWrite`) **(V)** |
| **drive permission** | what a caller may do on a whole drive, from their grants: `Read` 1, `Write` 2, `React` 4, `Comment` 8, `ConditionalTemporalRead` 16 (`DrivePermission`, `PermissionContext`) **(V)** |
| **public drive** | a drive with the option `AllowAnonymousReads` |
| **drive option** | a typed setting on the drive the drive API validates: `OwnerOnly`, `AllowAnonymousReads`, `AllowSubscriptions`, `AllowCdn`, `IsArchived`, `IsReadonly`, `RequirePayloadHashes` (`StorageDriveDetails`) **(V)** |
| **drive attribute** | a free key/value string an app sets on a drive; the server gives meaning to one, the community channel (`StorageDriveDetails.Attributes`) **(V)**. An option and an attribute do the same job and differ only in how they're stored |

### Community channel

**Community channel** is a drive attribute, not a kind of drive: `IsCollaborativeChannel` in the code, read through
`StorageDrive.IsCollaborationDrive()` **(V)**. It needs the server to hold the drive key, so only a Server Share drive
may carry it: a zero-knowledge drive, Private or Private Share, refuses it at creation, conversion and when its
attributes change (`DriveSettings.AssertFit`, `FitForConversion`, `AssertChangeFits`) **(V)**. On a drive marked as a community channel:

- **A member's post keeps the sender's ACL**, so other members see it; on other drives a peer's new file lands
  owner-only, except a comment, which takes its file's ACL, and on a Private Share drive a file a member sends with
  shares (`PeerFileWriter`) **(V)**.
- **A member who may read a post may change it**, naming its version tag (`SentFileChanges.ContextForChangeAsync`)
  **(V)**.
- **Only a post's original sender may delete it** (`PeerDriveIncomingTransferService.AcceptDeleteLinkedFileRequestAsync`)
  **(V)**. Whether that is intended is open (#2067).
- **The owner's server sends members' posts on** to the drive's followers (`FeedDriveDistributionRouter`) **(V)**, and
  the owner's upload may push an app notification to members (`FileSystemStreamWriterBase`) **(V)**.

Write "a drive marked as a community channel", not "community drive" or "collaborative drive".

## Callers

| Term | Means |
|---|---|
| **owner** | the identity that owns the drive |
| **owner callers** | the **owner console** (signed in with the master key) and the **owner's apps** (an app's client token). Both act at the `Owner` level (`CallerContext.IsOwner`) **(V)** |
| **outside callers** | everyone else: peers, YouAuth domains, signed-in identities that aren't connected, and anonymous visitors |
| **peer** | another identity, calling this one through the Peer API (`/api/peer/…`, `/api/v2/peer/…`) |
| **member** | an identity in a circle |
| **original sender** | the peer whose file a copy on this identity is (`FileMetadata.SenderOdinId`) **(V)** |
| **`System`** | the server's own work (outbox, jobs, the CDN): a caller level, not a person |

## Levels and groups

| Term | Means |
|---|---|
| **level** | where a caller stands on the ladder `Anonymous` 111 < `Authenticated` 444 < `AutoConnected` 555 < `Connected` 777 < `Owner` 999 (`CallerContext.SecurityLevel`) **(V)**. An unreviewed connection's level counts as `Authenticated` wherever content is judged (`ReviewedSecurityTier.EffectiveLevel`) **(V)** |
| **required group** | the rung a file asks for: the ACL's `RequiredSecurityGroup`, on the same ladder **(V)**. A group that is missing or undefined (0, or any other number that names no rung) means `Owner` (`AccessControlList.RequiredSecurityGroup`); `System` is defined, and refused as a file's group by the ACL check |

## Files

| Term | Means |
|---|---|
| **header** | the file's server-side record (`ServerFileHeader`): metadata, key header and server metadata, stored in `driveMainIndex` **(V)** |
| **ACL** | the file's access control list, in the header's server metadata (`ServerMetadata.AccessControlList`): a **required group**, plus optional **named circles** (`CircleIdList`) and **named identities** (`OdinIdList`) **(V)** |
| **the read rule** | the one check that decides whether a caller may see a stored file: `IDriveAclAuthorizationService.CallerMayReadAsync` **(V)** |
| **version tag**, **`modified`** | the header's version (a new one on every header write, used to refuse a stale update) and its last-change time (what clients sync by) **(V)** |
| **the sender's last write** | the record of the last write a peer made to a file on this identity: who, their tag and the tag it gave this copy (`ServerMetadata.SendersWrite`); this server's own, never sent between identities **(V)** |
| **deposit** | a file someone else adds to a drive they may write but can't read: its key header `sealed` to the drive's write-only key, so only the owner's devices can open it **(V)** |
| **tombstone** | what a soft-deleted file leaves: its header without content or payloads (`FileState.Deleted`) **(V)** |

### Key headers

A file's **key header** is its file key and IV, locked. Its kind says who locked it with what (`EncryptionType`) **(V)**:

| Kind | Code | Locked with | Where |
|---|---|---|---|
| **server-locked** | `Aes` | the drive key, by the server | Server Share drives |
| **drive-locked** | `ZkDrive` | the drive key, by an owner device | zero-knowledge drives |
| **sealed** | `Sealed` | the drive's write-only public key, by whoever wrote the file | deposits: only a zero-knowledge drive takes one, and the transient drive a deposit is sent from (`StorageDrive.CanHoldDeposits`) **(V)** |
| **file-key** | `FileKey` | nothing: a comment encrypted with the key of the file it comments on, which every reader of that file holds, so the header holds only the comment's IV | Private Share comments |
| **share** | `Share` | a circle key: the share row's locked file key and the file's IV | sent to a member for a file shared with them; a member's update sends one back with only the new IV. Never stored as a file's key header (`ZkDriveFiles.AssertPeerMayUpdate`) **(V)** |

## Files from others

| Term | Means |
|---|---|
| **copy** | what a recipient stores of a file another identity sent; it has its own file id and version tags **(V)** |
| **outbox**, **inbox** | the sender's queue of files to send, and the recipient's queue of arrivals that wait for a key holder to sign in. Only a Server Share drive keeps an inbox for a peer's files, since only there does the server re-lock what arrives: a zero-knowledge drive (`StorageDrive.HasInbox` false) writes a peer's new files, updates, deletes and reactions at once (`PeerDirectWrites.CanWriteAtOnce`, `PeerDriveIncomingTransferService.AcceptDeleteLinkedFileRequestAsync`, `PeerIncomingGroupReactionInboxRouterService`) **(V)**. Read receipts go through the inbox on every drive (`PeerDriveIncomingTransferService.MarkFileAsReadAsync`) **(V)** |
| **direct write** | an arrival written to the drive at once, without the inbox. One check decides it for a peer's new files and updates alike (`PeerDirectWrites.CanWriteAtOnce`), made once on arrival and reused when the transfer finishes: at once unless it is encrypted, on a Server Share drive, with a key header the server must re-lock, and the caller lacks the drive key. A key header passed on as sent (sealed, share, file-key) needs no key; a comment that can't be written at once is refused, never queued **(V)** |

## Shares (Private Share)

| Term | Means |
|---|---|
| **share**, **share row** | on a Private Share drive, a file's `DriveAclIndex` row for a circle: permission bits, an expiry, the **locked file key** (`lockedFileKey`: the file key locked with the circle key, no IV), and its own `created` and `modified` **(V)** |
| **index row** | a `DriveAclIndex` row on any drive: one per named circle or named identity of a file (an identity as its OdinId's hash), so the query can find "files my circles may see" **(V)** |
| **circle key**, **share key** | a circle's own key, which a share's locked file key is locked with; the owner's app holds it locked, each member a copy sealed to their own drive's public key **(V)** |
| **live share**, **live row** | a share with `Read` that hasn't expired |
| **withdrawn share** | a share kept with no bits and no locked file key, so members syncing see it go: to a share what a tombstone is to a file |
| **individual circle** | a circle with one member (`CircleDesignation.Individual`), in the same `Circle` table as every other circle, so that a share with one person is a share like any other |
| **group circle** | a circle an app makes for a group of people it shares with; its designation is `Personal`, as for any circle a person makes (`CircleDesignation.Personal`) **(V)** |
