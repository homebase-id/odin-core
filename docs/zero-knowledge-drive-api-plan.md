# Zero-knowledge drive API: plan

Status: plan, agreed point by point on 2026-10-26. Not built. It details items 1-3 and 6 of
"odin-core API changes" in [zero-knowledge-drives.md](zero-knowledge-drives.md). Key names follow
[owner-keys.md](owner-keys.md).

**(V)** = read in code on 2026-10-26. **(I)** = inferred, to be checked while building.

## The model

- **Every file on a drive has its key header locked with that drive's DrvK.**
  - On a normal drive the server locked it, and its `type` is `aes`.
  - On a zero-knowledge drive an owner device locked it, and its `type` is `zkDrive`.
- **Someone who doesn't hold DrvK writes through the drive's inbox.**
  - They seal the key header to the drive's `WO-PK-Pub` (`type: sealed`), and the file waits in
    that drive's inbox.
  - When an owner device holding DrvK comes online, it opens the header with `WO-PK-Priv`, re-locks
    it as `zkDrive`, and the server moves it onto the drive. Only the header changes; the payloads
    stay where they are, because FileKey doesn't change.
- **Normal drives already work this way.**
  - The inbox is per drive (`TransitInboxBoxStorage`, `boxId = driveId`) **(V)**.
  - `PeerInboxProcessor` opens the sender's header and re-locks it with DrvK **(V)**.
  - The only difference on a zero-knowledge drive is that a device does the re-lock, not the
    server.
- **What differs today:** deposits (Shamir friend shards) land on the drive still `sealed`
  (`PeerInboxProcessor` → `DecryptOrPassSealed`) **(V)**. That is why a zero-knowledge drive holds
  two kinds of header today, with special cases for visibility, updates and payloads. Step 2
  removes it.

## Order

1. **The `zkDrive` type, owner only** (this plan, below). Server only, and it doesn't touch
   deposits.
2. **Deposits wait in the inbox.** A device-side inbox step: list the sealed items waiting for a
   drive, then accept each one re-locked as `zkDrive`.
   - The holder's console runs it at login, and Shamir approval opens the shard with DrvK.
   - The deposit exceptions on drive files go.
   - It is planned after checking how automated holders' shards are stored and whether shard
     verification reads the sealed files.
   - Chat reuses this step.
   - What it undoes is listed under [Step 2's inventory](#step-2s-inventory-deposits-on-the-drive).
3. **The rest, for Vault:**
   - odin-js uploads, updates and reads with DrvK;
   - payload and thumbnail reads from FileKey plus the payload IV;
   - making any empty drive Private from the console.

Each step is its own PR (pair), and each targets main.

## Decisions (all steps)

1. **A third value for the key header's `type`: `zkDrive`** (JSON `"zkDrive"`, enum
   `EncryptionType.ZkDrive = 13`).
   - Why not `driveKey`: every stored header is already locked by its drive's key
     (`DriveStorageServiceBase.EncryptKeyHeader`) **(V)**.
   - Why not `zk`: in owner-keys.md, `_zk` means locked by ZKMK.
2. **The value is for writes.**
   - Released apps always send `aes`, locked with the shared secret (odin-js `encryptKeyHeader`,
     chat-kmp `encryptKeyHeaderAes`, both hardcoded) **(V)**.
   - A zero-knowledge drive refuses `aes` by its type, before opening it. An old app then can't
     store a header nobody can open, or show the server a file key.
   - Readers choose the key from the drive.
