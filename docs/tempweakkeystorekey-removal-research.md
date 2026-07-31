# Research: removing `TempWeakKeyStoreKey`

**Status:** research only — no code changed. Line references are against `main` as of 2026-07-30.

**Scope note:** this document is about `TempWeakKeyStoreKey` (the escrowed **Peer Key**). Its
sibling `TemporaryWeakClientAccessToken` (the escrowed **CAT**) is a separate problem with a
different answer; it is summarised at the end but not solved here.

---

## Problem statement

When a connection is finalised without the owner's master key, the code has no way to write
`PeerKeyStore.MasterKeyEncryptedPeerKey`. To keep a path open for the owner to fix that later,
`ConnectAsync` escrows a copy of the connection's Peer Key in
`IdentityConnectionRegistration.TempWeakKeyStoreKey` (`CircleNetworkService.cs:510`),
ECC-sealed via `PublicPrivateKeyService`.

Two things are wrong with this.

**1. For introduction- and app-origin connections the escrow is sealed to a key the host can
open.** `GetPublicPrivateKeyType` (`CircleNetworkRequestService.cs:1736-1741`) returns
`PublicPrivateKeyType.OfflineKey` for every origin except `IdentityOwner`. The offline keypair's
private half is encrypted with an all-zero constant:

```csharp
// PublicPrivateKeyService.cs:43
public static readonly byte[] OfflinePrivateKeyEncryptionKey = { 0, 0, 0, ... };
```

So for those connections the Peer Key is effectively at rest in the clear, from the host's
perspective. That is the "weak" in the field name, and it is the security motivation for this work.

**2. It is a deferred-repair mechanism that a newer primitive has made largely unnecessary.**
`PeerKeyStore.WriteOnlyKeyPair` + `DepositedGrants` now allow a grant to be written into a peer's
store *without* holding the Peer Key. The escrow's original job — "keep the Peer Key around so we
can write grants later" — no longer needs doing.

**Goal:** delete `TempWeakKeyStoreKey`, or reduce it to something that is not host-readable.

---

## What the field is and where it lives

| Concern | Location |
|---|---|
| Field | `IdentityConnectionRegistration.TempWeakKeyStoreKey` |
| Written | `CircleNetworkService.ConnectAsync:510` |
| Persisted | `CircleNetworkStorage.cs:256` (read), `:298` (write), column `WeakKeyStoreKey` `:323` |
| Cleared after upgrade | `CircleNetworkStorage.cs:112` |
| **Sole functional reader** | `UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync:1769` |

The reader decrypts the escrow, then writes `MasterKeyEncryptedPeerKey` under the owner's master
key. It is master-key-gated by construction — `odinContext.Caller.GetMasterKey()` is called on the
line after the decrypt. **An app never reads this field; it only writes it.**

---

## When it gets written

### Send-side scenarios (no master key)

| # | Name | Entry point | Runs as |
|---|---|---|---|
| 1 | **App-Initiated Send** | `V2ConnectionRequestsController.cs:107` | App client token |
| 2 | **App Auto-Connect** | `CircleNetworkRequestService.SendAutoConnectRequestAsync` | App client token |
| 3 | **Attended Introduction** | `CircleNetworkIntroductionService.SendOutstandingConnectionRequestsAsync` | Owner or app (inherits caller) |
| 4 | **Unattended Introduction** | `ConnectIntroduceeOutboxWorker.Send` | `system.domain`, `SecurityGroupType.System` |

Two findings worth calling out:

- **Origin beats caller.** `HandleConnectionRequestInternalForIntroductionAsync` passes
  `masterKey: null` at every call site regardless of whether the owner is online. The same is true
  of the app handler. So scenario 3 mints keyless grants even when the master key was available.
- **Scenario 4 has neither key.** The outbox worker builds its context at
  `PeerOutboxProcessorBackgroundService.cs:129-136` with `masterKey: null` and
  `PermissionContext(null, null, true)` — no permission groups, so `GetIcrKey()` returns null too.
  It is the only path where no key of any kind is reachable.

### Accept-side triggers

The field is actually written at finalisation, reached from two places:
`AcceptConnectionRequestAsync` (recipient) and `EstablishConnection` (sender, when the reply lands).

