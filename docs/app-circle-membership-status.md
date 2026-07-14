# Status: Allow apps to add OdinIds to a circle

Companion to [`app-circle-membership-plan.md`](./app-circle-membership-plan.md), which
documents the design reasoning, naming, and rejected approaches. This doc records what
has actually **shipped** on `app-add-odinid-to-circle`, confirmed by tests, superseding
the plan doc's "not yet built" / "undecided" markers where they're now stale.

## All four blockers are resolved

**#1 — auth gate.** `GrantCircleAsync` / `RevokeCircleAccessAsync` no longer hard-require
the master key. `AssertCanManageCircleMembership` (`CircleNetworkService.cs`) gates
non-master-key callers on a permission instead.

**#2 — permission key.** `PermissionKeys.ManageCircleMembership` (= 51,
`CirclePermissionFlags.cs`) exists, sits in the app-allowed set (not the circle-grantable
set — a peer must never manage your circles), and implies `ReadConnections` +
`ReadCircleMembership`.

**#4 — storage-key sourcing.** `IStorageKeySource` (`StorageKeySource.cs`) replaced the
single hardcoded master-key path. `PermissionContextStorageKeySource` sources a drive's
storage key from the *caller's own* permission context and throws
(`OdinSecurityException`) for any drive beyond that caller's reach — the cryptographic
enforcement of "an app can only grant what it already has." The "incidental fix" the plan
doc called for (silent null-storage-key grants should throw, not mint a member who can
read nothing) is done: minting is now explicit-source-or-throw everywhere.

**#3 — write-without-read into the peer store.** Built, not just designed. Every
`PeerKeyStore` has a `WriteOnlyKeyPair` (ECC-384; public key clear, private key encrypted
under the Peer Key) and a `DepositedGrants` list (`PeerKeyStore.cs`,
`DepositedGrant.cs`). An app deposits a sealed grant via
`CircleNetworkService.CreateDepositedGrantAsync` — it can write, and cryptographically
cannot read anything back. The deposit converts into a real `CircleGrant` the moment the
Peer Key is next in scope, via two independent triggers:

- **Owner grant touch** — `GrantCircleAsync`'s master-key branch provisions the keypair
  on first touch and converts any pending deposits as a side effect.
- **Peer-CAT auth** — `CreateTransitPermissionContextAsync` /
  `TryConvertDepositedGrantsAtPeerAuthAsync` reconstruct the Peer Key from the peer's own
  CAT whenever their server authenticates any transit call, and convert opportunistically,
  never failing the underlying request.

Both conversion paths also fan out the app-implied `AppCircleGrant`s (e.g. Chat's
`Write | React` on ChatDrive/ListsDrive/MomentsDrive) via a shared
`FanOutAppCircleGrantsAsync` helper — a peer added to a circle via deposit ends up able to
*use* that circle's access, not just appear as a member. The peer-CAT path sources those
grants via `NoStorageKeySource` (no master key on that path); since none of the built-in
apps' `CircleMemberPermissionGrant`s request Read, this produces fully working grants in
practice, not degraded ones.

## The policy question is answered, in code if not in the doc

The plan doc marks *"partial grant vs. reject the whole add"* as **undecided**. The
shipped behavior already decided it: `CreateDepositedGrantAsync` is all-or-nothing — any
single drive in the target circle that the depositing app can't itself access aborts the
entire deposit, nothing partial is ever saved. Confirmed by
`AppDepositsCircleWithOutOfScopeDrive_FailsEntirely_NoPartialState`.

## Backfill and rollout

Existing (pre-branch) connections and app registrations don't get the new fields for
free — two things had to be retrofitted, both new this session:

- **`WriteOnlyKeyPair` backfill.** `CircleNetworkService.UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync`
  (the pre-existing, unconditional pre-pass `VersionUpgradeService` runs before the
  version ladder) now also provisions a missing `WriteOnlyKeyPair` for every connected
  identity, in the same pass as the existing master-key store-key encryption upgrade.
- **`ManageCircleMembership` on the Chat app.** `V11ToV12VersionMigrationService`
  backfills the permission onto the stored Chat app registration (mirroring how
  `V10ToV11` backfilled `ManageProfile`). `Version.DataVersionNumber` bumped to 12 —
  necessary because `VersionUpgradeScheduler` only re-enters `VersionUpgradeService` when
  `currentVersion < Version.DataVersionNumber`, so without the bump the pre-pass above
  would never re-run for already-migrated tenants.

