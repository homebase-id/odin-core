# Distinguishing AutoConnected from Connected at the SecurityLevel

**Status:** Design note / audit — not yet implemented.
**Question:** What would it look like to treat an identity as `Connected` **only** if it is
in `SystemCircleConstants.ConfirmedConnectionsCircleId`, and give auto-connected identities
(in `AutoConnectionsCircleId`) a lower security level?

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

## Recommended implementation path

1. **Set the level from circle membership at the 3 construction sites.** The ICR already
   knows via `IdentityConnectionRegistration.IsConfirmedConnection()`:

   ```csharp
   securityLevel: icr.IsConfirmedConnection()
       ? SecurityGroupType.Connected
       : SecurityGroupType.AutoConnected,
   ```

   - `CircleNetworkService.cs:149` — has `icr` in scope.
   - `HomeAuthenticatorService.cs:357`.
   - `TransitAuthenticationService.cs:58` — builds from `circleIds`; check
     `circleIds.Contains(ConfirmedConnectionsCircleId)`, or thread the icr in.

   Best centralized as a helper (e.g. `CircleNetworkUtils.SecurityLevelFor(...)`) so all three
   agree.

2. **Getter semantics (the key decision).** Recommended:
   `IsConnected => SecurityLevel >= AutoConnected`; add `IsConfirmedConnection`. This avoids
   regressing peer transfer / verification for auto-connected identities.

3. **Split the ACL switch** in `DriveAclAuthorizationService.cs:105-107` (see Category B) so the
   `Connected` case is `>= Connected` and does not just call `IsConnected`.

4. **(cosmetic)** add an `AutoConnected` case to `DriveFileUtility.cs` priority switch.

---

## Bottom line

The mechanical edit is small: **3 construction sites** + **1 ACL switch split** + **1 getter
decision** (+ 1 cosmetic). The work is not the diff — it is two judgment calls:

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
- `src/services/Odin.Services/Membership/Connections/IdentityConnectionRegistration.cs` — `IsConfirmedConnection()`
- `src/services/Odin.Services/Membership/Circles/CircleConstants.cs` — system circle ids/defs
- `src/apps/Odin.Hosting/Controllers/PeerIncoming/Drive/PeerIncomingDriveUpload/UpdateController.cs` — peer transfer gates
- `src/services/Odin.Services/Authentication/Transit/TransitAuthenticationService.cs` — transit context
- `src/apps/Odin.Hosting/Controllers/Home/Service/HomeAuthenticatorService.cs` — home app context