| Trigger | Entry | Master key |
|---|---|---|
| Explicit accept (V1) | `CircleNetworkRequestsControllerBase.cs:67` | Owner yes / app no |
| Explicit accept (V2) | `V2ConnectionRequestsController.cs:149` | Owner yes / app no |
| Send diverted into accept | `CircleNetworkRequestService.cs:1358`, `:1451`, `:1476` | inherits |
| **Auto-accept on arrival** | mediator publish `:558` → `CircleNetworkIntroductionService.cs:628` → `:669` | **no** — peer CAPI context |
| Auto-accept, forced | `ForceAutoAcceptEligibleConnectionRequestsAsync` from controller | Owner yes / app no |
| **Sender-side establish** | `InvitationsController.cs:43` → `EstablishConnection` | **no** — peer CAPI context |

The sender-side outcome is decided back at send time, not at establish time:
`EstablishConnectionAsync:1077` branches on `originalRequest.TempEncryptedIcrKey == null`, and that
field is only populated for `IdentityOwner` origin (`:1549`).

---

## Worked example: Frodo's app sends, Sam auto-accepts

### Frodo sends

1. App → `SendConnectionRequestAsync`, origin `IdentityOwnerApp` →
   `HandleConnectionRequestInternalForAppAsync` → `CreateAndSendRequestInternalAsync(masterKey: null)`.
2. Frodo generates a `keyStoreKey` (his Peer Key for this connection) and builds
   `PendingPeerKeyStore` (`:1611-1612`): `MasterKeyEncryptedPeerKey = null`, `WriteOnlyKeyPair`
   created from it, circle grants minted **keyless**, `PeerClientKey` = the access registration.
3. `:1549` — origin is not `IdentityOwner`, so `TempEncryptedIcrKey` stays null. **This decides
   Frodo's escrow at step 9.**
4. The request is sealed to Sam's *offline* public key (`:1630` → `GetPublicPrivateKeyType`) and
   POSTed to `/PeerIncoming/invitations/connect`. Frodo has written no ICR yet.

### Sam auto-accepts

5. `InvitationsController.cs:31` → `ReceiveConnectionRequestAsync` (peer CAPI, no master key) →
   stores the pending request → publishes `ConnectionRequestReceivedNotification` (`:558`).
6. MediatR → `CircleNetworkIntroductionService.cs:628` → `AutoAcceptEligibleConnectionRequestAsync`.
   The `requiresIcr` guard at `:492-493` does not trip, because the payload was sealed with
   `OfflineKey`, not `OnlineIcrEncryptedKey`.
7. `:534-537` — pending origin is `IdentityOwnerApp` and `DisableAutoAcceptConnectionRequests` is
   false → `AutoAcceptAsync:669`, which adds only `ReadCircleMembership` + `ManageFeed`. Still no
   master key, still no ICR key.
8. `AcceptConnectionRequestAsync` — Sam generates his own `keyStoreKey`, mints his `PeerClientKey`
   and the CAT reply for Frodo; his `PeerKeyStore` gets `MasterKeyEncryptedPeerKey = null`, a
   `WriteOnlyKeyPair`, keyless grants (`:819-822`). At `:842` the no-master-key branch seals Frodo's
   CAT and Sam's `keyStoreKey` to `OfflineKey`. **Sam's escrow is written** (`ConnectAsync:509-510`).
9. Sam POSTs the reply to Frodo's `/PeerIncoming/invitations/establishconnection`.
   `EstablishConnectionAsync:1077` sees `TempEncryptedIcrKey == null` and does the same.
   **Frodo's escrow is written.**

**End state on both sides:** `MasterKeyEncryptedPeerKey` null, circle grants keyless, both weak
fields populated and sealed to the all-zero-constant-protected offline key.

---

## What already exists that replaces it

Three pieces, all already in `main`:

**`PeerKeyStore.WriteOnlyKeyPair`** (`PeerKeyStore.cs:43`) — an ECC-384 keypair whose private half
is encrypted under the Peer Key (`PeerKeyStoreWriteOnlyKey.CreateKeyPair`, `DepositedGrant.cs:62`).
Anyone holding the public half can seal a grant *into* the store while reading nothing out of it.
Created unconditionally on every new store (`CircleNetworkRequestService.cs:822`, `:1612`), so it is
present even on master-key-less connections.