Only the **Chat** app gets `ManageCircleMembership` automatically (default set for fresh
installs via `SystemAppConstants`, backfilled for existing installs via the migration).
Any other app needs its own permission request and owner consent — nothing here
generalizes that.

## Test coverage

`tests/apps/Odin.Hosting.Tests.V2/Ported/Connections/CircleMembership/` and
`Ported/Profile/V11ToV12ChatAppMigrationTests.cs` — 18 tests:

- **Deposit creation** (`DepositGrantTests.cs`): permission gate, pending-not-yet-member
  deposit, all-or-nothing scope rejection, duplicate-add rejection, missing-keypair
  rejection, and the auto-connected/unconfirmed-identity guard exercised via an app
  caller.
- **Owner-driven conversion** (`OwnerGrantConversionTests.cs`): owner grant is
  immediately real; a grant touch on one circle also converts a pending deposit on
  another circle for the same connection.
- **Peer-CAT conversion** (`PeerCatConversionTests.cs`): a peer's incoming call converts
  a deposit into a real `CircleGrant` *and* `AppCircleGrant` (verified functionally — the
  peer can actually use the granted permission afterward, not just hold the grant
  object), an ordinary call with no pending deposits is a no-op, a deposit for a
  since-deleted circle is dropped silently.
- **Backfill pre-pass** (`WriteOnlyKeyPairBackfillTests.cs`): missing-keypair backfill,
  no-op when already provisioned, the compound case (KSK upgrade *and* keypair
  provisioning succeeding in the same pass, verified against the original key material),
  and the genuinely-unrecoverable case (no weak fallback key — skipped gracefully, no
  crash, existing keypair left untouched).
- **Migration** (`V11ToV12ChatAppMigrationTests.cs`): grants the permission and preserves
  existing grants, skips revoked apps, idempotent on repeat runs.

Also extended: shared V2 test Refit client (`circles/add`, `circles/revoke`,
circle-member-list — none existed before), `AppSession.SetupAsync` gained an
`authorizedCircles`/`circleMemberGrantRequest` overload.

## A real bug found and fixed along the way

`FanOutAppCircleGrantsAsync` originally called `AppRegistrationService.GetRegisteredAppsAsync`,
which asserts the master key — fine for the owner path, fatal for the peer-CAT path,
which never has one. The resulting exception was swallowed by
`TryConvertDepositedGrantsAtPeerAuthAsync`'s intentionally-non-fatal `catch`, but because
the fan-out ran *before* `SaveIcrAsync`, it silently discarded the deposit→`CircleGrant`
conversion too — not just the app-grant fan-out. Fixed by switching to
`GetAppsGrantingCircleAsync`, which does the identical per-circle filtering without the
master-key assertion. Found by a test (`PeerCallFromSam_Converts...`) failing, not by
inspection — a concrete argument for the peer-CAT test existing at all.

## Known gaps that remain (see the fuller discussion in-conversation)

- **No visibility into pending deposits.** `GetCircleMembersAsync` and the circle-member
  index are driven purely by `CircleGrants`; a successful `circles/add` call and a
  silently-never-converted one look identical from any caller's perspective until
  conversion actually happens. No API surfaces "N deposits pending."
- **TOCTOU on the depositing app's scope.** The drive-access check runs once, at deposit
  time. Conversion — which can happen arbitrarily later — never re-validates that the
  depositing app still has (or should still have) that access.
- **`AppGrants` fan-out completeness depends on which path converts first.** Both paths
  now fan out correctly (this session's fix), but only for apps whose
  `CircleMemberPermissionGrant` needs no Read; an app that did request Read would still
  get a keyless grant on the peer-CAT path, self-healing only via
  `ReconcileAuthorizedCircles` on that app's next registration change — not on any fixed
  timeline.
- **No forcing function for a stuck deposit.** Resolution depends entirely on the owner
  eventually touching that connection's grants or the peer's server eventually
  authenticating for something else. No timeout, no sweep.
- **Scope: Chat only.** Any other app needing this capability needs its own permission
  grant and, if it predates the feature, its own backfill migration.
