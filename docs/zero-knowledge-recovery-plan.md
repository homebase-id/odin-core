# Shamir in the browser: plan B (reference)

**Status:** reference, not being built yet. Half B of steps 1 and 2 in the
[order of work](zero-knowledge-drives.md#order-of-work). Half A, the keys and the database, is
the current plan: [zero-knowledge-keys-plan.md](zero-knowledge-keys-plan.md). This half is kept so
A gets B's foundations right.

Markers: **(V)** verified by reading the code (2026-10-22). Line counts and estimates are
inferred.

## What we end up with

- Shamir is dealt and rebuilt in the browser. Servers only ever hold sealed shards.
- Recovery gives back the login keys, and the browser logs in as usual.

## What B takes from A

- The ZK master key, opened by the browser at login.
- The login keys, locked by the ZK master key and kept current.
- Each holder's zero-knowledge Shard Recovery drive, with its write-only key pair.

## Decisions first

1. **Existing Shamir setups.** Recommended: the old server-dealt setup keeps working until the
   owner deals again in the browser. The console asks for it after login. Delete the old path
   later.
2. **Automated recovery** (Homebase-run holder identities, no human approval;
   `ConfigureAutomatedRecovery`, called from `TenantConfigService.cs`) **(V)**. Recommended for
   this step: leave it as today, labelled clearly. It restores today's master key only, not
   zero-knowledge data. The alternative, browser dealing to automated holders, means Homebase
   can recover everything by design.
3. **The password is asked for at login and password change only, as today.** B adds keeping
   the ZK master key for the owner session (the ZK session key), so Shamir setup, approving a
   shard and app consent never ask again.
   - Why not ask each time: browsers prefill the password, so a prompt protects almost nothing,
     and every extra owner console step hurts the experience.
   - How it's kept: in browser storage, locked by a random session key. The server keeps that
     session key with the owner session, the way the owner token already splits today's master
     key between the cookie's half key and the server's token record **(V)**
     (`OwnerAuthenticationService.cs`, `AccessTokenHalfKey`). The server never sees the locked
     copy, and the browser copy is useless after logout or session expiry.
   - Login keys (password hash and KEK) are stored locked by the ZK master key (A), so Shamir
     setup doesn't need the password either.
   - The limit: malware on the machine, or JS served by a bad server, can read it during the
     session. Both can read a prefilled or typed password too.
   - Work: odin-core makes the session key at login, keeps it with the server token, returns it
     to the logged-in owner and drops it at logout and expiry. odin-js keeps the ZK master key
     locked by it and clears it at logout.
4. **Shards are locked to the holder's Shard Recovery write-only key,** which A makes zero
   knowledge. No extra key pair.
5. **An encrypted file on a zero-knowledge drive is always `IsEncrypted = true`** (decided
   2026-10-23). Nothing encrypted is stored as `IsEncrypted = false` with ciphertext in its
   content; that would be a second format, to be upgraded later or kept forever beside the real
   one.
   - So a sealed shard is the first **deposit**: the content is encrypted with the file's own key
     (the KeyHeader, as on any encrypted file), and that file key is sealed to a public key. When
     dealing it's the holder's Shard Recovery write-only key; when a holder releases a shard,
     it's the recovering browser's throwaway key.
   - Chat, Mail and Feed deposits later reuse this format, so it is designed for them too: the
     sealed file key must say which public key opens it.
   - The server never re-locks such a file key. On a zero-knowledge drive it stores the file
     key as received and returns it as stored. That pulls a piece of step 4 into B (see
     [odin-core](#odin-core)).
   - Today's cleartext shards (`IsEncrypted = false`, locked inside by the dealer's per-shard
     keys) are not upgraded: browser dealing replaces them, and B2 removes the old path.
## Shamir in the browser

### Today's two kinds of shards

All verified by reading the code (`ShamirConfigurationService`, `ShamirRecoveryService`).

- **Both kinds are made the same way.** The dealer's server splits the secret and locks each
  shard with a random per-shard key (`CreateShards`). The holder gets only the locked shard
  (`PlayerEncryptedShard`). The dealer keeps the per-shard keys in its dealer package
  (`DealerShardEnvelope`), a file on its own Shard Recovery drive that isn't encrypted.
- **Automated holders** (`PlayerType.Automatic`): on `request-shard`, the holder's server returns
  the locked shard at once. No person is involved.
- **Friend holders** (`PlayerType.Delegate`): on `request-shard`, the holder's server records an
  approval request and returns "waiting". The friend logs in to the owner console and approves
  (`ApproveShardRequest`). The friend's server then sends the shard to the dealer, still locked
  (`accept-player-shard`).
- **The friend's login is an approval, not a key.** `ApproveShardRequest` checks that the caller
  is the owner (`AssertCallerIsOwner`) and never opens anything. The friend's server can read
  and send the locked shard without the friend.
- **The dealer's server opens every returned shard itself** with its stored per-shard keys
  (`PrepareFinalKeys`). So whoever runs the dealer's server, plus enough returned shards, has the
  recovery key.
- **The holder stores the shard as received, not under its drive key.** An unencrypted incoming
  file is written as is (`PeerDriveIncomingTransferService`, `IsEncrypted == false` branch).
- **So today's shards can be harvested from storage.** The per-shard keys sit in the dealer's
  unencrypted package, and the locked shards sit in the holders' unencrypted files. With the
  database or a backup of a hosting that holds the dealer and enough holders, anyone can rebuild
  the secret, open the recovery key (`ShamirDistributionKeyEncryptedRecoveryKey`) and so the
  master key. No login is needed. The intended design was for the holder's drive key to lock the
  shard; the code doesn't do that.

**What changes for friend shards:** the dealer's browser seals each shard to the friend's Shard
Recovery write-only public key, as a deposit (decision 5). Only the friend's browser can open it,
so the friend's approval becomes a real key step. The dealer keeps no per-shard keys.

**Automated shards:** unchanged (decision 2). They never use the holder's drive key, so
converting an automated holder's Shard Recovery drive wouldn't break them.

### Dealing

1. The owner opens the Shamir setup. The browser opens the stored login keys with the ZK master
   key it already holds: password hash, KEK and second password key. They're the values login
   already sends (`{hpwd64, kek64}` in `prepareAuthPassword`) **(V)**, plus the second password
   key.
2. The browser makes a random Shamir secret and locks the login keys with it. It also keeps a
   copy of the secret locked by the ZK master key, for password changes.
3. The browser splits the secret, fetches each holder's Shard Recovery write-only public key,
   and seals one shard to each: an `IsEncrypted = true` file whose file key is sealed to that
   public key (decision 5).
4. The server stores the two locked blobs and sends each sealed shard to its holder on the
   existing path: a file on the holder's Shard Recovery drive (`transferService.SendFile`)
   **(V)**.

### Recovering

1. The recovering browser makes a throwaway key pair, keeps the private key in browser storage,
   and starts recovery with the public key. The email check stays as today.
2. Each holder's server records the request with that public key. The holder approves in their
   owner console (no password prompt, decision 3). The holder's browser opens its Shard Recovery
   drive key with the ZK master key, then the write-only private key, then the shard, and seals
   it to the throwaway public key.
3. The recovering server collects the sealed shards. It can't open them.
4. The recovering browser fetches the sealed shards and the locked login keys. Both are useless
   without the throwaway private key, so this needs no extra gate beyond recovery mode. The
   browser rebuilds the secret and opens the login keys.
5. The browser logs in with the login keys, using the normal login call: the reply is built from
   `hpwd64` and `kek64`, not the password **(V)**. Then it asks for a new password and uses the
   normal password change.

### Password change

The browser locks the new login keys again with the Shamir secret, which it opens with the ZK
master key. No new shards are dealt. Today's server re-deals after every password change
(`RotateShardKeysIfNeeded`, run by `OwnerAuthenticationHandler.cs` on every owner request when
the password is newer than the shards) **(V)**. That trigger goes away for browser-dealt setups.

### odin-core

- `ShamirConfigurationService.cs` (654 lines):
  - `configure-shards` accepts the browser's sealed shards and the two locked blobs instead of
    dealing.
  - Removed for browser-dealt setups: making the distribution key, `CreateShards` (per-shard keys
    kept unencrypted on the dealer's drive: `DealerShardEnvelope`, file `IsEncrypted = false`)
    **(V)**, and rotation.
  - The sending plumbing stays.
- `ShamirRecoveryService.cs` (516 lines):
  - `EnterRecoveryMode` carries the throwaway public key to the holders.
  - The holder-side release takes the browser-sealed shard instead of releasing its own copy.
  - Removed for browser-dealt setups: `PrepareFinalKeys`, the "We have assembled your recovery
    key" email, and `FinalizeRecovery`. A new read returns the sealed shards and the locked login
    keys.
- Deposits on zero-knowledge drives (decision 5). The server stores and returns the sealed
  file key as is, with no re-locking, wherever a shard passes:
  - incoming peer files: the `IsEncrypted` branch needs the drive key today
    (`PeerDriveIncomingTransferService.cs`, `hasStorageKey`) **(V)**;
  - reads: building the reader's header re-locks the file key for the caller
    (`DriveFileUtility.CreateClientFileHeader`) **(V)**;
  - queries hide encrypted files from a caller without the drive key
    (`DriveQueryServiceBase.cs`, `isEncrypted && hasStorageKey`) **(V)**; on a zero-knowledge
    drive no caller has it on the server, so the drive permission decides instead;
  - and plan A's refusal of encrypted uploads (`DriveIsZeroKnowledge`) becomes "refused unless
    the file key is sealed".
- Peer controller (`ShamirPasswordRecoveryPeerController.cs`: `request-shard`,
  `accept-player-shard`) **(V)**: request and response carry the public key and sealed shards.
- Readiness and health (`ShamirReadinessCheckerService.cs`, `OwnerSecurityHealthService.cs`,
  `SecurityHealthCheckJob.cs`) **(V)**: a holder is ready when its Shard Recovery drive is zero
  knowledge. Shard checks move to "is the sealed file there".
- Old setups (decision 1): a flag on the dealer record picks the path.
- Tests: the Shamir suite (`tests/apps/Odin.Hosting.Tests.V2/Ported/Shamir/`, fixture 453
  lines, about 18 tests) **(V)** needs a C# "browser" helper that deals, seals, opens and
  rebuilds, like `OwnerPasswordFlow.cs` does for login.

### odin-js

- js-lib:
  - Shamir split and rebuild, ported from `ShamirSecretSharing.cs` (140 lines: a 257-bit prime,
    BouncyCastle `BigInteger`) **(V)** to JS `BigInt`.
  - Seal and open to a public key: a throwaway P-384 key pair, ECDH plus salt, AES-GCM. The
    building blocks exist (`createEccPair`, `getEccSharedSecret`) **(V)**; an "open" helper
    doesn't.
  - Vitest vectors produced by the C# code.
- Shamir setup (`ShamirDistributionDialog.tsx`, `ShamirProvider.ts`): open the login keys,
  deal, seal, post.
- Approving (`ApproveAndReleaseShardsTabs.tsx`): open, seal, post.
- Recovery page (`ShamirAccountRecovery.tsx`, 314 lines; `SecurityRecoveryProvider.ts`):
  - Make and keep the throwaway key pair.
  - Rebuild the secret, log in with the login keys, set a new password.
  - `prepareAuthPassword` learns to take `hpwd64` and `kek64` directly.
- Change password: lock the login keys again.

## Size estimate

Inferred. Lines include tests.

| Part | Estimate | Reference |
|---|---|---|
| odin-core | ~1,950–3,000 | about the size of the two WriteOnlyKeyPair commits together (1,915+ and 1,158+), plus the session key |
| odin-js | ~1,000–1,500 | Shamir port, seal/open, three screens, session key |
| SQL generator | none | |

## Risks

- **Test rewrite is the biggest odin-core item.** The Shamir tests assume the server deals.
- **Two Shamir paths at once** (decision 1) doubles that suite until the old path is deleted.
- **Recovery must finish in the same browser** that started it, because that browser holds the
  throwaway private key.
- **Holders need a converted Shard Recovery drive,** so they must have logged in once since A
  shipped. Readiness shows "not ready" until then.
- **odin-js installs:** `npm install` needs GitHub Packages auth for `@homebase-id/ffmpeg`
  (README) **(V)**. The plan adds no npm dependency: Shamir is ported, and WebCrypto covers the
  rest.

## Out of scope here

Everything in A, converting any drive besides Shard Recovery, deposits other than shards (the
format is set here, decision 5), Chat, the recovery
phrase (step 3, same pattern as Shamir), and Selective Share.