**`DepositedGrants`** (`PeerKeyStore.cs:49`) — grants written via that public key, awaiting
conversion. Metadata (`CircleId`, `PermissionSet`, `PermissionedDrive`) is kept **in clear** so the
server can validate at deposit and conversion time; only the drive storage keys are sealed.
Produced by `CreateDepositedGrantAsync:1193`, which enforces the scope boundary — it throws for any
drive the depositing caller cannot itself read.

**`PeerClientKey`** — the recovery path. The Peer Key is not stored anywhere; it is reconstituted on
demand from the server half (`PeerClientKey`) plus the peer's own CAT:

```csharp
// CircleNetworkService.cs:1315
var (keyStoreKey, sharedSecret) = icr.PeerKeyStore.PeerClientKey
    .DecryptUsingClientAuthenticationToken(remoteIcrToken);
```

This fires from `CreateTransitPermissionContextAsync:89` — i.e. **the first time the peer makes an
authenticated server-to-server call to us**. Conversion happens before the permission context is
built, so the converted grants apply to that very request. No chicken-and-egg, no master key.

### Old vs new

| | Old | New |
|---|---|---|
| Write | `ConnectAsync:510` escrows the Peer Key | `CreateDepositedGrantAsync:1193` → `DepositedGrants` |
| Repair | `UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync:1761` | `ConvertDepositedGrantsAsync:1250` |
| Trigger | Owner comes online | Peer calls in (`:89` → `TryConvertDepositedGrantsAtPeerAuthAsync:1305`) |
| Key source | `TempWeakKeyStoreKey` + master key | `PeerClientKey` + the peer's CAT |

Note that `GrantCircleAsync` **already contains both branches** — the master-key path at `:559-606`
and the deposit path at `:606`. The change is not new machinery; it is which branch runs.

---

## The blocking problem: deposits do not create circle membership

This is the finding that should gate any decision.

Circle membership rows are written from `CircleGrants` — **not** `DepositedGrants`:

```csharp
// CircleNetworkStorage.cs:71-78
foreach (var (circleId, circleGrant) in icr.PeerKeyStore?.CircleGrants ?? [])
{
    ...
    await _circleMembershipService.AddCircleMemberAsync(circleId, icr.OdinId, circleGrant, DomainType.Identity);
}
```

So while a grant sits as a deposit, the identity is not a circle member as far as the rest of the
system is concerned:

- `GetCircleMembersAsync:459` does not list them.
- `CircleNetworkService.cs:880` derives `isCircleMember` from `CircleGrants.TryGetValue`, so the
  owner UI reports *"Identity is not a member of this circle."*
- `UpdateCircleDefinitionAsync:712` and `ReconcileAuthorizedCircles:968,982` fan out over
  `GetCircleMembersAsync`, so deposited members are skipped by circle edits entirely.

Consequence if `TempWeakKeyStoreKey` is dropped without addressing this: the owner grants a circle,
the UI says the peer is not in it, and subsequent circle edits pass them by — until the peer happens
to make a peer call.

**This gap exists today** for app deposits via the `else` branch at `:606`. Dropping the escrow
widens it from "circles an app granted" to "every owner grant on an app-created connection".

It looks tractable: `DepositedGrant` deliberately keeps `CircleId` and `PermissionSet` in clear, so
a membership row can be written at deposit time with drive keys filled in at conversion.

---

## Candidate solutions

### A. Delete the field; owner grants become deposits

Reroute every owner call site to deposit when the Peer Key is unreachable, i.e. change the branch
condition from *"does the caller have the master key"* to *"is the Peer Key actually reachable"*
(`HasMasterKey && !RequiresMasterKeyEncryptionUpgrade()`).

- **Pro:** the escrow disappears entirely; nothing host-readable remains. No new crypto.
- **Con:** `MasterKeyEncryptedPeerKey` stays null forever on these connections, so the owner has no
  independent path — grant activation is hostage to the peer initiating contact.
- **Con:** `ConfirmConnectionAsync:1054` (promoting an auto-connection from `AutoConnectionsCircle`
  to `ConfirmedConnectionsCircle`) becomes deferred. This is the flow where the owner is most
  explicitly asserting control, so deferring it is the least comfortable part of this option.
- **Blocked on:** the membership-row problem above.

### B. Keep the TempWeakKeyStoreKey field, re-seal it to `OnlineKey`

Leave the escrow in place but stop sealing it to a key the host can open.

The online keypair's private half is encrypted under the **master key**
(`PublicPrivateKeyService.cs:323`, decrypt at `:294` and `:191`), while encryption needs only the
public key — so any context can seal to it, including `system.domain` in scenario 4.

