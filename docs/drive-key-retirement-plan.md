# Drive public keys: retired keys, a peer key cache and a better fingerprint

Status: plan, 2026-10-28. The first piece towards Chat going private. Server side (odin-core), with
the seal format change mirrored in odin-js and chat-kmp.

**(V)** = verified by reading code on main `e3a6af496`. **(I)** = inferred.

## Goal

A sender seals a file key to a recipient drive's write-only public key. That must keep working when
the recipient replaces the key, and the sender's server must not have to fetch the key for every
message:

- the recipient keeps **retired** keys and still accepts seals made with them for a while, answering
  "replaced";
- the sender's server **caches** peers' drive public keys per identity;
- a seal names its key by an **8-byte SHA-256 fingerprint** instead of a CRC;
- a seal made with a key the drive doesn't know is **refused** with its own error code.

With the timing rule below, the outbox always delivers: nothing ever has to be sealed again.

## What exists today

- **One key pair per drive**, in the Drives row: `WriteOnlyKeyPair` (`StorageDrive.cs:376`), and on a
  zero-knowledge drive the browser-locked private half `ZkStorageKeyEncryptedWriteOnlyPrivateKey`
  (`StorageDrive.cs:315`) **(V)**. Both columns are capped at 4096 bytes (`TableDrivesCRUD.cs:71,74`)
  **(V)**, too small for a list of keys.
- **Who writes it** (`DriveManager.cs`) **(V)**: creation (:205); the v14→v15 backfill, which never
  overwrites (:485, "Key rotation is a separate feature with its own grace-window handling"); and both
  zero-knowledge conversions, which overwrite it outright (:543).
- **The seal already names its key.** `ZkSeal` v1 is `0x01 ‖ key id (4 bytes) ‖ throwaway public key
  (97) ‖ IV (12) ‖ AES-GCM ciphertext + tag` (`ZkSeal.cs:20-31`) **(V)**. The key id is CRC-32 (IEEE)
  over the raw point `0x04‖x‖y`. odin-js mirrors it (`sealKeyId`, `readSealKeyId`) **(V)**.
- **Two different "CRCs" exist** **(V)**: the seal's key id (raw point), and
  `EccPublicKeyData.crc32c`, a CRC32C over the DER, which differs by origin (a JWK import writes
  explicit curve parameters, a generated key doesn't; `EccKeyData.cs:280-287`). The second is what the
  owner console shows and what `DrivePublicKeyResponse.PublicKeyCrc32` returns.
- **Arrival checks only the shape of a seal**, never its key: `EncryptedKeyHeader.AssertValidSealed`,
  "Nothing more can be checked: nobody here can open it" **(V)**. Only Shamir compares keys, against the
  current key only (`ShamirBaseService.cs:106-107`) **(V)**.
- **Fetching a peer's key**: `GetRemoteDriveWriteOnlyPublicKeyAsync`, deliberately not cached
  (`PeerDriveQueryService.cs:126-129`) **(V)**. Callers: the V2 endpoint for apps, and Shamir dealing.
- **A pattern to copy for the cache**: `PublicPrivateKeyService` caches peers' identity ECC keys per
  tenant in key-value storage with an expiry, and invalidates on a failed send (`:58-116`) **(V)**.
- **Outbox limits** **(V)**: `Host:OutboxRetryLaterMaxAgeSeconds`, default 7 days, measured from when
  the item was added; a deferral can land up to 6 hours after that (`OutboxRetryLater.cs:21-58`). Normal
  retries (30 attempts) add up to about 4 hours **(I)**. A 400 from the recipient makes the outbox give up
  (`OutboxWorkerBase.cs:81-85`) **(V)**.

## The timing rule

- **O**: the longest an outbox item can still be delivered: `OutboxRetryLaterMaxAge` + 6 h.
- **C**: how long the sender's server caches a peer's drive key (new, `Host:PeerDriveKeyCacheSeconds`).
- **G**: how long a retired key still accepts new seals (new, `Host:RetiredDriveKeyAcceptSeconds`).

A key fetched just before it was retired is handed out for up to C; a seal made at the end of that
waits in the outbox for up to O. So **G ≥ C + O**, checked at startup. Defaults: C = 7 days, O = 7 days
+ 6 h, G = 30 days.

## Design

### 1. The fingerprint
- `KeyId` = the first 8 bytes of the key's **JWK thumbprint** (RFC 7638): SHA-256 over
  `{"crv":"P-384","kty":"EC","x":"…","y":"…"}`, members in that order, no whitespace. Built from the JWK,
  which is minimal and the same everywhere; the DER is not (it differs by origin).
- It is only an index: it picks which of a drive's keys a seal used. A wrong match fails when the
  device opens the seal; it can't make anyone accept anything.

### 2. Seal format v2
- `ZkSeal` v2: `0x02 ‖ key id (8 bytes) ‖ throwaway public key (97) ‖ IV (12) ‖ ciphertext + tag`, and the
  header before the IV (`0x02 ‖ key id ‖ throwaway public key`) is the AES-GCM associated data, so the
  authentication covers it. v1 authenticates the ciphertext only (`ZkSeal.cs:53-70`) **(V)**.
- The seal already protects what it carries: AES-GCM over `IV ‖ file key` (`KeyHeader.Combine`) **(V)**.
  #2024 (the CBC key header, content integrity) is a different format and stays its own issue.
- Three implementations seal and open: odin-core `ZkSeal.cs`, odin-js `ZeroKnowledgeKeys.ts`, chat-kmp
  `ZkKeys.kt` **(V)**. Apps in the field update slowly, so **every reader accepts v2 before any writer
  makes it**. Readers keep accepting v1 for good: v1 seals are stored (Shamir shards, deposits), and a
  v1 key id is matched by computing the CRC of each candidate key, as today.

### 3. Retired keys, in the key-value store
- The current key stays in the Drives row. Retired keys go in one key-value record per drive (the tenant's
  key-value table, up to 1 MB), each with its public key, its browser-locked private half and `retiredAt`.
  Not a table of its own: that needs the SQLite generator (decided 2026-10-28).
- **Retiring is time-based**, never by count: a retired key accepts new seals until `retiredAt + G`.
- **Its private half is kept after G.** G ends acceptance only. Files already sealed to a retired key
  still need its private half to be opened, until a device transforms them (the designed transform) or
  they are deleted.
- `EccKeyListManagement` (count-based, no rotation caller in src **(V)**) is not reused; the identity
  keys keep it.

### 4. Arrival: three answers
At the arrival points that already have the drive loaded **(V)**: `ZkDriveFiles.OpenForWriteAsync`
(local upload, V1 and V2) and `PeerDriveIncomingTransferService` / `PeerDriveIncomingFileUpdateService`
(peer, before staging). The transient drive is skipped: it has no recipient key.
- **Current key:** accept.
- **Retired, within G:** accept, and say "replaced": a new field on the local upload result, and on the
  peer transfer response (`PeerResponseCode` has no room for it today **(V)**).
- **Unknown, or retired past G:** refuse with a new error code, `SealedToUnknownKey`. For a peer
  transfer that is a 400, so the sender's outbox gives up and the sending app is told through its outbox
  failure.

### 5. The sender's cache, per identity
- `GetRemoteDriveWriteOnlyPublicKeyAsync` reads through a per-tenant cache, keyed by
  `(odinId, appSlug, driveSlug)`, entries kept for up to C.
- An entry is dropped when a delivery answers "replaced" or "unknown key", so the next fetch gets the
  current key.
- Per identity, not per host: the recipient grants the key per asker (it requires Write), and every
  other cache here is per tenant.

### 6. Serving keys
- `DrivePublicKeyResponse` gains `KeyId` (the fingerprint). `PublicKeyCrc32` stays until the clients
  that read it have moved (the owner console shows it; chat-kmp may read it).
- Devices that hold the drive key get every key's locked private half (current and retired), each with
  its key id, through the same grant fields as today. A device opens a seal with the key its id names.
- The owner console shows the fingerprint instead of `CRC`.

### 7. Shamir
- Release checks a shard against the drive's keys, not only the current one: a retired key whose
  private half is still held is fine.

## Who retires a key today

Nobody yet, with a grace period:
- **Converting a drive** (Vault) replaces a key the server held. A Collaborative drive can't hold
  deposits (`StorageDrive.cs:78`) **(V)**, so nothing was ever sealed to the old key: it is dropped, not
  retired.
- **Converting the Shard Recovery drive again after a reset** replaces a key whose private half was
  locked with a zero-knowledge master key that is lost. Seals to it can never be opened, so it is
  dropped, and a late seal gets "unknown key" instead of being stored unreadable, as it is today.

So step 3's grace period has no caller until a voluntary rotation exists (for Chat, perhaps on removing
a member). The retired-key record, the arrival answers and the cache are what Chat needs first.

