# Distinguishing Vetted (Confirmed) from Unvetted Connections at the SecurityLevel

**Status:** Design note / plan — not yet implemented.

## Problem Statement

Today, `SecurityGroupType.Connected` is granted to *any* connected identity, regardless of
whether they're a member of the Confirmed Connections circle or just auto-connected/introduced.
The client-side "Unvetted" filter in Contacts (issue #919) has to work around this by fetching
circle membership separately and hardcoding the Confirmed Connections circle GUID to compute the
complement itself — fragile, and it doesn't actually restrict what an unvetted connection can
access, only what the UI displays. There's no server-side signal or enforcement distinguishing
"vetted" (confirmed) from "unvetted" (connected but unconfirmed) connections.

## Summary

Two independent pieces of work: (1) expose a `vetted` field on the connection DTO so clients can
read confirmed-vs-not directly (issue #919 — no risk, ships alone), and (2) actually enforce
vetted/unvetted as a security tier by using the existing unused `AutoConnected` SecurityGroupType
instead of collapsing everyone into `Connected` (higher-risk — silently changes what unvetted
callers can see, needs a content audit first).

---

## Plan: Vetted/Unvetted as a real security tier

1. **Assign SecurityLevel from circle membership, not connection status** — at the 3 places a
   `CallerContext` gets built (`CircleNetworkService.cs:149`,
   `Authentication/Transit/TransitAuthenticationService.cs:58`,
   `Controllers/Home/Service/HomeAuthenticatorService.cs:357`), set `Connected` only if
   `icr.IsConfirmedConnection()` is true, else `AutoConnected`.

2. **Centralize that check** — add a small helper (e.g. `CircleNetworkUtils.SecurityLevelFor(icr)`)
   so all 3 sites compute it the same way instead of duplicating the ternary.

3. **Split the ACL switch** — `DriveAclAuthorizationService.cs:105-107` currently collapses
   `AutoConnected`/`Connected` into one `IsConnected()` call; split into two explicit cases
   (`>= AutoConnected` vs `>= Connected`) so direct-file-fetch matches the query-filter behavior.

4. **Redefine `IsConnected`, add `IsConfirmedConnection`** — on `CallerContext.cs`, keep
   `IsConnected => SecurityLevel >= AutoConnected` ("any connection," preserves peer
   transfer/verification for unvetted callers) and add a new getter for confirmed-only checks.

5. **(cosmetic)** add an `AutoConnected` case to `DriveFileUtility.cs`'s priority switch so
   unvetted-ACL'd files sort correctly.

6. **Expose `vetted` on the DTO** (issue #919, independent of the above) — add `Vetted` to
   `RedactedIdentityConnectionRegistration` and set it in
   `IdentityConnectionRegistration.Redacted()` as `IsConnected() && IsConfirmedConnection()`.
   Propagates to all endpoints for free since they all funnel through `Redacted()`.

7. **Audit before flipping the switch** — before step 1 goes live, check how much stored content
   is ACL'd `Connected`-only (feed/profile/connection-scoped drives), since the query range filter
   will silently stop showing it to unvetted callers the moment their level drops to 555.

8. **Tests** — add a case asserting `vetted` is correct for a confirmed vs. unconfirmed-but-connected
   identity, and one confirming an unvetted caller loses access to a `Connected`-ACL'd file via both
   the query path and the direct-fetch path (step 3's whole point).

Step 6 is the original #919 ask and can ship alone; steps 1-5+7 are the enforcement layer and carry
the real risk (step 7 is the gate before doing them).

---

## Does this mean you can enforce security on vetted vs. unvetted?

Yes — that's precisely what steps 1-4 deliver. Once `AutoConnected` (555) is actually assigned to
unvetted callers instead of `Connected` (777), enforcement is real, not cosmetic:

- **Drive queries** — unvetted callers stop seeing any file ACL'd `Connected`-only, automatically,
  via the existing range filter.
- **Direct file fetch** — same enforcement, once step 3 splits the ACL switch (without that split,
  this path would still let unvetted callers through — a real gap, not just an edge case).
- **Anything gated by `AssertCallerIsConnected`** (circle management, verification) — still open to
  unvetted callers by design, because step 4 keeps `IsConnected` as "any connection." That's a
  deliberate carve-out, not a leftover hole — the whole point is to not regress peer transfer for
  auto-connected/unvetted identities. If you also want to deny that, you'd swap the assert to
  `IsConfirmedConnection` at those specific call sites — not yet scoped which of those should switch.

So: yes for drive-content access (the main target), with the two named exceptions above being
conscious opt-in decisions, not omissions. Step 7 (the content audit) is the precondition — you get
real enforcement the moment you flip step 1, whether or not you've checked what it hides.

---

## Background: how `SecurityLevel` is set today

A `CallerContext` is assigned `SecurityGroupType.Connected` whenever an identity is
connected — **regardless of which system circle it is in**. The gate is connection *status*
(`icr.IsConnected()` / `ConnectionStatus.Connected`), never circle membership.

Three construction sites hardcode `Connected`:

- `CircleNetworkService.cs:149` — guest / YouAuth context (gated by `isConnected`)
- `Authentication/Transit/TransitAuthenticationService.cs:58` — peer CAPI (transit)
- `Controllers/Home/Service/HomeAuthenticatorService.cs:357` — home app

Which system circle an identity lands in is decided at **accept time** by
`ConnectionRequestOrigin` (`CircleNetworkUtils.EnsureSystemCircles`):

- `IdentityOwner` → `ConfirmedConnectionsCircleId`
- `Introduction` / `IdentityOwnerApp` → `AutoConnectionsCircleId`

Both still resolve to `SecurityGroupType.Connected`. The only place the two circles are
distinguished on the caller today is `CallerContext.Redacted()`
(`IsGrantedConnectedIdentitiesSystemCircle` checks the *confirmed* circle only), plus
`IdentityConnectionRegistration.IsConfirmedConnection()` (true iff the ICR holds the
confirmed circle grant).

---

## The mechanism is already half-built

`SecurityGroupType` already defines a distinct, lower rung for auto-connections
(`Authorization/Acl/SecurityGroupType.cs`), numerically ordered (not flags):

```
Anonymous=111  <  Authenticated=444  <  AutoConnected=555  <  Connected=777  <  Owner=999
System=1
```

`AutoConnected = 555` exists but is **never assigned to a caller today**. The design is
really "start using the level that already exists."

Critically, the drive query filter is **range-based**:

```csharp
var securityRange = new IntRange(0, (int)odinContext.Caller.SecurityLevel);  // DriveQuery.cs
```

The DB filters `requiredSecurityGroup <= callerLevel`. So the moment an auto caller carries
`555` instead of `777`, they automatically stop seeing any file whose ACL requires
`Connected (777)` — **with zero query changes**. That is the intended win and also the main
risk (a silent, data-driven behavior change).

---

## Audit — every relevant call site, classified

### Category A — Unaffected (555 does not straddle these boundaries)

| Site | Check | Why safe |
|---|---|---|
| `DriveAclAuthorizationService.cs:74` | `== System` | auto ≠ System |
| `DriveAclAuthorizationService.cs:103` | `>= Authenticated` | 555 ≥ 444 ✓ (auto still passes Authenticated ACLs) |
| `CallerContext.cs:123` `AssertCallerIsAuthenticated` | `< Authenticated` | 555 ≮ 444 ✓ |
| `CircleNetworkVerificationService.cs:47` | `== Authenticated` | auto ≠ Authenticated (correct) |
| `AppRegistrationService.cs:394`, `YouAuthDomainRegistrationService.cs:230` | `== Owner` | unaffected |
| `CallerContext.cs:78` `IsAuthenticated` | `== 444` | already false for auto today |
| all `icr.IsConnected()` / `IsConnectedAsync()` | `ConnectionStatus == Connected` | **status, not level** — auto-connected ICRs are still `Connected`; introductions / verification-status logic untouched |

### Category B — The intended change (fires automatically; this is the real blast radius)

- **`DriveQuery.cs:43, 79, 123, 495`** — `new IntRange(0, (int)SecurityLevel)`. The instant an
  auto caller carries `555`, the DB stops returning any file with
  `RequiredSecurityGroup = Connected (777)`. **Silent, wide, data-driven.** No code lists
  these files — the blast radius is however much content was written with a `Connected` ACL
  (feed, profile, connection-scoped drives). This is the effect you want, but it can't be
  enumerated statically; it needs a runtime/data judgment about what is stored `Connected`-only.

- **`DriveAclAuthorizationService.cs:105-107`** — the switch collapses `AutoConnected` and
  `Connected` into one `CallerIsConnected()` call. **Must be split regardless of the getter
  decision:**

  ```csharp
  case SecurityGroupType.AutoConnected: return level >= (int)AutoConnected; // 555+
  case SecurityGroupType.Connected:     return level >= (int)Connected;     // 777 only
  ```

  ⚠️ **Latent inconsistency if skipped:** query filtering (B) excludes auto callers from
  `Connected` files, but a *direct* file fetch goes through this switch. If line 107 still
  says `IsConnected` and the getter has been relaxed, the two paths **disagree** — query hides
  the file, direct-by-id serves it.

### Category C — The pivot: `IsConnected` getter (`CallerContext.cs:77`) and its riders

`IsConnected => SecurityLevel == Connected` flips to **false** for auto callers the moment
they carry `555`. Everything below rides it:

| Site | Gates | If `IsConnected` = confirmed-only |
|---|---|---|
| `PeerIncomingDriveUploadController.cs:243` & `:102` | incoming peer **file upload** | auto-connected **can no longer send you files** |
| `PeerIncomingDriveUpdateController.cs:162` | incoming peer **file update** | same |
| `CircleNetworkService.cs:962` `GetCallerVerificationHash` | verify/heal handshake | auto excluded from verification |
| `CircleNetworkService.cs:188, 1051, 1100` `AssertCallerIsConnected` | connection-management ops | throw for auto |
| `CircleNetworkVerificationService.cs:299` `AssertCallerIsConnected` | verification | throw for auto |

**This is the crux.** Auto-connected identities are granted write to
Chat/Mail/Moments/Lists/Feed by `AutoConnectionsSystemCircleDefinition` — the whole point is
that they can send you files. So defining `IsConnected` as confirmed-only would **regress
peer transfer from auto-connected identities**, which almost certainly is not intended.

The coherent design is therefore:

- Keep `IsConnected => SecurityLevel >= AutoConnected` ("any connection" — preserves
  Category C behavior, zero edits there), **and**
- add `IsConfirmedConnection => SecurityLevel == Connected` (or `>= Connected`) for anything
  that must be confirmed-only.

With that choice, the **only** enforcement of the new distinction is: (1) the query range
filter (automatic), and (2) the split ACL switch. Category C stays intact.

### Cosmetic (not access control)

- `DriveFileUtility.cs:60-74` — a display-priority switch with no `AutoConnected` case;
  `Connected`-ACL files get priority 300, auto-ACL files fall through to default 1000. Worth a
  one-line case for correct sort ordering, but no security impact.

---

## The #919 ask: expose `vetted` on the DTO

Separately from the enforcement plan above, issue #919 asks for a `vetted: boolean` field on
`RedactedIdentityConnectionRegistration` so the Contacts UI can read confirmed-vs-not directly
instead of cross-referencing circle membership and a hardcoded circle GUID client-side.

**Definition:** a connection is vetted if it is currently connected AND a member of the
Confirmed Connections circle. This is a full complement over all connected identities — not
restricted to auto-connected/introduced identities.

**The fix is one property in one method.** `IdentityConnectionRegistration.Redacted()`
(`src/services/Odin.Services/Membership/Connections/IdentityConnectionRegistration.cs:123-138`)
is the single choke point every relevant endpoint funnels through:

```csharp
public RedactedIdentityConnectionRegistration Redacted(bool omitContactData = true)
{
    return new RedactedIdentityConnectionRegistration()
    {
        ...
        Vetted = this.IsConnected() && this.IsConfirmedConnection(),
    };
}
```

Plus `public bool Vetted { get; init; }` on `RedactedIdentityConnectionRegistration`.

**Why `IsConnected() && IsConfirmedConnection()`, not just `IsConfirmedConnection()`:**
`IsConfirmedConnection()` only checks whether `AccessGrant.CircleGrants` contains the
confirmed-circle GUID — it doesn't check `Status`. Since `Redacted()` is also called for
blocked-profile results, a blocked identity that still happens to hold the confirmed-circle
grant would otherwise read `vetted: true`, contradicting the "currently connected" part of the
definition. Gating on both is free and removes the ambiguity.

**Propagation is automatic** — no controller edits needed, since every endpoint below calls
`.Redacted()` on the ICR rather than doing its own mapping:

- `GET /api/v2/connections/connected` — `V2ConnectionNetworkController.GetConnectedIdentities`
- V1 `GET/POST status`, `POST connected`, `POST blocked` on `CircleNetworkControllerBase.cs`,
  inherited by `OwnerCircleNetworkController` and `AppCircleNetworkController`
- Guest/App shared `CircleNetworkController.cs` `GET connected`
- `IcrTroubleshootingInfo` (wraps a `RedactedIdentityConnectionRegistration`)

**Not in scope:** `GetCirclesWithMembers` (`GET /api/v2/connections/circles/with-members`) —
the endpoint the client currently cross-references with the hardcoded GUID — returns
`CircleWithMembers { RedactedCircleDefinition Circle; List<OdinId> Members }`, a different
shape that doesn't need a `Vetted` field; it just becomes redundant once the client reads
`vetted` directly off `/connections/connected`.

**Blast radius:** additive DTO field — existing Refit-based test clients deserialize into typed
C# classes, so nothing breaks. No existing test asserts the JSON shape/field-count of this DTO.
No frontend lives in this repo to update. No checked-in OpenAPI/swagger file to maintain (spec
is generated at runtime).

---

## Bottom line

The mechanical edit for enforcement is small: **3 construction sites** + **1 ACL switch split** +
**1 getter decision** (+ 1 cosmetic). The #919 DTO exposure is even smaller: **1 method, 2 edits**,
and can ship independently and immediately. The enforcement work is not the diff — it is two
judgment calls:

- **(a)** Keep `IsConnected` = "any connection" so peer transfer / verification for
  auto-connected does not regress (recommended).
- **(b)** Confirm which stored content is `Connected`-ACL, since the query filter will
  silently hide it from auto-connected callers.

Those two decisions separate a clean ~5-file change from a subtle production behavior shift.

---

## Key file references

- `src/services/Odin.Services/Authorization/Acl/SecurityGroupType.cs` — enum
- `src/services/Odin.Services/Authorization/Acl/DriveAclAuthorizationService.cs` — ACL check
- `src/services/Odin.Services/Authorization/Acl/AccessControlList.cs` — `RequiredSecurityGroup`
- `src/services/Odin.Services/Base/CallerContext.cs` — `SecurityLevel`, `IsConnected`, asserts
- `src/services/Odin.Services/Drives/DriveCore/Query/DriveQuery.cs` — range filter
- `src/services/Odin.Services/Membership/Connections/CircleNetworkService.cs` — context build, verification
- `src/services/Odin.Services/Membership/Connections/CircleNetworkUtils.cs` — origin→circle
- `src/services/Odin.Services/Membership/Connections/IdentityConnectionRegistration.cs` — `IsConfirmedConnection()`, `Redacted()`
- `src/services/Odin.Services/Membership/Circles/CircleConstants.cs` — system circle ids/defs
- `src/apps/Odin.Hosting/Controllers/PeerIncoming/Drive/PeerIncomingDriveUpload/UpdateController.cs` — peer transfer gates
- `src/services/Odin.Services/Authentication/Transit/TransitAuthenticationService.cs` — transit context
- `src/apps/Odin.Hosting/Controllers/Home/Service/HomeAuthenticatorService.cs` — home app context
- `src/apps/Odin.Hosting/UnifiedV2/Connections/V2ConnectionNetworkController.cs` — V2 connections endpoints
