# Status: Allow apps to add OdinIds to a circle

Companion to [`app-circle-membership-plan.md`](./app-circle-membership-plan.md), which
documents the design reasoning, naming, and rejected approaches. This doc tracks what
has actually **shipped** on `app-add-odinid-to-circle`, confirmed by tests, versus
what's still open — superseding the plan doc's "not yet built" / "undecided" markers.

## Complete

**All four blockers resolved.**
- **#1 — auth gate.** `GrantCircleAsync` / `RevokeCircleAccessAsync` gate non-master-key
  callers on a permission instead of hard-requiring the master key
  (`AssertCanManageCircleMembership`, `CircleNetworkService.cs`).
- **#2 — permission key.** `PermissionKeys.ManageCircleMembership` (= 51) exists, in the
  app-allowed set, implying `ReadConnections` + `ReadCircleMembership`.
- **#4 — storage-key sourcing.** `IStorageKeySource` (`StorageKeySource.cs`) replaced the
  single hardcoded master-key path; `PermissionContextStorageKeySource` throws for any
  drive beyond the caller's own reach — the cryptographic enforcement of "an app can
  only grant what it already has." The "silent null-storage-key grant" footgun the plan
  doc flagged is gone: minting is explicit-source-or-throw everywhere.
- **#3 — write-without-read into the peer store.** Built: `PeerKeyStore.WriteOnlyKeyPair`
  + `DepositedGrants` (`PeerKeyStore.cs`, `DepositedGrant.cs`). An app deposits a sealed
  grant via `CreateDepositedGrantAsync`; it converts into a real `CircleGrant` (and the
  app-implied `AppCircleGrant`s, via the shared `FanOutAppCircleGrantsAsync`) the moment
  the Peer Key is next in scope — owner grant touch, or the peer's own server
  authenticating any transit call.

**The policy question is answered in code**, even though the plan doc still marks it
undecided: deposits are all-or-nothing. Any single drive in the target circle that the
depositing app can't itself access aborts the whole deposit — confirmed by
`AppDepositsCircleWithOutOfScopeDrive_FailsEntirely_NoPartialState`.

**Backfill and rollout.** `UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync`
(the pre-existing, unconditional VersionUpgradeService pre-pass) now also backfills a
missing `WriteOnlyKeyPair`. `V11ToV12VersionMigrationService` backfills
`ManageCircleMembership` onto the stored Chat app registration, and bumps
`Version.DataVersionNumber` to 12 — necessary because `VersionUpgradeScheduler` only
re-enters `VersionUpgradeService` when `currentVersion < Version.DataVersionNumber`, so
without the bump the pre-pass never re-runs for already-migrated tenants. Only the
**Chat** app gets `ManageCircleMembership` automatically; any other app needs its own
permission request and owner consent.

**Pending-deposit visibility.** `RedactedPeerKeyStore.PendingCircleIds` (`PeerKeyStore.cs`)
surfaces `DepositedGrants` through `GetConnectionInfo` — closes what was the single
biggest observability gap: a caller can now tell "deposited, awaiting conversion" apart
from "never added" or "already a member," instead of every pending add looking silently
identical to a no-op.

**Two real bugs found and fixed, both by tests failing, not by inspection:**
1. `FanOutAppCircleGrantsAsync` originally called `GetRegisteredAppsAsync`, which
   asserts the master key — fatal on the peer-CAT path, which never has one. Worse than
   just failing the fan-out: because it ran before `SaveIcrAsync`, it silently discarded
   the deposit→`CircleGrant` conversion too. Fixed by switching to
   `GetAppsGrantingCircleAsync`, which needs no master key.
2. `GET /api/v2/connections/status` 415'd on every real call. Two stacked causes:
   `GetConnectionInfo(OdinId odinId)` had no `[FromQuery]`, so `[ApiController]`
   inferred `[FromBody]` for the (TypeConverter-less) `OdinId` struct; fixing that alone
   surfaced a second bug where Refit's default URL formatter didn't serialize `OdinId`
   into a usable query value either. Fixed on both sides by binding `string` and
   converting manually — nothing in the test suite had ever called this endpoint via a
   real HTTP request before (every other caller used the V1 API), which is exactly why
   it went undetected until a dedicated regression test exercised it.

