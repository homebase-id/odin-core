# Zero-knowledge keys in the database and the browser: plan A

**Status:** built, in review: odin-core #1998, odin-js #988 and Odin-SQLite-Generator #38
(2026-10-23). [As built](#as-built-2026-10-23) lists where the build differs from the plan below.
Half A of steps 1 and 2 in the
[order of work](zero-knowledge-drives.md#order-of-work). Half B, Shamir in the browser, is kept as
a reference in [zero-knowledge-recovery-plan.md](zero-knowledge-recovery-plan.md). It is there so
A gets B's foundations right; it is not being built now.

Markers: **(V)** verified by reading the code (2026-10-22). Line counts and estimates are
inferred.

## As built (2026-10-23)

Built on branches `zk-keys-plan-a` (odin-core, odin-js) and `zk-drive-columns`
(Odin-SQLite-Generator). Where it differs from the plan below:

- **The keys have their own record** (`ZeroKnowledgeKeyService`, KeyValue), not new fields in
  `PasswordData`. The browser makes the salt itself and reads it back after login from
  `GET security/zk`, so the nonce and salt endpoints are unchanged. (Correction to the plan:
  `getsalts` makes new salts; only `nonce` returns the stored ones **(V)**.)
- **Keys are made at the first login**, not at first run: the first-run page logs in right after
  setting the password (`FirstRun.tsx`) **(V)**. The login keys come with them, so there is no
  separate endpoint for them.
- **A password change must carry the re-locked keys** once they exist (`ZkKeys` on
  `resetpasswd`); the password and the keys are saved in one transaction.
- **A reset with the recovery key forgets the keys**: they are locked by the forgotten password.
  The browser makes new ones at the next login and converts the drive again; it reconverts
  whenever the drive key no longer opens with the current master key.
- **Old grant copies of the drive key are left alone.** Conversion replaces the key, so the old
  one locks nothing, and the server refuses it from then on: the drive id is encrypted with the
  new key. The server never encrypts for a zero-knowledge drive at all: an encrypted upload is
  refused with `DriveIsZeroKnowledge`, and a test proves it.
- **The login keys copy was replaced by the password copy** in plan B (the password, locked by the ZK
  master key): recovery gives back the password itself.
- **Shard Recovery is the first Private drive.** It starts Collaborative like every drive, and
  conversion makes it Private. Conversion drops the master-key copy, stores the browser's locked
  keys and the new write-only pair, and resets the whole `OdinContextCache` after commit.
- **The check value becomes the drive id.** The browser encrypts the drive id, but
  `AssertValidStorageKey` compares with `StorageKeyCheckValue`, which on drives older than the
  alias migration is not the drive id. Conversion sets it to the drive id.
- **No drive is saved without a copy of its key**: a drive is zero knowledge because it has no
  master-key copy, so `DriveManager` refuses to save one with neither copy.
- **Grants never escrow a zero-knowledge drive's key** (`ExchangeGrantService.CreateDriveGrant`).
- **A drive is Private only once it is converted.** The migration leaves every existing drive
  Collaborative, every new drive is created Collaborative, and only conversion sets Private.
  `DriveManager` refuses to save a Private drive that still has a master-key copy, so no drive
  can claim zero knowledge it doesn't have. A client may trust the label. (The first draft
  labelled Vault, Chat and others Private while the server still held their keys.)
- **The write-only pair**: the public half is the server's usual record, built from the browser's
  JWK; the private half (PKCS8, locked by the drive key) is in a third new column,
  `ZkStorageKeyEncryptedWriteOnlyPrivateKey`.
- **B's check 2 holds**: a dealer has Write on a holder's Shard Recovery drive through the
  `shard_recovery_drive_writer` grant (`CircleNetworkService`) **(V)**, which is what
  `GetDriveWriteOnlyPublicKey` asks for.
- **One shared vector** pins the formats: odin-core's `ZeroKnowledgeKeysTests.TheSharedVectorOpens`
  and js-lib's `ZeroKnowledgeKeys.test.ts` open the same value, made with Python's hashlib and
  cryptography.
- In passing, the three owner endpoints that build a drive response now share one factory
  (`OwnerClientDriveData.From`); the two by-type endpoints now also return `DriveId`.

## What A delivers

Nothing a user sees. After A, every owner who logs in has, without noticing:

- A **ZK master key**, made in the browser and locked by a second password key. The server
  stores only the locked copy.
- Their **login keys** (password hash and KEK) saved, locked by the ZK master key.
- A **zero-knowledge Shard Recovery drive**: a new drive key locked by the ZK master key, and a
  new write-only key pair. The server's old copies are gone.

The database has the drive columns `SharingModel` and `ZkMasterKeyEncryptedStorageKey`, with
`SharingModel` filled in for existing drives.

**Why A first:** the login keys and the conversion need the password, so they can only happen at
a login. Shipping A first lets owners convert quietly, so most Shamir holders are ready by the
time B ships.

**Why A is low risk:** nothing is locked by the ZK master key except the Shard Recovery drive
key, and that drive has no files encrypted with its drive key. Every file Shamir writes there is
unencrypted at the drive level (`IsEncrypted = false` in `ShamirConfigurationService`,
`PlayerShardCollector`, `ShardRequestApprovalCollector`) **(V)**. A bug in A loses nothing; the
drive can be converted again.

## Decisions

1. **The password is asked for at login and password change only.** In A the browser needs the
   ZK master key only during login, when it has the password anyway. Keeping it for the rest of
   the session (the ZK session key) is part of B.
2. **Shard Recovery is the only drive A converts.** It is Private in the migration table.
3. **Conversion replaces the keys; it doesn't re-lock them.** The server made the drive key and
   the write-only key pair (`DriveManager.EnsureWriteOnlyKeyPairAsync`,
   `DriveWriteOnlyKey.CreateKeyPair`) **(V)**, so it has seen both. The browser makes new ones.
   No MK last look is needed.
4. **Conversion refuses if the drive holds any file encrypted with the drive key.** That keeps
   "nothing to lose" true, and allows converting again after a bug.

## SQL generator

**Synced (2026-10-22).** `Odin-SQLite-Generator` is pulled to `2e9aabf` ("added circle slug",
after "marked drive items not null"). Every table's `migrationVersion` now matches odin-core's
newest migration, including Drives (`202610130807`) and Circle (`202610140701`) **(V)**. The local
checkout keeps one uncommitted edit: a comment on the Registrations `disabled` column. The
`ClientRegistrations` version edit it used to carry is now upstream.

**Running it leaves odin-core unchanged (2026-10-22) (V).** Run against a copy of odin-core
`origin/main` (`35031efe3`) via `ODIN_ROOT`: it rewrote 45 files there, all byte-identical. It
also writes into `homebase-social-sync`, where it changes 5 generated files (header comment,
usings, CRUD layout; 139+/429− against its `origin/main`). That repo isn't part of this work, so
run the generator with `ODIN_ROOT` pointing at a folder where only odin-core matters, or revert
social-sync after a run.

**Change (one generator commit):**
- In `Drives()` in `Program.cs`, add:
  - `ZkMasterKeyEncryptedStorageKey`: String, nullable. The browser's locked value, not JSON.
  - `SharingModel`: Int32 with a default, like Circle's `GrantOn`.
- Change `MasterKeyEncryptedStorageKeyJson` to `notNull = false`. There is no precedent for making
  a NOT NULL column nullable; only the other direction has been done.
- Bump `migrationVersion`. The generator writes a first migration file and the full CRUD. The
  table is rebuilt on both SQLite and Postgres.

**Estimate:** ~30–60 lines in `Program.cs`. Reference: the WriteOnlyKeyPair generator commit
598bc72 (267+/4−, bundled with other tables).

## odin-core

### Drive columns and migration

- The generated migration, hand-edited for the new columns.
- No `SharingModel` backfill: every existing drive stays Collaborative until it is converted (see
  "As built").
- `StorageDrive` and `DriveManager` carry both fields. Drive reads return `SharingModel`, and to
  the owner, the ZK-locked drive key.
- **Null guards: the biggest odin-core item in A.** Today the code assumes every drive has an MK
  copy of its key. It reads `MasterKeyEncryptedStorageKey` 12 times in 5 files **(V)**:
  `StorageDrive.cs`, `DriveManager.cs`, `StorageDriveComparer.cs`, `StorageKeySource.cs`, and
  `OwnerAuthenticationService.cs`. The last one builds the owner's permissions by opening every
  drive key with MK. Each place must skip a drive with no MK copy and not fail.

### Password data

- `PasswordData.cs`: `ZkSaltKek` and `ZkKekEncryptedZkMasterKey` (JSON in KeyValue storage via
  `OwnerSecretService.cs`) **(V)**. No SQL.
- `OwnerSecretService.cs`:
  - Return `ZkSaltKek` with the salts the browser already fetches: `GET authentication/nonce` and
    `GET authentication/getsalts` (`GenerateAuthenticationNonceAsync`, `GetStoredSaltsAsync`,
    `GenerateNewSaltsAsync`) **(V)**.
  - Accept `ZkKekEncryptedZkMasterKey` at first-run password set (`SetNewPasswordAsync`) and at
    password change (`ResetPasswordAsync`, route `POST security/resetpasswd`) **(V)**.
- Login keys: store `ZkMasterKeyEncryptedLoginKeys` (KeyValue). Accept it at login when missing,
  and at password change.

### Endpoints (owner only)

- After login: return the locked ZK master key and whether the login keys and the Shard Recovery
  conversion exist. Either in the login response or in one GET. Never before login.
- Save the ZK master key and the login keys, for owners who don't have them yet.
- Convert the Shard Recovery drive:
  - Take the ZK-locked drive key and the new write-only key pair.
  - Refuse if any file on the drive is encrypted with the drive key (decision 4).
  - Delete the MK copy. Grant copies of the old key are left alone (see As built). Write grants
    carry no key (`ExchangeGrantService.CreateDriveGrant`) **(V)**, so the Recovery circle is
    unaffected.
  - Reset the `OdinContextCache`, whose owner contexts hold the old key.

### Tests

- First run, an existing owner's first login, and password change keeping the ZK master key.
- The conversion: the old key copies are gone, it refuses when an encrypted file exists, and
  today's server-dealt shards still arrive on a converted drive. The incoming path for
  unencrypted files needs no drive key (`PeerDriveIncomingTransferService`) **(V)**; the test
  proves it.
- The migration and backfill on SQLite and Postgres.
- Owner login and drive listing with a converted drive, which exercises the null guards.
- Base the tests on `ResetPasswordTests` and `OwnerPasswordFlow.cs`, the C# copy of the browser's
  login maths **(V)**. Extend that helper to make the ZK keys the same way the browser does, so the
  tests check the formats (see [What B needs from A](#what-b-needs-from-a)).

## odin-js

- js-lib:
  - Move `pbkdf2` out of the owner app (`AuthenticationHelper.ts`) and export it.
  - Add a generic AES-GCM encrypt. js-lib has only a GCM decrypt today, plus an ECC-only GCM
    encrypt **(V)**.
  - Make the write-only key pair and export it as JWK (see formats below).
- Owner app login (`useAuth.ts`, `AuthenticationProvider.ts`):
  - Work out the second password key with the same pbkdf2 and iterations (100000) as today's
    KEK **(V)**.
  - The first time: make the ZK master key and save it locked; save the login keys; convert
    the Shard Recovery drive.
- First-run password (`FirstRun.tsx`) and change password (`ChangePasswordTab.tsx`): lock the ZK
  master key again with the new second password key, and save the new login keys.
- Tests: vitest in js-lib, with vectors produced by the C# helper. The owner app has no tests
  **(V)**.

## Formats to pin down in A

Decide these before coding, because B reads them and they are stored for every owner.

| Blob | Who makes it | Who opens it | Proposal |
|---|---|---|---|
| `ZkKekEncryptedZkMasterKey` | browser | browser | AES-GCM, random IV, 16-byte key like today's master key |
| `ZkMasterKeyEncryptedLoginKeys` | browser | browser (B: Shamir setup) | AES-GCM; JSON with `hpwd64` and `kek64` as login sends them |
| `ZkMasterKeyEncryptedStorageKey` | browser | browser, then sealed to apps | AES-GCM, 16-byte drive key like today's |
| Write-only key pair | browser | server reads the public half; browsers open the private half (B) | public: JWK as today; private: PKCS8 locked with AES-GCM |

The write-only key pair reuses the formats the browser and the server already share; nothing
new:

- Public half: the JWK the browser already exports (`exportEccPublicKey` in js-lib) **(V)**. The
  server reads it with `EccPublicKeyData.FromJwkPublicKey` and serves it back as a JWK
  (`PublicKeyJwk()` in `GetDriveWriteOnlyPublicKeyAsync`) **(V)**.
- Private half: PKCS8, as the browser already exports private ECC keys (`saveEccKey`) **(V)**,
  locked by the drive key with AES-GCM. Only browsers open it (B). To decide: where it is stored,
  either inside the existing `WriteOnlyKeyPair` record or in a field of its own.

## What B needs from A

Check each before A merges. B's plan is the reference.

1. The login keys are always current: saved at first run, at first login and at every password
   change. B locks them with the Shamir secret.
2. The Shard Recovery write-only public key is served to a dealer. `GetDriveWriteOnlyPublicKey`
   exists **(V)**, but I haven't checked whether a Recovery circle member may call it for this
   drive. If not, A fixes the permission.
3. The browser can open the write-only private half with the ZK master key: ZK master key → drive
   key → private half. A's tests open it with the C# helper.
4. Readiness can tell a converted drive (`SharingModel` Private with a ZK-locked key). B uses it to
   mark holders ready.
5. Today's Shamir keeps working on converted drives: a test in A.
6. The ZK master key can be opened at login in the browser. B adds keeping it for the session.

## Size estimate

Inferred. Lines include tests.

| Part | Estimate | Reference |
|---|---|---|
| SQL generator: drive columns | ~30–60 | 598bc72: 267+/4−, bundled |
| odin-core: drive columns, migration, backfill, null guards, tests | ~700–1,000, about half generated | WriteOnlyKeyPair drive part: 18 files, 802+; NOT NULL plus backfill 5e72bf33b: 871+/980− |
| odin-core: password data, login keys, endpoints, conversion, tests | ~700–1,100 | small services; most of it is endpoints and tests |
| odin-js | ~600–900 | login and password screens, js-lib crypto, key pair, tests |

A in total: about 1,400–2,100 lines in odin-core (a quarter of it generated), 30–60 in the
generator and 600–900 in odin-js. The generator catch-up is no longer needed: it's synced.

## Order and PRs

1. The generator commit (drive columns) and the odin-core PR, together. odin-core's PR is
   additive: today's owner console keeps working against it.
2. The odin-js PR, after odin-core is merged. Following the no-stacked-PRs rule, each PR targets
   main.

## Risks

- **Null guards missed somewhere:** an owner request fails on a converted drive. Mitigation: the
  login and drive listing tests with a converted drive, and the 12 known reads listed above.
- **A format chosen wrong:** every owner who logs in writes it, and B depends on it. Mitigation:
  the format table above is decided first, and checked by C# vectors.
- **Making a NOT NULL column nullable** has no precedent in the generator.
- **odin-js installs:** `npm install` needs GitHub Packages auth for `@homebase-id/ffmpeg`
  (README) **(V)**. A adds no npm dependency.

## Out of scope for A

Everything in B (Shamir in the browser, the ZK session key), converting any other drive,
deposits, Chat, the recovery phrase, and Selective Share.
