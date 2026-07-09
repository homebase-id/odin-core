# Class Structure: Allow apps to add OdinIds to a circle

> Companion to [app-circle-membership-plan.md](app-circle-membership-plan.md). A proposed class
> structure implementing the plan — the rename vocabulary, the two policy blockers (#1/#2), the
> storage-key sourcing fix (#4), and the write-only deposit candidate for #3. All grant-shaped
> classes are clones with the new names (nothing existing is reused); only crypto/value primitives
> (`SensitiveByteArray`, `SymmetricKeyEncryptedAes`, `EccPublicKeyData`, `PermissionSet`,
> `PermissionedDrive`, `OdinId`) are referenced as-is.

## Design in one picture

```
                     ┌─ policy (#1/#2) ─────────────────────────────┐
 app request ──────► │ CircleMembershipManager                      │
                     │  gate: master key OR ManageCircleMembership  │
                     └───────────────┬──────────────────────────────┘
                                     │ mint (#4)                 seal (#3)
                     ┌───────────────▼─────────────┐   ┌─────────▼──────────────────┐
                     │ CircleGrantMinter           │   │ IPeerKeyStoreWriter        │
                     │  + IStorageKeySource        │   │  ├ PeerKeySealer (owner /  │
                     │    ├ MasterKeySource        │   │  │   peer-accept: has key) │
                     │    └ AppKeySource           │   │  └ PublicKeyDepositor      │
                     │  (throws on unreachable key)│   │      (app: writes deposit) │
                     └─────────────────────────────┘   └────────────────────────────┘
                                                                    │ later, Peer Key in scope
                                                       ┌────────────▼───────────────┐
                                                       │ DepositedGrantConverter    │
                                                       └────────────────────────────┘
```

The core idea: **minting** a grant needs drive storage keys (#4 — source them from whatever hub
key the caller can actually reach), and **sealing** it into the peer's store needs a path that
isn't the symmetric Peer Key (#3 — an ECIES deposit to the store's public key). Splitting those
two concerns into separate types is what makes the constraint enforceable by construction.

## 1. The hub keys, as distinct types (the rename, made structural)

The plan's diagnosis is that one reused name (`keyStoreKey`) hid two principals. Rather than only
renaming fields, give each hub key its own wrapper type so the compiler refuses to pass an App Key
where a Peer Key belongs:

```csharp
namespace Odin.Services.Authorization.KeyStores;

/// <summary>The shared per-app hub key. One per app; every device's App Client Key
/// unwraps this same key. (Formerly the ExchangeGrant's "keyStoreKey".)</summary>
public sealed class AppKey(SensitiveByteArray key) : IDisposable
{
    internal SensitiveByteArray Raw { get; } = key;
    public void Dispose() => Raw.Wipe();
}

/// <summary>The per-connection hub key unlocking one peer's grant store.
/// (Formerly the AccessExchangeGrant's "keyStoreKey".)</summary>
public sealed class PeerKey(SensitiveByteArray key) : IDisposable
{
    internal SensitiveByteArray Raw { get; } = key;
    public void Dispose() => Raw.Wipe();
}
```

## 2. App side — App Key Store (clone of `ExchangeGrant` + `AccessRegistration`)

```csharp
/// <summary>An app's grant container: everything the App Key unlocks.
/// Clone of ExchangeGrant with the proposed names.</summary>
public class AppKeyStore
{
    public UnixTimeUtc Created { get; set; }
    public UnixTimeUtc Modified { get; set; }
    public bool IsRevoked { get; set; }

    /// <summary>App Key wrapped for the owner (was MasterKeyEncryptedKeyStoreKey).</summary>
    public SymmetricKeyEncryptedAes MasterKeyEncryptedAppKey { get; set; }

    public PermissionSet PermissionSet { get; set; }

    /// <summary>Per-drive leaves, each wrapped individually under the App Key
    /// (was KeyStoreKeyEncryptedDriveGrants).</summary>
    public List<AppDriveGrant> AppKeyEncryptedDriveGrants { get; set; } = new();

    /// <summary>Transit/ICR key under the App Key (was KeyStoreKeyEncryptedIcrKey).</summary>
    public SymmetricKeyEncryptedAes AppKeyEncryptedIcrKey { get; set; }
}

/// <summary>Clone of DriveGrant with the wrapping named for its principal.</summary>
public class AppDriveGrant
{
    public Guid DriveId { get; set; }
    public PermissionedDrive PermissionedDrive { get; set; }
    public SymmetricKeyEncryptedAes AppKeyEncryptedStorageKey { get; set; }  // null ⇒ write-only
    public bool HasStorageKey => AppKeyEncryptedStorageKey != null;
}

/// <summary>One per device login. Reconstructs the App Client Key from the device's
/// token-half ⊕ the server-stored half, then unwraps the shared App Key.
/// Clone of AccessRegistration.</summary>
public class AppClientRegistration
{
    public Guid Id { get; set; }
    public UnixTimeUtc Created { get; set; }
    public bool IsRevoked { get; set; }

    /// <summary>Server half of the split App Client Key (was ClientAccessKeyEncryptedKeyStoreKey).</summary>
    public SymmetricKeyEncryptedXor ClientHalfEncryptedAppClientKey { get; set; }

    /// <summary>The App Key wrapped for this device
    /// (was AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey).</summary>
    public SymmetricKeyEncryptedAes AppClientKeyEncryptedAppKey { get; set; }

    public SymmetricKeyEncryptedAes AppClientKeyEncryptedSharedSecret { get; set; }

    /// <summary>token-half ⊕ server half → App Client Key → App Key. No master key.</summary>
    public (AppKey appKey, SensitiveByteArray sharedSecret)
        DecryptUsingClientAuthenticationToken(ClientAuthenticationToken token) => throw null!;
}
```

## 3. Peer side — Peer Key Store (clone of `AccessExchangeGrant`) plus the #3 keypair

```csharp
/// <summary>A connected peer's grant container (the ICR's grant bundle).
/// Clone of AccessExchangeGrant, extended with the write-without-read primitive.</summary>
public class PeerKeyStore
{
    /// <summary>Peer Key wrapped for the owner (was MasterKeyEncryptedKeyStoreKey).
    /// Null ⇒ pending master-key upgrade (the legacy weak-connection state).</summary>
    public SymmetricKeyEncryptedAes MasterKeyEncryptedPeerKey { get; set; }

    public Dictionary<Guid, CircleMembershipGrant> CircleGrants { get; set; } = new();

    /// <summary>AppId → CircleId → grant (as today's AppGrants).</summary>
    public Dictionary<Guid, Dictionary<Guid, AppScopedCircleGrant>> AppGrants { get; set; } = new();

    /// <summary>The peer's client keys — ≥1 per connection, one per peer device
    /// (was AccessRegistration on the ICR). Same split-key shape as AppClientRegistration.</summary>
    public List<PeerClientRegistration> PeerClientRegistrations { get; set; } = new();

    public bool IsRevoked { get; set; }

    // ── Blocker #3 candidate: the write-only door into this store ──

    /// <summary>ECC-384 keypair; private key escrowed under the Peer Key.
    /// Anyone may encrypt TO this store; only a Peer-Key holder decrypts.</summary>
    public EscrowedWriteOnlyKeyPair WriteOnlyKey { get; set; }

    /// <summary>Grants deposited via the public key, awaiting conversion.</summary>
    public List<DepositedGrant> DepositedGrants { get; set; } = new();

    public bool HasPendingDeposits => DepositedGrants.Count > 0;
    public bool RequiresMasterKeyEncryptionUpgrade() => MasterKeyEncryptedPeerKey == null;
}

/// <summary>Clone of CircleGrant, leaves wrapped under the Peer Key.</summary>
public class CircleMembershipGrant
{
    public GuidId CircleId { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<PeerDriveGrant> PeerKeyEncryptedDriveGrants { get; set; } = new();
}

public class PeerDriveGrant
{
    public Guid DriveId { get; set; }
    public PermissionedDrive PermissionedDrive { get; set; }
    public SymmetricKeyEncryptedAes PeerKeyEncryptedStorageKey { get; set; }
}

/// <summary>Permissions this peer receives because they are in a circle that an app has
/// authorized (AuthorizedCircles). Clone of AppCircleGrant — same shape as
/// CircleMembershipGrant but keyed by the granting app; leaves are still wrapped
/// under this connection's Peer Key.</summary>
public class AppScopedCircleGrant
{
    public GuidId AppId { get; set; }        // the app granting the permissions
    public GuidId CircleId { get; set; }     // the circle through which they're granted
    public PermissionSet PermissionSet { get; set; }
    public List<PeerDriveGrant> PeerKeyEncryptedDriveGrants { get; set; } = new();
}

/// <summary>One per peer device/CAT. The peer's master-key-free path to the Peer Key —
/// the exact split-key shape as AppClientRegistration (clone of the ICR's
/// AccessRegistration): CAT token-half ⊕ server half → Peer Client Key → Peer Key.</summary>
public class PeerClientRegistration
{
    public Guid Id { get; set; }
    public UnixTimeUtc Created { get; set; }
    public bool IsRevoked { get; set; }

    /// <summary>Server half of the split Peer Client Key.</summary>
    public SymmetricKeyEncryptedXor ClientHalfEncryptedPeerClientKey { get; set; }

    /// <summary>The Peer Key wrapped for this peer device.</summary>
    public SymmetricKeyEncryptedAes PeerClientKeyEncryptedPeerKey { get; set; }

    public SymmetricKeyEncryptedAes PeerClientKeyEncryptedSharedSecret { get; set; }

    public (PeerKey peerKey, SensitiveByteArray sharedSecret)
        DecryptUsingClientAuthenticationToken(ClientAuthenticationToken cat) => throw null!;
}
```

## 4. The write-only primitive, shared by #3 and the future Drive PK

The plan notes the Peer Key Store PK and the per-drive PK are *the same pattern* with different
escrow keys — so it's one class, instantiated twice:

```csharp
/// <summary>An ECC keypair whose private key is escrowed under a symmetric key.
/// The public key lets you write, never read. Instantiations:
///   • Peer Key Store PK — escrowed under the Peer Key (unblocks #3)
///   • Drive PK (forward-looking) — escrowed under the drive's storage key</summary>
public class EscrowedWriteOnlyKeyPair
{
    public EccPublicKeyData PublicKey { get; set; }                       // in clear
    public SymmetricKeyEncryptedAes EscrowKeyEncryptedPrivateKey { get; set; }
    public uint PublicKeyCrc32 { get; set; }                              // deposit → keypair match

    public static EscrowedWriteOnlyKeyPair Create(SensitiveByteArray escrowKey) => throw null!;
    public EccFullKeyData RecoverPrivateKey(SensitiveByteArray escrowKey) => throw null!;
}

/// <summary>ECIES envelope: payload sealed to a recipient public key with a fresh
/// ECC-384 temp key stored alongside. Clone of EccEncryptedPayload, minus the
/// identity-level KeyType routing (this one is addressed to a specific store).</summary>
public class EciesSealedBox
{
    public byte[] Iv { get; set; }
    public byte[] Salt { get; set; }
    public byte[] EncryptedData { get; set; }
    public string TempPublicKeyJwk { get; set; }        // needed to decrypt
    public uint RecipientPublicKeyCrc32 { get; set; }
    public Guid TimestampId { get; set; } = SequentialGuid.CreateGuid();
}

/// <summary>A grant deposited into a Peer Key Store via its PK, awaiting conversion.
/// Metadata stays in clear so the server can validate at deposit time;
/// ONLY the storage key is sealed.</summary>
public class DepositedGrant
{
    public GuidId CircleId { get; set; }                 // clear — validated
    public Guid DriveId { get; set; }                    // clear — must be ∈ depositor's drives
    public PermissionedDrive PermissionedDrive { get; set; }  // clear — permission being granted
    public PermissionSet PermissionSet { get; set; }     // clear — circle's permission keys
    public Guid DepositingAppId { get; set; }            // provenance / audit / revoke-on-app-delete
    public UnixTimeUtc Deposited { get; set; }

    /// <summary>The drive's storage key, sealed to the store's PK. Null for
    /// permission-only entries (write-only drives / out-of-scope policy (a)).</summary>
    public EciesSealedBox SealedStorageKey { get; set; }
}
```

## 5. Blocker #4 — storage-key sourcing behind an interface

Today `CreateDriveGrant` hardcodes `drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey)`
and silently mints null keys. Replace the assumption with a source abstraction, and make the null
case throw (the plan's incidental fix):

```csharp
/// <summary>"Which hub key can the caller reach" — the plan's central question, as a type.</summary>
public interface IStorageKeySource
{
    bool CanSourceStorageKey(Guid driveId);

    /// <summary>Returns the drive's storage key or THROWS OdinSecurityException.
    /// Never silently returns null — no more members who are "in the circle"
    /// but can read nothing.</summary>
    Task<SensitiveByteArray> GetStorageKeyAsync(Guid driveId);
}

/// <summary>Owner path — identical reach to today: the drive's canonical root.</summary>
public sealed class MasterKeyStorageKeySource(
    SensitiveByteArray masterKey, IDriveManager driveManager) : IStorageKeySource
{ /* drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey) */ }

/// <summary>App path — App Client Key → App Key → AppKeyEncryptedStorageKey.
/// Sources keys ONLY for drives the app can already read (incl. temporal read).
/// This is where Example #1 (banking) is enforced by cryptography: no grant on the
/// drive ⇒ no key here ⇒ GetStorageKeyAsync throws ⇒ no working grant can exist.</summary>
public sealed class AppKeyStorageKeySource(
    AppKey appKey, IReadOnlyList<AppDriveGrant> appDriveGrants) : IStorageKeySource
{ /* find grant where HasStorageKey && (Read|ConditionalTemporalRead), unwrap with appKey */ }
```

## 6. Mint vs. seal — the split that solves the layering

```csharp
/// <summary>A fully-built circle grant with storage keys still in memory —
/// minted (#4 done) but not yet sealed into any store. Short-lived; disposed after sealing.</summary>
public sealed class MintedCircleGrant : IDisposable
{
    public GuidId CircleId { get; init; }
    public PermissionSet PermissionSet { get; init; }
    public List<MintedDriveKey> DriveKeys { get; init; } = new();
    public void Dispose() { /* wipe all storage keys */ }

    public sealed record MintedDriveKey(
        Guid DriveId, PermissionedDrive PermissionedDrive, SensitiveByteArray StorageKey);
}

/// <summary>Builds MintedCircleGrants from a circle definition + whatever key reach
/// the caller has. Replaces the master-key-only path in ExchangeGrantService.</summary>
public class CircleGrantMinter(IDriveManager driveManager)
{
    /// <exception cref="OdinSecurityException">a readable circle drive's key is
    /// unreachable and policy = Reject</exception>
    public Task<MintedCircleGrant> MintAsync(
        CircleDefinition circle,
        IStorageKeySource keySource,
        OutOfScopeDrivePolicy outOfScopePolicy) => throw null!;
}

/// <summary>The plan's open policy question, as an explicit knob rather than a buried branch.</summary>
public enum OutOfScopeDrivePolicy
{
    Reject,               // (b) the add fails if any circle drive is beyond the app's reach
    GrantPermissionOnly   // (a) mint the partial / permission-only result
}
```

Sealing is where #3 lives. Two writers, one interface — and only one of them ever holds a
`PeerKey`:

```csharp
/// <summary>A path to persist a minted grant into a peer's store.</summary>
public interface IPeerKeyStoreWriter
{
    Task WriteAsync(PeerKeyStore store, MintedCircleGrant grant);
}

/// <summary>Direct write — requires the Peer Key in plaintext. Available to:
///   • the owner (master key → MasterKeyEncryptedPeerKey), and
///   • the ACCEPTING side of an app-driven connection, which GENERATES the new
///     Peer Key and so holds it legitimately at accept time (Example #4).
/// Wraps each storage key as PeerKeyEncryptedStorageKey → CircleMembershipGrant.</summary>
public sealed class PeerKeySealer(PeerKey peerKey) : IPeerKeyStoreWriter { }

/// <summary>Write-only deposit — the #3 candidate. Needs ONLY the store's public key.
/// ECIES-seals each storage key to PeerKeyStorePublicKey with a fresh temp key and
/// appends DepositedGrants. Cannot read anything in the store, by construction.</summary>
public sealed class PublicKeyDepositor(
    EscrowedWriteOnlyKeyPair storeWriteKey, DepositValidator validator) : IPeerKeyStoreWriter { }

/// <summary>Deposit-time server checks against the CLEAR metadata:
/// driveId ∈ the depositing app's granted drives, circle exists, granted
/// permission ⊆ circle definition, no duplicate (circleId, driveId) pending.</summary>
public class DepositValidator { }
```

## 7. Policy layer — #1 and #2

```csharp
public static class CirclePermissionKeysAdditions
{
    /// <summary>New named permission an app can present (was: nothing — only
    /// ReadCircleMembership existed). Added to the app-allowed set.</summary>
    public const int ManageCircleMembership = 51;
}

/// <summary>App-callable clone of GrantCircleAsync / RevokeCircleAccessAsync.
/// Gates on capability, not on the master key.</summary>
public class CircleMembershipManager(
    CircleGrantMinter minter,
    DepositedGrantConverter converter /*, storage, mediator*/)
{
    public async Task GrantCircleAsync(GuidId circleId, OdinId odinId, IOdinContext ctx)
    {
        // #1: no AssertHasMasterKey. Either path authorizes:
        //   owner: ctx.Caller.HasMasterKey
        //   app:   ctx.PermissionsContext.HasPermission(ManageCircleMembership)   // #2
        //
        // Then compose by what the caller actually holds:
        //   owner → MasterKeyStorageKeySource + PeerKeySealer(unwrap MasterKeyEncryptedPeerKey)
        //           (byte-for-byte today's behavior)
        //   app   → AppKeyStorageKeySource   + PublicKeyDepositor(store.WriteOnlyKey)
        //           (no Peer Key ever touched)
        //
        // using var minted = await minter.MintAsync(circle, keySource, policy);
        // await writer.WriteAsync(icr.PeerKeyStore, minted);
    }

    public Task RevokeCircleAccessAsync(GuidId circleId, OdinId odinId, IOdinContext ctx)
        => throw null!;  // same gate; removal also purges matching DepositedGrants
}
```

## 8. Conversion — "access converts it"

```csharp
/// <summary>Rewrites DepositedGrants as normal Peer-Key-encrypted grants whenever the
/// Peer Key is next in scope. Two triggers, both existing touch points:
///   • peer authenticates via CAT (PeerClientRegistration unwraps the Peer Key)
///   • owner comes online (master key path) — same hook as today's deferred upgrade</summary>
public class DepositedGrantConverter
{
    public Task ConvertPendingAsync(PeerKeyStore store, PeerKey peerKey, IOdinContext ctx)
    {
        // 1. privateKey = store.WriteOnlyKey.RecoverPrivateKey(peerKey.Raw)
        // 2. per deposit: ECDH(TempPublicKeyJwk, privateKey) → unseal storage key
        // 3. re-validate clear metadata (drive/circle may have changed since deposit)
        // 4. upsert CircleMembershipGrant with PeerKeyEncryptedStorageKey
        // 5. remove deposit, save store, publish ConnectionChangedNotification
        throw null!;
    }
}
```

## 9. App-owned drives (committed direction — the two additive storage changes)

```csharp
/// <summary>Dedicated table replacing the KeyThreeValue blob (shadow-table migration).</summary>
public class AppRegistrationRecord
{
    public Guid IdentityId { get; set; }
    public Guid AppId { get; set; }              // PK
    public string Name { get; set; }
    public string CorsHostName { get; set; }
    public string AuthorizedCirclesJson { get; set; }
    public string CircleMemberPermissionGrantJson { get; set; }
    public string GrantJson { get; set; }        // the AppKeyStore
    public UnixTimeUtc Created { get; set; }
    public UnixTimeUtc Modified { get; set; }
}

// On the existing Drives table — one nullable column, no new table:
//   Guid? OwningAppId    // null = system/owner drive, behaves exactly as today
// Co-owned model: MasterKeyEncryptedStorageKey stays (owner recovery); the app
// reaches the key via its normal AppDriveGrant, so no extra key copy on the drive.
```

## How the worked examples fall out

- **#1 Banking (must stay impossible):** the chat app's `AppKeyStorageKeySource` has no
  `AppDriveGrant` for the banking drive → `MintAsync` throws (or omits it under
  `GrantPermissionOnly`). No storage key ever existed in the app's reach, so no working grant
  *can* be produced — policy line = crypto line.
- **#2 GPS (newly enabled):** the app has `ConditionalTemporalRead` →
  `AppDriveGrant.HasStorageKey` is true → mint succeeds → `PublicKeyDepositor` deposits →
  converted on next peer login. This is exactly the case #4 unblocks.
- **#3 Write-only drive:** the app holds no storage key for it; `SealedStorageKey` stays null and
  the deposit carries permission only — write-without-read preserved.
- **#4 App-driven connection:** the accepting app *generates* the new Peer Key, so it uses
  `PeerKeySealer` directly for its own drives' read grants at accept — strong within app scope,
  no `AutoConnectionsCircle` jail, no deferred upgrade. Adding members to *existing* connections
  later uses `PublicKeyDepositor`.

## Deliberate deviations from a literal transcription of the plan

1. **`AppKey`/`PeerKey` as wrapper types** rather than just renamed fields — the plan's root-cause
   is principal confusion, and types make it un-confusable at compile time.
2. **The mint/seal split** (`CircleGrantMinter` vs `IPeerKeyStoreWriter`) — it keeps #4 and #3 in
   separate classes so the "app never touches a Peer Key" invariant is visible in the dependency
   graph: `PublicKeyDepositor` has no `PeerKey` in its constructor, and that's the whole security
   argument.

## Transition strategy: build the new structure, migrate all apps at go-live

Build the new app-registration structure (the new `AppRegistrations` table and the classes above)
alongside the existing system during development, then — in a single release — migrate **all**
existing apps into it and retire the old store. There is deliberately **no dual-running period**
where old-format and new-format apps coexist in production.

Mechanics (the plan doc's shadow-table pattern, cf. `TableDrivesMigrationV202510311515`):

1. Create the new `AppRegistrations` table; deploy the new classes dark (no reader).
2. At go-live, copy every `KeyThreeValue` row where `key3 = AppRegistrationDataType` into the
   new table, deserializing the blob into columns.
3. **Preserve `AppId` byte-for-byte** — it is the join key into every existing ICR's `AppGrants`
   dictionary; change it and existing members' app access orphans.
4. **Transfer grant contents verbatim** — the wrapped keys and `AccessRegistration`s
   (device logins) copy as-is, or every device gets logged out at go-live.
5. Verify by comparing each migrated row **prop-by-prop against the raw old JSON** (not against
   the deserialized old object — a reader that silently drops a field to null would make both
   sides equally wrong and pass).
6. Repoint **every** reader in the same release — `GetRegisteredAppsAsync` /
   `GetAppRegistration(appId)`, which serve CAT→permission-context resolution on every app
   request, circle-grant propagation (`CircleNetworkService.GrantCircleAsync`), app revocation,
   and YouAuth registration — then retire the old rows.

The migration is registration-only: **ICRs are never touched.** `AppGrants` entries in
connections are self-contained snapshots (permission set + drive keys wrapped under that
connection's Peer Key, keyed by `AppId`); they were minted *from* the old registration but do not
reference it.

### Benefits

- **One code path in production at all times.** No composite/shim service probing two stores, no
  "which system is this app in?" discriminator leaking through auth resolution.
- **No silent-miss bugs.** Call sites that enumerate all apps (`GrantCircleAsync`'s
  `AuthorizedCircles` walk, connection accept) can't miss apps living in the other store —
  there is no other store.
- **Clean new home without behavioral fork.** The new table and classes land whole; the old
  `KeyThreeValue` blob retires; no divergence tax from maintaining grant logic twice.
- **Migration verification replaces serialization-snapshot tests for the migrated types** — the
  raw-JSON prop-by-prop comparison runs against every real production row, which is a stronger
  check than a captured sample.
- **ICR safety for free** — existing connections keep working without rewriting a single ICR,
  because the migration never touches them.

### Trade-offs

- **Big-bang cutover risk.** All readers flip in one release; a missed reader is a production
  auth failure, not a quiet degradation. Mitigation: the reader surface is small and enumerable
  (registration service is the single seam), and each identity is its own server — dogfood the
  cutover on one identity before rolling out.
- **The migration must be right the first time.** `AppId` fidelity and verbatim grant transfer
  are hard requirements; failure modes are user-visible (device logouts, orphaned member access).
  Mitigation: step 5's verification, plus keeping the old rows until verified rather than
  deleting in the same transaction.
- **No incremental validation in production.** Unlike a strangler, you can't watch new-format
  apps behave in prod before committing old apps to the format. Mitigation: the format is
  exercised end-to-end in integration tests and on a canary identity first.
- **Rollback is a restore, not a toggle.** Once old rows are retired, reverting means restoring
  them and re-flipping readers. Mitigation: retire lazily (a release later), so rollback within
  the window is just re-pointing the reader.

### Rejected variant: dual systems (new apps on new format, migrate old apps later)

Considered and rejected. App grants don't stay inside the registration — they fan out into every
ICR's `AppGrants` via code that enumerates **one** store (`CircleNetworkService.cs:552`:
`GetRegisteredAppsAsync` → `AuthorizedCircles` → `icr.AccessGrant.AddUpdateAppCircleGrant`). With
two live stores, a new-format app authorized for a circle is invisible to that walk: new members
connect fine but **silently get no access through that app**. Every merge point (CAT auth, circle
propagation, revoke's `AppGrants` cleanup) needs a complete composite shim from day one — and a
complete shim that down-converts to the old shapes *is* the single-code-path design, minus the
benefit of ever finishing. The deferred "migrate old apps later" step also historically never
ships, leaving the discriminator permanent.

## Rename exclusions

The plan doc says "do the rename first" across its whole table. Scoped to this feature, several
rows should be **excluded** — two reasons recur: the feature never touches the type's internals,
or the type is genuinely shared across principals and a one-sided name would be wrong somewhere.

### Excluded — feature treats these as black boxes

| Rename (from the plan's table) | Why excluded |
|---|---|
| `accessKeyStoreKey` / `AccessRegistration` → **App Client Key** | The feature calls `DecryptUsingClientAuthenticationToken` as-is; internals unchanged. Also shared: the same class is the ICR's peer-device registration — renaming means splitting one class in two. |
| `AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey` → **AppClientKeyEncryptedAppKey** | Lives inside that black box. |
| ICR's `AccessRegistration` → **Peer Client Key** | The deposit-conversion trigger fires at CAT auth through existing code, unchanged. |
| `MasterKeyEncryptedKeyStoreKey` (app grant) → **MasterKeyEncryptedAppKey** | The app path deliberately never uses the master-key wrapping. |
| `KeyStoreKeyEncryptedIcrKey` → **AppKeyEncryptedIcrKey / PeerKeyEncryptedIcrKey** | Deposits don't involve the ICR/transit key. |

### Excluded — the generic name is correct: `ExchangeGrant` → ~~App Key Store~~

`ExchangeGrant` is not the app's container; it is the codebase's **hub-key-generic** grant shape.
`ExchangeGrantService.CreatePermissionContext` takes `Dictionary<Guid, ExchangeGrant>` as its
universal input, and every auth scheme funnels through it with a *different* hub key doing the
wrapping:

- `CircleMembershipService.cs:233` (`MapCircleGrantsToExchangeGrantsAsync`) — adapts a **connected
  peer's** `CircleGrant`s into `ExchangeGrant`s; the drive grants inside are wrapped under the
  **Peer Key**, so `AppKeyEncryptedDriveGrants` would be factually wrong here.
- `CircleNetworkService.cs:1291` — same adapter for a peer's `AppCircleGrant`s.
- `YouAuthDomainRegistrationService.cs:506` — **guest/domain** visitors' permission contexts.
- `HomeAuthenticatorService.cs:208` — authenticated **home** visitors.

The class earns its generic name. If the App-Key-Store vocabulary is wanted, put it at the usage
site — e.g. rename the property `AppRegistration.Grant` → `KeyStore`, still typed as the generic
class — the same reasoning that keeps `AccessRegistration` and `DriveGrant` unsplit.

### Kept — the vocabulary the feature's code actually speaks

- Grant `keyStoreKey` (`ExchangeGrant`) → **App Key** — the #4 storage-key source unwraps drive
  keys under it.
- `keyStoreKey` on `AccessExchangeGrant` → **Peer Key** — the entire #3 story is stated in terms
  of it.
- `MasterKeyEncryptedKeyStoreKey` (ICR) → **MasterKeyEncryptedPeerKey** — the owner path in
  `GrantCircleAsync` (which this feature modifies) unwraps it.
- `AccessExchangeGrant` → **Peer Key Store** — this class gains `WriteOnlyKey` +
  `DepositedGrants` anyway.
- `KeyStoreKeyEncryptedStorageKey` → **AppKeyEncryptedStorageKey / PeerKeyEncryptedStorageKey** —
  what the source reads and the sealer/deposit writes. *Caution:* `DriveGrant` is one class used
  in both contexts, so this is either two classes or one kept-generic C# name with the
  principal-specific naming at the containing collections.
- The three new #3 names (`PeerKeyStorePublicKey`, `PeerKeyEncryptedStorePrivateKey`,
  `DepositedGrant`) — new fields, not renames.

Net effect: the serialization-fidelity guardrail shrinks to the **non-migrated** stored types the
rename touches — `AccessExchangeGrant` (ICRs), `ExchangeGrant`, and `DriveGrant` as persisted in
ICRs — since the app-registration types are covered by the migration's raw-JSON verification
instead.