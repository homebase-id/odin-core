# Zero-knowledge drives

**Status:** built for Private and Private Share drives: client-locked files, the zero-knowledge app key, and drives
created or upgraded zero knowledge (Vault, Email and Shard Recovery are Private, Filesync is Private Share). Shares
are built in the open Private Share PRs ([file-access.md](file-access.md), Status). Chat is **(to build)**. [How a zero-knowledge drive holds files](#how-a-zero-knowledge-drive-holds-files)
has the model in short. Words follow [terminology.md](terminology.md): the three sharing models are Server Share
(`Collaborative` in code), Private and Private Share (`SelectiveShare` in code).

Markers follow [security-model.md](security-model.md): **(V)** verified in the code (file named). **(to build)**
marks what is decided and not built, **Not decided** what is open. Anything without a marker is the design as
described, not checked here line by line.

## How a zero-knowledge drive holds files

The model in short; the reasons, alternatives and work are in
[zero-knowledge-drive-api-plan.md](zero-knowledge-drive-api-plan.md).

- **What makes a drive zero knowledge:** its sharing model is Private or Private Share, one definition
  (`StorageDrive.IsZeroKnowledge` asks `DriveSharingModel.IsZeroKnowledgeDrive()`), and the drive key's copies
  follow from it: a save refuses a zero-knowledge model with a master-key copy, a Server Share model without one,
  and a drive with neither (`DriveManager.AssertKeyCopies`) **(V)**. Creation sets the model, and so does conversion
  (`DriveManager.ApplyZeroKnowledgeKeysAsync`) **(V)**.
- **One key header per file, the rest under FileKey.** A file's key header holds FileKey (its AES key)
  and an IV. `appData.content`, `localAppData.content` (its own IV), payloads and thumbnails (the
  payload's IV) are all encrypted with FileKey **(V)**. Only the key header is ever locked or re-locked;
  content and payloads never are.
- **On a zero-knowledge drive every encrypted file is client-locked,** in one of two ways, and the server
  opens neither:
  - `zkDrive` (drive-locked): locked with the drive key (DrvK) by a device holding it: the owner console, or a
    device handed its app's zero-knowledge key at sign-in (`ZkDriveFiles.CallerHoldsDriveKey`) **(V)**;
  - `sealed`: sealed to the drive's write-only public key (`WO-PK-Pub`) by anyone with Write, so a
    friend or a sender can write without DrvK; opened with `WO-PK-Priv` (P-384 ECDH, HKDF, AES-GCM).
  - A key header locked with the shared secret (`aes`) is refused by its type, before it is opened.
- **One read rule** (`IDriveAclAuthorizationService.CallerMayReadAsync`): the file's ACL, and a
  client-locked file only for a caller that can open it (a device holding the drive key, as above, or the
  system's own jobs); on a Private Share drive a member reads through a live share, and gets the `share` key
  header (`DriveFileUtility.CreateClientFileHeader`) **(V)**. Reads, queries and sockets all ask it. A
  client-locked key header is returned as stored, and payload and thumbnail responses carry no packed key header:
  the client uses FileKey and the payload's IV.
- **One write rule:** a key holder writes `zkDrive`, and can't upload a `sealed` file to the drive; a
  sender's `sealed` new file is stored as sent. **A `sealed` key header replaces only a `sealed` one**: a
  file a key holder took over (`zkDrive`) never goes back to sealed, and an `aes` file never loses the key
  only the server could open. Updates and overwrites go through their usual version-tag and IV checks.
  Nothing else changes a client-locked key header, and a zero-knowledge drive sends nothing (no recipients,
  feeds or CDN) (`ZkDriveFiles.AssertNotSent`) **(V)**.
- **One "may a peer write directly" check** for new files and updates (`PeerDirectWrites.CanWriteAtOnce`): at once
  unless the file is encrypted, on a Server Share drive, with a key header the server must re-lock, and the caller
  lacks the drive's key. A key header passed on as sent (`sealed`, `share`, `fileKey`) needs no key; a comment that
  can't be written at once is refused, never queued **(V)**. It is decided once, on arrival, and reused when the
  transfer finishes (`PeerDriveIncomingTransferService`, `PeerDriveIncomingFileUpdateService`) **(V)**.
- **No inbox for a peer's files:** only Server Share drives keep one for them (`StorageDrive.HasInbox`). On a
  zero-knowledge drive a peer's new file, update, delete and reaction are all written at once **(V, tested)**. Read
  receipts still go through the inbox on every drive (`PeerDriveIncomingTransferService.MarkFileAsReadAsync`)
  **(V)**. A delete or a reaction finds the file without the read rule
  (`GetServerFileHeaderByGlobalTransitIdSkippingFileAcl`), which would hide a deposit from its sender, in
  `PeerDriveIncomingTransferService.AcceptDeleteLinkedFileRequestAsync` and
  `PeerIncomingGroupReactionInboxRouterService.TryApplyToZeroKnowledgeDriveAsync` **(V)**. Who may delete is the rule
  for changing a file you sent: the original sender, or on Private Share a member through a live share with `Write`
  ([file-access.md](file-access.md), "Changing a file you sent") **(V, tested)**. The sender's delete is refused
  unless the file's record of their last write (`ServerMetadata.SendersWrite`) names them and still matches the file's
  version tag, so a file stored before that record was kept is refused too; on a drive still marked as a community
  channel only the original sender deletes (`SentFileChanges.ContextForDeleteAsync`) **(V)**. The delete is of the
  version judged (`expectedVersionTag`), and a write that lands in between is judged again, once, rather than lost
  (`PeerDriveIncomingTransferService.DeleteAtOnceAsync`) **(V)**. A delete of a file already deleted does nothing
  **(V)**. A reaction's lookup asks drive `React`, not `Read` or `Write`
  (`GetServerFileHeaderByGlobalTransitIdSkippingFileAcl(…, permission)`), in the request's file system, so a reaction
  to a comment works; on Private Share the share's `React` bit is asked too **(V; I: the group-reaction route has no
  test of its own for a peer holding `React` alone)**. The reaction summary is changed in one read inside storage,
  without the read rule (`DriveStorageServiceBase.ChangeReactionSummary`, from all three `ReactionPreviewCalculator`
  reaction handlers) **(V)**.
- **Transforming `sealed` into `zkDrive`** is optional: a key holder opens a deposit's `sealed` key header and
  locks the same FileKey and IV with DrvK, and sends it to `POST /api/v2/drives/{driveId}/files/transform-key-headers`
  with the version tag it read alongside the sealed one (`DriveStorageServiceBase.TransformKeyHeadersAsync`) **(V,
  tested)**:
  - **Who:** a caller holding the drive's key (`ZkDriveFiles.CallerHoldsDriveKey`): the owner console, or an app
    device handed its app's zero-knowledge key, such as chat-kmp's, with Write on the drive. Anyone else is refused
    (403), before the drive is looked at **(V, tested for an app device without the key; I: a guest, by the same
    check)**. Never on the Shard Recovery drive: the server releases a friend's shard only as the sealed deposit it
    holds (`ShamirBaseService`), so shards stay `sealed` **(V, tested)**. That is Shamir's need, not a rule of drives:
    the server tells whether a friend's browser can still open a shard from the key its seal names, and a `zkDrive`
    key header names none. Once Shamir keeps its own record of the key a shard is locked under, the exception goes
    **(to build)**.
  - **Per file**, in request order, one file's refusal stopping no other: the file exists and is active
    (`fileNotFound`, `cannotUpdateNonActiveFile`); its stored key header is `sealed` (`notSealed`); the new one is a
    valid `zkDrive` key header with the stored IV, which the content was encrypted under (`invalidKeyHeader`); the
    file is still at the version tag read (`versionTagMismatch`, also for a file hard-deleted since)
    (`ZkDriveFiles.TransformedKeyHeader`) **(V, tested)**.
  - **Only the key header changes:** one statement per file, guarded by its version tag
    (`TableDriveMainIndex.UpdateEncryptedKeyHeaderAsync`, the one a drive's conversion relabels with), leaves the
    version tag and `modified` as they are **(V, tested)**, and raises no file-changed event **(V; not tested)**, all in
    one transaction. So no reader re-fetches,
    every writer's cached version tag stays valid, and a deposit's sender may still delete it, as their record of their
    last write still matches ([file-access.md](file-access.md), "Changing a file you sent") **(V, tested)**.
  - **Races:** a sender's change that lands between the read and the transform stands (`versionTagMismatch`), and the
    key holder transforms the new one when it reads it **(V, tested)**. After the transform, a sender's `sealed`
    change is refused like any `sealed` key header over a taken-over one **(V, tested)**: whether a deposit's sender
    may keep editing it (keeping the drive's key header, with a new IV) is **not decided**. Two devices transforming
    the same file send the same bytes, since a `zkDrive` key header depends only on DrvK, FileKey and the IV; the
    second to arrive gets `notSealed`, as the stored key header is no longer sealed (I: not tested).
  - **Known race:** a full header write that read the file before the transform keeps the same version tag, so its
    version-tag guard still passes and it writes the `sealed` key header it read back over the transformed one. The
    file is then `sealed` again, which still opens to the same FileKey; a key holder transforms it again when it next
    reads it (I: not tested; on SQLite the stale writer more likely fails).
  - **Not adjusted:** the file's byte count, which counted the larger `sealed` key header, until the defragmenter
    recounts it (I).
  - **What the server can't check:** that the new key header locks the same FileKey. The client opens what it locked
    before sending it.
  - **Golden vector:** `ClientLockedKeyHeaderVectorTests` (and odin-js's copy) holds a `sealed` key header and the
    `zkDrive` one a transform makes of it **(V)**. Shamir leaves its shards `sealed`.
- **Decided against:** a separate client inbox (it became the drive query in a second
  place), and a hidden "waiting" file on the drive (updates would hide files that were already
  readable).
- **Known weakness of key header version 1:** the CBC format lets the IV cancel out, so the server sees
  which key headers share a FileKey. Version 2 fixes it
  ([#2024](https://github.com/homebase-id/odin-core/issues/2024)).
- **No content integrity, by decision:** out of the threat model ([security-model.md](security-model.md),
  section 8).
- **Drive settings:** a zero-knowledge drive allows neither anonymous reads, the CDN nor subscriptions, and can't be
  marked as a community channel; a Private Share drive can't be `OwnerOnly` either. One rule, by the drive's model:
  every setting at creation (`CreateDriveRequest.AssertSharingModel`, `DriveSettings.AssertFit`); a conversion
  (`DriveManager.ApplyZeroKnowledgeKeysAsync`) turns anonymous reads, the CDN and subscriptions off and refuses the
  rest (`DriveSettings.FitForConversion`), so a re-key after recovery never fails on them; an attribute change
  (`UpdateAttributesAsync`) and `SetDriveReadModeAsync`, `SetDriveAllowSubscriptionsAsync` and `SetDriveAllowCdnAsync`
  ask only the setting they change, as it will be (`DriveSettings.AssertChangeFits`), so turning a setting off always
  works and a drive with a setting set before the rule can be brought into line **(V, tested)**.
- **Comments and reactions:** on a Server Share drive members who may read comment; on a Private Share drive
  members comment through shares ([file-access.md](file-access.md)); a member's update of a comment never creates a
  new one (`PeerFileUpdateWriter`) **(V)**. On a Private drive only owner callers comment (the server's own jobs, a comment's
  TTL, pass too): any other caller's comment, new, an update or a delete, is refused, a local upload (a guest or YouAuth caller) as well as a peer's
  (`CommentFileStorageService.AssertCanWriteToDrive` / `CanWriteToDrive`) **(V, tested for a peer)**; reactions stay
  allowed there (Chat needs them).

## Goal

- **Server Share drives:** zero knowledge at rest. The server stores the keys locked, but holds them in memory
  while the owner, an app or a member calls the API.
- **Private and Private Share drives:** zero knowledge. The server never sees a drive key or a file key, even
  briefly. Keys are opened only on the owner's own devices, and a member's.
- **Also:** a peer you share with never gets your drive key.

## Where the server sees keys on a Server Share drive

- **Upload:** the client sends the KeyHeader wrapped in its shared secret, and the server
  rewraps it with the storage key (`DriveStorageServiceBase.cs`, `EncryptKeyHeaderAes`) **(V)**.
- **Read:** the server unwraps the KeyHeader with the storage key and rewraps it for the caller
  (`DriveFileUtility.CreateClientFileHeader`) **(V)**.
- **Peer transfer:** the server rewraps the KeyHeader once per recipient
  (`PeerOutgoingTransferService.cs`) **(V)**. The recipient's server unwraps it and rewraps it
  with its own storage key (`PeerInboxProcessor`) **(V)**.
- **Circle grants:** a circle's drive grant carries the drive storage key, wrapped under the
  Peer Key (`DriveGrant.KeyStoreKeyEncryptedStorageKey`) **(V)**. So a connected peer's
  credentials unlock the whole drive key on our server.

## Three sharing models

A drive's app picks its sharing model when the drive is created; nothing said means Server Share
(`CreateDriveRequest.SharingModel`) **(V)**. An existing drive changes it only by conversion (below). All three
are encrypted at rest. Private and Private Share are zero knowledge: no key is ever on the server, not even in
memory. Server Share is zero knowledge at rest, with in-memory blips.

| Sharing model | In one line | Example |
|---|---|---|
| **Private** | Zero knowledge, nothing shared. Others can only deposit. | Vault, Email, Shard Recovery **(V)**; Chat **(to build)** |
| **Private Share** | Zero knowledge, plus a key per circle, and a share per file and circle | Filesync: share one folder **(V)** |
| **Server Share** (`Collaborative` in code) | Zero knowledge at rest, with in-memory blips. No per-file client work, however many files | every other drive, among them the drives marked as a community channel, and Chat today |

Naming notes:
- "Collaborative" is the code's name for Server Share, not a name in the docs ([terminology.md](terminology.md)).
- "Type" is taken by `DriveType` / `TargetDrive.Type`.
- "Key model" is already used for key vocabulary in
  [app-circle-membership-plan.md](app-circle-membership-plan.md).
- `OwnerOnly` is an existing flag.

### Private

- **For:** data nobody else reads (Vault), and inboxes others only write to (chat).
- **Who holds the drive key:** the owner's devices and apps only.
- **Others:** deposit by locking to the drive's public key (`WriteOnlyKeyPair`, private half
  escrowed under the storage key, `StorageDrive.cs`) **(V)**. They can write but never read.
- **Chat (to build):** the sending client locks each message key to every recipient's chat drive public key,
  and the recipient's server stores each as a deposit, as received. How the sender's own copy on a Private drive
  goes out is **Not decided**: a zero-knowledge drive sends nothing today (`ZkDriveFiles.AssertNotSent`) **(V)**.

### Private Share

- **For:** a private drive where you share chosen files or folders. Example: sharing one filesync
  folder with John.
- **Who holds the drive key:** the owner only, as with Private.
- **Circle key:** each circle that shares has its own key. The owner's app holds it locked
  (`Circle.appKeyLockedShareKey`), and each member a copy sealed to their own drive's public key
  (`CircleMember.sealedShareKey`) **(V)**. One person is an individual circle, a group a group circle
  ([file-access.md](file-access.md), "Sharing with one person").
- **Files:** a shared file has one share per circle: its `DriveAclIndex` row for that circle, holding the file key
  locked with the circle key (`lockedFileKey`), the share's expiry, what it may do and its own `modified`, and only
  there, not in the file's ACL. The share API writes them (`DriveShareService`) **(V)**. A share changes who may read
  the file, not its version tag **(V)**.
- **Never public:** a file's ACL is `Owner` or `Connected`, and names no circles or identities
  (`SelectiveShareDrives.AssertAclFits`) **(V)**.
- **Cost:** sharing new files is free, because the shares ride in the upload (`UploadFileDescriptor.Shares`)
  **(V)**. Sharing many existing files is a client bulk job.
- **Links (Not decided):** sharing with someone not on Homebase by a link that carries the key. It fits
  the same share rows, as a third kind of share target next to circles and persons: see
  [Links: sharing with someone not on Homebase](#links-sharing-with-someone-not-on-homebase).
  Plan and exact columns: [selective-share-plan.md](selective-share-plan.md).

#### Links: sharing with someone not on Homebase

To consider, not decided. The idea is that a link is just another share. Every share has its own share key,
and every shared file has one share row per share holding the file key locked with that key. Only the way
a reader gets the share key differs:

| Share target | How the reader gets the share key |
|---|---|
| Circle | locked to each member's drive public key, one row per member |
| Person | the same, with one member |
| Link | in the link itself, never stored on the server |

- **The key travels in the URL fragment**, `https://michael.id/s#<key>`, never in a query parameter. A
  fragment is not sent to the server, so it stays out of request logs, proxies and `Referer` headers. WebDrop
  already does this ([web-drop-plan.md](web-drop-plan.md#url-contract)) **(V)**.
- **One secret in the link.** The viewer works out the share's id from the key
  (`shareId = HKDF(key, "share-id")`). The server sees the id, never the key, and can't work the key out
  from it. Holding the link is both the right to fetch and the key to open.
- **One new read path, nothing else changes.** An anonymous caller with a share id can list and fetch only
  the files with a row for that share: its locked file key and the payloads exactly as stored, ciphertext. File ACLs, encryption, the
  drive's `AllowAnonymousReads` and enumeration all stay as they are, and the file is not copied, as
  WebDrop copies it today. Today an encrypted file can't be anonymous at all
  (`FileSystemStreamWriterBase.cs`, `CannotUploadEncryptedFileForAnonymous`) **(V)**; the link path doesn't
  change that, it is a separate way in.
- **Revoke or expire** withdraws the share and its rows. A reader keeps any file key they already opened,
  as with circles and persons.
- **A link can become a person share** when the recipient joins Homebase: the owner seals the same share
  key to their public key, and no file's entry changes.
- **Optional password:** the client can lock the entries with a key made from the fragment key and a
  password. The server doesn't change.

### Server Share

- **For:** spaces where every member reads basically everything, such as a drive marked as a community channel
  or a secure feed channel. Things on the drive that members cannot read are protected by ACL, not encryption.
- **Zero knowledge at rest.** The server briefly unlocks the drive key in memory to rewrap headers for
  members and peers.
- **Only it may be marked as a community channel**, since that needs the server to hold the drive key
  ([terminology.md](terminology.md), "Community channel").
- **Why not zero knowledge:** a big community channel can easily hold a million files. Any
  scheme where clients write keys per file would in some scenarios require clients update 
  very large volume of file records over the network. Today's model needs no client work at all.
- **Writing:** the server enforces who may write, as today.

## Direction: how we get there

For Private and Private Share drives:

- **A second master key, the zero-knowledge master key.**
  - Today's master key stays as it is, and keeps serving Server Share drives and the server-side
    features.
  - The new one is random and stored on the server locked by a second password key. It is unlocked
    only in the browser.
  - The second password key is pbkdf2(password, second salt): the same algorithm as today's KEK
    (`PasswordDataManager.cs`) **(V)**. It is made from the password itself, never from the KEK,
    because the KEK is sent to the server today **(V)**.
  - Password change works as today. The browser also re-locks the new master key.
- **ZK drive keys are stored locked by the zero-knowledge master key** (see
  [Schema additions](#schema-additions)): a new zero-knowledge drive's key is made in the browser, and a converted
  drive keeps its key, re-locked (see [Converting an existing drive](#converting-an-existing-drive)).
- **ZK Keys open on devices only.**
  - App devices get their app's zero-knowledge key sealed to their own ECC P-384 public key, in the same
    hand-off as today's app sign-in (see [Getting ZK drive keys to an app](#getting-zk-drive-keys-to-an-app)).
- **Lock-to-public-key is the one primitive.** Plain ECDH P-384 plus AES, the shape of today's
  `EccEncryptedPayload` (throwaway key pair + hint) **(V)**.
- **The receive key is the receiving drive's public key**, not a new identity or app key. Drives
  belong to apps, so apps stay isolated from each other.
- **Peers' servers only pass locked data through** (see [Peers](#peers)).
- **Recovery** comes first (see [Order of work](#order-of-work)).

## Order of work

Recovery comes before any drive goes zero knowledge, so nobody ever has a zero-knowledge drive
without a way back. Steps 1 to 4 are built: plan A, keys and database
([zero-knowledge-keys-plan.md](zero-knowledge-keys-plan.md)); plan B, recovery in the browser
([zero-knowledge-recovery-plan.md](zero-knowledge-recovery-plan.md)); and the drive steps
([zero-knowledge-drive-api-plan.md](zero-knowledge-drive-api-plan.md)), except where marked below.

1. **Database and browser keys.**
   - The new password data and drive columns (see [Schema additions](#schema-additions)). They
     can land before anything uses them.
   - At login the browser works out the second password key. The first time, it creates the ZK
     master key and saves it locked. Password change locks it again.
   - Owner-only endpoints to read and save the two password fields.
   - The Shard Recovery drive converts to zero knowledge. Its Shamir files aren't encrypted with
     the drive key (the shards inside are locked with the dealer's own keys), so the browser
     simply replaces its drive key and write-only key pair.
2. **Friend Shamir in the browser** (plan B). Recovery gives back the password, shown only in the
   owner's browser. The browser deals the shards and seals each to its holder's Shard Recovery
   drive public key.
   - Holders' keys are reachable today: peers have Write on that drive through a built-in circle
     and an automatic write grant (`BuiltinCircles.cs`, `CircleNetworkService.cs`) **(V)**.
   - Existing friend setups are dealt again by the browser at a password login, once.
3. **The recovery phrase, taken over by the browser** (plan B, with step 2). The owner keeps the
   phrase they wrote down; from the takeover it opens the ZK master key and so the password, the
   same way the Shamir secret does, and the server deletes its copies.
4. **Private drives and conversion,** Vault first (see
   [Private drives and conversion](#private-drives-and-conversion)). Vault, Email and Filesync convert from
   their app; a new identity's Vault and Email are created Private; an app may ask for a new drive's model
   (`ZkDriveConversionService`, `BuiltinDrives`, `CreateDriveRequest.SharingModel`) **(V)**.
   - Consent seals the app's zero-knowledge key to the app, which opens the keys of the zero-knowledge
     drives it is granted (see [Getting ZK drive keys to an app](#getting-zk-drive-keys-to-an-app)).
5. **Chat (to build),** with deposits (see [Work for Chat](#work-for-chat)).

## Peers

The goal for both zero-knowledge models: no server opens anything for a peer read or a deposit.
Keys open on the peer's devices.

### Server Share **(V)**

- **Peer read, on your server:** the incoming peer query (`PeerDriveQueryService`, incoming) builds
  the header with `DriveFileUtility.CreateClientFileHeader`. That unwraps the KeyHeader with the
  storage key from the circle's drive grant and rewraps it with the connection's shared secret.
- **Peer read, on the reader's server:** `PeerDriveQueryService.TransformSharedSecret` (outgoing)
  unwraps it with the connection's shared secret and rewraps it for the reader's app.
- **Incoming transfer:** the recipient's server rewraps with its own storage key
  (`PeerInboxProcessor`).

### Private

- **Reads:** the Peer API does no encryption or decryption for any read. A Private drive has no
  peer readers, and no drive grant on a zero-knowledge drive carries its storage key
  (`StorageDrive.GrantCarriesStorageKey`) **(V)**.
- **Deposits:** the sending client seals to the drive's public key. The sending server and the
  receiving server store it as received, written directly. A deposit on a Private drive carries no payloads
  (`Deposits.MayCarryPayloads`) **(V)**. There is no inbox for a peer's files (read receipts still use it): a peer's
  update, delete and reaction are written at once too (see [How a zero-knowledge drive holds files](#how-a-zero-knowledge-drive-holds-files)) **(V)**.

### Private Share

- **Reads:** the Peer API returns the member's share as a `share` key header: the share row's locked file key,
  as stored, and the file's IV (`EncryptedKeyHeader.ForShare`) **(V)**. Neither server opens it.
- **On the peer's device:** open the member's copy of the circle key (sealed to the peer's drive public
  key), then the locked file key, then the file.
- **No storage key in the grant:** the circle's drive grant for a Private Share drive carries no
  storage key (`StorageDrive.GrantCarriesStorageKey`) **(V)**.
- **Deposits:** a member's new file may carry payloads (`Deposits.MayCarryPayloads`) **(V)**, and the payload-upload
  route adds payloads to a deposit too (`Deposits.AssertTakesNoPayloads`) **(V, tested)**. There is no inbox for a
  member's files (read receipts still use it): a member's update or delete is written at once, through a live share
  with `Write`, or as the file's sender while it has no share **(V, tested)**. A member's update never creates a file:
  one with no stored file to update, a standard file or a comment, is refused, and a standard file keeps its ACL
  (`PeerFileUpdateWriter`) **(V)**.

### Anonymous and temporal reads

Both can make the server open files for the reader.
- **Anonymous reads** (`AllowAnonymousReads`): an anonymous reader has no keys.
  - An unencrypted file needs no key: the server returns an empty key header
    (`DriveFileUtility.CreateClientFileHeader`) **(V)**. Serving it opens nothing.
  - **Private and Private Share:** no anonymous reads. A zero-knowledge drive is created without them
    (`CreateDriveRequest.AssertSharingModel`) **(V)**, and a Private Share file's ACL is `Owner` or `Connected`
    (`SelectiveShareDrives.AssertAclFits`) **(V)**, and the read-mode setter refuses them too
    (`DriveSettings.AssertChangeFits`; see [How a zero-knowledge drive holds files](#how-a-zero-knowledge-drive-holds-files))
    **(V, tested)**. So a public drive
    (Homepage Config, Profile, WebDrop, Public Posts) stays Server Share.
- **Temporal reads** (`DrivePermission.ConditionalTemporalRead`, a circle may read files changed
  in a recent window): on a Server Share drive the grant escrows the drive storage key so the server can rewrap
  for the reader (`ExchangeGrantService.cs`) **(V)**. On a zero-knowledge drive the grant carries no key
  (`StorageDrive.GrantCarriesStorageKey`) **(V)**, so the server can't open a file for it. Proposed:
  a zero-knowledge drive refuses the permission (**Not decided**); on Private Share, share the recent files
  instead.

## Schema additions

Built. Every key's shorthand is in [owner-keys.md](owner-keys.md).

- **The owner's zero-knowledge keys** have their own record in the KeyValue table, not `PasswordData`
  (`ZeroKnowledgeKeys.cs`) **(V)**:
  - `ZkSaltKek64`: the second salt. The browser works out the second password key as
    pbkdf2(password, salt), with the same algorithm and iterations as `SaltKek`.
  - `ZkKekEncryptedZkMasterKey`: the zero-knowledge master key, locked by the second password key.
- **Drives** (`TableDrivesCRUD.cs`) **(V)**:
  - `ZkMasterKeyEncryptedStorageKey`: the drive storage key, locked by the zero-knowledge
    master key. Only the browser can open it.
  - `SharingModel`: Server Share (`Collaborative`), Private or Private Share.
  - `MasterKeyEncryptedStorageKeyJson`: nullable; a zero-knowledge drive has none, and a save refuses a
    zero-knowledge drive that has one, a Server Share drive that has none, or a drive with neither copy
    (`DriveManager.AssertKeyCopies`) **(V)**.
  - `WriteOnlyKeyPair`, and `ZkStorageKeyEncryptedWriteOnlyPrivateKey`: on a zero-knowledge drive the browser
    makes the pair and the private half is locked by the drive key; on a Server Share drive the server makes it
    (`DriveManager.CreateDriveAsync`, `DriveWriteOnlyKey`) **(V)**.
- **New drives:** an app opens the owner console to create a drive, as it does today. For a zero-knowledge
  drive the browser makes the storage key and the key pair there, and sends them with the create request
  (`CreateDriveRequest.ZkKeys`) **(V)**.

### The sharing model of existing and new drives

A drive made before sharing models existed is Server Share. A new
drive takes the model its app asks for, Server Share when it asks nothing (`CreateDriveRequest.SharingModel`)
**(V)**. An existing drive becomes zero knowledge only when the browser converts it, so the label never claims
zero knowledge the drive doesn't have, and `DriveManager` refuses to save a zero-knowledge drive that still has a
master-key copy **(V)**. Only Vault, Filesync and Email convert this way (`ZkDriveConversionService.ConvertibleDrives`),
and the Shard Recovery drive (`ZeroKnowledgeKeyService.ConvertShardRecoveryDriveAsync`) **(V)**, which the owner's
browser does at login **(I: client behaviour not read)**.

- **The built-in drives** (`BuiltinDrives.cs` **(V)**):

  | Drive | Sharing model | Why |
  |---|---|---|
  | Vault, Email | Private: created so on a new identity; an older one's converts from its app **(V)** | Owner reads; others only deposit |
  | Shard Recovery | Private, converted by the owner's browser (at login, **I**) | Friends deposit shards |
  | Chat | Server Share; Private **(to build)** | Owner reads; others only deposit |
  | Contacts, Feed, Wallet, Mail | Server Share; whether they become Private is **Not decided** | Contacts, Feed and Wallet are `OwnerOnly` **(V)** |
  | Photo Library, Sticker, Location, Moments | Server Share; Private Share is **Not decided**: converting a drive to Private Share may never be built ([selective-share-plan.md](selective-share-plan.md)) | Private, with chosen shares |
  | Lists | Server Share **(V)** | marked as a community channel **(V)**, which a zero-knowledge drive can't be |
  | Homepage Config, Profile, WebDrop, Public Posts | Server Share **(V)** | public (`AllowAnonymousReads`) **(V)**, which a zero-knowledge drive can't be |
  | Community, transient temp | Server Share **(V)** | Members read everything; the transient drive stages sends |

- Filesync's drive (an app drive, not built in) is Private Share: created so at its app's request, or an older
  one converted from its app (`WellKnownAppDrives.FilesyncDrive` in `ConvertibleDrives`) **(V)**; that Filesync asks
  for Private Share is (I: filesync not read here).
- **Every other drive, including third-party app drives:** Server Share unless its app asks for another model.
- `DriveSharingModel.Unclassified` exists, and nothing sets it **(V)**.
- **To check before a drive becomes Private:**
  - Feed and Contacts may have server features that read plaintext (see [Open](#open)).
  - Chat and Mail have `OwnerOnly = false` today **(V)**, so check what reads them besides the owner.

## Getting ZK drive keys to an app

Same principle and same protection as today's app sign-in (YouAuth): what the app receives is
sealed to the app's own key, so nobody in between can open it.

**Who is who:**
- **Browser:** the owner console page, running on the owner's own device. It is the only place
  the zero-knowledge master key is ever unlocked. Every zero-knowledge step below runs here,
  never on the server.
- **Server:** the identity's server. It only stores and forwards sealed data.
- **App:** the client app on one device (chat, filesync). It makes an ECC P-384 key pair for each
  sign-in.

**Today** (`YouAuthUnifiedController`, `YouAuthUnifiedService.CreateClientAccessTokenAsync`) **(V)**:
1. The app sends its public key in the authorize request (`public_key`).
2. The server makes a throwaway P-384 key pair and a 16-byte salt. ECDH(server throwaway private
   key, app public key, salt) gives the exchange secret. That secret seals the shared secret and
   the client token (`EncryptedTokenExchange`).
3. The server keeps the sealed result in the cache for 5 minutes, under a hash of the exchange
   secret.
4. The redirect back to the app carries only the server's throwaway public key and the salt. The
   browser passes them on but can't open anything with them.
5. The app works out the same exchange secret and fetches the sealed result once, by its hash.
   The server then deletes it (`ExchangeDigestForEncryptedToken`).

**For zero-knowledge drives:** the same steps, carrying one **zero-knowledge app key** (`AppZkK`; the
plan is step 3 of [zero-knowledge-drive-api-plan.md](zero-knowledge-drive-api-plan.md)), only when the app asks
for it (`zk_app_key=1`). The browser locks, and the server only carries (`YouAuthTokenResponse.ZkAppKey`) **(V)**.

- `AppZkK` is 16 random bytes per app, made by the browser. The server keeps `aes(ZKMK, AppZkK)` for the
  browser, and `aes(AppZkK, DrvK)` for each zero-knowledge drive the app is granted, both on the app
  registration, opaque to it.
- It is the zero-knowledge twin of the app's key store key, which the device's token half opens on the
  server today **(V)**.
- So a device needs one key from the browser, once. Drives converted or granted later reach it through
  the server, locked, with no new sign-in.

Labels: **ZK** = the zero-knowledge master key, **MK** = today's master key, **PK** = locking to an
ECC P-384 public key, **App** = locking with `AppZkK`.

Browser (owner console, consent page):
1. Take the ZK master key the console holds since its last password login, or ask for the password. (**ZK**)
2. Open the app's `AppZkK` with it, or make one. (**ZK**)
3. Lock the key of each zero-knowledge drive granted to the app with `AppZkK`, if not already. (**App**)
4. Seal `AppZkK` to the app's public key from the authorize request, which travels in the consent
   page's return URL **(V)**. (**PK**)
5. Post the sealed key with the consent to the server, and the locked drive keys.

Server (YouAuth):

6. Store the sealed key untouched, as it holds the consent today (`TempConsentCacheKey`) **(V)**. It
   can't open it: it has neither the ZK master key nor the app's private key.
7. Put it in the token response as one new field, `ZkAppKey`, in the same 5-minute, one-time entry
   (`EncryptedTokenExchange`, `YouAuthTokenResponse`), and mark the device as holding it.

App:

8. Open `ZkAppKey` with its own private key, and keep `AppZkK` in the device's key store. (**PK**)
9. Fetch its locked drive keys and open them with `AppZkK`. (**App**)

**MK** isn't used here. It is used once per existing drive, at conversion: the server opens the
drive key with MK, the browser locks it with ZK, and the MK copy is deleted (see
[Converting an existing drive](#converting-an-existing-drive)).

**Who sees what:**
- **Server:** only the sealed blob. It has neither the app's private key nor the browser's
  throwaway private key.
- **Redirect, or anyone in the middle:** public keys and salts only, as today.
- **Browser:** the drive keys, while it seals them. It is the owner's own page, and it holds the
  zero-knowledge master key anyway.
- **App:** `AppZkK` and the storage keys of the zero-knowledge drives it was granted, never the
  zero-knowledge master key.

**Same protection, same limits:**
- The browser seals to the key in the authorize request, the same key the server seals the
  token to today. Anyone able to swap that key would get today's token too, so this adds no new
  gap.
- The browser runs owner console code that the server delivers. A server sending altered code
  could take keys from the page. That is a limit of any zero-knowledge design that runs in a web
  page; it is reasoning, not something checked in this repo.

**Each device, each new grant:**
- Each device signs in on its own and gets its own sealed copy of `AppZkK`.
- Granting an app more drives later, or converting one, happens in the browser, which locks the drive
  key with `AppZkK`; the app's devices fetch it with no new sign-in.
- Every device of an app shares `AppZkK`, so revoking a device doesn't replace it: the same "no rotation
  on removal" risk as drive keys.

**Trusted apps (no consent screen):**
- Today an app whose redirect is on the owner's own host skips consent: the server answers the
  authorize request itself and redirects straight back to the app (`YouAuthUnifiedService.NeedConsent`,
  `YouAuthUnifiedController`) **(V)**. They are short-circuited on purpose: they are apps we
  trust, and it keeps sign-in smooth.
- New: an app that asks for its zero-knowledge key (`zk_app_key=1`) goes to the consent page anyway
  (`NeedConsent`) **(V)**. As built, the page shows the usual consent form and needs a click; it seals
  the key meanwhile, and the Login button waits for it **(V)**. A page that continues on its own is
  possible later.
- It has to pass through the page because only the browser holds the ZK master key. A redirect
  from the server alone has no keys to hand over.

## Private drives and conversion

The smallest change that makes Private and Private Share drives zero knowledge, and moves existing drives over
without re-encrypting any file header.

### Converting an existing drive

The existing drive key is kept and re-locked with the zero-knowledge master key. Every file
header stays locked with the same drive key, so nothing is re-encrypted.

1. The owner logs in. The browser works out the second password key from the password. On the
   first login after the change, it also creates the zero-knowledge master key and saves it
   locked.
2. The browser asks for the drive key. The server opens it with today's master key and sends it
   locked to the owner's shared secret, as reads do today. This is the server's last look at
   that key.
3. The browser locks the key with the zero-knowledge master key and sends that copy back.
4. In one step, the server stores the new copy, sets the drive to the model the app asked for (Private or
   Private Share), relabels every encrypted file's key header `zkDrive`, and deletes every copy it could open
   (`ZkDriveConversionService`) **(V)**:
   - the master key copy (`MasterKeyEncryptedStorageKeyJson`)
   - the copy in each app and circle grant (`DriveGrant.KeyStoreKeyEncryptedStorageKey`) **(V)**
   - cached permission contexts (`OdinContextCache`, 60 minutes) **(V)**

### What changes

Built, except item 7 **(to build)**. Server:
1. Password data: the two new fields, and endpoints to save them (first login, password change).
2. Drives: `ZkMasterKeyEncryptedStorageKey` and `SharingModel`. The master key copy becomes
   nullable.
3. On a Private drive the server stores and returns file headers as they are, with no
   re-locking. That covers four places: building a reader's header
   (`DriveFileUtility.CreateClientFileHeader`), upload, metadata update, and payload key headers
   (`DriveStorageServiceBase`) **(V)**.
4. Grants on a zero-knowledge drive carry no key (`StorageDrive.GrantCarriesStorageKey`) **(V)**.
5. Consent carries the browser's sealed `AppZkK`: one more field, `ZkAppKey`, in `EncryptedTokenExchange`
   (see [Getting ZK drive keys to an app](#getting-zk-drive-keys-to-an-app)).
6. An owner-only conversion endpoint for steps 2 to 4 above.

Owner console (browser): the second password key at login, the zero-knowledge master key,
sealing at consent, and the conversion.

Apps: lock and open file headers with the drive key instead of the shared secret, and keep the
drive keys in the device key store.

### Not in this step

- **Deposits.** Chat, Mail and Feed receive files from peers, and the receiving server
  re-locks them with the drive key (`PeerInboxProcessor`) **(V)**. They stay Server Share until Chat's deposits
  are built.
- Server Share sharing, which doesn't change. A Server Share drive has only the server's copy of its key: the
  console and apps get zero-knowledge copies only of zero-knowledge drives (`AppRegistrationService`) **(V)**.

### First drive: Vault

- Owner only, and no server feature uses its content: the server code names it only in the
  built-in app and circle definitions (`BuiltinApps.cs`, `BuiltinCircles.cs`,
  `SystemAppConstants.cs`) **(V)**.
- The built-in Vault circle ("Members can use your vault") grants ReadWrite on the
  Vault drive (`BuiltinCircles.cs`) **(V)**. `OwnerOnly` doesn't stop that grant; conversion strips the key from
  it, as from every grant, and it keeps its permission
  ([zero-knowledge-drive-api-plan.md](zero-knowledge-drive-api-plan.md), 3.3 and 3.7).

### Catches

- **A forgotten password would lose the converted drives** without the new recovery. That's
  why recovery comes first (see [Order of work](#order-of-work)).
- **Apps lose read access to a converted drive until they sign in again.** Their grant copy is
  gone, and new keys only arrive through consent.

### Risk we accept: the old key may already have leaked

- Before conversion, the server opened the drive key in memory every time an app or the owner
  read the drive. Any of those moments could have leaked it.
- Conversion doesn't change the key. A key leaked earlier still opens old files and new ones,
  because new file headers are locked with the same key.
- So conversion gives "from now on, the server can't open it", unless the key had already leaked.
  That is the trade for re-encrypting nothing.
- **Fix, later and optional: key rotation.**
  - The browser makes a new random drive key, and new files use it. Old headers stay locked with
    the old key.
  - Each file header then needs a key version, so a device knows which drive key opens it.
    Devices hold both keys.
  - Re-encrypting old headers under the new key is a separate, optional bulk job. It is only
    worth doing if old files must also be safe from a key leaked in the past.

## Work for Chat

Vault, Email and Filesync already read and write their zero-knowledge drives (steps 3 and 4 of
[zero-knowledge-drive-api-plan.md](zero-knowledge-drive-api-plan.md)). Chat is the app still to change
**(to build)**. Every other app keeps working as today, because its drives stay Server Share.

chat-kmp paths are relative to the chat-kmp repo, read at `13f06b94f`; its Vault parts may differ now (I: not
re-read). `api/` means
`homebase-api/src/commonMain/kotlin/id/homebase/api/`.

### How chat-kmp handles drive keys (read at `13f06b94f`) **(V)**

- **One app, one sign-in.** Chat and Vault ship in one binary per platform and share one app
  registration (`APP_ID`, slug "chat"). Vault is a permission extension of that app
  (`homebase-common/.../config/AppConfig.kt`). One consent can carry both drive keys.
- **Sign-in** makes a P-384 key pair (`api/youauth/YouAuthFlowManager.kt`) and opens the token
  exchange with AES-CBC only (`api/youauth/YouAuthProvider.kt`).
- **Every file header is opened in two places:**
  - `ServerFile.asHomebaseFile` (`api/client/drives/ServerFile.kt`) opens the header with the
    shared secret. It serves file header, query batch, both websockets and temporal reads.
  - The payload and thumbnail response header (`sharedsecretencryptedheader64`) is opened in
    `DriveFileProvider.kt`.
- **Uploads** lock the header with the shared secret in `UploadDescriptorBuilder.kt` (create) and
  `DriveUploadProvider.kt` (update). The outbox re-keys to the header the server returns
  (`DriveOutboxUploader.kt`).
- **Chat sends** set the recipients and the server fans out
  (`homebase-chat/.../services/ChatMessageSenderService.kt`). No per-recipient key work, and no
  use of drive public keys.
- **Push text goes in the clear:** each chat send carries `unEncryptedMessage`, the notification
  text, to the server (`ChatMessageSenderService.kt`).
- **Vault** uploads and reads its own drive, shares with no one (`recipients = emptyList()`), and
  hands decrypted files to WebDrop. WebDrop uses its own drive, its own random link key, and
  stores the drop unencrypted on the server (`WebDropService.kt`). It isn't affected.
- **Where secrets live:** one `SecureStorage` abstraction (`api/storage/SecureStorage.kt`).
  - Android Keystore, iOS Keychain: good.
  - Desktop: a PKCS12 file with a hardcoded password (`SecureStorage.jvm.kt`).
  - Web: plain `localStorage` (`SharedPreferences.web.kt`).
  - The local database keeps each file's opened key header (`MainIndexMeta.kt`, `jsonHeader`),
    and the outbox keeps the plain key header (`OutboxSync.kt`). SQLCipher on Android, iOS and
    desktop. The web database is unencrypted, in memory.

### chat-kmp changes

Items 1 to 3 are built for Vault (plan, step 3; I: not read in chat-kmp here); 4 to 6 are Chat's **(to build)**.

1. **Sign-in:** open the new `ZkAppKey` field in the token response (sealed with `ZkSeal`, AES-GCM),
   keep `AppZkK` in `SecureStorage`, and fetch and open the app's locked drive keys with it.
2. **One switch for headers:** on a zero-knowledge drive, open and lock headers with the drive key
   instead of the shared secret. That's `ServerFile.asHomebaseFile`, the payload/thumbnail
   decrypt in `DriveFileProvider.kt`, `UploadDescriptorBuilder.kt` and the update descriptor.
   The outbox stops re-keying to a server-returned header.
3. **Vault:** nothing beyond 1 and 2.
4. **Chat sending (deposits):** fetch each recipient's chat drive public key (endpoint below,
   cached), lock the file key to it, and put one locked header per recipient in the upload.
5. **Chat receiving:** open a deposited header with the chat drive's private key. That private
   key is stored locked by the drive key (`WriteOnlyKeyPair`), so the app fetches it and opens it
   on the device.
6. **Push text:** a zero-knowledge chat can't send `unEncryptedMessage`. Either a generic
   notification, or the text as a deposit opened on the device (push decrypt is a TODO today,
   `homebase-common/.../notifications/NotificationService.kt`).
7. **Device storage becomes the security boundary.** The drive keys join the shared secret in
   `SecureStorage` on every platform. Web (`localStorage`) and desktop (hardcoded keystore password) are weaker
   than mobile; memory-only keys on the web and proper key storage on desktop are follow-ups (plan, 3.6).

Not changing: peer reads, the peer websocket, temporal reads and sending to a peer's drive
(`PeerDriveUploadProvider.kt`). Those all reach other people's Server Share drives.

### odin-core API changes

Items 1 to 6 are built; 7 is **(to build)**.

1. **Drive info carries `SharingModel`,** so a client knows which key opens a header (`ClientDriveData`,
   `OwnerClientDriveData`) **(V)**.
2. **Uploads, updates and local metadata on a zero-knowledge drive:** the key header arrives
   locked with the drive key. The server stores it as is, and the upload response carries no
   re-locked header.
3. **Reads on a zero-knowledge drive return the stored header as is:** file header, query batch,
   websocket notifications (`ClientDriveNotification`) and the payload/thumbnail response header
   (`DriveStorageControllerBase`) **(V)**. The field keeps its name.
   - **The key header's `type` has the value `zkDrive`** (`EncryptionType.ZkDrive`) **(V)**: the header is
     `aes(DrvK, FileKey)`, locked by an owner device. The format is the same CBC format as `aes`.
     - A zero-knowledge drive refuses `aes` by its type, before opening it. Released apps always
       send `aes`.
     - Readers choose the key from the drive.
     - A `sealed` header from someone without DrvK is stored on the drive as it is; a key holder
       may transform it into `zkDrive` later (see
       [How a zero-knowledge drive holds files](#how-a-zero-knowledge-drive-holds-files)).
   - **No packed response header for `zkDrive` or `sealed` files.** The client uses FileKey and
     the payload's IV from the file metadata. The packed format stays for `aes`, and isn't
     replaced with JSON. GCM isn't used.
   - The plan, with the reasons and the work: [zero-knowledge-drive-api-plan.md](zero-knowledge-drive-api-plan.md).
4. **Sign-in:** consent accepts the browser's sealed `AppZkK`, and `YouAuthTokenResponse` gets one new
   field, `ZkAppKey` (`YouAuthTokenResponse.cs`) **(V)**. The YouAuth reference client too.
   Trusted apps that skip consent go through the consent page when they ask for the key (see
   [Trusted apps](#getting-zk-drive-keys-to-an-app)).
5. **Grants carry no storage key on a zero-knowledge drive** (see
   [What changes](#what-changes)).
6. **Owner console:** password data, conversion, and creating a drive with its zero-knowledge
   fields.
7. **Chat deposits:**
   - An upload with recipients carries one locked header per recipient. The sending server
     forwards each as is, and the receiving server stores it as a deposit instead of re-locking it
     (`PeerInboxProcessor` re-locks today) **(V)**. How the sender's copy on a zero-knowledge drive goes out
     is **Not decided** (see [Private](#private)).
   - The client fetches a recipient's drive public key with the existing endpoint
     `V2PeerAppDrivePublicKeyController.GetDriveWriteOnlyPublicKey` **(V)**.
   - The app fetches its own drive's `WriteOnlyKeyPair` private half, still locked, to open
     deposits.

## Decisions so far

- **Client-locked files:** `sealed` and `zkDrive` side by side on a zero-knowledge drive,
  one read rule and one write rule; the transform is optional and keeps the version tag and `modified`. See [How a zero-knowledge drive holds files](#how-a-zero-knowledge-drive-holds-files).

- **V1: no key rotation when a member is removed.** Removing someone deletes their row, and the
  server stops serving them. Accepted risk: the removed person would also have to break into the
  server to read newer files.
- **No proxy re-encryption** (server-side "tickets", e.g. Umbral). It is novel crypto, and a
  colluding server plus member could recover the shared key.
- **The limit we accept:** you can't have exact per-file sharing, zero knowledge, and no per-file
  client work all at once. Private Share gives up the last one for sharing existing files.
- **Ruled out:**
  - a drive-wide "share key": for reading content it's the same as handing out the drive key.
  - a header per member per file: the rows grow as files × members.
- **The password is asked for at login only.** The browser keeps the zero-knowledge master key
  for the owner session, locked by a session key the server holds. See decision 3 in
  [plan B](zero-knowledge-recovery-plan.md#decisions-first).
- **Trusted apps keep skipping the consent screen** unless they ask for their zero-knowledge key. Then they pass
  through the consent page, which as built shows the consent form and needs a click (see
  [Trusted apps](#getting-zk-drive-keys-to-an-app)); a page that continues on its own is possible later.

## Open

- Key rotation after conversion: when, and the key version on file headers (see
  [Risk we accept](#risk-we-accept-the-old-key-may-already-have-leaked)).
- Splitting the app token into a login part and a key part. Today the token half is both **(V)**.
- Server features that need plaintext today, still to audit: feed distribution, email, profile
  publishing, guest browsing.
- How a sender finds the right receiving drive on the peer's side (via
  [archive/drive-addressing.md](archive/drive-addressing.md)).

## Recovery

Every key named here, and what locks it: [owner-keys.md](owner-keys.md).

- **Built** ([plan B](zero-knowledge-recovery-plan.md)): recovery gives back the password, so the zero-knowledge
  master key opens again; the browser takes the phrase over (`OwnerZeroKnowledgeController`, `phrase-takeover`)
  **(V)**. The server-side reset, which brings back only today's master key, stays for automated recovery and an
  old phrase not yet taken over.

### Redesign recovery

- **Why:** on the server-side path the server can reach the phrase **(V)**, so anything worked out from it could
  be worked out by the server too:
  - It is stored locked by today's master key (`MasterKeyEncryptedRecoverKey`,
    `PasswordKeyRecoveryService.cs`), which the server unlocks at every owner login.
  - Showing the phrase again unlocks it on the server.
  - Recovering sends it to the server (`VerifyRecoveryKeyRequest.EncryptedRecoveryKey`).
  - Shamir rebuilds it on the server, then emails a one-time link that unlocks it
    (`ShamirRecoveryService.cs`, `RecoveryNotifier.cs`).
- **The password itself is never on the server.** Login sends only a hash and today's password
  key (`PasswordReply`) **(V)**. So recovery can't just email the old password.

**The design (details in [plan B](zero-knowledge-recovery-plan.md)): recovery gives you back your password,**
shown only in your own browser. You log in as usual and
may then change it.

- The recovery phrase locks the zero-knowledge master key and a key pair the password is sealed
  to, so only the phrase reads it (the friends pair gives Shamir the same shape). A password
  change re-seals only the password copy.
- **Recovery phrase:** the one the owner wrote down is kept. At a password login the browser takes
  it over and the server deletes its copies; the server having seen it before is an accepted risk,
  like converting a drive with its existing key. To show it again, the browser keeps it locked by
  the zero-knowledge master key.
- **Shamir, dealt in the browser:** each shard is sealed to its holder's Shard Recovery drive
  public key. On recovery each holder's browser re-seals its shard to the recovering browser's
  throwaway key, and the recovering browser rebuilds the secret.
- Why each holder's public key, and not the server-side way: there the dealer's server keeps a key per
  shard (`DealerShardEnvelope`) and opens the returned shards itself
  (`ShamirRecoveryService.PrepareFinalKeys`) **(V)**. Many identities are on the same hosting, so
  the hosting could gather enough shards.
- Today's server-side reset stays only for automated recovery and the old phrase.

**Catches:**
- The recovering browser must keep its throwaway private key from start to finish (in browser
  storage). Finishing on another device doesn't work unless the key travels too.
- A shard is safe from the holder's server only once the holder's Shard Recovery drive is zero
  knowledge. That drive converts in step 1, at the holder's first login, so a holder is ready
  only after that login.
- The dealer's server fetches each holder's public key, so a dishonest server could swap in its
  own. That is the same kind of limit as the server delivering the console code.

**Why first:** a forgotten password must never lose a zero-knowledge drive, so recovery is in
place before any drive converts (see [Order of work](#order-of-work)).

**Automated holders:** Shamir supports automated holder identities
(`AccountRecovery.AutomatedIdentityKey`, `ShamirConfigurationService.cs`) **(V)**. Their shard
opens on a server by design. They stay as today for now (plan B, decision 6).