## Decided (2026-10-28)

- Build the retirement with its grace period now; retired keys in the key-value store. Defaults:
  C = 7 days, G = 30 days.
- The key id is the JWK thumbprint, 8 bytes; the seal v2 header is authenticated.

## Build order

Each a PR to main:
1. **odin-core: drive keys** (this branch). No seal format change and no client change: seals stay v1 and
   are matched by their 4-byte key id.
   - Retired keys in the tenant's key-value store, one record per drive (`DriveManager`); retiring by time.
   - The arrival answers: current key, retired key within G ("replaced", on the peer transfer response),
     anything else refused with `SealedToUnknownKey`. A local upload is checked the same way but says
     nothing back: its writer reads the key from its own identity, with no cache to correct.
   - The per-identity cache of peers' drive keys (`PeerDriveQueryService`), forgotten by the file outbox
     workers on "replaced" or `SealedToUnknownKey`.
   - Shamir release against the current or any retired key.
   - `Host:PeerDriveKeyCacheSeconds`, `Host:RetiredDriveKeyAcceptSeconds`, and the G ≥ C + O check at
     startup.
   - `DriveManager.ReplaceWriteOnlyKeyPairAsync` retires a key; it has no caller yet (see "Who retires a
     key today"). The first voluntary rotation also serves the retired private halves to devices.
2. **Seal v2**, when Chat needs it: readers in all three code bases first, with one shared test vector,
   then writers. The 8-byte key id (JWK thumbprint) comes with it, in `DrivePublicKeyResponse` and the
   owner's drive data.

## Tests

- V2: a seal to the current key is accepted; to a retired key within G it is accepted with "replaced";
  past G or to an unknown key it is refused with `SealedToUnknownKey`. The same for a peer transfer.
- The sender's cache: a second fetch within C doesn't call the peer; "replaced" drops the entry.
- Startup refuses G < C + O.
- Seal v1 and v2 both open; a shared test vector across odin-core, odin-js and chat-kmp for the key id
  and the v2 seal.
- Shamir: a shard sealed to a retired key is still released.

## Not in this work

- Voluntary key rotation itself, and when Chat would use it.
- Sealing per recipient in the outbox, receiving, the transform: the next Chat pieces.
