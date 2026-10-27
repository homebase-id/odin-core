# Zero-knowledge drive API: plan

Status: step 1 built and merged (#2023, follow-up #2027). Step 2 built on branch `zk-drive-step-2` (2026-10-27). It details items 1-3 and 6 of
"odin-core API changes" in [zero-knowledge-drives.md](zero-knowledge-drives.md). Key names follow
[owner-keys.md](owner-keys.md).

**(V)** = read in code on 2026-10-26. **(I)** = inferred, to be checked while building.

## The model

- **Every encrypted file on a drive has its key header locked for that drive.**
  - On a normal drive the server locked it with DrvK, and its `type` is `aes`.
  - On a zero-knowledge drive a client locked it (step 2): an owner device with DrvK (`zkDrive`), or
    a sender sealing it to the drive's `WO-PK-Pub` (`sealed`).
- **Someone who doesn't hold DrvK either goes through the drive's inbox or seals.**
  - **Normal drive, server inbox** (`TransitInboxBoxStorage`, `boxId = driveId`) **(V)**: the sender
    locked the key header with the connection's shared secret. The item waits until the owner or an app
    is signed in; the server then turns the key header into `aes` with the keys that caller lends it
    (`PeerInboxProcessor`), and moves the file onto the drive **(V)**.
  - **Zero-knowledge drive, sealed** (step 2): the sender seals the key header to the drive's
    `WO-PK-Pub` (`type: sealed`), and the file is stored on the drive as it is. A device holding DrvK
    reads it with `WO-PK-Priv`, and may turn it into `zkDrive`. Only the key header changes; the
    payloads stay where they are, because FileKey doesn't change.
- **What differs today:** deposits (Shamir friend shards) land on the drive `sealed`
  (`PeerInboxProcessor` → `DecryptOrPassSealed`) **(V)**, handled by special cases for visibility,
  updates and payloads spread over nine places. Step 2 keeps `sealed` on the drive and folds those
  cases into one read rule and one write rule for client-locked files.

## Order

1. **The `zkDrive` type, owner only** (this plan, below). Server only, and it doesn't touch
   deposits.
2. **Client-locked files** ([Step 2](#step-2-client-locked-files)): `sealed` and `zkDrive` side by side on a
   zero-knowledge drive, one read rule and one write rule for both. The optional transform of `sealed`
   into `zkDrive` is designed and [deferred to Chat](#deferred-to-chat-the-transform).
   - Shamir leaves its shards `sealed`; approval unseals the one it needs.
   - The deposit exceptions on drive files fold into the two rules (2.8).
   - Chat reuses this step.

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

## Step 2: client-locked files

Status: agreed and built 2026-10-27, on branch `zk-drive-step-2`. **(V)** = read in code on 2026-10-27;
**(I)** = inferred, to check while building.

### 2.1 The rule
On a zero-knowledge drive every encrypted file is **locked by a client**, in one of two ways. The
server opens neither.

| Key header `type` | Locked by | Opened with |
|---|---|---|
| `sealed` | anyone with Write: the sender seals `IV ‖ FileKey` to the drive's `WO-PK-Pub` | `WO-PK-Priv` |
| `zkDrive` | a device holding DrvK (the owner console, for now) | DrvK |

Only the one key header is locked either way: `appData.content`, `localAppData.content` (its own IV),
every payload and every thumbnail (the payload's IV) are encrypted with FileKey **(V)**. So a reader
that has opened the key header reads everything else the same way, whichever type it was.

**Transform on sight (optional; designed in [Deferred to Chat](#deferred-to-chat-the-transform), built with
Chat):** a device holding DrvK may turn a `sealed` key header into a
`zkDrive` one: open it with `WO-PK-Priv`, lock the same IV and FileKey with DrvK, and send it back.
Only the key header changes; content, payloads and IVs stay as stored. Nothing depends on it
happening: a `sealed` file is readable by every key holder, and a sender's later update may make it
`sealed` again. Transforming saves later reads the unseal (see [Measured](#measured-unseal-cost)) and leaves the drive
mostly `zkDrive`. Step 2 leaves every `sealed` file as it is: Shamir doesn't transform.

`EncryptedKeyHeader.IsLockedByClient()` already means "sealed or zkDrive" **(V)**; step 2 makes it the
one test the rules use.

### 2.2 Why this and not an inbox (decided 2026-10-27)
Considered and dropped:
- **A client inbox** (`clientInbox` table; list, then answer a batch; the server moves accepted items
  onto the drive). It mirrors the server inbox, which waits for the owner's keys and turns headers with
  them (`InboxDrainOnQuery`, `PeerInboxDriveQueue.IsAuthorizedToDrain`) **(V)**. But to open a chat
  without an extra round trip, a client would render from the inbox item and the query would carry the
  waiting items, so the client inbox became the drive query in a second place: the same queue,
  two routes. It also needed a new table, its delete, purge, move and export plumbing, and Shamir looking
  in two places.
- **A "waiting" file on the drive** under a reserved file type, hidden until accepted. A sender's update
  to an accepted file would hide it again until the next accept, so sync clients would see files vanish
  and return on every update (each Chat edit, each re-deal); the server would also keep the app's file
  type aside meanwhile.

What client-locked files cost, accepted:
- Two key header types on a zero-knowledge drive. On the server, they meet one read rule and one write
  rule; on a client, one `openClientLockedKeyHeader` branch.
- Every reader of such a drive needs `WO-PK-Priv` as well as DrvK (for Chat, true either way: the chat
  app processes what arrives).
- An unseal (ECDH P-384) per `sealed` file read until it is transformed ([Measured](#measured-unseal-cost)).

### 2.3 Server: arrival (unchanged)
A sealed item lands on the drive as today, by direct write (S1200) or through the server inbox
(`DecryptOrPassSealed`) **(V)**. `Deposits.AssertValid` (no payloads) stays until Chat needs payloads.

### 2.4 Server: one read rule, one write rule
- **Read:** `CallerMayReadAsync` (step 1's follow-up, #2027) hides a client-locked file from a caller
  that can't open it: owner console (`CallerHoldsDriveKey`) or the system only. A deposit staged on the
  transient drive reaches any caller there: its writer reads it back to send it. Today an app with Read on
  the drive sees deposits (`DriveQueryServiceBase`, `isDeposit`) **(V)**; that goes.
- **Read results:** a client-locked key header is returned as stored (`DriveFileUtility.CreateClientFileHeader`);
  payload and thumbnail responses carry no packed header for either type (step 1 decision 4).
- **Writes, in one place** (the `ZkDriveFiles` checks, extended to `sealed`):
  - the owner may write `zkDrive` (step 1), and may update a client-locked file of either type with a
    `zkDrive` key header (`AssertFitsUpdate`); `Deposits.AssertNotDeposit` goes **(V, built)**;
  - a sealed key header comes only with a new file: an update or a metadata-only upload with one is
    refused (`DepositCannotBeUpdated`), and so is a payload upload onto a stored deposit
    (`Deposits.AssertTakesNoPayloads`) **(V, built)**;
  - a caller holding the drive's key can't upload a `sealed` file to that zero-knowledge drive
    (`DriveIsZeroKnowledge`): it writes `zkDrive`. An app with Write may still deposit there (and then
    can't read it back), and the transient drive takes one to send (`ZkDriveFiles.OpenForWrite`)
    **(V, built)**;
  - a sender's `sealed` new file is stored as sent. Once a file exists, a sender can't change it: a new
    sealed version of it (an update, or an overwrite matched by global transit id or unique id) is refused
    (`DepositCannotBeUpdated`), whoever applies it: a direct peer write can't see the file, and an inbox
    drained by the owner console refuses it in `OverwriteFile` **(V, built)**. Shamir isn't affected: every
    deal uses new shard ids (`Guid.NewGuid()`, odin-js `getNewId()`) and never deletes a friend's shard
    **(V)**. A sender editing what it sent (a Chat edit) needs a write rule saying who may find and replace
    what, and comes with Chat;
  - the transform, once Chat builds it (2.5);
  - nothing else changes a client-locked key header, and a zero-knowledge drive sends nothing (step 1).
- **One type for an unopened key header:** `SealedKeyHeader` and `ZkDriveKeyHeader` merge into one
  `ClientLockedKeyHeader` whose `Type` says which (found in step 1's /simplify). `EncryptKeyHeader`'s two
  pass-through branches and the writers' per-type "no AES key" exemptions become one.

### 2.5 Server: the transform (deferred to Chat)
Shamir leaves its shards `sealed`, so nothing in step 2 transforms. The transform is designed, with its
API, version-tag guard and races, under [Deferred to Chat](#deferred-to-chat-the-transform), and is built
with Chat.

### 2.6 Server: Shamir
- **Lookup** (`ShamirBaseService.GetShardStoredForDealer`, by unique id): moved to the metadata-only
  `GetServerFileHeaderByClientUniqueIdSkippingFileAcl`, still behind the drive Read the ACL-bypass context
  grants. The query it used applies the read rule, which hides a sealed shard from that context (Owner
  level, no master key): with the old lookup all 17 `FriendRecoveryTests` fail **(V, measured)**. A
  System-level context isn't the way round it: a System caller's queries match no rows
  (`requiredSecurityGroup BETWEEN 0 AND 1`, `ProfilePublishService.cs:190`) **(V)**.
- **Releasable** (`CanReleaseAsync`): unchanged, a shard sealed to the drive's key; it needs the
  holder's approval. Automated holders' shards are unencrypted and untouched **(V)**. A `zkDrive` shard
  only exists once Chat's transform does; `CanReleaseAsync` takes it then.
- **Approval:** the console opens the shard with `openClientLockedKeyHeader` (a `sealed` one, with
  `WO-PK-Priv`), then re-seals it for the dealer as today.
- **Converting the drive** is built already (#2013) and not touched here:
  `ZeroKnowledgeKeyService.ConvertShardRecoveryDriveAsync` gives the Shard Recovery drive the keys the
  browser made, and when it runs again (after a reset with the recovery key, say) deletes what the old
  keys locked, both types (`IsLockedByClient`) **(V)**.

### 2.7 odin-js (pair PR)
- **Shared helpers** (`libs/js-lib/src/core/DriveData/SecurityHelpers.ts`, for every odin-js reader of
  a zero-knowledge drive, not only Shamir) **(V, built)**:
  - `isZkDriveKeyHeader`, `isClientLockedKeyHeader` and `isClientLocked(file)`, beside `isSealedKeyHeader`
    and `isDeposit`;
  - `openClientLockedKeyHeader(header, { writeOnlyPrivateKey, driveKey })`: `sealed` with `WO-PK-Priv`,
    `zkDrive` with DrvK. Everything after it (content, payloads, thumbnails) is as for any file.
    `decryptKeyHeader` (the shared secret) refuses both. It replaces `openSealedKeyHeader`.
- **No transform at sign-in:** the Shard Recovery drive's shards stay `sealed`; approval
  (`ShamirProvider.ts`) unseals the one it needs, through `releaseGatedShard` and
  `openClientLockedKeyHeader`.
- **The guards** in `FileCard`, `useFiles` and `getContentFromHeaderOrPayload` ask `isClientLocked`, so a
  `zkDrive` file is left unopened like a deposit.
- **Test vectors** (`SecurityHelpers.test.ts`, and odin-core's `ClientLockedKeyHeaderVectorTests`): a fixed
  DrvK, FileKey and IV with the expected `zkDrive` key header, and a sealed key header made once by
  odin-core with its `WO-PK-Priv` and public key. Plain base64 and JWK, so chat-kmp can check the same
  values later.
- **Cost of an unseal:** see the measurement under [Measured](#measured-unseal-cost).

### 2.8 The inventory, under this rule
| # | Where | Step 2 |
|---|---|---|
| 1 | `PeerDriveIncomingTransferService` direct write | stays |
| 2 | `PeerInboxProcessor` (`DecryptOrPassSealed`) | stays |
| 3 | `EncryptKeyHeader`, `CanHoldDeposits` | stays, through the merged `ClientLockedKeyHeader` |
| 4 | the owner uploads a `sealed` file to their own drive | refused for a caller holding DrvK, which writes `zkDrive`; an app with Write may still deposit, and the transient drive takes one to send |
| 5 | `Deposits.AssertValid` (no payloads) | stays until Chat, and `Deposits.AssertTakesNoPayloads` refuses a payload upload onto a stored deposit; payload responses already carry no header for client-locked files |
| 6 | `Deposits.AssertNotDeposit` (no updates) | replaced by the write rule (2.4): a sealed key header only comes with a new file (`DepositCannotBeUpdated`), and a client-locked file is updated only with a `zkDrive` one; Chat adds the transform (2.5) |
| 7 | `DriveQueryServiceBase` `isDeposit` (any reader sees deposits) | replaced by the read rule (2.4), which still shows a deposit staged on the transient drive, so an app can send its own |
| 8 | `CreateClientFileHeader` → `Deposits.OpenWithDriveKey` | merged with the `zkDrive` branch: client-locked, returned as stored; `OpenWithDriveKey` stays for sending from the transient drive |
| 9 | `PeerDriveQueryService` `IsSealed()` pass-through | widened to any client-locked key header, passed on as stored rather than failing the whole remote read; a peer's read rule normally hides one **(I)** |

### 2.9 Tests
- **V2 (`DepositTests`):** a deposit lands `sealed` and the owner reads it by unique id and by query, as
  stored; an app with Read on the drive doesn't see it (query, unique id 404, file id 403); an app sends a
  deposit through the transient drive; an app with Write deposits on a zero-knowledge drive and can't read
  it back; the owner can't upload a `sealed` file to their own zero-knowledge drive; the owner updates a
  deposit with a `zkDrive` key header; a sealed key header can't update a file (update, metadata-only);
  a payload can't be added to a deposit; a deposit with payloads is refused on the transient drive. Each
  of the four round-1 fixes was checked by undoing it: its test fails.
- **Services:** a socket event for a deposit reaches the owner console, not an app.
- **Shamir (Ported):** the dealer's verify sees a sealed shard through the system lookup; approval
  releases it; automated holders unchanged.
- **odin-js:** `openClientLockedKeyHeader` against the test vectors (both repos check the same values).

### 2.10 Done when
Server and odin-js PRs green; on bleeding edge: a gated friend deal → the dealer's readiness shows the
shard held → an app with Read on the friend's Shard Recovery drive doesn't see it → recovery with the
friend's approval works.

### Decided
- Client-locked files: `sealed` and `zkDrive` side by side, transform optional (2.2 for why).
- Shamir leaves its shards `sealed`; the transform is designed and deferred to Chat (2.5).
- The transform keeps the version tag and `updated`: a new version tag would break collaboration.
- One server PR, plus the odin-js pair; the odin-js helpers are shared by odin-js readers.
- No data upgrade: there are no users of gated friends yet; the user removes stray files by hand.

### Measured: unseal cost
Node 22.22 WebCrypto (OpenSSL), Intel i7-8700K, 2026-10-27, 1000 operations each, after a warm-up. The
script mirrors odin-js `zkUnseal` (P-384 ECDH, HKDF-SHA256, AES-GCM) and a `zkDrive` open (AES-CBC, 48
bytes) **(V, measured)**:

| Operation | Wall per item | CPU per 1000 |
|---|---|---|
| open a `zkDrive` key header (DrvK) | 56 µs | 75 ms |
| unseal, as `zkUnseal` today (re-imports the PKCS8 private key each call) | 1.57 ms | 1.55 s |
| unseal, private key imported once | 1.17 ms | 1.16 s |
| transform (unseal + lock as `zkDrive`) | 1.11 ms | 1.12 s |
| unseal, key imported once, 1000 at once | 0.35 ms | 1.06 s (spread over threads) |

- An unseal costs about 1.1 ms of CPU, about 20 times a `zkDrive` open; the ECDH is nearly all of it.
  A batch of 100 is about 110 ms of CPU, about 35 ms of wall time when run in parallel.
- Importing the private key once per batch, rather than per item as `zkUnseal` does, saves about a
  quarter.
- Not measured: browsers (Chrome and Firefox WebCrypto run off the main thread), phones (likely several
  times slower) and chat-kmp's Kotlin crypto **(I)**. Shamir's few shards cost nothing noticeable; for
  Chat it argues for transforming once rather than unsealing on every read.

## Deferred to Chat: the transform

Designed and reviewed with step 2 (2026-10-27), built with Chat, which reads every incoming message and
gains most from transforming once. Kept as reviewed.

### The transform (server)
**Request:** `POST /api/v2/drives/{driveId}/files/transform-key-headers`, owner console only
(`ZkDriveFiles.CallerHoldsDriveKey`), up to 100 items:

```json
{ "files": [ { "fileId": "…", "versionTag": "…", "keyHeader": { "type": "zkDrive", "encryptionVersion": 1, "iv": "…", "encryptedAesKey": "…" } } ] }
```

`versionTag` is the one the client read alongside the `sealed` key header it opened.

**Per item, the server checks:**
- the file exists, isn't deleted, and its stored key header is `sealed` (otherwise `notSealed`);
- the new key header passes `AssertValidZkDrive`, and its IV equals the stored one: `appData.content`
  was encrypted under it, and payloads keep their own IVs;
- then it replaces **only the key header**, in one statement guarded by the version tag:
  `UPDATE driveMainIndex SET hdrEncryptedKeyHeader = @new WHERE identityId = … AND driveId = … AND
  fileId = … AND hdrVersionTag = @expected`. A write that lands between the client's read and the
  transform has given the file a new version tag (every header write does, `TableDriveMainIndex.cs:119`
  **(V)**), so the item fails with `versionTagMismatch` rather than overwrite it.
- **Nothing else changes:** not the version tag, not `modified` (the change feed's `updated`), and no
  file-changed event. Content, metadata, payloads and the ACL are as stored, and the `sealed` key header
  still opens to the same FileKey, so no reader or cache needs to re-fetch, and every other writer's
  cached version tag stays valid: a new one would fail a collaborator's or another device's next update,
  guarded by the version tag it read, for a write that changed nothing it cares about (the user's call,
  2026-10-27). The header upsert can't be
  used: it always sets a new version tag and bumps `modified`.
- Any cache in front of the main index must not return the old `sealed` key header afterwards **(I:
  which cache, to check)**.

**Response**, one result per item, in request order; one bad item never fails the batch:

```json
{ "results": [ { "fileId": "…", "errorCode": null, "keyHeader": { "type": "zkDrive", … } } ] }
```

On success the client gets the stored key header (its version tag is unchanged) and swaps it into its
cache. Error codes: `versionTagMismatch`, `notSealed`, `fileNotFound`, `invalidKeyHeader`.

**Races:**
- **A sender's update lands before the transform** (a re-deal, a Chat edit): its version tag changed,
  so the transform answers `versionTagMismatch` and the sender's update stands. The client reads the
  new `sealed` key header next time and transforms that.
- **A sender's update lands after the transform:** it replaces the `zkDrive` key header with its own
  `sealed` one and the file is `sealed` again. Nothing is lost; the client transforms it again.
- **Two devices transform the same file:** both succeed with the same bytes. A `zkDrive` key header
  depends only on DrvK, FileKey and the IV (the IV cancels out, decision 3) **(V, `ZkDriveFileTests`)**,
  so the second write changes nothing. A device that read the file again first gets `notSealed`.
- **The owner edits the file meanwhile:** the same version-tag rule as any update.

**What the server can't check:** that the `zkDrive` key header locks the same FileKey. A client that
locked the wrong key would make the file unreadable, so `transformKeyHeaders` opens the key header it
made and compares FileKeys before sending ([odin-js](#the-transform-odin-js)).

### The transform (odin-js)
- Shared helpers, beside `openClientLockedKeyHeader` (`libs/js-lib/src/core/DriveData/SecurityHelpers.ts`):
  - `lockFileKey(drvK, fileKey, iv)`: the `zkDrive` key header, in the format of `EncryptKeyHeaderAes`.
  - `transformKeyHeaders(driveId, files)`: unseal, lock, check the lock opens to the same FileKey,
    send one batch of up to 100 with each file's version tag; returns the stored key headers. A
    `versionTagMismatch` item is left for the next read.
- **Where it runs:** Chat transforms what arrives as it reads it. Optional throughout: a failure
  leaves the file `sealed` and readable.

### The transform (tests)
- **V2:** the owner transforms a sealed file; the batch returns the stored key header, and the file's
  version tag and `updated` are unchanged (a query with `modifiedAfter` doesn't return it); a transform
  with another IV, an `aes` key header, a stale version tag or a non-sealed file is refused per item
  while the rest go through; a sender's update between the read and the transform survives it
  (`versionTagMismatch`), and one after it makes the file `sealed` again; an app or guest can't
  transform.

## Deferred to Chat: a sender's edit

Read 2026-10-27 **(V)**: a chat edit (chat-kmp `ChatMessageSenderService.updateMessage`) is an update by
unique id sent to the recipients, with a new IV and the **same AES key**
(`KeyHeader(iv = random, aesKey = msg.keyHeader.aesKey)`) and new content encrypted under them. The server
already insists on that: `UpdateBatchCopyFilesAsync` refuses an update whose AES key differs, and
`KeyHeader.AssertIvRotated` wants a new IV. So in an edit only the IV changes, never the file key.

The rule for Chat (the user's, 2026-10-27): **a peer's update may change the content and the IVs, never the
key header a key holder locked with DrvK.**
- With key header version 1 the server can apply that today: the IV cancels out of the format (decision 3),
  so it keeps the DrvK-locked bytes and takes the sender's new visible IV, and the key header opens to the
  new IV and the same FileKey.
- That relies on the weakness #2024 removes. Version 2 should give `appData.content` its own IV, as
  payloads (`PayloadDescriptor.Iv`) and local app data (`LocalAppMetadata.Iv`) already have; then a key
  header only holds FileKey, and an edit never touches it.
- The server still can't check that the sender kept the file key; a sender that didn't makes only its own
  edit unreadable.

## Step 2's inventory: deposits on the drive (as found in step 1)

What #2013 (`1a5684df2`) added so that a deposit lands on its drive still sealed. Step 1 leaves all of
it as it is (read 2026-10-26) **(V)**.

**Drive side: all go or change**

| # | Where | Today | Step 2, as first planned (superseded: see 2.8) |
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
  `IDriveAclAuthorizationService.CallerMayReadAsync`; how a refusal answers stays each path's own. A read by file id
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

## Open from step 2's review

- **The read rule gates the server's own lookups too** (review round 2). `ZkDriveFiles.VisibleTo` decides
  who may be handed a key header it can't open, but it also runs for lookups the server makes in a
  caller's context: an inbox drained by an app (`PeerInboxProcessor` under `ForOthersWrites`) misses a
  client-locked file, so a peer's delete of a deposit is dropped there and applied when the owner
  console drains. Step 2 works round two such flows (the transient drive's staged deposits, the Shamir
  lookup) and makes a `sealed` key header never replace a client-locked file whoever drains. The general
  fix: hide client-locked files where responses are built (`CreateClientFileHeader`, client queries,
  socket events) and let internal flows look rows up in an explicit system context. Needed before Chat,
  whose peers edit and delete what they sent.
- **Who reads the transient drive.** Its staged deposits reach any caller with Read there, so one app
  with transit sees another's staged deposit (the sealed key header and plain metadata, such as a gated
  shard's hashed recovery email). As before step 2; narrowing it needs knowing which app wrote a file.

- **odin-js layering.** `core/DriveData/SecurityHelpers` imports `zkUnseal` from `auth/providers/ZeroKnowledgeKeys`
  (no import cycle). Moving the seal helpers to `helpers/` would take `EccKeyProvider` and the lock helpers
  with them; left for when another core module needs them.
- **System APIs and the read rule (from step 2's /simplify).** The Shamir lookup skips the query and its
  read rule (`GetServerFileHeaderByClientUniqueIdSkippingFileAcl`), then re-applies what the query filtered
  (expiry, the file type) by hand. The deeper fix is a read rule that knows "a system API acting for the
  owner" (the context `UpgradeToByPassAclCheck` builds), so the other bypass readers of this drive
  (`PlayerShardCollector`, `ShamirRecoveryService`) can't hit the same wall if their files become
  client-locked. Wider than step 2; touches security-model.md section 9.

## Not in this work
- **Vault** (chat-kmp, and consent giving apps DrvK): a separate discussion, including how to
  change an existing drive such as Vault's. This plan covers the `zkDrive` type (step 1) and
  client-locked files for Shamir (step 2).
- **chat-kmp:** comes with consent. Two small bugs found on the way, both harmless today:
  - `EncryptedKeyHeader.toBase64()` writes the version big-endian, but has no production caller;
  - the `MissingEncryptedHeader` check compares by reference, so it never fires.
- **Per-file integrity, with key header version 2:** [#2024](https://github.com/homebase-id/odin-core/issues/2024).
  CBC has none, so zero knowledge means confidential from the server, not tamper-proof.
