# Zero-knowledge drive API: plan

Status: step 1 built and merged (#2023, follow-up #2027). Step 2 merged (#2029, odin-js #1006, 2026-10-27). Step 3 (Vault) built 2026-10-27 on branch `zk-drive-step-3` (odin-core and odin-js), not yet pushed. It details items 1-3 and 6 of
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
3. **Vault goes private** ([Step 3](#step-3-vault-goes-private)): a zero-knowledge app key (`AppZkK`)
   reaches each device at sign-in and opens the app's drive keys; the owner converts Vault in the
   console; the drive keeps its key, re-locked with ZKMK; every grant loses its copy; every file is
   relabelled `zkDrive`.

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
  - **a sealed key header replaces only a sealed one** (the user's rule, 2026-10-27;
    `ZkDriveFiles.AssertMayReplace`): never a `zkDrive` key header, so a file a key holder took over doesn't
    go back to sealed, and never an `aes` one, whose file key only the server could open. Asked inside the
    update, metadata-only and full-overwrite paths, which keep their version-tag and IV checks
    (`DepositCannotBeUpdated`); a sealed replacement and a payload upload onto a stored deposit carry no
    payloads (`Deposits.AssertTakesNoPayloads`) **(V, built)**;
  - a caller holding the drive's key can't upload a `sealed` file to that zero-knowledge drive
    (`DriveIsZeroKnowledge`): it writes `zkDrive`. An app with Write may still deposit there (and then
    can't read it back), and the transient drive takes one to send (`ZkDriveFiles.OpenForWrite`)
    **(V, built)**;
  - a sender's `sealed` new file is stored as sent; a peer's key header is checked against the drive when
    the transfer or update arrives (`ZkDriveFiles.AssertPeerMayWrite`: never `zkDrive`, and only what the
    drive takes). A sender can't yet reach its own deposit to update it: the read rule hides it from the
    sender's lookup by global transit id or unique id (see "Open from step 2's review"). Shamir isn't
    affected: every deal uses new shard ids (`Guid.NewGuid()`, odin-js `getNewId()`) and never deletes a
    friend's shard **(V)**. A sender's edit is Chat's ("Deferred to Chat: a sender's edit");
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
| 6 | `Deposits.AssertNotDeposit` (no updates) | replaced by the write rule (2.4): a sealed key header replaces only a sealed one (`ZkDriveFiles.AssertMayReplace`, `DepositCannotBeUpdated`), and the owner updates a client-locked file with a `zkDrive` one; Chat adds the transform (2.5) |
| 7 | `DriveQueryServiceBase` `isDeposit` (any reader sees deposits) | replaced by the read rule (2.4), which still shows a deposit staged on the transient drive, so an app can send its own |
| 8 | `CreateClientFileHeader` → `Deposits.OpenWithDriveKey` | merged with the `zkDrive` branch: client-locked, returned as stored; `OpenWithDriveKey` stays for sending from the transient drive |
| 9 | `PeerDriveQueryService` `IsSealed()` pass-through | widened to any client-locked key header, passed on as stored rather than failing the whole remote read; a peer's read rule normally hides one **(I)** |

### 2.9 Tests
- **V2 (`DepositTests`):** a deposit lands `sealed` and the owner reads it by unique id and by query, as
  stored; an app with Read on the drive doesn't see it (query, unique id 404, file id 403); an app sends a
  deposit through the transient drive; an app with Write deposits on a zero-knowledge drive and can't read
  it back; the owner can't upload a `sealed` file to their own zero-knowledge drive; the owner updates a
  deposit with a `zkDrive` key header, and can't with a sealed one; on the transient drive a deposit
  replaces a deposit and can't replace an `aes` file; a payload can't be added to a deposit; a deposit
  with payloads is refused on the transient drive. Each fix was checked by undoing it: its test fails.
- **Services:** a socket event for a deposit reaches the owner console, not an app; `ZkDriveFilesReplaceTests`
  (a sealed key header replaces only a sealed one; no payloads with it).
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

## Step 3: Vault goes private

Status: built 2026-10-27 on branch `zk-drive-step-3` (odin-core and odin-js); chat-kmp built in parallel on its branch `vault-zero-knowledge`. **(V)** = read in code on 2026-10-27; **(I)** = inferred, to
check while building. Decided with the user:
- keep the drive key, re-locked from the master key to the zero-knowledge master key (no new key, no
  re-encryption); anything else is too complex for now;
- the owner converts Vault in the owner console; each app device gets the key at its next sign-in;
- **a zero-knowledge app key (`AppZkK`)** carries drive keys to an app. ZKMK never leaves the browser.

### 3.1 The zero-knowledge app key
Today an app reaches its drive keys through a chain the server opens on every request: the device's
token half opens the app's key store key (`ClientKeyEncryptedKeyStoreKey`), which opens the drive key on
each of the app's grants (`KeyStoreKeyEncryptedStorageKey`) **(V)**. `AppZkK` is that chain's
zero-knowledge twin, opened only on the owner's devices:

| Today (the server opens) | Zero knowledge (the device opens) |
|---|---|
| device token half → app key store key | `AppZkK`, sealed to the device once, through YouAuth |
| key store key → drive key on each grant | `AppZkK` → each zero-knowledge drive key the app is granted, locked by the browser |
| master key → key store key | `ZKMK` → `AppZkK` (the browser's copy) |

- **Made:** 16 random bytes, by the browser, the first time an app needs it (a sign-in or a conversion).
  Not derived from ZKMK (`hkdf(ZKMK, appId)`): one app's key could then never be replaced on its own, and
  a new ZKMK after a reset would change every app's key.
- **Stored on the app registration** (server, opaque to it):
  - `EncAppZkK = aes(ZKMK, AppZkK)` (`zkLock`), for the browser;
  - one `aes(AppZkK, DrvK)` per zero-knowledge drive the app is granted, keyed by drive id, for the app.
    On the registration, not on the grant: the server re-mints an app's grants without the browser
    (`UpdateAppPermissionsAsync`) **(V)**, which would drop a field it can't make.
- **Delivered once per device, through YouAuth** (3.2). From then on the device fetches its locked drive keys
  like any other app data (3.4), so a drive converted or granted later reaches every device that already
  holds `AppZkK`, with no new sign-in.
- **Accepted:** every device of an app shares `AppZkK`, so revoking one device doesn't replace it. The
  same "no rotation on removal" risk as drive keys (zero-knowledge-drives.md, decisions).
- Every device of every app holds only `AppZkK` and the drive keys it opens, never ZKMK.

### 3.2 The flow
Two independent steps: the owner converts the drive in the console, and each app device gets `AppZkK` at
its next sign-in. Nothing new carries a key to an app beyond one field in the sign-in it already does.

1. **The owner opts in from the Vault app** (decided 2026-10-27). While Vault isn't private, the Vault
   screen in chat-kmp shows a section at the top: "Click to change your Vault Drive to true zero knowledge
   encryption". It opens the owner console's login page as the password confirm,
   `/owner/login?redeal=vault-private&returnUrl=…`, which asks for the password even when the owner is
   logged in. After the password, the browser makes Vault private and sends the owner back to `returnUrl`:
   same origin (web Chat), one of our own `homebase…://` app schemes (the rule the signup's return already
   uses, `isAppReturnUrl`), or a desktop app's loopback listener (`http://localhost:{port}/…`). There is no "Make Vault private" section anywhere else in the console.
   If the server still holds the recovery phrase, the browser takes it over first, as a login does: a reset
   with that phrase brings back only today's master key, so it would lose ZKMK and with it Vault's key. The
   browser then converts the drive (3.3) and locks `DrvK` with the `AppZkK` of every app granted the drive,
   making one for an app that has none. The Vault app's section ships with chat-kmp's zero-knowledge client:
   once Vault is private, a device must sign in again with `zk_app_key=1`, or it sees Vault empty.
2. **Every sign-in of an app that reads zero-knowledge drives carries `AppZkK`.** The app says so in its
   authorize request; the consent page opens (or makes) the app's `AppZkK`, seals it to the request's
   public key, and posts it with the consent; the server carries it untouched in the token exchange, as one
   new field (`ZkAppKey`) of `EncryptedTokenExchange` / `YouAuthTokenResponse`, and marks the new token's
   device record (3.4).
   - The field is the minimum: today the exchange carries only what the server made, the shared secret and
     the client token (`EncryptedTokenExchange`) **(V)**, and `AppZkK` can only come from the browser.
   - Sealing it to the key the token exchange is already bound to adds no gap (zero-knowledge-drives.md,
     "Same protection, same limits").
   - Only `AppZkK` travels in the sign-in. The drive keys stay on the server, each locked with `AppZkK`,
     and reach the app through its security context (3.5).
   - **The consent page backfills** what the app is missing first (below).
3. **The app opens `ZkAppKey`** with its private key, keeps `AppZkK` in `SecureStorage`, and fetches its
   locked drive keys (3.4).

**Backfill at consent (decided 2026-10-27).** Before sealing, the consent page makes sure the app is
complete, whatever was defined before step 3:
- **No `AppZkK` yet:** the browser makes one and posts `EncAppZkK = aes(ZKMK, AppZkK)` with the consent.
- **A zero-knowledge drive granted to the app without a locked copy** (the Shard Recovery drive, converted
  before step 3; a grant made later without the browser, such as built-in provisioning at a version upgrade,
  `BuiltinProvisioner` **(V)**): the browser opens `DrvK` from the drive's `aes(ZKMK, DrvK)`
  (`StorageDrive.ZkMasterKeyEncryptedStorageKey` **(V)**), locks it with `AppZkK`, and posts it with the
  consent.
- The server hands the page what it needs: the app's `EncAppZkK` (if any), and for each zero-knowledge
  drive the app is granted, whether a locked copy exists and the drive's `aes(ZKMK, DrvK)`. It stores a
  posted copy only for a zero-knowledge drive the app is granted, and only on the app it is for.
- Whether any app holds a grant on the Shard Recovery drive today is unchecked (I); the backfill covers
  it either way.

**Backfill at the console's password login (decided 2026-10-27, built with step 3):** the same check for every app, so an app whose
devices don't sign in again still gets the locked copy of a drive granted to it. The console already runs a
login check of this kind: `ensureZeroKnowledgeKeys` converts the Shard Recovery drive when its zero-knowledge
copy doesn't open **(V)**.

**How an existing device gets there:** it signs in again. Logging out and in works with no other app
change, but chat-kmp clears its local data and resyncs. Optional, in chat-kmp: a banner on the Vault screen
that re-runs the sign-in without clearing data: a new token doesn't revoke the old one
(`YouAuthUnifiedService.CreateClientAccessTokenAsync` refuses only a revoked app) **(V)**, so the app
moves to the new token and keeps its data.

Until a device signs in again it has no mark, so it sees the converted drive as empty (3.4).

### 3.3 Server: converting a drive that keeps its key
One owner-only endpoint, given what the browser made: `aes(ZKMK, DrvK)`, a new write-only key pair (public
JWK, private half locked with `DrvK`), and `aes(AppZkK, DrvK)` (plus `EncAppZkK` where new) for each app
granted the drive. In one transaction:

- **The drive row** (`DriveManager`, reusing `ApplyZeroKnowledgeKeysAsync` with the key kept): store the
  ZKMK copy, drop `MasterKeyEncryptedStorageKey`, set `Private`, store the new write-only pair.
  - Keep `EncryptedIdIv` / `EncryptedIdValue` and `TempOriginalDriveId`: they encrypt the drive id with
    the same key and stay valid. `ApplyZeroKnowledgeKeysAsync` sets `TempOriginalDriveId = Id`, which is
    wrong here: on older drives the two differ (`StorageDrive.cs:236-245`) **(V)**.
  - A new write-only pair, not the server-made one: the server made that pair, so it has seen the private
    half (`DriveWriteOnlyKey.CreateKeyPair`) **(V)**. Nothing is sealed to it: only a zero-knowledge drive
    holds deposits (`CanHoldDeposits`) **(V)**.
- **The apps' locked copies** go on their registrations (3.1). An app granted the drive whose copy is
  missing is refused, so no app is silently cut off (I: or warned on the page).
- **Every grant loses its copy of the key, and keeps its permission.** New grants already come out keyless
  (`GrantCarriesStorageKey`) **(V)**; stored ones keep theirs. The walk is the one drive deletion does
  (`DriveDeletionService.DeleteDriveAsync`) **(V)**, stripping the key instead of the grant:
  - app registrations (`AppRegistrationService`, `AppKeyStore.DriveGrants`);
  - circle members, identities and YouAuth domains alike, and app circle grants (`CircleMembershipService`);
  - connections' deposited grants (`PeerKeyStore.DepositedGrants`) and sent connection requests
    (`PendingPeerKeyStore`);
  - app devices hold no drive keys, only the app's key store key **(V)**.
- **Every encrypted file's key header becomes `zkDrive`.** A stored `aes` header is `aes(DrvK, IV ‖ FileKey)`,
  locked with this drive's key (`EncryptKeyHeader`) **(V)**, and version 1 of `zkDrive` is the same 48 bytes
  (decision 3) **(V)**. So conversion only changes the type, and nothing is opened.
  - As built: per file, only the key header column (`TableDriveMainIndex.UpdateEncryptedKeyHeaderAsync`),
    guarded by the version tag the conversion read; the version tag and `modified` stay, so no client
    resyncs, as for the transform (2.5). A file updated in between fails the conversion
    (`VersionTagMismatch`), and the owner retries.
  - Decided by the header's shape (`EncryptedKeyHeader.HasZkDriveShape`, the check every `zkDrive` write
    passes), not by `IsEncrypted`: an unencrypted file stores `EncryptedKeyHeader.Empty()`, type `aes` with
    a zero IV, which stays as is; a deleted file's tombstone keeps its real key header but not
    `IsEncrypted`, and is relabelled **(V)**.
  - Accepted, narrow: a file *added* while the conversion commits (an upload that read the drive key just
    before, or a queued transfer) can still land as `aes`, which nobody then opens (I). Vault takes no
    transfers today; the owner converts from the console, with the apps otherwise idle.
  - An unencrypted file on the drive stays readable by the server. Vault encrypts its uploads (I: check
    chat-kmp).
- **Refused** if the drive is already zero knowledge, or isn't Vault (3.7).
- **After commit:** reset `OdinContextCache` (as `ConvertShardRecoveryDriveAsync` does) **(V)**. Open
  websockets keep the context they started with (`DeviceSocket`) (I: check what they can still reach).

The browser gets the existing key the way an owner read does today: the server opens it with the master
key and sends it locked to the owner's shared secret. That is the server's last look at it
(zero-knowledge-drives.md, "Converting an existing drive").

### 3.4 Server: a device that holds the key
Today `ZkDriveFiles.CallerHoldsDriveKey` is the owner console only (`HasMasterKey`) **(V)**, which keeps
every app away from client-locked files: an app that can't open them stalls its sync.

- **The device record marks which `AppZkK` it received,** at the token exchange that carried it (3.2,
  step 2), and only when the app asked for it: an older build never asks, so it never gets a mark. The old
  token, still valid, has no mark. The mark is the id of the key, a hash of `EncAppZkK`
  (`AppClientRegistration.ZkAppKeyId`, `AppRegistration.ZkAppKeyId`), and the device counts only while it
  is still the app's key: a concurrent first sign-in whose key won leaves the device with none (as built). The consent page sends the `EncAppZkK` it opened next to the
  sealed key, and the server refuses it unless it is still the app's (`ZkAppKeyAlreadySet`). The mark
  travels with the caller's context (`OdinClientContext.HoldsZkAppKey`).
- **`CallerHoldsDriveKey(driveId)`** becomes: the owner console, or a marked device of an app whose
  registration holds a locked copy of this drive's key. The read and write rules (2.4) don't change, only
  who counts as a key holder. A device on an older build sees the drive as empty, and its `aes` writes are
  refused by type, rather than stalling on headers it can't open.
- **The app fetches its locked drive keys** with the security context it already reads (3.5).
- Watch: `ExtendLife` rewrites the whole device row, so the mark must not be lost to a concurrent token
  refresh (`ClientRegistrationStorage.ExtendLife`, an upsert of the row) **(V)**.

### 3.5 The contract (pinned 2026-10-27, so chat-kmp can build in parallel)
Names are proposals until the server PR; the shapes are what the clients build against.

1. **Authorize request:** a new query parameter, `zk_app_key=1`, next to `public_key`. Precedent: an app
   opts into GCM the same way, with `cipher=aes-gcm` (`YouAuthDefaults.Cipher`) **(V)**. Without it, nothing
   changes: no `zkAppKey`, no device mark.
2. **Token response:** a new field, `zkAppKey` (`YouAuthTokenResponse`, next to
   `base64SharedSecretCipher` and `base64ClientAuthTokenCipher` **(V)**): `ZkSeal` version 1, base64, of the
   16-byte `AppZkK`, sealed to the authorize request's `public_key`, which is a base64url P-384 JWK
   (`EccPublicKeyData.FromJwkBase64UrlPublicKey`) **(V)**. The format and vectors are owner-keys.md's
   `seal`. Absent when not asked for.
3. **Security context:** the app already reads `GET /api/v2/auth/context` (`V2AuthController.GetSecurityContext`,
   `WebOdinContext.Redacted()`) **(V)**: chat-kmp fetches it in the background after every connect and at
   the first foreground open (`AuthConnectionCoordinator.scheduleGrantReconcile`, `SecurityContextProvider`)
   **(V)**, and reads each drive grant by name, so new fields are ignored by released builds
   (`parseDriveGrant`) **(V)**. Each drive grant (`RedactedDriveGrant`, today `permissionedDrive` and
   `hasStorageKey` **(V)**) gains two fields, set only on a zero-knowledge drive the app holds a copy for,
   null otherwise:
   - `zkAppKeyEncryptedStorageKey`: `aes(AppZkK, DrvK)`, `zkLock` (base64 of a 12-byte IV, the ciphertext
     and its 16-byte tag);
   - `zkStorageKeyEncryptedWriteOnlyPrivateKey`: the drive's `EncWO-PK-Priv_zk`, which `DrvK` opens (the
     same name as on the drive, `StorageDrive` **(V)**). Chat needs it for deposits; Vault doesn't.

   Any token of the app gets them: they are useless without `AppZkK`, and chat-kmp reads a set
   `zkAppKeyEncryptedStorageKey` as "this drive is zero knowledge" to show its banner on a device that has no
   `AppZkK` yet. Conversion refuses unless every app granted reading the drive gets a copy, so the field is set
   on every converted drive an app reads. A write-only grant gets no copy: it deposits, sealed to the drive's
   public key (`StorageDrive.PermissionNeedsKey`, as built). Only a marked device counts as a key holder (3.4). A conversion reaches a device at its
   next connect. Attaching the app registration's locked copies to the redacted grant is server plumbing (I).
4. **Files:** unchanged from steps 1 and 2. A file on a zero-knowledge drive comes back with its key header
   as stored (`zkDrive`, or `sealed`), and payload responses carry no packed key header: the client opens
   payloads with FileKey and the payload's IV (`payloads[].iv`).

**Server, besides the contract** (as built: owner endpoints under `security/zk`):
- `GET app-keys`: every app with its `EncAppZkK` and every drive it is granted (`isZeroKnowledge`, the drive's
  `aes(ZKMK, DrvK)` on a zero-knowledge one, `hasAppCopy`), so the browser knows which apps need a copy before it
  converts a drive; `POST app-keys`: the backfill (refuses replacing an app's key, a drive not granted, a drive
  not zero knowledge);
- `GET drives/{driveId}/key-for-conversion` and `POST convert-drive` (3.3).

- consent takes the sealed `AppZkK` from the browser and the `EncAppZkK` it came from (3.4), after any
  backfill (3.2), and the token exchange carries it and marks the device;
- the consent page's read: the app's `EncAppZkK`, and its granted zero-knowledge drives with their
  `aes(ZKMK, DrvK)` and whether a locked copy exists;
- app registration: `EncAppZkK` and the locked drive keys. A copy lives as long as a grant that reads the drive:
  every save drops the others (a grant removed or narrowed to writing, a drive deleted); a drive given a new key
  (Shard Recovery after a reset) drops every app's copy; a reset with today's master key keeps every
  `EncAppZkK` and copy, since the devices holding the app's key may be the only way left into its drives (as built);
- owner: get a drive's key to convert it; convert (3.3); read and write `EncAppZkK` for the console;
- `ZkDriveFiles.CallerHoldsDriveKey` takes the drive (3.4).

### 3.6 Clients
- **Owner console (odin-js):**
  - the login page with `redeal=vault-private`, the Vault app's password confirm (3.2): password, the phrase
    takeover if the server still holds the phrase, convert, lock `DrvK` with each granted app's `AppZkK`
    (open or make), back to the app;
  - the consent page, for an app that reads zero-knowledge drives: backfill (`AppZkK` if missing, a locked
    copy of each granted zero-knowledge drive that has none), then seal `AppZkK` to the app;
  - the same backfill for every app at a password login;
  - `makeZkDriveKeys` takes an existing drive key (today it always makes a new one,
    `ZeroKnowledgeKeys.ts` **(V)**);
  - after a reset with today's master key every `EncAppZkK` stays, locked by the old ZKMK: the devices that
    hold an app's key keep opening its drives, but this browser can no longer open the key, so a new device's
    `zk_app_key=1` sign-in fails at consent and the login backfill skips the app (open item: how the owner gets
    the key back from a device that holds it).
- **chat-kmp:**
  - `zk_app_key=1` in its authorize request, `zkAppKey` in the token response, the two new grant fields
    (3.5); optionally the banner that signs in again without clearing data;
  - the Vault screen's opt-in section, which opens the owner console's password confirm (3.2);
  - a `ZkSeal` port to open `ZkAppKey`: GCM exists (`AesGcm.kt`) and ECDH P-384 too, but its hkdf has no
    `info` and wants a 16-byte salt (`EccKeyFunctions.kt`, `HashUtil.kt`) **(V)**, so the port needs an
    hkdf with `info` and no salt; checked against the C#/js vectors;
  - `AppZkK` in `SecureStorage`; fetch and open the drive keys at start;
  - the header switch on a zero-knowledge drive: open and lock with DrvK (`ServerFile.asHomebaseFile`,
    the payload decrypt with FileKey and the payload's IV instead of `sharedsecretencryptedheader64`,
    `UploadDescriptorBuilder`, the update descriptor), and the outbox stops re-keying to a header the
    server returns (`DriveOutboxUploader.rekeyedUpdateForExistingServerFile`) **(V)**;
  - device storage (decided 2026-10-27): `AppZkK` and the drive keys go in `SecureStorage` on every
    platform, next to the token and shared secret. Two follow-ups after step 3: WASM, whose `SecureStorage`
    is plain `localStorage` and persists (`SecureStorage.web.kt`) **(V)**, moves to memory-only keys (a
    reload then signs in again); desktop explores securing tokens and keys properly instead of the PKCS12
    file with a hardcoded password (an OS keychain, I).

### 3.7 Scope
- **Vault only.** Vault gets its drive through the chat app's registration (a permission extension,
  `AppConfig.kt`) **(V)**; the built-in "Homebase - Vault" app and the Vault circle also hold ReadWrite
  grants on it (`SystemAppConstants`, `BuiltinCircles.VaultCircle`) **(V)**, and lose the key with every
  other grant. `OwnerOnly` doesn't stop the circle's grant (`CircleDefinitionService.cs:609-614`) **(V)**.
- The conversion takes the drive as a parameter, so Chat and others reuse it later; refusing every drive
  but Vault for now is one check.
- **No way back.** A Private drive depends on zero-knowledge recovery (Shamir, the phrase), released in
  v0.1.968.

### 3.8 Later: everyone, at a login
Once every app that uses a drive reads `zkDrive`, a data upgrade at a password login converts the drives
still Collaborative with the same endpoint and locks their keys for each granted app's `AppZkK`. Apps
pick the keys up at their next fetch; a device without `AppZkK` gets it at its next sign-in. Until then
the opt-in is the only way in, because converting a drive cuts off every app that can't read it.

### 3.9 Open
- ~~A grant on a zero-knowledge drive made without the browser can't lock the key for the app.~~ Resolved:
  the backfill at consent and at login (3.2).
- After step 3: WASM memory-only keys; securing desktop tokens and keys (3.6).

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
  client-locked file, so a peer's delete of a deposit applied there is lost, and a sender's own server
  can't find its deposit to update it. Step 2 works round two such flows (the transient drive's staged
  deposits, the Shamir lookup), and its replace rule (2.4) holds whoever drains. The general
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
- **Consent giving apps DrvK** and the upgrade of every drive at a login: after step 3 (3.7).
- **chat-kmp:** comes with consent. Two small bugs found on the way, both harmless today:
  - `EncryptedKeyHeader.toBase64()` writes the version big-endian, but has no production caller;
  - the `MissingEncryptedHeader` check compares by reference, so it never fires.
- **Per-file integrity, with key header version 2:** [#2024](https://github.com/homebase-id/odin-core/issues/2024).
  CBC has none, so zero knowledge means confidential from the server, not tamper-proof.