**Test coverage** — 21 tests across
`tests/apps/Odin.Hosting.Tests.V2/Ported/Connections/CircleMembership/`,
`Ported/Profile/V11ToV12ChatAppMigrationTests.cs`, and
`Ported/Connections/CircleNetworkListTests.cs`:
- `DepositGrantTests.cs` (7): permission gate, pending-not-yet-member deposit,
  all-or-nothing scope rejection, duplicate-add rejection, missing-keypair rejection,
  the auto-connected/unconfirmed-identity guard via an app caller, and
  pending→real visibility through `GetConnectionInfo`.
- `OwnerGrantConversionTests.cs` (2): owner grant is immediately real; a grant touch on
  one circle also converts a pending deposit on another circle for the same connection.
- `PeerCatConversionTests.cs` (3): peer-CAT conversion mints both `CircleGrant` and
  `AppCircleGrant` (verified functionally — the peer can actually use the grant
  afterward), a call with no pending deposits is a no-op, a deposit for a
  since-deleted circle is dropped silently.
- `WriteOnlyKeyPairBackfillTests.cs` (4): missing-keypair backfill, no-op when already
  provisioned, the KSK-upgrade-and-keypair-backfill compound success case (verified
  against the original key material, not just "didn't throw"), and the genuinely
  unrecoverable case (no weak fallback key) skipping gracefully without touching the
  connection's existing keypair.
- `V11ToV12ChatAppMigrationTests.cs` (3): grants the permission and preserves existing
  grants, skips revoked apps, idempotent on repeat runs.
- `CircleNetworkListTests.cs` (+1): the 415 regression test — the only test in the whole
  suite that hits `GET /api/v2/connections/status` as a real HTTP call.

**FE integration spec** shipped separately:
[`app-circle-membership-fe-integration.md`](./app-circle-membership-fe-integration.md) —
endpoint reference, exact HTTP status/errorCode pairs per failure mode (verified against
`ExceptionHandlingMiddleware`, not guessed), GUID-format gotcha (`GuidId` fields
serialize with no hyphens, plain `Guid` fields do), and the pending-vs-real distinction.

## Pending / not addressed

**Example #4 — the plan doc's actual motivating case — is still unsolved.** Everything
above lets an app manage circles on a connection that **already exists**. The doc opens
with a different goal: an app **establishing** a new connection (send + accept, no
console, no master key) that's immediately strong within that app's scope. That flow is
untouched — `CircleNetworkRequestService.cs`'s no-master-key accept path still uses the
old `TempWeakKeyStoreKey` / `AutoConnectionsCircle`-only / deferred-upgrade stop-gap the
doc explicitly wanted to retire. We built the machinery (#3's deposit scheme, #4's
`IStorageKeySource`) but never rewired accept-time bootstrap to use it.

**Per-drive public key (Drive PK) doesn't exist.** Needed for the write side of
Example #4's accept bootstrap and Example #3's write-to-a-stranger case. The plan doc
calls this "the most promising direction" for further work here; nothing built.

**App-owned drives** — explicitly "committed direction, timing TBD" in the plan doc,
untouched: no `AppRegistrations` table split, no `AppId` column on `Drives`, ownership
model questions (co-owned vs. app-exclusive, revocation policy) unresolved.

**TOCTOU on the depositing app's scope.** The drive-access check runs once, at deposit
time. Conversion — which can happen arbitrarily later — never re-validates that the
depositing app still has, or should still have, that access.

**`AppGrants` fan-out still degrades for a hypothetical Read-requesting app.** Both
conversion paths fan out correctly now, but only apps whose `CircleMemberPermissionGrant`
needs no Read get a fully-working grant on the peer-CAT path (true for every built-in
app today). One that requested Read would get a keyless grant there, self-healing only
via `ReconcileAuthorizedCircles` on that app's next registration change — not on any
fixed timeline. Not a regression, just an edge case nothing exercises today.

**No forcing function for a stuck deposit — deliberately not building one.** Considered
and rejected this session: prodding the peer's server to call back doesn't actually help
(the peer gets real access the moment they try to use it regardless, since conversion
runs inline before the permission check on that same request), and it would make
Frodo's-own-UI staleness — the only real problem — depend on a third party's server
cooperating. The right fix for that staleness is the `PendingCircleIds` visibility
already shipped, not faster conversion.

**Scope: Chat only.** Any other app needing this capability needs its own permission
grant and, if it predates the feature, its own backfill migration step.
