# A Server Share drive accepts and converts sealed files: plan

Status: step 1 built 2026-11-06 on branch `server-share-sealed-arrivals` (not merged); step 1b and steps 2-3 not built. Step 1 of three towards a Server Share drive without an inbox for a peer's
files; steps 2 and 3 are at the end, only so step 1 is seen in its place. Words follow [terminology.md](terminology.md),
keys [owner-keys.md](owner-keys.md). **(V)** read in the code (file named), **(I)** inferred.

## Why

A Server Share drive (`DriveSharingModel.Collaborative`) keeps an inbox for a peer's encrypted files: a peer who may
write the drive but not read it doesn't hold the drive key, and the server can lock the file's key with the drive key
only while a caller holding it is online, so the file waits in the inbox until then
(`PeerDirectWrites.CanWriteAtOnce`, `StorageDrive.HasInbox`) **(V)**. The inbox is the part of peer writes that keeps
costing: a second path for every arrival, with its own ordering, retries and draining.

A zero-knowledge drive has no inbox: a peer seals the file's key header to the drive's write-only public key (a
deposit), and it is written at once (`EncryptedKeyHeader.IsPassedOnAsSent`, checked first in `CanWriteAtOnce`) **(V)**.
This plan gives a Server Share drive the same way in, and has the server do what an owner device does on a
zero-knowledge drive: open the deposit and lock its file key with the drive key.

Step 1 changes no existing flow. Senders keep sending as they do; the inbox stays. It adds a second way in that no
sender uses yet, except clients that already seal (chat-kmp's `lockKeyHeader(sealedTo = …)`) **(V)**.

## What exists

- **Every Server Share drive has a write-only key pair** whose private half the server can open: it is minted with the
  drive's storage key and escrowed under it (`DriveWriteOnlyKey.CreateKeyPair(storageKey)`, `DriveManager.cs:486`;
  `StorageDrive.WriteOnlyKeyPair`) **(V)**. Drives from before it were backfilled by the v14 → v15 migration, except any
  created with no master key in scope, which have none (`StorageDriveData.WriteOnlyKeyPair` remarks) **(V)**.
- **Its public key is served** to peers, and senders cache it (`PeerDriveQueryService.GetDriveWriteOnlyPublicKeyAsync`,
  `PeerDriveKeyCacheTests`) **(V)**.
- **A sealed arrival already skips the inbox** on any drive (`CanWriteAtOnce`) **(V)**, but a Server Share drive refuses
  it: only a zero-knowledge drive or the transient drive holds a deposit (`StorageDrive.CanHoldDeposits`,
  `Deposits.AssertCanHold`, `DepositNeedsZeroKnowledgeDrive`) **(V)**.