**This option is independent of the deposits work.** Every reader of `TempWeakKeyStoreKey` has
always held the master key: `UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync:1769` calls
`GetMasterKey()` on the next line, and both migrations that reach it assert `HasMasterKey` at entry
(`V7ToV8VersionMigrationService.cs:31`, `:99`). `OnlineKey` was therefore always a valid choice for
this payload. It is `OfflineKey` today only because the CAT and the keyStoreKey share a single
`keyType` variable at both seal sites, and the **CAT** genuinely cannot use `OnlineKey` — it is read
from `CircleNetworkService.GetIcrAsync:417`, which has no master key. The keyStoreKey inherited that
constraint by proximity, not by requirement.

So B and A are orthogonal: deposits decide whether the escrow is needed *at all*; the key type
decides whether it is *host-readable*. B does not wait on the membership-row problem.

#### Do not flip `GetPublicPrivateKeyType`

That helper (`CircleNetworkRequestService.cs:1736`) feeds two unrelated jobs:

| Call site | Method | What it seals | Who opens it |
|---|---|---|---|
| `:1630` | `TrySendRequestInternalAsync` | the outgoing request, **to the recipient** | the recipient, in `GetPendingRequestAsync:113` — **without a master key** |
| `:850` | `AcceptConnectionRequestAsync` | our local escrow (CAT + keyStoreKey) | our own owner, later, with the master key |
| `:1082` | `EstablishConnection` | our local escrow (CAT + keyStoreKey) | our own owner, later, with the master key |

Changing the helper would take `:1630` with it, so a recipient would need their master key to read an
incoming request — which breaks auto-accept, the entire point of these flows. Only the keyStoreKey
seal at `:850` and `:1082` should move.

#### The change

At `AcceptConnectionRequestAsync:850-856`, leave the CAT on `keyType` and move only the keyStoreKey:

```csharp
var keyType = GetPublicPrivateKeyType(incomingRequest.ConnectionRequestOrigin);
var eccEncryptedCat = await publicPrivateKeyService.EccEncryptPayload(
    keyType,
    remoteClientAccessToken.ToPortableBytes());

// The Peer Key escrow is only ever opened by our own owner, in
// CircleNetworkService.UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync, which holds the
// master key -- so seal it to the master-key-rooted online key rather than the host-readable
// offline key. Note this deliberately differs from `keyType` above.
var eccEncryptedKeyStoreKey = await publicPrivateKeyService.EccEncryptPayload(
    PublicPrivateKeyType.OnlineKey,
    keyStoreKey.GetKey());
```

`EstablishConnection:1082-1088` takes the identical change. Nothing else moves.

No decrypt-side change is required: `EccEncryptedPayload` carries its own `KeyType` and
`PublicPrivateKeyService.EccDecryptPayload:185-197` switches on it, so existing `OfflineKey` records
keep working beside new `OnlineKey` ones.

- **Pro:** two lines; closes the host-readable hole immediately; every existing consumer keeps
  working unchanged; the owner retains an independent path to the Peer Key.
- **Pro:** applies uniformly to all four scenarios, including Unattended Introduction.
- **Pro:** available today — no dependency on deposits, membership rows, or `ConfirmConnectionAsync`.
- **Con:** does not remove the field. `TempWeakKeyStoreKey` should be renamed (e.g.
  `OnlineKeyEncryptedPeerKey`) since "weak" stops being accurate.
- **Follow-up:** records already sealed to `OfflineKey` stay host-readable until re-sealed. Scope is
  limited — `CircleNetworkStorage.cs:112` nulls the escrow after a successful upgrade, so only
  connections the owner has not yet touched are exposed.
- **Caveat:** do **not** apply the same change to `TemporaryWeakClientAccessToken` without first
  guarding `UpgradeTokenEncryptionIfNeededAsync`. It is reached from `GetIcrAsync:417` with no
  master-key guarantee; `OfflineKey` never throws there because the all-zero constant is always
  available, but `OnlineKey` would.
- **Caveat:** `IsValidEccPublicKeyAsync` validates against the *current* key's crc32, so rotating
  the online ECC key would strand outstanding escrows. This risk is pre-existing and equally true of
  `OfflineKey` today, but leaning harder on the field raises its importance.

### C. B now, A later