3. **Cipher: version 1 is the same CBC format as `aes`:** AES-CBC of `iv ‖ FileKey`, 48 bytes,
   with the IV visible. Content stays CBC, because HLS-AES-128 and the range reads need it **(V)**.
   GCM is not used.
   - **Found while building:** the format encrypts `iv ‖ FileKey` under that same IV, so the IV
     cancels out of the first block. The 48 bytes depend only on DrvK and FileKey, and the IV a reader
     gets is the visible field **(V, `ZkDriveFileTests`)**. So the server sees which `zkDrive` headers
     share a file key (every update of a file does), and could change a header's IV, which flips chosen bits in the
     first 16 bytes of `appData.content` for the reader. The same holds for `aes` headers today. Kept for
     version 1 as agreed. Key header version 2 (likely GCM) fixes it, together with per-file integrity:
     [#2024](https://github.com/homebase-id/odin-core/issues/2024).
4. **No packed `SharedSecretEncryptedHeader64` for a header the server can't open.**
   - The client decrypts a payload with FileKey and the payload's IV (`payloads[].iv`), the same
     values the server packs today (`DriveFileUtility.GetPayloadEncryptedKeyHeader`) **(V)**.
   - The packed 68-byte format stays for `aes`, because released apps parse it **(V)**.
5. **Who sees `zkDrive` files:** the owner, until consent (doc item 4) hands apps DrvK.
   - Apps hold no storage key for a zero-knowledge drive (`GrantCarriesStorageKey`) **(V)**, so
     today's rule leaves the files out for them.
   - That matters: an old chat-kmp coerces an unknown `type` to `aes`, fails, and stalls its sync
     on the whole query page **(V)**.

## Step 1: the `zkDrive` type, owner only

Goal: the owner's console (or a test) can write a `zkDrive` file to a zero-knowledge drive, update
it, and read it back unchanged, including its payloads and thumbnails, while the server never
holds a key that opens it. No client changes ship in this step.

### 1.1 The type
- `EncryptionType.ZkDrive = 13`, with a doc comment next to `Sealed`.
- `EncryptedKeyHeader`:
  - `IsZkDrive()`.
  - `DecryptAesToKeyHeader` refuses a `zkDrive` header the way it refuses a sealed one, with a
    clear `InvalidKeyHeader`, not a CBC exception.
  - `AssertValidZkDrive()`: version 1, a strong IV, and a 48-byte `EncryptedAesKey`. Nothing more
    can be checked.
- `ZkDriveKeyHeader : KeyHeader`, beside `SealedKeyHeader` (`KeyHeader.cs:91`) **(V)**. It carries
  the header through the write path unopened. `EncryptKeyHeader` returns it as is, and only on a
  zero-knowledge drive.
- The deposit code (`SealedKeyHeader`, `Deposits`) is left alone. Step 2 rewrites it.

### 1.2 Writes: decide by `type` before opening anything
The drive is known before any header is opened, through `package.InternalFile` on both uploads and
updates **(V)**.

| Drive | `aes` | `zkDrive` | `sealed` |
|---|---|---|---|
| zero knowledge | refused, unopened (`DriveIsZeroKnowledge`) | stored as is | as today (step 2 changes it) |
| normal | as today | refused | as today (transient drive only) |

- **New file or overwrite** (`FileSystemStreamWriterBase.UnpackMetadataForNewFileOrOverwrite`,
  `:468-478`) **(V)**: look at the type and the drive first. Today an `aes` header for a
  zero-knowledge drive is opened with the shared secret and refused only later, in
  `EncryptKeyHeader` (`DriveStorageServiceBase.cs:693`) **(V)**.
- **Metadata-only upload** (`FileSystemStreamWriterBase.cs:500-520`) **(V)**:
  - A `zkDrive` header needs a strong IV that differs from the stored header's IV. Both are
    visible, so the server compares them without opening anything.
  - The new header is stored as given.
- **Updates** (`FileSystemUpdateWriterBase.UnpackMetadata`, `:376-390`; `DriveStorageServiceBase.cs:1586`
  and `:2436`) **(V)**:
  - Today these open the stored header to check that the AES key is unchanged and the IV is new.
  - For `zkDrive`, check only the IV, and store the client's header.
  - Whether FileKey is unchanged is the client's responsibility; a code comment says so, next to
    the existing "lost for-ev-er" note.
- **Local metadata:** no key work there (`DriveStorageServiceBase.cs:1737-1790`) **(V)**. The
  client encrypts the content with FileKey and its own IV, unchanged.

### 1.3 Reads
- **File header, query batch, websocket and temporal reads** (`DriveFileUtility.CreateClientFileHeader`,
  `:35-55`) **(V)**: a `zkDrive` header goes back as stored, untouched by the shared secret.
- **Query visibility** (`DriveQueryServiceBase.cs:476-483`) **(V)**: the owner sees `zkDrive` files.
  - Today the owner wouldn't: the owner's grant on a zero-knowledge drive has no storage key
    (`OwnerAuthenticationService.cs:163-167`) **(V)**, and today's rule needs one.
  - The rule becomes: an encrypted file is visible with the drive key, or as a deposit (unchanged),
    or as a `zkDrive` file when the caller is the owner.
  - Apps: no change (decision 5).
- **Single-file reads** (header, payload, thumbnail by id, unique id or global transit id):
  - Same rule as queries, so the owner can fetch one, and an app with read access but no drive
    key gets what it gets today for a file it can't open.
  - **(I)**: list each read path and what it does today, in the PR.
- **Payload and thumbnail GETs** (`DriveStorageControllerBase.cs:143` and `:199`) **(V)**:
  - `GetPayloadEncryptedKeyHeader` returns null for a `zkDrive` file instead of opening it.
  - The controllers then send no `SharedSecretEncryptedHeader64` (today they would append a null
    one).
  - Range requests are unaffected: the server never decrypts content.

### 1.4 Everything else that opens a stored header
Each one either can't reach a `zkDrive` file, or refuses it with a clear error:
- outgoing transfers (`PeerOutgoingTransferService.cs:665`): refuse "a zero-knowledge file can't
  be sent", until Chat;
- feed distribution (`FeedDriveDistributionRouter.cs:238`);
- contacts (`ContactService`, 5 places);
- peer file update (`PeerDriveIncomingFileUpdateService.cs:236`);
- CDN and static publish.

**(I)**: most can't reach a zero-knowledge drive at all. The PR lists each one with the reason or
the refusal.

### 1.5 Tests (V2)
Test drive: the Shard Recovery drive, converted the way `ZkOwnerSteps` does. The owner uploads to it
through the normal drive API **(V)**. Built as `ZeroKnowledge/ZkDriveFileTests.cs`.

The test client locks headers with a DrvK it made itself, as a device would.
1. A `zkDrive` upload is stored and read back byte for byte, through the file header and query
   batch. The websocket builds its header with the same `DriveFileUtility.CreateClientFileHeader`
   **(V)** and has no test of its own.
2. The owner sees it in a query, and an app with read access to the drive doesn't.
3. An `aes` upload to the zero-knowledge drive is refused for its type. Proof that it was never
   opened: use a header the shared secret can't open, and expect `DriveIsZeroKnowledge`, not a
   decryption error.
4. A `zkDrive` upload to a normal drive is refused.
5. A malformed `zkDrive` header (short key, weak IV, version 2) is refused.
6. An update or metadata-only upload that keeps the IV is refused; with a new IV, the client's
   header is stored as given.
7. A payload and a thumbnail GET on a `zkDrive` file send no key header, and the content opens
   with FileKey plus the descriptor IV, including a range read.
8. Sending a `zkDrive` file to a recipient is refused cleanly.
9. Regression: every normal drive test passes unchanged, as do the Shamir deposit tests.

### 1.6 Done when
- The Release build passes with `--warnaserror`, and the Services, Core, Storage and V2 suites
  are green.
- `docs/zero-knowledge-drives.md` and `owner-keys.md` (FileKey row) mention `zkDrive`.
- `/simplify` and code review run before the PR opens. Release only when asked.

## Step 2's inventory: deposits on the drive

What #2013 (`1a5684df2`) added so that a deposit lands on its drive still sealed. Step 1 leaves all of
it as it is (read 2026-10-26) **(V)**.

**Drive side: all go or change**

| # | Where | Today | Step 2 |
|---|---|---|---|
| 1 | `PeerDriveIncomingTransferService` (S1200 direct write) | a deposit is written straight to the drive with no drive key | it stays in the drive's inbox |
| 2 | `PeerInboxProcessor.DecryptedKeyHeaderAsync` (`DecryptOrPassSealed`) | inbox processing moves a deposit onto the drive still sealed | the server skips deposits; a device accepts them re-locked as `zkDrive` |
| 3 | `DriveStorageServiceBase.EncryptKeyHeader`, `StorageDrive.CanHoldDeposits` | a sealed key header is stored on any zero-knowledge drive | storing a sealed key header on a drive is refused |
| 4 | `FileSystemStreamWriterBase` upload, `ValidateUploadCoreAsync` | the owner may upload a sealed file straight to their own drive (`DepositTests`); `ZkDriveFiles.OpenForWrite` keeps that pass-through | refused: the owner's devices hold DrvK and write `zkDrive` |
| 5 | `Deposits.AssertValid` ("no payloads") | needed because the packed response header can't carry a sealed one | removed |
| 6 | `Deposits.AssertNotDeposit` (storage ×3, update writer, metadata-only upload) | a deposit can't be updated | removed |
| 7 | `DriveQueryServiceBase` (`isDeposit`) | a deposit is visible to any reader of the drive, apps included (an old chat-kmp would stall on one; no app reads the Shard Recovery drive today) | removed |
| 8 | `DriveFileUtility.CreateClientFileHeader` → `Deposits.OpenWithDriveKey` | reads return the sealed key header as stored | removed |
| 9 | `PeerDriveQueryService` (outgoing), `IsSealed()` pass-through | a peer reading someone's drive gets a sealed key header | removed |

**Built on it: moves to the inbox step**
- **Shamir:**
  - `ShamirBaseService` reads the shard as a deposit file.
  - `ShamirConfigurationService` handles the deposit key header when dealing (`:201`, `:822`).
  - `ConfigureShardsRequest.DepositKeyHeader` is the sealed key header the browser makes.
- **Conversion:** `ZeroKnowledgeKeyService` (`:311`, `:324`) counts deposits and deletes dead ones
  when a drive is converted.
- **odin-js:**
  - `isDeposit` and `openSealedKeyHeader`;
  - `releaseGatedShard`, which opens the shard from the drive with `WO-PK-Priv`;
  - the deposit guards in `FileCard`, `useFiles` and `getContentFromHeaderOrPayload`.
- **Docs:**
  - owner-keys.md, the "gated friend's shard" row;
  - zero-knowledge-recovery-plan.md, "Deposits";
  - the remarks in `Deposits.cs`.

**Simplify when deposits move (found in step 1's /simplify)**
- `SealedKeyHeader` and `ZkDriveKeyHeader` have the same shape: an unopened key header carried through
  a write. One base class (a locked key header whose `Type` says which) would merge
  `EncryptKeyHeader`'s two pass-through branches and the per-type "no AES key" exemptions in both
  writers.

**Stays, because it fits the model**
- The `Sealed` type and `seal()`: a key header on its way, or waiting in the inbox.
- `PeerOutgoingTransferService` sending a deposit from the transient drive: that is the outbox
  route, and the transient drive is staging, not a destination.
- `DrivePublicKeyResponse.IsZeroKnowledge`: a sender knows whether sealing to a drive is worth it.

## Open from step 1's review (the user's call)

- **One "may the caller see this file" check: done.** The header load, the query filter and the socket
  used to combine the ACL and the zero-knowledge rule on their own. All three now ask
  `FileReadRule.CallerMayReadAsync`; how a refusal answers stays each path's own. A read by file id
  refuses (403). A query, and so a read by unique id or global transit id, leaves a zero-knowledge
  drive's own file out as missing, not as refused, so a write's check for a taken unique id still
  falls through to the index (400). The socket still doesn't leave out an encrypted file it holds no
  storage key for, as a query does; that predates this.
- **Which writes need the storage key: waits for [#2026](https://github.com/homebase-id/odin-core/issues/2026).**
  On a normal drive an unencrypted full overwrite still locks an empty key header, so it needs the
  storage key (pinned by `OverwriteNeedsTheStorageKeyTests`). A metadata-only upload and the update
  endpoint don't, so a guest with Write on a public drive can replace an unencrypted file. Whether Write
  is meant to allow that is #2026's question. The explicit write rule follows from the answer; a new
  upload doesn't need the storage key either way.
- **The CDN for a zero-knowledge drive: later.** Step 1 keeps zero-knowledge drives off the CDN, even
  with `AllowCdn` on. We may want a zero-knowledge drive's files on the CDN later, still encrypted, when
  the owner ticks CDN for the drive.

## Not in this work
- **chat-kmp:** comes with consent. Two small bugs found on the way, both harmless today:
  - `EncryptedKeyHeader.toBase64()` writes the version big-endian, but has no production caller;
  - the `MissingEncryptedHeader` check compares by reference, so it never fires.
- **Per-file integrity, with key header version 2:** [#2024](https://github.com/homebase-id/odin-core/issues/2024).
  CBC has none, so zero knowledge means confidential from the server, not tamper-proof.