- **The key-header-only write** that keeps the version tag and `modified`, guarded by the version tag
  (`TableDriveMainIndex.UpdateEncryptedKeyHeaderAsync`, used by a drive's conversion and the transform) **(V)**.
- **Serving a file:** a client-locked key header is returned as stored; any other encrypted file's key header is opened
  with the drive key and re-locked with the caller's shared secret (`DriveFileUtility.CreateClientFileHeader`) **(V)**.
- **Not there:** the server never opens a sealed value. `ZkSeal` seals (`ZkSeal.Seal`) and reads a seal's key id; the
  open (P-384 ECDH, HKDF, AES-GCM) is only in the clients and the tests' `ZkBrowser.Unseal` **(V)**.

## What step 1 builds

1. **The server opens a seal.** `ZkSeal.Open(sealedValue, privateKey)`, the counterpart of `ZkSeal.Seal`, checked
   against the existing golden vector (`ClientLockedKeyHeaderVectorTests`: the sealed value opens to its IV and key).
2. **A Server Share drive takes a deposit.** `CanHoldDeposits` holds for it too, and every drive that takes deposits
   takes their payloads (`Deposits.MayCarryPayloads`), the Private drive included: a peer's file has its content. The peer write path then writes it at once, as on a zero-knowledge drive.
   - Not on a drive marked as a community channel, and standard files only (no sealed comments).
   - Only sealed to the drive's current write-only key (`ZkDriveFiles.AssertSealedToItsKey`, as today), and refused on
     a drive with no write-only key pair.
3. **The server converts it** on a read (decision 1): open the drive's write-only private key with the drive key, open
   the seal, lock the file key with the drive key as an ordinary `aes` key header under the same IV, and write only the
   key header, at the version tag read. Version tag, `modified`, content, ACL and the sender's record of their last
   write stay, so the sender may still change or delete it (decision 3).
4. **Reading a deposit not yet converted:** opened for a reader whose request carries the drive key (decision 2).
5. **Tests**, V2, beside `KeyHeaderTransformTests`:
   - a peer's sealed file on a Server Share drive is written at once, with no inbox item;
   - it is converted, and its version tag and `updated` don't move;
   - a reader holding the drive key gets the file opened, before and after conversion;
   - a seal to another key, a drive with no write-only key, and a community channel are refused;
   - the sender may still delete it after conversion.
6. **Docs:** zero-knowledge-drives.md and file-access.md, present tense, once built.

## Decisions (the user's, 2026-11-06)

**One set of deposit rules for every drive that takes deposits** (Server Share, Private, Private Share), so the same
code serves all three; the drive's model decides only who converts and what a reader gets:

| Same on every drive that takes deposits | By the drive's model |
|---|---|
| sealed to the drive's current write-only key (`ZkDriveFiles.AssertSealedToItsKey`) | **who converts:** the server on Server Share; a key-holding client on a zero-knowledge drive (the transform) |
| payloads allowed: the Private drive's "a deposit is a header only" goes too (`Deposits.MayCarryPayloads`) | **what a reader gets before conversion:** on Server Share, opened by the server for any reader whose request carries the drive key; on a zero-knowledge drive, as stored |
| standard files only: no sealed comments (a Private Share member's comment is `fileKey`, not sealed) | |
| no deposit on a drive marked as a community channel (a zero-knowledge drive can't be one) | |
| conversion keeps the version tag and `modified` | |

1. **When the server converts:** on a read whose request carries the drive key, inside that request: where a read by id
   or a write loads the header (`DriveStorageServiceBase.ReadForCallerAsync`) and where a query filters what the caller
   sees (`DriveQueryServiceBase.FilterVisibleAsync`). No background pass: finding sealed files needs a scan (the key
   header's type is no column), and since a reader gets an unconverted file opened anyway (decision 2), a file nobody
   reads loses nothing by staying sealed. A pass can come later (a per-drive "converted up to" cursor in the key-value
   table) if sealed files pile up.
2. **Before conversion** a reader holding the drive key gets the file opened, re-locked with their shared secret as for
   any file. Clients never see the sealed form on a Server Share drive.
3. **A sender's sealed update over a converted file** is refused in step 1, on every drive, as on a zero-knowledge drive
   today (`ZkDriveFiles.AssertMayReplace`). Step 1b relaxes it for every drive alike.

## Step 1b: a sender's sealed update replaces a converted key header

A sealed key header may replace a converted one (`zkDrive`, or the server's own `aes` on Server Share) **while the
file's record of the sender's last write is current**: a conversion doesn't move the version tag, so the update is still
the sender changing their own file, and their new seal opens to whatever key their new content uses. The file is sealed
again and converted again. Someone else's write in between (the owner's edit, under a key of their own) moves the version
tag, after which the sender's change is refused as today (`SentFileChanges`) **(V)**, so a sealed key header never
replaces another writer's. One rule for every drive; it settles "a sender's edit after the transform"
(zero-knowledge-drive-api-plan.md). Rejected: keeping the converted key header and dropping the seal, which only works
if the sender kept the same file key (the server can't check that) and leans on key header version 1's weakness (#2024).

## Built (step 1), and where it differs

- `ZkSeal.Open`, checked by `ServerShareDepositTests` against what `ZkSeal.Seal` makes; the write-only private key is
  opened per call (`EccFullKeyData.OpenPrivateKey`), never kept on the cached drive.
- `StorageDrive.CanHoldDeposits` and `ServerOpensDeposits` (one property, asked both where a deposit arrives and where
  it is converted): a Server Share drive with a write-only key pair that isn't a community channel. A
  channel drive with followers takes deposits: only its owner's posts are sent on (`FeedDriveDistributionRouter.ShouldDistribute`:
  "if the file was received from another identity, do not redistribute", unless a community channel) **(V)**. **And
  never the Shard Recovery drive** before it is zero knowledge: its shards must stay sealed (`StorageDrive.ShardsStaySealed`,
  shared with the transform; #2075).
- No rule refuses a deposit's payloads: every drive that takes a deposit takes its payloads, and a drive that takes none
  refuses the sealed key header itself (`ZkDriveFiles.AssertDriveTakes`), new or an update. `Deposits.AssertCanHold`
  refuses a sealed comment on every drive.
- A local caller holding the drive key is refused a sealed upload, as on a zero-knowledge drive: a key holder writes a
  key header for the server to lock (`ZkDriveFiles.OpenForWrite`).
- A delete and the TTL jobs don't convert: they load the file with `includeExpired`, and `ReadForCallerAsync` converts
  only without it. The single V2 delete loads it as a write, which converts. A sealed key header on a Server Share drive
  reaches whoever holds the drive's storage key (`ZkDriveFiles.VisibleTo`, from the code review: without it an app was
  refused a delete of a deposit no read had converted, and a tombstone of one never reached an app).
- Converting the drive to a zero-knowledge one (Vault, Filesync, Email) opens its deposits inside the conversion, with
  the write-only key pair they were sealed to, and relabels them with the rest; a private key that doesn't open fails
  the conversion (from the code review).
- A tombstone has no content, so a Server Share deposit's sealed one goes out with an empty key header, as any other.
- Accepted (the user's): a read converting a deposit while the drive is converted to zero knowledge can write its `aes`
  key header over the relabelled one. Nobody converts a drive and reads it at once.
- `Deposits.ConvertedOnServerShare` (one file) and `DriveStorageServiceBase.ConvertDepositsAsync` (the write, from
  `ReadForCallerAsync` and `DriveQueryServiceBase.FilterVisibleAsync`, before the read rule); socket events open a deposit
  per device the same way, unwritten (`DriveEventMessage`).
- From the code review: the server opens a deposit it took before the drive was marked as a community channel (only new
  deposits are refused there); a seal that doesn't open to exactly the file's IV and key is logged and left sealed, never
  a failed read; a file that moved on between read and write is still served opened; conversion skips expired files; a socket event opens a deposit once for every device holding the drive key, and a
  device whose open fails retries it.
- Sending a deposit on converts it first: `SendFile` loads the header through `GetServerFileHeader`, which is
  `ReadForCallerAsync` **(V)**.
- Not converted: the loads that go straight to storage (a reaction, transfer history, a TTL job, the member-write guard)
  open no key header **(V)**. The one that does is a community channel's distribution
  (`FeedDriveDistributionRouter.DistributeToCollaborativeChannelMembers`), which opens the key header of the header the
  change event carries, and throws on a sealed one: only for a deposit taken before the drive was marked as a community
  channel (I: not tested). Opening a deposit in the server's one opener, `DriveFileUtility.OpenWithStorageKey`, would
  cover it and any later caller (a step 2 candidate).
- One open-and-save already exists: conversion and its save happen where headers are loaded for a caller
  (`ConvertDepositsAsync`, from `ReadForCallerAsync` and `FilterVisibleAsync`), which opens the drive key and the
  write-only private key once per batch. `DriveFileUtility.OpenWithStorageKey` is the one opener of a stored key header,
  static and called while responses are built and inside writes; it holds no drive and no storage, so it doesn't save.

## Races

None loses data; the worst case is the server converting a file again.
- **A read while converting:** clients never see the sealed form (decision 2): the server opens it for any reader
  holding the drive key, so a client reading before, during or after the conversion gets the same key header.
- **A write lands between the conversion's read and its write:** the conversion writes only at the version tag it read,
  so it skips the file, which is converted on a later pass.
- **A full header write that read the file before the conversion:** the version tag hasn't moved, so its guard passes
  and it writes the sealed key header back over the converted one (as for the transform, zero-knowledge-drives.md). The
  file is still readable, and the server converts it again.

## Considered and rejected

- **Leaving deposits sealed for clients to convert**, as on a zero-knowledge drive. There the clients convert because
  only they hold the drive key; on a Server Share drive the server is the key holder, and:
  - no client can open the seal: the write-only private key is escrowed under the drive key on the server
    (`DriveWriteOnlyKey.CreateKeyPair`); only a zero-knowledge drive hands clients a copy **(V)**. Giving it out is new
    key distribution;
  - readers (members, guests, apps) get key headers the server re-locks to their shared secret, and a client-locked one
    is returned as stored (`DriveFileUtility.CreateClientFileHeader`) **(V)**: they would see a file they can't open;
  - what the server shares breaks until someone converts it: community channels and feeds re-lock and send on
    (`FeedDriveDistributionRouter`) **(V)**;
  - the conversion's target is the server's own `aes` key header, so every client, odin-js included, would need a second
    conversion endpoint that hands the file key back under its shared secret.
- **Opening the seal on every read and never storing the conversion:** no write inside a read, but one ECDH per read
  forever, and the server still needs `ZkSeal.Open`. Converting once is cheaper and leaves the drive as it is today.

## Open

- **Generic drive subscriptions** (to come, beyond today's "follow a channel drive": notification only, header copy, …):
  a subscription type that sends a *peer's* file on needs its key when it distributes. It either converts a deposit
  first, as a read does, in a context carrying the drive key, or its drive takes no deposits, as a community channel.

- Whether a Server Share drive with no write-only key pair exists in production, and whether to mint one for it
  (it needs the drive key in scope, as the v14 → v15 migration did).
- What the conversion costs a read under decision 1 (a): one ECDH per sealed file, once.

## Later steps (not this plan)

- **Step 2, senders seal.** The sending server seals the key header to each recipient drive's public key, instead of
  re-locking it with the connection's shared secret (`PeerOutgoingTransferService`), when the recipient's drive has a
  key; otherwise as today. From then on most files skip the inbox.
- **Step 3, the inbox goes** for a peer's files, once every sender seals. Read receipts move separately: they go through
  the inbox on every drive (`PeerDriveIncomingTransferService.MarkFileAsReadAsync`) **(V)**.