Ship B as the security fix (small, safe, reversible), then do A once deposits write membership rows
and `ConfirmConnectionAsync` has an acceptable story.

This is the recommended sequencing: B is independently valuable and does not foreclose A.

---

## Blast radius for option A

`UpgradeMasterKeyStoreKeyEncryptionIfNeededInternalAsync` exists solely to consume the field, so
deleting the field means deleting the method. Call sites:

| Site | Enclosing method | Becomes |
|---|---|---|
| `CircleNetworkService.cs:559` | `GrantCircleAsync` | deposit (branch already exists at `:606`) |
| `:732` | `UpdateCircleDefinitionAsync` | deposit |
| `:987` | `ReconcileAuthorizedCircles` | deposit |
| `:1074` | `ConfirmConnectionAsync` | deposit |
| `:1690` | `TryUpgradeMasterKeyStoreKeyEncryptionAsync` wrapper | delete |
| `:1709` | `UpgradeMasterKeyStoreKeyEncryptionForConnectedIdentitiesAsync` sweep | delete |
| `V7ToV8VersionMigrationService.cs:116` | historical migration | must be neutered to compile |
| `V8ToV9VersionMigrationService.cs:356` | historical migration | must be neutered to compile |

The two migration services are a real constraint: they compile against the field, and any identity
still below V9 would lose that upgrade.

Storage cleanup: `IdentityConnectionRegistration.TempWeakKeyStoreKey`,
`CircleNetworkStorage.cs:256`, `:298`, `:112`, and the `WeakKeyStoreKey` column at `:323`.

---

## Open questions

1. Is `AddCircleMemberAsync` safe to call with a keyless/placeholder `CircleGrant`, or is the
   membership table's copy of the grant load-bearing elsewhere? This decides whether the
   membership-row fix is cheap.
2. Is rotation of the online ECC key on the roadmap? If so, option B needs a re-seal story.
3. `ConfirmConnectionAsync` under option A — is a deferred promotion out of `AutoConnectionsCircle`
   acceptable product behaviour, or does the owner need it to take effect immediately?
4. Should `HandleConnectionRequestInternalForIntroductionAsync` keep discarding an available master
   key? Fixing that would shrink scenario 3 out of the problem set independently of everything else.

---

## Sibling problem: `TemporaryWeakClientAccessToken`

Not solved by any of the above, and it is a different shape: the CAT is a credential *we* must
replay later, so something must hold it until the ICR key is available.

The relevant finding is that apps registered with transit permission already hold the raw ICR key —
`AppRegistrationService.cs:55` passes it into `CreateExchangeGrantAsync`, which stores it as
`KeyStoreKeyEncryptedIcrKey`, retrievable at request time via `PermissionsContext.GetIcrKey()` with
no master key. `EncryptedClientAccessToken.Encrypt` needs nothing else. So for scenarios 1-3 the
accept path could set `EncryptedClientAccessToken` directly and skip the escrow, by branching on ICR
key availability rather than `HasMasterKey` (`CircleNetworkRequestService.cs:842`).

Scenario 4 (Unattended Introduction) has no ICR key and cannot be fixed this way. Options there are
to defer the accept until a context with an ICR key handles it, or to keep the escrow with the
option-B re-seal.

---

## Verified vs inferred

**Verified by reading code on `main`:** every line reference and call site in this document; the
all-zero offline key constant; that `WriteOnlyKeyPair` is created unconditionally; that
`TryConvertDepositedGrantsAtPeerAuthAsync` runs before the permission context is built; that
membership rows derive from `CircleGrants` only; that the outbox worker context has neither key;
that introduction and app origins pass `masterKey: null` unconditionally; that apps with transit
permission receive the raw ICR key; that the online ECC keypair is master-key-rooted; that every
reader of `TempWeakKeyStoreKey` holds the master key (including both migrations, which assert it at
entry); that `GetPublicPrivateKeyType` feeds both the wire payload and the local escrow; that
`EccDecryptPayload` dispatches on each payload's own stored `KeyType`.

**Inferred, not verified:** that writing a membership row at deposit time is safe (open question 1);
that deferring `ConfirmConnectionAsync` is acceptable product behaviour (open question 3); the
option-B rotation caveat, which describes a risk rather than an observed failure. No runtime
behaviour was exercised — no tests were run and no server was started for this research. Client-side
behaviour was not examined at all.
