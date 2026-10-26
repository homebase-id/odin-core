# Owner keys: names, shorthand, and what locks what

One place for every key the owner's identity uses, today's and the zero-knowledge ones, with the
shorthand we write them in. Names agreed 2026-10-25. It describes the branch `zk-recovery-friends`
(odin-core #2013, odin-js #990): both recovery pairs, built and not yet released. The design behind
it is in [zero-knowledge-recovery-plan.md](zero-knowledge-recovery-plan.md) and
[zero-knowledge-keys-plan.md](zero-knowledge-keys-plan.md).

Claims checked against the code are marked **(V)**, as in [security-model.md](security-model.md).

## How to read the shorthand

| Written | Means |
|---|---|
| `aes(K, x)` | `x` locked by the key `K`. Only `K` opens it. |
| `seal(Pub, x)` | `x` sealed to the public key `Pub`. Anyone with `Pub` can seal; only the private half opens it. |
| `pbkdf2(pw, salt)` | a key stretched from the password: slow on purpose, so guessing is slow. |
| `hkdf(K, "label")` | a key derived from `K`, one way: it can't be turned back into `K`. |
| `sha256(x)` | a hash of `x`. |
| `PK` | a public/private key pair. |
| `PK-Pub` / `PK-Priv` | the pair's halves: the public one seals and may be shared, the private one opens and stays locked. |
| `Enc…` | a stored value, locked with `aes` or sealed with `seal`. `EncMK` is "MK, locked". A suffix names the key that locks it (`EncMK_bip`: locked by `BipK`). The definition spells it out. |

Two kinds of `aes`: the server's own values are AES-CBC with a key check (`SymmetricKeyEncryptedAes`)
**(V)**; the browser's zero-knowledge values are AES-GCM, base64 of a 12-byte IV, the ciphertext and
its 16-byte tag **(V)**. The shorthand doesn't tell them apart; the tables say which.

## The keys

### Root keys

| Shorthand | Name | What it is | Made by | Kept where |
|---|---|---|---|---|
| `pw` | password | what the owner types | the owner | the owner's head; the browser only at login and password change |
| `MK` | master key | 16 random bytes; opens drive keys, connections, everything the server decrypts today | the server, at setup | never stored in the clear |
| `ZKMK` | zero-knowledge master key | 16 random bytes; opens every zero-knowledge drive key | the browser, at its first login | never stored in the clear; the server never sees it |
| `BipK` | BIP39 key (the recovery phrase) | 16 random bytes = the 12 BIP39 words the owner wrote down | the server, at setup; the browser takes it over at the next password login (below) | the owner's paper; copies below |
| `MK-KEK` | master key's key-encryption key | `pbkdf2(pw, SaltKek)` | the browser, at login | never stored; split between the session cookie and the server (below) |
| `ZKMK-KEK` | zero-knowledge master key's key-encryption key | `pbkdf2(pw, ZkSaltKek)`: the same maths as the MK-KEK with its own salt | the browser, at login | never stored |

### BIP keys

| Shorthand | Name | What it is | Made by | Kept where |
|---|---|---|---|---|
| `Bip-PK` (`Bip-PK-Pub` / `Bip-PK-Priv`) | BIP key pair | a P-384 key pair. **New in the second review.** | the browser, whenever it has the phrase (takeover, re-entry) | `Bip-PK-Pub` stored as is (a JWK); `Bip-PK-Priv` locked, next line |
| `EncBip-PK-Priv` | the stored BIP private key | `aes(BipK, Bip-PK-Priv)`: the private half as PKCS8, locked by `BipK` with AES-GCM: base64 of a 12-byte IV, the ciphertext and its 16-byte tag (`zkLock`) **(V)** | the browser, with the pair | the server (`ZkKeyRecord.PhraseKeyEncryptedPhrasePrivateKey`) **(V)**, which can't open it |
| `Bip-RK` | BIP reset key | `hkdf(BipK, "homebase-recovery-reset-key")`, 16 bytes. **From the phrase since the second review.** | the browser, from the phrase | never stored; the server keeps only what it locks |

### Shamir keys

| Shorthand | Name | What it is | Made by | Kept where |
|---|---|---|---|---|
| `AutoK` | automated-recovery key (the server's distribution key, `distributionKey` in code) | 16 random bytes, split into shards | the server, for a server-made deal only: every automated deal before the browser dealt it, and the takeover's move off the phrase **(V)** | never stored; the shards go to the automated identities. The next password login deals the automated deal again in the browser, with `SmrK`, and that deletes `EncMK_auto` **(V)** |
| `SmrK` | Shamir key: the deal's secret, `BipK`'s twin | 16 random bytes, split into one piece per holder | the browser, at each deal: to friends, or the automated deal to the host's automated identities (`dealToFriends`) **(V)** | never stored; only enough shards rebuild it |
| `Smr-PK` (`Smr-PK-Pub` / `Smr-PK-Priv`) | Shamir recovery key pair | a P-384 key pair; the BIP key pair's twin | the browser, at each deal it makes (friends or the automated deal) | `Smr-PK-Pub` stored as is (a JWK); `Smr-PK-Priv` locked, next line |
| `EncSmr-PK-Priv` | the stored Shamir private key | `aes(SmrK, Smr-PK-Priv)`: the private half as PKCS8, locked by `SmrK` with AES-GCM: base64 of a 12-byte IV, the ciphertext and its 16-byte tag (`zkLock`) **(V)** | the browser, at each deal it makes | the server (`ZkKeyRecord.ShamirKeyEncryptedShamirPrivateKey`) **(V)**, which can't open it |
| `Smr-RK` | Shamir reset key | `hkdf(SmrK, "homebase-shamir-reset-key")`, 16 bytes **(V)** | the browser, from `SmrK` | never stored; the server keeps only what it locks |
| `ShardKey` | per-shard key | 16 random bytes and a 16-byte IV per shard, AES-CBC: the dealer's lock on each piece, so a holder never sees a raw piece **(V)** | whoever deals: the browser (`dealToFriends`), or the server for a server-made deal (`CreateShards`) **(V)** | the dealer package (below), which the dealer's own server can read **(V)** |
| `Tmp-PK` (`Tmp-PK-Pub` / `Tmp-PK-Priv`) | throwaway key pair | P-384, made when a recovery starts in this browser, and reused for the next start from it unless a recovery is under way for another browser **(V)** | the recovering browser | `Tmp-PK-Priv` in that browser's localStorage (`shamir-recovery-key`) until the recovery is over, a reset, or the next login **(V)**; `Tmp-PK-Pub` goes to the server with the start and to each friend with the shard request **(V)** |

### Session keys

| Shorthand | Name | What it is | Made by | Kept where |
|---|---|---|---|---|
| `SessK` | session key | 16 random bytes per owner session | the server, at login | the server's session record; released only to a request carrying the HttpOnly cookie |

### Drive keys

| Shorthand | Name | What it is | Made by | Kept where |
|---|---|---|---|---|
| `DrvK` | drive storage key | 16 random bytes per drive | the server (today) or the browser (zero knowledge) | locked, see below |
| `WO-PK` (`WO-PK-Pub` / `WO-PK-Priv`) | a drive's write-only key pair | P-384; lets others deposit to a drive they can't read | the server (today) or the browser (zero knowledge) | `WO-PK-Pub` as is; `WO-PK-Priv` locked, next lines |
| `EncWO-PK-Priv` | the stored write-only private key, normal drive | `aes(DrvK, WO-PK-Priv)`: the private half as PKCS8 DER, locked by `DrvK` with AES-CBC, a 16-byte IV and a key check (`keyHash`, a reduced SHA-256 of `DrvK`) (`EccFullKeyData.storedKey`) **(V)** | the server, when it creates the drive | the server (`Drives.WriteOnlyKeyPair`, with the public half) **(V)**; it opens it whenever it holds `DrvK` |
| `EncWO-PK-Priv_zk` | the stored write-only private key, zero-knowledge drive | `aes(DrvK, WO-PK-Priv)`: the private half as PKCS8, locked by `DrvK` with AES-GCM: base64 of a 12-byte IV, the ciphertext and its 16-byte tag (`zkLock`) **(V)** | the browser, when it converts the drive | the server (`StorageDrive.ZkStorageKeyEncryptedWriteOnlyPrivateKey`) **(V)**, which can't open it; `Drives.WriteOnlyKeyPair` then holds only the public half **(V)** |

### File keys

| Shorthand | Name | What it is | Made by | Kept where |
|---|---|---|---|---|
| `FileKey` | a file's own key (its key header) | 16 random bytes and a 16-byte IV per file; the content is AES-CBC **(V)** | whoever writes the file; for a deposit, the depositor | locked by `DrvK`; for a deposit, `seal(WO-PK-Pub, IV ‖ FileKey)` in a key header of type `Sealed` (12) **(V)** |

## What is stored

| Stored | = | Field in code | Server can open? | Written when |
|---|---|---|---|---|
| `PwHash` | `pbkdf2(pw, SaltPassword)` | `PasswordData.HashPassword` **(V)** | it checks it; can't reverse it | password set or changed |
| `EncMK` | `aes(MK-KEK, MK)` | `PasswordData.KekEncryptedMasterKey` **(V)** | only with a live session's MK-KEK | password set or changed |
| session split | `MK-KEK` = cookie half ⊕ server half | `OwnerConsoleClientRegistration.TokenEncryptedKek` **(V)** | only with the cookie | login |
| `EncDrvK` | `aes(MK, DrvK)` | `StorageDrive.MasterKeyEncryptedStorageKey` **(V)** | yes, with MK | drive created (non-zero-knowledge drives) |
| `EncZKMK` | `aes(ZKMK-KEK, ZKMK)` | `ZkKeyRecord.ZkKekEncryptedZkMasterKey` **(V)** | no | first login; re-locked at password change |
| `ZKMK` in the browser | `aes(SessK, ZKMK)` | localStorage `ZKMK` **(V)** | no (no localStorage); the browser alone has no `SessK` | every password login |
| `EncDrvK_zk` | `aes(ZKMK, DrvK)` | `StorageDrive.ZkMasterKeyEncryptedStorageKey` **(V)** | no | drive converted (Shard Recovery today) |
| `EncWO-PK-Priv` | `aes(DrvK, WO-PK-Priv)`, AES-CBC | `StorageDrive.WriteOnlyKeyPair` (`EccFullKeyData.storedKey`) **(V)** | yes, with `DrvK` | drive created (non-zero-knowledge drives) |
| `EncWO-PK-Priv_zk` | `aes(DrvK, WO-PK-Priv)`, AES-GCM | `StorageDrive.ZkStorageKeyEncryptedWriteOnlyPrivateKey` **(V)** | no | drive converted |
| `EncZKMK_bip` | `aes(BipK, ZKMK)` | `ZkKeyRecord.PhraseKeyEncryptedZkMasterKey` **(V)** | no | takeover; re-entry |
| `EncBipK_zk` | `aes(ZKMK, BipK)` | `ZkKeyRecord.ZkMasterKeyEncryptedPhraseKey` **(V)** | no | takeover; re-entry |
| `Bip-PK-Pub` | as is | `ZkKeyRecord.PhrasePublicKeyJwk` **(V)** | it's public | takeover; re-entry |
| `EncBip-PK-Priv` | `aes(BipK, Bip-PK-Priv)` | `ZkKeyRecord.PhraseKeyEncryptedPhrasePrivateKey` **(V)** | no | takeover; re-entry |
| `EncPw_bip` | `seal(Bip-PK-Pub, pw)` | `ZkKeyRecord.PhrasePublicKeyEncryptedPassword` **(V)** | no | takeover; password change; a login that finds it missing |
| `EncMK_bipRK` | `aes(Bip-RK, MK)` | `ZkKeyRecord.ResetKeyEncryptedMasterKey` **(V)** | only with `Bip-RK` | takeover; re-entry (the server locks it itself) |
| `PhraseCheck` | `sha256("homebase-recovery-phrase-check" ‖ BipK)` | `RecoveryKeyRecord.PhraseCheck64` **(V)** | it compares; `BipK` is random, so it can't be reversed | takeover |
| `EncBipK_server` | `aes(MK, BipK)` | `RecoveryKeyRecord.MasterKeyEncryptedRecoverKey` **(V)** | yes | setup (today); **deleted at the takeover** |
| `EncMK_bip` | `aes(BipK, MK)` | `RecoveryKeyRecord.RecoveryKeyEncryptedMasterKey` **(V)** | with the phrase | setup (today); **deleted at the takeover** |
| `EncMK_auto` | `aes(AutoK, MK)` | `ShamirKeyRecord.ShamirDistributionKeyEncryptedMasterKey` **(V)** | when enough shards come back | a server-made deal; deleted when the browser deals |
| `EncBipK_auto` (old) | `aes(AutoK, BipK)` | `ShamirKeyRecord.ShamirDistributionKeyEncryptedRecoveryKey` **(V)** | when enough shards come back | deals made before this change; re-dealt at the handover |
| `EncZKMK_smr` | `aes(SmrK, ZKMK)` | `ZkKeyRecord.ShamirKeyEncryptedZkMasterKey` **(V)** | no | each browser deal |
| `Smr-PK-Pub` | as is | `ZkKeyRecord.ShamirPublicKeyJwk` **(V)** | it's public | each browser deal |
| `EncSmr-PK-Priv` | `aes(SmrK, Smr-PK-Priv)` | `ZkKeyRecord.ShamirKeyEncryptedShamirPrivateKey` **(V)** | no | each browser deal |
| `EncPw_smr` | `seal(Smr-PK-Pub, pw)` | `ZkKeyRecord.ShamirPublicKeyEncryptedPassword` **(V)** | no | the next password login after a deal (the console deals without `pw`); password change **(V)** |
| `EncMK_smrRK` | `aes(Smr-RK, MK)` | `ZkKeyRecord.ShamirResetKeyEncryptedMasterKey` **(V)** | only with `Smr-RK` | each browser deal: the browser sends `Smr-RK`, the server locks `MK` with it and keeps nothing else of it **(V)** |
| the dealer package | each shard's `ShardKey`, IV, index and player | an unencrypted, owner-only file on the dealer's Shard Recovery drive (`DealerShardPackage`) **(V)** | yes | each deal |
| a gated friend's shard | a deposit. Content: `aes(FileKey, {id, index, player, aes(ShardKey, piece of SmrK)})`; key header: `seal(WO-PK-Pub_friend, IV ‖ FileKey)`. The holder's checks read plain metadata: the shard id (`UniqueId`) and the recovery email hash (`GroupId`) **(V)** | a file on the friend's Shard Recovery drive, which must be zero knowledge (9020) **(V)** | no: only the friend's browser opens `WO-PK-Priv_friend` | each friend deal |
| an automatic holder's shard (an automatic friend, or an automated identity) | `aes(ShardKey, piece of SmrK)`, unencrypted file **(V)** | a file on the holder's Shard Recovery drive | the holder's server holds it, but releases it only once the owner clicks the link it emailed, sealed to `Tmp-PK-Pub` **(V)** | each deal |
| a server-made deal's shard | `aes(ShardKey, piece of AutoK)`, unencrypted file **(V)** | a file on that identity's Shard Recovery drive | as an automatic holder's | each server-made deal |

The `seal` format (v1, `ZkSeal` in C#, `zkSeal` in js-lib) **(V)**: base64 of
`0x01 ‖ key id (4 bytes, big-endian) ‖ throwaway P-384 public key (97 bytes) ‖ 12-byte IV ‖ AES-GCM
ciphertext and tag`. The AES key is `hkdf-sha256(ECDH(throwaway, recipient), no salt,
"homebase-zk-seal")`, 16 bytes.
- The key id is the IEEE CRC-32 of the recipient's raw public point, so a sealed value says which
  key opens it, and the server can check a value is sealed to the current key. It is computed with
  `CRC32C.CalculateCRC32C`, which despite its name is the IEEE CRC-32 (a test pins `cbf43926`).
- It is not `PublicKeyCrc32`: that hashes BouncyCastle's DER, and the same key has a different DER
  depending on where it came from (441, 464 or 120 bytes), so a browser can't reproduce it.
- C# and the browser share vectors both ways.
- Sealed with it: `EncPw_bip`, `EncPw_smr`, a deposit's `FileKey`, every holder's piece on its way
  back, the recovery bundle, and the status's slot list.

## The flows, step by step

### At every password login
1. The browser proves `pw` and gets a session: the cookie holds one half of `MK-KEK`, the server the
   other; `MK` opens on each request **(V)**.
2. The server makes `SessK` for the session **(V)**.
3. The browser makes `ZKMK-KEK` from `pw` and opens `ZKMK` from `EncZKMK` (or makes `ZKMK` the first
   time). It stores `aes(SessK, ZKMK)` in localStorage **(V)**.
4. If the phrase is taken over and `EncPw_bip` is missing, it writes `seal(Bip-PK-Pub, pw)`, with a
   fresh proof of `pw` **(V)**. The same for `EncPw_smr` after a friend deal, with `Smr-PK-Pub`
   **(V)**.
5. If the server still holds the phrase: the takeover (next).
6. It forgets any `Tmp-PK` left from a recovery **(V)**.

### The takeover (once per owner)
1. **Handover.** With a fresh proof of `pw`, the server first moves any old Shamir deal off the
   phrase: an automated deal on a host with automated recovery is dealt again (`EncBipK_auto` →
   `EncMK_auto`), any other is removed. Then it opens `EncBipK_server` with `MK` and hands `BipK` to
   the browser **(V)**. If moving the deal fails, nothing is handed over **(V)**.
2. The browser makes `EncZKMK_bip`, `EncBipK_zk`, a new `Bip-PK-Pub`/`Bip-PK-Priv`, `EncBip-PK-Priv`,
   `EncPw_bip`, `PhraseCheck` and `Bip-RK` **(V)**.
3. **Takeover.** With another proof of `pw`, it sends all of them. The server checks `PhraseCheck`
   against its own copy, stores them, locks `EncMK_bipRK = aes(Bip-RK, MK)` itself, and deletes
   `EncBipK_server` and `EncMK_bip` **(V)**. From here the server can't open `BipK`.

### Password change
The browser re-locks `ZKMK` under the new `pw` (`EncZKMK`) and sends `seal(Bip-PK-Pub, new pw)` in
the same request **(V)**. `ZKMK`, `BipK` and the phrase copies don't change.

### Showing the phrase again
After the 14-day wait, the server releases `EncBipK_zk`; the browser opens it with `ZKMK` and shows
the words **(V)**.

### Recovery with the phrase (forgot password, no login)
1. The owner types the 12 words; the browser gets `BipK`.
2. It fetches `EncZKMK_bip`, `EncBip-PK-Priv`, `Bip-PK-Pub` and `EncPw_bip` (no login: all useless
   without `BipK`) **(V)**.
3. `BipK` opens `Bip-PK-Priv`, which opens `EncPw_bip`: **the password**, shown only in this browser **(V)**.
4. If there is no `EncPw_bip`, or it doesn't open or doesn't log in: the browser computes `Bip-RK` from
   `BipK`, opens `ZKMK` from `EncZKMK_bip`, and sends `Bip-RK` (encrypted to the host's key), a new
   password, and `ZKMK` locked by it. The server opens `MK` from `EncMK_bipRK`, sets the new
   password, keeps `ZKMK`, and ends every owner-console session **(V)**.
5. If the server answers "needs re-entry" (409), the phrase can't recover: another reset forgot
   its copies (below) **(V)**.

### Reset with today's master key (a server-made deal, or the phrase before the takeover)
The server gets `MK` (from `EncMK_auto`, the secret the recovering browser rebuilt and sent, or from
`EncMK_bip`), sets the new password, and
deletes the whole zero-knowledge record: `ZKMK` is locked by the forgotten password, so it and
everything locked by it are lost **(V)**. The next login makes a new `ZKMK`. The phrase is still
valid on paper, but nothing on the server is locked by it any more: the console asks the owner to
type it once (**re-entry**), which makes the phrase copies again; the next login writes `EncPw_bip`
**(V)**. The deal's copies go with the record: a friend deal can't recover any more, so the next
owner request removes it and the security page shows the red dot **(V)**; an automated deal is dealt
again at the next password login **(V)**.

## ⚠ Changed in the second review: the phrase, not ZKMK, guards the password

This is the part to read slowly.

**Before** (first version, never released):
- `EncPw_bip` was `aes(ZKMK, pw)`.
- `Bip-RK` was `hkdf(ZKMK, …)`.

**The hole, step by step:**
1. Someone learns your password once (phished, shoulder-surfed).
2. They log in. The browser code gives them `ZKMK` (that's how your own browser gets it).
3. You notice and change your password. `ZKMK` doesn't change; only `EncZKMK` is re-locked.
4. They still hold `ZKMK`. With no login at all they could now:
   - fetch `aes(ZKMK, new pw)` from the recovery read and **read your new password**;
   - compute `Bip-RK = hkdf(ZKMK)` and **reset your password**.
5. They never needed the phrase, so the 14-day wait before the phrase is shown didn't apply.

**After** (as built):
- `Bip-RK = hkdf(BipK, …)`: only the phrase gives it.
- `EncPw_bip = seal(Bip-PK-Pub, pw)`: your browser writes it at login without the phrase, using
  `Bip-PK-Pub`; only `Bip-PK-Priv` opens it, and only `BipK` opens `Bip-PK-Priv`.
- Nothing `ZKMK` opens reads `EncPw_bip` or makes `Bip-RK`. A test proves it **(V)**.

**What the same attacker can still do:** write junk as `EncPw_bip` while they know the password.
Recovery then finds junk and falls back to `Bip-RK`, which they can't make. We decided against a check
value or rewriting the copy at every login: whoever writes junk knows the password, so a check
can't tell junk from good, and a check bound to `ZKMK` would let them test guesses at your new
password.

**What still never changes:** `ZKMK`, like `MK` today. Using it needs a session, which needs the
current password.

## Recovery with friends, and the automated deal

Plan and as-built notes: [zero-knowledge-recovery-plan.md, Building the friends pair](zero-knowledge-recovery-plan.md#building-the-friends-pair)
and [Building the automated deal](zero-knowledge-recovery-plan.md#building-the-automated-deal).

The automated deal is a deal like a friend deal, made by the same browser code, to the host's
automated identities, all automatic: it gives back the password too. Every automatic holder, an
automated identity or an automatic friend, releases its shard only once the owner clicks a link it
emailed them itself. So the automated deal trusts the owner's inbox: that is its point, for owners
who want only a password reset. A deal with a gated friend never does.

**The simple model, and the one step it needs.** The friends' shards rebuild a random 128-bit `SmrK`,
and `SmrK` gives back the password. Recovery works with just `aes(SmrK, pw)`. A password change breaks
it: the copy then holds the old password, and to store the new one the browser needs `SmrK`, which
nobody holds (only the friends' shards together make it).

So `SmrK` opens a key pair, as `BipK` does:
- `SmrK` opens `Smr-PK-Priv`, and `Smr-PK-Priv` opens `EncPw_smr`: **the password**.
- `Smr-PK-Pub` is stored, so a password change seals the new password with it, without `SmrK` and
  without touching the friends.
- Recovery is still "shards → `SmrK` → password".

Rejected:
- **Keeping `SmrK` locked by `ZKMK`**, so a password change can re-lock the copy. That is the hole
  from the second review: anyone who once knew the password opens `ZKMK`, then `SmrK`, then every
  later password.
- **Dealing again at every password change** (today's server rotation, moved to the browser). A
  password change would have to reach every friend, and while one can't be reached, the deal keeps
  the old password. A friend deal is never rotated **(V)**.

**A junk copy.** Anyone who knows the current password can seal junk as `EncPw_smr`, as with
`EncPw_bip`. `Smr-RK` repairs it, and `EncZKMK_smr` keeps `ZKMK` across that reset: the same steps
as phrase recovery, step 4.

### Dealing to friends
1. The owner console's browser makes `SmrK`, `Smr-PK` and `Smr-RK`, and the copies above **(V)**.
2. It splits `SmrK` into one piece per friend and locks each piece with its own `ShardKey`, as
   `CreateShards` does for a server-made deal **(V)**.
3. **Gated friend** (must approve): the browser fetches the friend's Shard Recovery `WO-PK-Pub` and
   makes the shard a deposit sealed to it. The server refuses a friend whose drive isn't zero
   knowledge (9020), or a shard not sealed to their current key **(V)**.
   **Automatic friend:** the shard keeps today's form; their server releases it once the owner
   clicks the link it emails.
4. Fewer automatic friends than the threshold, and a threshold of at least 2, so the owner's inbox
   alone can never rebuild `SmrK`; at least one gated friend must approve. The browser and the
   server both refuse otherwise (9021), except for the automated deal: its players exactly the
   host's automated identities, all automatic **(V)**.
5. The browser posts the deal: the shards, the dealer package, the copies and `Smr-RK`. The server
   stores the dealer package, locks `EncMK_smrRK`, deletes a server-made deal's `EncMK_auto`, and
   sends the shards, as today **(V)**.
6. The next password login writes `EncPw_smr` **(V)**.

The automated deal is dealt the same way, at first run, from the security page, or by a password
login that finds the server-made one (`DealNeedsDealing`), which also writes `EncPw_smr` at once
**(V)**. A recovery email change deals the deal again the same way: each shard hashed the address
(trimmed, lower case; a server-made deal hashed it as stored), so the verify link sends the owner to
the login, which re-deals to the same holders and writes `EncPw_smr` **(V)**. Not flagged: a deal
whose package records no hash (from before), and an automated deal on a host without automated
identities, which no login could deal again **(V)**.

### Password change (with the friends pair)
As above, plus `seal(Smr-PK-Pub, new pw)` in the same request. Nothing is dealt again **(V)**.

### Recovery (forgot password, no login)
1. The recovering browser makes `Tmp-PK` and starts recovery with `Tmp-PK-Pub`. The server keeps it
   with the email's one-time link **(V)**.
2. The email link only opens the recovery page, which posts the link with `Tmp-PK-Pub`. Another key
   is refused (9022) before the link is used up **(V)**. Why: anyone can start recovery, and without
   this check the friends would seal their shards to a stranger's key.
3. The dealer's server asks each holder, with `Tmp-PK-Pub`. The start email already said whom to ask
   and who will email a link **(V)**.
   - **An automatic holder** checks the recovery address the dealer sends against the hash it stored
     at the deal, and emails it a link with a code (only the code's hash is kept; seven days, until a
     newer email replaces it; at most one email per five minutes). The click, on any device, posts the
     code to the dealer's server, which passes it on; the holder seals `aes(ShardKey, piece)` to
     `Tmp-PK-Pub` and releases it. A click doesn't use the link up, and the dealer keeps one copy of
     each shard however often it comes **(V)**.
   - **A gated friend** approves in their console: their browser opens `ZKMK`, then `DrvK`, then
     `WO-PK-Priv_friend`, then `FileKey`, then the content, and seals `aes(ShardKey, piece)` to
     `Tmp-PK-Pub`.
   - Either way the holder learns nothing: the piece stays locked by `ShardKey` **(V)**.
4. The dealer's server takes a shard only from its own player, and only if it is sealed to
   `Tmp-PK-Pub` **(V)**. While shards come in, the status shows the counts, and the holders with
   which are in, sealed to `Tmp-PK-Pub` for the browser that started **(V)**.
5. At the threshold it sends one email without a link: "go back to the browser where you started"
   **(V)**.
6. That browser fetches the recovery bundle, sealed to `Tmp-PK-Pub`: the dealer package's
   envelopes, the collected shards and `Smr-PK-Pub`, `EncSmr-PK-Priv`, `EncZKMK_smr` and `EncPw_smr`.
   It opens every piece with `Tmp-PK-Priv` and then its `ShardKey`, and rebuilds `SmrK` with the
   dealer's indexes. If one piece spoils the rebuild it tries subsets. Then `SmrK` opens
   `Smr-PK-Priv`, which opens `EncPw_smr`: **the password**, shown only in this browser **(V)**.
7. If `EncPw_smr` is missing or junk: `Smr-RK` and a new password, as phrase recovery step 4. The
   same request ends recovery mode **(V)**.
8. **A server-made deal** (not yet dealt again): the bundle has no copies, and the browser rebuilds
   `AutoK` with the dealer's envelope indexes (the server shifts old 0-based positions to the piece's
   x first) and sends it with a new password. That is a reset with today's master key (above) **(V)**.

## Who can do what

| Attacker | Can | Can't |
|---|---|---|
| A stolen session (no password) | what the owner console can do until it expires or a reset ends it | the handover, the takeover, write `EncPw_bip`: each needs a fresh proof of `pw` **(V)** |
| Someone who knew the password once | log in until it changes; write junk `EncPw_bip` meanwhile; leave app tokens, a Shamir deal or a recovery email behind (#2005) | read a later password; reset without the phrase; read the phrase before the 14-day wait (except the one-time handover window, accepted) |
| Someone with the phrase | recover the password; reset it | nothing less: the phrase is the master recovery secret |
| Anyone, starting a recovery | make the owner get a recovery email | get shards sealed to their own key: the link checks the browser's key before it is used up **(V)** |
| A gated friend | approve or not | read their piece: it stays locked by `ShardKey` **(V)** |
| Someone with the owner's inbox | with the automated deal, recover the account and its zero-knowledge data, by design | with a gated friend in the deal, rebuild `SmrK`: the automatic shards are below the threshold **(V)** |
| The dealer's server taken over, or a copy of its data and peer credentials | ask the holders; read the dealer package | get an automatic shard without the link the holder emails the owner **(V)**. (Running the server live, they could serve the console code that reads `pw` at the next login: a limit of any web app.) |
| A database copy | `PwHash` (offline guessing at pbkdf2 cost, as today); every locked value | open any zero-knowledge value without `pw` or `BipK`; open `MK` without a session's MK-KEK |
| The server operator, live | today's `MK` during a session; `BipK` until the takeover (accepted, like converting a drive with its existing key); `Bip-RK` and `Smr-RK` when they are set, which reset the password but open nothing zero knowledge | `ZKMK`, `BipK` after the takeover, `SmrK`, `pw` |
