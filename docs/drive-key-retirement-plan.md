# Drive public keys: a peer key cache, a key check on arrival, and later retired keys

Status: plan, 2026-10-28. The first piece towards Chat going private. **(V)** = verified by reading code
on main `e3a6af496`. **(I)** = inferred.

## Goal

A sender seals a file key to a recipient drive's write-only public key. The sender's server shouldn't
have to fetch that key for every message, and a seal made with a key the drive doesn't hold should be
refused, not stored where nobody can open it.

Decided 2026-10-28: build the cache and the key check now, and **leave retired keys for later**
(see "Later"). Seals stay v1.

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

## Now (odin-core only, no client change)

1. **A deposit must be sealed to the drive's key.** On arrival, local upload and peer transfer alike,
   a sealed key header must name the drive's current write-only key by its v1 key id; any other is
   refused with `SealedToUnknownKey` (9033). The shape is checked first, so a malformed seal still gets
   `InvalidKeyHeader`. Not on the transient drive, where a sender's deposit waits to go elsewhere.
   (`ZkDriveFiles`, no database read.)
2. **The sender's server caches a peer drive's key** per identity, for `Host:PeerDriveKeyCacheSeconds`
   (7 days; at least the cache's 2-second minimum), in `PeerDriveQueryService`'s tenant cache.
   - Only a zero-knowledge drive's answer is kept: one that isn't yet takes no deposits, and becomes
     one at its owner's next login.
   - Per identity, not per host: the recipient grants the key per asker (it requires Write), and every
     other cache here is per tenant. A cached answer skips the remote's per-asker check, so after a
     revoke the key is still served until it expires; the deposit is refused at the transfer.
   - The file outbox workers forget a recipient's keys when a deposit is refused with
     `SealedToUnknownKey`. The sending device learns of it through its outbox failure, and seals again
     with the key it fetches next.
   - Shamir dealing checks the holder's key fresh (`fresh: true`), so a browser that sealed with a copy
     is told at dealing time, and the copy is replaced.

**What it costs without retired keys:** a drive's key changes only when the drive is converted (the
first time, or again after a reset), and seals still in flight with the old key are refused. Before
this, they were stored and could never be opened.

## Later: retired keys, when a voluntary rotation needs them

Nothing replaces a drive key with a grace period today: converting a drive drops a key that either took
no deposits (Collaborative) or whose private half is lost (a reset). When a rotation is built, for Chat
perhaps on removing a member:

### The timing rule

- **O**: the longest an outbox item can still be delivered: `OutboxRetryLaterMaxAge` + 6 h.
- **C**: how long the sender's server caches a peer's drive key (new, `Host:PeerDriveKeyCacheSeconds`).
- **G**: how long a retired key still accepts new seals (new, `Host:RetiredDriveKeyAcceptSeconds`).

A key fetched just before it was retired is handed out for up to C; a seal made at the end of that
waits in the outbox for up to O. So **G ≥ C + O**, checked at startup. Defaults: C = 7 days, O = 7 days
+ 6 h, G = 30 days.

- **Retired keys**: the current key stays in the Drives row; retired ones in one key-value record per
  drive (not a table: that needs the SQLite generator), each with its browser-locked private half and
  `retiredAt`. A retired key takes new seals until G has passed, answering "replaced" on the peer
  transfer response so the sender forgets its copy; its private half is kept for good. Devices then
  need every key's private half, and Shamir release must accept retired keys.
- **From the review of a first build** (2026-10-28): a reset or a drive deletion must forget the retired
  keys too (the reset rule); retiring needs a compare-and-swap against a second rotation; a refusal
  should re-read the retired list past the cache before answering "unknown"; G needs a margin for the
  outbox's normal retries after its retry-later window.

## Seal v2, when Chat needs it

### The fingerprint
- `KeyId` = the first 8 bytes of the key's **JWK thumbprint** (RFC 7638): SHA-256 over
  `{"crv":"P-384","kty":"EC","x":"…","y":"…"}`, members in that order, no whitespace. Built from the JWK,
  which is minimal and the same everywhere; the DER is not (it differs by origin).
- It is only an index: it picks which of a drive's keys a seal used. A wrong match fails when the
  device opens the seal; it can't make anyone accept anything.

### The format
- `ZkSeal` v2: `0x02 ‖ key id (8 bytes) ‖ throwaway public key (97) ‖ IV (12) ‖ ciphertext + tag`, and the
  header before the IV (`0x02 ‖ key id ‖ throwaway public key`) is the AES-GCM associated data, so the
  authentication covers it. v1 authenticates the ciphertext only (`ZkSeal.cs:53-70`) **(V)**.
- The seal already protects what it carries: AES-GCM over `IV ‖ file key` (`KeyHeader.Combine`) **(V)**.
  #2024 (the CBC key header, content integrity) is a different format and stays its own issue.
- Three implementations seal and open: odin-core `ZkSeal.cs`, odin-js `ZeroKnowledgeKeys.ts`, chat-kmp
  `ZkKeys.kt` **(V)**. Apps in the field update slowly, so **every reader accepts v2 before any writer
  makes it**. Readers keep accepting v1 for good: v1 seals are stored (Shamir shards, deposits), and a
  v1 key id is matched by computing the CRC of each candidate key, as today.

## Tests (now)

- V2: a deposit sealed to the drive's key is taken, to another key refused with `SealedToUnknownKey`;
  a sender keeps its copy of a peer's key until a deposit sealed with it is refused, then fetches the
  new one; an answer from a drive that isn't zero knowledge yet is not kept.

## Not in this work

- Retired keys and voluntary rotation; seal v2.
- Sealing per recipient in the outbox, receiving, the transform: the next Chat pieces.
