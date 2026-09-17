# App registration V2: an app declares the drives and circles it owns

Status: **plan**, 2026-09-16. Registration decisions (D1–D6) settled; update (U1–U6), token (T1–T9)
and UI (UI1–UI7) decisions proposed. Nothing implemented.

## Goal

When an app is registered, the registration can also say which drives and circles the app **owns**. The
server creates them with `AppId` set to that app. This is the same idea as a `WellknownAppDefinition`
(`Apps/Builtin/WellknownAppDefinition.cs`), but the app supplies it at runtime instead of it being
compiled into the tree. Afterwards, the registration and everything it owns can be **updated** through V2.

**Dark launch.** V1 registration and update stay exactly as they are: `AppRegistrationController`,
`AppRegistrationService`, `AppRegistrationRequest`, `UpdateAppPermissionsRequest` and
`UpdateAuthorizedCirclesRequest` are unchanged. V2 adds new endpoints with new request types.

## Ground rule: additive only

The dark launch is safe only if nothing existing changes behaviour. The new code just sits there
until something calls it. **Any step that modifies existing code must be listed in
[Existing code touched](#existing-code-touched) with a warning**, and changes to this plan must keep
that section up to date.

## Where things stand (verified in code)

### Registration and creation

| Fact | Where |
|---|---|
| The tree already expresses "app owns drives + circles" | `WellknownAppDefinition(AppId, Name, AppSlug, Drives, Circles, Permissions)` |
| Drives carry ownership: `CreateDriveRequest.AppId / DriveSlug / DriveTypeSlug` → columns on `Drives` | `CreateDriveRequest.cs`, `DriveManager.CreateDriveAsync` |
| `CreateDriveAsync` takes `AppId` on trust and does not require the app to exist | `DriveManager.cs:125` comment |
| Drive slug is refused if taken within the same app (`UNIQUE(identityId, AppId, DriveSlug)`) | `DriveManager.cs:143-177` |
| `CircleMembershipService.CreateCircleDefinitionAsync` → `CircleDefinitionService.CreateAsync`, then publishes `CircleDefinitionChangedNotification(Created)`. This is the path the owner controller uses. | `CircleMembershipService.cs:295-306`, `CircleDefinitionControllerBase.cs:51` |
| `CircleDefinitionService.CreateAsync` **validates** (drive exists, not owner-only, permission keys) and copies `AppId`, `GrantOn`, `Designation`, `Emoji` onto the row | `CircleDefinitionService.cs:43, 532-563` |
| Drive adoption: `SetDriveOwningAppAsync` refuses a drive that already has an owner | `DriveManager.cs:530` |
| `IDriveManager.GetDrivesByAppIdAsync` exists | `IDriveManager.cs:44` |
| No circles-by-AppId query exists (`TableCircleCached` has `GetAllAsync`, `GetByGrantOnAsync`), though `Idx0Circle(identityId, AppId)` does | `TableCircleCached.cs`, `TableCircleCRUD.cs:91` |
| Required order is drives (non-anonymous first) → circles → registration, and each step depends on the one before | `BuiltinProvisioner.EnsureAllAsync` remarks |
| `CreateDriveAsync` is not purely a DB write: it creates directories and publishes `DriveDefinitionAddedNotification`, whose handlers re-grant circles | `DriveManager.cs:236-246`, `BuiltinProvisioner.cs:124` |
| `RegisterAppAsync` requires the master key, so it is owner-only | `AppRegistrationService.cs:42` |
| `DeleteAppAsync` deletes only the registration row | `AppRegistrationService.cs:469` |
| V2 has an owner-only policy, `UnifiedPolicies.Owner` | `UnifiedPolicies.cs` |
| `/api/v2/apps/{appSlug}/...` is already used for slug addressing | `UnifiedApiRouteConstants.cs:30` |
| Tenant services are registered in `TenantServices.cs` (`BuiltinProvisioner` at :208, `AppRegistrationService` at :313) | `TenantServices.cs` |

### Updates

| Fact | Where |
|---|---|
| V1 has exactly two registration updates: `UpdateAppPermissionsAsync` (replaces `PermissionSet` + drive grants and rebuilds `AppKeyStore` with the same key-store key) and `UpdateAuthorizedCirclesAsync` (replaces `AuthorizedCircles` + `CircleMemberPermissionGrant`) | `AppRegistrationService.cs:97-193` |
| `UpdateAppPermissionsAsync` resets the app permission-context cache but publishes **no** notification | `AppRegistrationService.cs:136-138` |
| `UpdateAuthorizedCirclesAsync` publishes `AppRegistrationChangedNotification`; `CircleNetworkService` handles it with `ReconcileAuthorizedCircles`, which rewrites every affected member's ICR app grants in a stacked transaction | `AppRegistrationService.cs:191`, `CircleNetworkService.cs:1840, 1957` |
| `UpdateAuthorizedCirclesAsync` force-keeps the tree circles for **Chat, Mail and Feed** | `AppRegistrationService.cs:151-173` |
| There is **no** V1 update for `Name`, `CorsHostName` or `AppSlug`; `AppSlug` is immutable by design | `AppRegistrationService.cs:178` comment |
| The current drive grants are readable: `RedactedAppRegistration.Grant.DriveGrants[].PermissionedDrive` | `RedactedAppRegistration`, `KeyStore.cs:41`, `DriveGrant.cs:34` |
| Circle update that reaches members is `CircleNetworkService.UpdateCircleDefinitionAsync`: it re-mints the circle grant for every member, then calls `CircleDefinitionService.UpdateAsync`, then publishes `CircleDefinitionChangedNotification(Updated)` | `CircleNetworkService.cs:983-1072` |
| That method checks only `AssertValidDriveGrantsAsync` before re-granting members. The permission-set check and `AssertDepositOnlyIfAmbientAsync` run inside `UpdateAsync`, **after** the member ICRs are rewritten. | `CircleNetworkService.cs:985`, `CircleDefinitionService.cs:305-336` |
| `CircleDefinitionService.UpdateAsync` takes Name, Description, DriveGrants, Permissions, GrantOn, Designation, Emoji, and never `AppId` | `CircleDefinitionService.cs:319-330` |
| Circle delete: `CircleNetworkService.DeleteCircleDefinitionAsync` refuses a circle with members, then publishes `CircleDefinitionChangedNotification(Deleted)` | `CircleNetworkService.cs:1808-1826` |
| Mutable drive settings each have their own `DriveManager` method: `SetDriveReadModeAsync`, `SetDriveAllowSubscriptionsAsync`, `SetDriveAllowCdnAsync`, `SetArchiveDriveFlagAsync`, `UpdateMetadataAsync`, `UpdateAttributesAsync`. The first two and archive refuse `BuiltinDrives.Protected`. | `DriveManager.cs:251-500, 740` |
| There is **no** drive delete and no drive rename in `DriveManager` | `DriveManager.cs` public surface |

## Decisions: registration (settled)

| # | Question | Decision |
|---|---|---|
| D1 | Does owning a drive grant the app access? | **Yes, implicitly.** Each owned drive is merged into the registration's drive grants as `ReadWrite`, unless `Drives` already lists that drive, in which case the explicit permission wins. |
| D2 | An owned drive or circle already exists | Missing → create. Same `AppId` → accept, leave as-is. `AppId` null → **reject** (adoption stays the explicit owner action). Other `AppId` → reject. |
| D3 | `AppId` already registered | **Reject.** Changes go through the V2 update endpoints below, not an upsert. |
| D4 | `AppId` declared by the tree (`BuiltinApps.Get(appId) != null`) | **Reject.** Every upgrade re-applies tree ownership and grant rules (`ApplyTreeDefinitionAsync`), which would silently overwrite a runtime declaration. |
| D5 | Required fields | `AppSlug`, and `DriveSlug`/`DriveTypeSlug` on every owned drive, are **required** in v2. |
| D6 | Dark-launch gate | **No flag.** As long as the work is additive, the code simply exists to be called. If the implementation ever needs to change existing code, that is exactly what [Existing code touched](#existing-code-touched) must warn about. |

## Decisions: updates (proposed; recommendation shown)

| # | Question | Recommendation |
|---|---|---|
| U1 | Which apps can the V2 update endpoints act on? | **Any registered app, including ones registered through V1, except:** tree apps (D4, same reason) and **Mail** (`MailAppId`, not in the tree but force-kept circles in `UpdateAuthorizedCirclesAsync`). Those stay on V1. Letting V1-registered third-party apps start declaring owned drives and circles is how existing apps migrate to V2. |
| U2 | Shape: one desired-state `PUT` of the whole registration, or separate endpoints per concern? | **Separate endpoints.** Each maps to one existing method with known side effects (key-store rebuild, member re-grant, ICR reconciliation), so failures can be traced to one step and a partial failure stays small. A desired-state `PUT` would chain all of those non-transactional side effects in one call and need a diff engine. |
| U3 | D1 on update | **Re-applied on every permissions update.** Owned drives are merged back in as `ReadWrite` unless the request lists them explicitly, so the owner can lower access to an owned drive but not accidentally drop it by omission. Adding an owned drive also triggers the same merge. |
| U4 | Removing an owned drive | **Not supported.** No drive delete exists, and adding one is a change to `DriveManager` (and to storage cleanup). Archive it through the drive update endpoint instead. Disowning stays the owner's `reassign-owner` action. |
| U5 | Removing an owned circle | **Allowed through `CircleNetworkService.DeleteCircleDefinitionAsync`**, which already refuses a circle with members. Also **reject** if the circle is in the app's `AuthorizedCircles`; the owner removes it there first. Doing that automatically would chain a second non-transactional call. |
| U6 | `Name`, `CorsHostName`, `AppSlug`; drive `Name`, `TargetDrive`, `DriveSlug`, `DriveTypeSlug`, `OwnerOnly` | **Immutable in V2.** `AppSlug` and the drive slugs are addresses (immutable by design). The others have no existing update method, and adding one means changing `AppRegistrationService` or `DriveManager`. If you want `Name`/`CorsHostName` editable, it goes in the ⚠️ table. |

## Design

### 1. Request types (new files)

```csharp
public class AppRegistrationRequestV2
{
    public GuidId AppId { get; set; }
    public string Name { get; set; }
    public string AppSlug { get; set; }            // required (D5)
    public string CorsHostName { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<DriveGrantRequest> Drives { get; set; }          // access, as v1
    public List<Guid> AuthorizedCircles { get; set; }            // as v1
    public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }

    public List<OwnedDriveRequest> OwnedDrives { get; set; }
    public List<OwnedCircleRequest> OwnedCircles { get; set; }
}

public class OwnedDriveRequest   // CreateDriveRequest minus AppId
{
    public string Name; public TargetDrive TargetDrive; public string Metadata;
    public bool AllowAnonymousReads; public bool AllowSubscriptions; public bool AllowCdn; public bool OwnerOnly;
    public string DriveSlug;        // required (D5)
    public string DriveTypeSlug;    // required (D5)
    public Dictionary<string,string> Attributes;
}

public class OwnedCircleRequest  // CreateCircleRequest minus AppId; also the circle update body
{
    public Guid Id; public string Name; public string Description;
    public IEnumerable<DriveGrantRequest> DriveGrants; public PermissionSet Permissions;
    public CircleGrantOn GrantOn; public CircleDesignation Designation; public string Emoji;
}

// ---- updates ----

public class UpdateAppPermissionsRequestV2       // replaces, as v1
{
    public PermissionSet PermissionSet { get; set; }
    public List<DriveGrantRequest> Drives { get; set; }   // owned drives re-merged per U3
}

public class UpdateAuthorizedCirclesRequestV2    // replaces, as v1
{
    public List<Guid> AuthorizedCircles { get; set; }
    public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }
}

public class UpdateOwnedDriveRequest             // null = leave unchanged
{
    public bool? AllowAnonymousReads; public bool? AllowSubscriptions; public bool? AllowCdn;
    public bool? IsArchived; public string Metadata; public Dictionary<string,string> Attributes;
}
```

The owned-drive and owned-circle types have **no `AppId` field**. The owner always comes from the
registration or the route, so a request cannot name a different one. `AppId` is in the route for
every update, not the body.

### 2. Service: new `AppInstallService` (new file, `Odin.Services/Apps/`)

Dependencies: `IDriveManager`, `CircleDefinitionService`, `CircleMembershipService`,
`CircleNetworkService`, `IAppRegistrationService`. The service is a new top-level consumer that
nothing depends on, so it cannot introduce a dependency cycle (inferred from that fact; I haven't
built it). Everything below calls **existing public methods, unmodified**.

```csharp
Task<AppInstallResult>  RegisterAppV2Async(AppRegistrationRequestV2 request, IOdinContext ctx)
Task<AppInstallResult?> GetAppV2Async(Guid appId, IOdinContext ctx)

Task UpdatePermissionsV2Async(Guid appId, UpdateAppPermissionsRequestV2 request, IOdinContext ctx)
Task UpdateAuthorizedCirclesV2Async(Guid appId, UpdateAuthorizedCirclesRequestV2 request, IOdinContext ctx)

Task<OwnedDriveResult>  AddOwnedDriveAsync(Guid appId, OwnedDriveRequest request, IOdinContext ctx)
Task                    UpdateOwnedDriveAsync(Guid appId, Guid driveId, UpdateOwnedDriveRequest request, IOdinContext ctx)

Task<RedactedCircleDefinition> AddOwnedCircleAsync(Guid appId, OwnedCircleRequest request, IOdinContext ctx)
Task                    UpdateOwnedCircleAsync(Guid appId, Guid circleId, OwnedCircleRequest request, IOdinContext ctx)
Task                    DeleteOwnedCircleAsync(Guid appId, Guid circleId, IOdinContext ctx)
```

#### 2a. `RegisterAppV2Async`

1. **`AssertHasMasterKey`.**
2. **Validate everything before writing anything:**
   - app: `AppId` not empty; not registered (D3); not in the tree (D4); `Name` non-blank; `AppSlug`
     valid (`OdinSlug`) and not held by another registration; CORS valid.
   - owned drives: slugs present and valid (D5); no duplicate `TargetDrive` or `DriveSlug` within the
     request; neither `OwnerOnly && AllowAnonymousReads` nor `OwnerOnly && AllowSubscriptions`; the
     D2 table applied to each existing drive (`driveManager.GetDriveAsync(alias)`).
   - owned circles: no duplicate ids within the request; the D2 table applied to each existing
     circle (`circleDefinitionService.GetCircleAsync`); each `DriveGrant` names a drive that exists
     or is in `OwnedDrives`; no grant on an owner-only drive.
   - **Confused-deputy rule** (`drive-addressing.md`, Circles): a circle's `DriveGrants` may name
     only drives this app owns (in the request, or already carrying this `AppId`) or drives listed
     in `Drives`.
   - The permission-set / non-empty-grant checks `CreateAsync` performs are repeated here so they
     also fail before the first write.
3. **Create drives** that D2 says to create, non-anonymous first, via `driveManager.CreateDriveAsync(new CreateDriveRequest { AppId = appId, ... })`.
4. **Create circles** that D2 says to create, via
   `circleMembershipService.CreateCircleDefinitionAsync(new CreateCircleRequest { AppId = appId, ... })`.
   This is the same path the owner controller uses, so the `CircleDefinitionChangedNotification`
   fires. (An earlier draft called `CircleDefinitionService.CreateAsync` directly, which would have
   skipped it.)
5. **Register**: build a V1 `AppRegistrationRequest` (owned drives merged into `Drives` per D1) and
   call the existing `RegisterAppAsync`.
6. Return `AppInstallResult { Registration, OwnedDrives: [{driveId, targetDrive, driveSlug, driveTypeSlug}], OwnedCircles: [RedactedCircleDefinition] }`.

#### 2b. `GetAppV2Async`

`GetAppRegistration` + `GetDrivesByAppIdAsync` +
`circleDefinitionService.GetCirclesAsync(includeSystemCircle: false)` filtered in memory on `AppId`.
Circles number in the tens per identity, and filtering in memory avoids touching the table layer.

#### 2c. Common guard for every update

1. `AssertHasMasterKey`.
2. The app is registered → else 404 (`AppNotRegistered`).
3. U1: not a tree app, not `MailAppId` → else 400.
4. For drive or circle endpoints: the target exists and its `AppId == appId` → else 404 for missing
   or 400 for owned by someone else. An unowned (null) target is refused as well; adoption stays the
   owner's `set-owner` action.

#### 2d. `UpdatePermissionsV2Async`

1. Guard.
2. Validate: every `Drives` entry is a valid target drive that exists.
3. Merge owned drives (`GetDrivesByAppIdAsync`) per U3.
4. Call existing `UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest { AppId, PermissionSet, Drives = merged })`.

This call is a single step with the same side effects as V1: the key store is rebuilt and the cache
reset.

#### 2e. `UpdateAuthorizedCirclesV2Async`

1. Guard.
2. Validate: every circle id exists; no system circle (the tree excludes them from app ownership; see
   the note below).
3. Call existing `UpdateAuthorizedCirclesAsync`.

Its Chat/Mail/Feed special-casing never fires, because U1 already excluded those apps.

> Note: V1 lets Chat and Mail authorize the system circles. Refusing them in V2 is a stricter rule
> for a path V1 doesn't cover. Drop the check if a third-party app has a reason to authorize them.

#### 2f. `AddOwnedDriveAsync`

1. Guard.
2. Validate as in 2a step 2 (slugs required, flag combinations, D2 against existing drives).
3. `CreateDriveAsync` with `AppId = appId`.
4. Re-run the U3 merge by reading the current `Grant` (`PermissionSet` + `DriveGrants`) and calling
   `UpdateAppPermissionsAsync` with the new drive added as `ReadWrite`.

**Two non-transactional steps.** If step 4 fails, the drive exists and is owned but not yet granted.
Retrying `POST` hits D2 ("same `AppId` → accept") and runs step 4 again, so it is recoverable.

#### 2g. `UpdateOwnedDriveAsync`

1. Guard.
2. For each non-null field, call the matching existing `DriveManager` method:

| Field | Method |
|---|---|
| `AllowAnonymousReads` | `SetDriveReadModeAsync` |
| `AllowSubscriptions` | `SetDriveAllowSubscriptionsAsync` |
| `AllowCdn` | `SetDriveAllowCdnAsync` |
| `IsArchived` | `SetArchiveDriveFlagAsync` |
| `Metadata` | `UpdateMetadataAsync` |
| `Attributes` | `UpdateAttributesAsync` |

Pre-validate the combinations first (owner-only drives can't allow anonymous reads or
subscriptions) so none of the setters refuses partway through. The setters are independent writes,
so a mid-way server fault can leave some applied; a retry is idempotent (the setters only write on
change, verified for archive; I haven't checked the others).

#### 2h. `AddOwnedCircleAsync` / `UpdateOwnedCircleAsync`

1. Guard (update: circle exists and is owned by `appId`; add: D2 against an existing id).
2. **Pre-validate everything**, including the confused-deputy rule (grants only drives the app owns
   or is granted), the permission-set check, and `AssertDepositOnlyIfAmbientAsync` (public).
   - This matters more for update than for create. `UpdateCircleDefinitionAsync` rewrites every
     member's ICR **before** the permission and deposit-only checks run inside `UpdateAsync`, so a
     request that fails those checks today leaves members re-granted against a definition that was
     never saved. Pre-validating in V2 makes that unreachable from this path. The V1 ordering itself
     is not changed (that would be an existing-code change).
3. Add: `circleMembershipService.CreateCircleDefinitionAsync` with `AppId = appId`.
   Update: load the existing definition, overlay the request fields, and call
   `circleNetworkService.UpdateCircleDefinitionAsync`. `AppId` is never read from the request, and
   `UpdateAsync` ignores it anyway.

#### 2i. `DeleteOwnedCircleAsync`

1. Guard.
2. U5: reject if the circle is in the app's `AuthorizedCircles`.
3. `circleNetworkService.DeleteCircleDefinitionAsync`, which refuses when the circle has members.

### 3. Endpoints (new controller, `UnifiedV2/Apps/V2AppRegistrationController.cs`)

`[UnifiedV2Authorize(UnifiedPolicies.Owner)]` on the controller.

| Verb | Route (`/api/v2/app-registrations` …) | Service method |
|---|---|---|
| `POST` | `/` | `RegisterAppV2Async` |
| `GET` | `/{appId:guid}` | `GetAppV2Async` (404 when not registered) |
| `PUT` | `/{appId:guid}/permissions` | `UpdatePermissionsV2Async` |
| `PUT` | `/{appId:guid}/authorized-circles` | `UpdateAuthorizedCirclesV2Async` |
| `POST` | `/{appId:guid}/owned-drives` | `AddOwnedDriveAsync` |
| `PATCH` | `/{appId:guid}/owned-drives/{driveId:guid}` | `UpdateOwnedDriveAsync` |
| `POST` | `/{appId:guid}/owned-circles` | `AddOwnedCircleAsync` |
| `PUT` | `/{appId:guid}/owned-circles/{circleId:guid}` | `UpdateOwnedCircleAsync` |
| `DELETE` | `/{appId:guid}/owned-circles/{circleId:guid}` | `DeleteOwnedCircleAsync` |

A separate root, not `/api/v2/apps/...`, so it can never collide with `/apps/{appSlug}`. Revoke,
un-revoke, client management and app delete stay on V1; none of them involve ownership.

### 4. Atomicity

None of this is transactional end to end: drive creation makes directories and publishes
notifications, circle updates rewrite member ICRs one by one, and authorized-circle updates reconcile
through a notification handler. The approach is the same for every endpoint:

- **Validate the whole request up front**, so any failure a client can cause happens before the
  first write.
- A failure after that is a server fault, and **every endpoint is safe to retry**:
  - register: D2 accepts what already exists, and D3 rejects once the install is complete.
  - add drive: D2 accepts the drive, then the grant merge runs again.
  - permissions / authorized circles: full replacement, so idempotent.
  - drive update: setters only write on change.
  - circle update: re-mints grants from the same definition.
- No outer `BeginStackedTransactionAsync`: notification handlers would run inside it, which risks
  lock scope and the scoped-connection "Parallelism detected" rule (CLAUDE.md). I haven't checked
  whether those handlers run synchronously.
- **Concurrency:** permissions update and add-drive both read the current grant, modify it, and write
  it back. Two concurrent calls on the same app can lose one's changes. This is owner-console-only
  traffic, so the plan accepts it, but it's worth knowing.

### 5. Tests (`Odin.Hosting.Tests.V2`)

A new partial-class file, `Api/OwnerAdmin.AppsV2.cs`, holds helpers for every endpoint. Existing
`OwnerAdmin.Apps.cs` is untouched.

Registration:
- happy path: drives created with `AppId`/slugs; circles with `AppId`/`GrantOn`/`Designation`; registration exists; GET returns all three; a `CircleDefinitionChangedNotification(Created)` is published per circle (if the fast framework can observe it)
- D1: owned drive granted `ReadWrite` implicitly; an explicit entry in `Drives` overrides it
- D2: existing drive/circle with same `AppId` accepted; unowned → 400; other app → 400; **nothing created on any 400**
- D3: registering twice → 400
- D4: tree `AppId` (e.g. `SystemAppConstants.PhotoAppId`) → 400
- D5: missing `AppSlug` / `DriveSlug` / `DriveTypeSlug` → 400
- confused deputy: circle granting a drive the app neither owns nor is granted → 400, nothing created
- duplicate `DriveSlug` within the request → 400

Updates:
- U1: every update endpoint → 400 for a tree app and for `MailAppId`; works on an app registered through **V1**
- guard: unregistered app → 404; drive/circle owned by another app → 400; unowned drive/circle → 400
- permissions (U3): omitting an owned drive keeps it `ReadWrite`; an explicit lower permission sticks; an app token sees the new permissions (cache reset)
- authorized circles: members of an added circle receive the app grant; members of a removed circle lose it
- add owned drive: drive created with `AppId`, and the app can write to it with its existing client token
- update owned drive: each flag changes; owner-only + anonymous → 400 with **no** flag applied
- add/update owned circle: confused-deputy → 400; **update with an invalid permission set → 400 and no member ICR changed**; a valid update re-grants an existing member
- delete owned circle: with members → 400; in `AuthorizedCircles` → 400 (U5); otherwise gone
- U6: nothing in the V2 surface can change `AppSlug`, `Name`, `CorsHostName`, drive slugs or `TargetDrive` (asserted by reading back after each update)

Both:
- every endpoint with an app token and a guest token → 401/403
- V1 app registration tests: unchanged and still passing

## V2 app tokens: one token, many apps

### Goal

A client (say the chat app, which also has vault and webdrop features) holds **one token** that maps
to **several registered apps** on the backend. The token's access is the union of those apps' grants:
their permission keys, their drive grants (owned drives included, per D1/U3), and their ICR keys for
transit. Built as new classes and new tables. V1 app tokens (`AppClientRegistration` in
`ClientRegistrations`) are untouched and keep working.

### How V1 tokens work today (verified in code)

| Fact | Where |
|---|---|
| An app's grant is a `KeyStore`: random 16-byte key-store key (KSK) encrypted with the master key; per-drive grants whose storage keys are encrypted with the KSK; optional ICR key encrypted with the KSK | `ExchangeGrantService.CreateExchangeGrantAsync` |
| A client is issued by splitting a random client key with XOR: the server keeps its half, the shared secret encrypted with the client key, and the **one** KSK encrypted with the client key. The client gets `{id, client half, shared secret, ClientTokenType.App}`. | `ExchangeGrantService.CreateClientAccessTokenInternal` (private) |
| The client key only exists inside that private method and is wiped there, so its result cannot be extended to wrap more than one KSK | same |
| Stored as `AppClientRegistration` (type 200, grouped by app id, 365-day TTL) in `ClientRegistrations` | `AppClientRegistration.cs`, `ClientRegistrationStorage.cs` |
| Issuance paths: YouAuth `Authorize` → `YouAuthUnifiedService.CreateClientAccessTokenAsync` (ECDH exchange, token cached 5 min under the secret digest); owner `register/client-ecc` + `register/client-ecc-exchange` | `YouAuthUnifiedController.cs`, `AppRegistrationController.cs` |
| Per request: V1 `YouAuthAuthenticationHandler` and V2 `AppAuthPathHandler` call `GetAppPermissionContextAsync`, which loads the `AppClientRegistration` **by token id** and the registration, builds one permission group from `AppKeyStore` plus anonymous drives plus implied keys, and caches the context 60 min | `AppRegistrationService.cs:244-298`, `ExchangeGrantService.CreatePermissionContext` |
| **`PermissionGroup` carries its own KSK and encrypted ICR key**, and storage-key and ICR lookups resolve per group. A context can hold several groups, each unlocked by a different key. | `PermissionGroup.cs:23-33, 99-120`; `PermissionContext.GetIcrKey`, `TryGetDriveStorageKey` |
| `PermissionContext` also has a context-level KSK; its only consumer is `CreatePeerIcrClientForCallerAsync`, a connected-peer path, not an app path | `CircleNetworkService.cs:2359` |
| V2 dispatches on `token.ClientTokenType` through a `switch` in `GetHandler`; unknown types fail. The success claim is `token.ClientTokenType`, and `OwnerOrApp` / `OwnerOrAppOrGuest` list the accepted types explicitly. | `UnifiedAuthenticationHandler.cs:309-331, 158-161`; `UnifiedPolicies.cs` |
| The V2 notification WebSocket switches on `ClientTokenType.App` and calls `GetAppPermissionContextAsync` directly | `V2NotificationSocketController.cs:185-194` |
| `OdinClientContext.AppId` is **single-valued** and is the "which app is acting" check in 13 places: circle ownership (`CircleNetworkService` ×8, `CircleMembershipService`), scheduled notifications, WebSocket device sockets, live relay | grep `OdinClientContext?.AppId` |
| `OdinContextCache.GetOrAddContextAsync` takes a `keySuffix`, so one token can have several cached contexts. `ResetAsync` clears every context by tag, which is what app revoke and permission updates call. | `OdinContextCache.cs:54-93` |
| `AccessRegistrationId` is looked up as an `AppClientRegistration` by `GetCallingAppIdAsync`, `RevokeClientAsync`, `DeleteCurrentAppClientAsync` (app logout) etc.; push device subscriptions key on it as a plain Guid | `AppRegistrationService.cs:371-467`, `PushNotificationService.cs` |
| Tables are generated by the external **Odin-SQLite-Generator**. Adding one means: a generator PR first, generated `Table*CRUD.cs` + `Table*.cs` + `Table*MigrationList` + `…MigrationV000000000000`, a line each in `IdentityDatabase.Generated.cs` and `IdentityMigrator.Generated.cs`, and **`DataImporter` registration** (four tests failed without it) | commit `67732fcee` (AppRegistrations table) |
| `AppRegistrationService.FromRecord` is `internal static`, so new code in `Odin.Services` can load the full `AppRegistration` (with `AppKeyStore`) from `db.AppRegistrations` without changing the service | `AppRegistrationService.cs:692` |
| A revoked **client** keeps authenticating until its cached context expires (up to 60 min): `RevokeClientAsync` saves through `ClientRegistrationStorage.SaveAsync`, which does not reset the cache | `AppRegistrationService.cs:400-412`, `ClientRegistrationStorage.cs` |

### Design

#### Tables (new; generator PR first)

```sql
-- One row per issued token (the server half).  Mirrors ServerHalfOfClientKey, as columns.
AppTokens
  identityId                     BYTEA  NOT NULL
  tokenId                        BYTEA  NOT NULL   -- the ClientAuthenticationToken.Id
  primaryAppId                   BYTEA  NOT NULL   -- default acting app; CORS source (T3, T4)
  friendlyName                   TEXT   NOT NULL
  serverHalfOfKey                TEXT   NOT NULL   -- SymmetricKeyEncryptedXor, json
  clientKeyEncryptedSharedSecret TEXT   NOT NULL   -- SymmetricKeyEncryptedAes, json
  isRevoked                      BOOL   NOT NULL
  expiresAt                      BIGINT NOT NULL   -- UnixTimeUtc
  created / modified
  PRIMARY KEY (identityId, tokenId)

-- One row per app the token maps to.
AppTokenApps
  identityId                     BYTEA  NOT NULL
  tokenId                        BYTEA  NOT NULL
  appId                          BYTEA  NOT NULL
  clientKeyEncryptedKeyStoreKey  TEXT   NOT NULL   -- that app's KSK under this token's client key
  created
  PRIMARY KEY (identityId, tokenId, appId)
  INDEX (identityId, appId)                        -- "which tokens reach app X" for revoke/delete
```

Why dedicated tables rather than a new `ClientRegistrations` type:
- Membership is relational (token ↔ apps). The app-side index makes "revoke every token that reaches
  vault" one query instead of a deserialize-and-filter.
- **V1 fails closed without any change.** Every V1 lookup (`GetAppPermissionContextAsync`,
  `GetCallingAppIdAsync`, `RevokeClientAsync`) reads `ClientRegistrations` by token id, so a
  multi-app token id is simply not found there. This is inferred from the lookup code; a test should
  confirm it.
- Revocation, expiry and cache reset live in new code, so the V1 client-revocation cache gap is not
  inherited.

#### Crypto (new class `AppTokenKeyFactory`)

Issuance (owner, master key present), for apps `A1..An`:
1. Random client key `CK`; `serverHalfOfKey = new SymmetricKeyEncryptedXor(CK, out clientHalf)`.
2. Random shared secret `SS`; `clientKeyEncryptedSharedSecret = new SymmetricKeyEncryptedAes(CK, SS)`.
3. For each app: `KSK_i = appReg_i.AppKeyStore.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey)`;
   row `clientKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(CK, KSK_i)`; wipe `KSK_i`.
4. Wipe `CK`. Return `ClientAccessToken { Id = tokenId, AccessTokenHalfKey = clientHalf, SharedSecret = SS, ClientTokenType = AppBundle }`.

This reuses the existing primitive types (`SymmetricKeyEncryptedXor`, `SymmetricKeyEncryptedAes`,
`ClientAccessToken`, `ClientAuthenticationToken`) unmodified. It re-implements about 20 lines of
`CreateClientAccessTokenInternal` rather than making that private method public or changing its shape.

Nothing about an app's own grant changes. `UpdateAppPermissionsAsync` keeps the app's KSK
(`AppRegistrationService.cs:110`), so a bundle token picks up permission and drive changes on the next
request after the cache reset. **The `//TODO: regen the key store key` at line 107 would break bundle
tokens exactly as it would break V1 tokens.**

#### Per-request context (new classes)

`AppTokenPermissionContextFactory` (Odin.Services):
1. Load the `AppTokens` row by token id. Missing, revoked or expired → fail.
2. `CK = serverHalfOfKey.DecryptKeyClone(token.AccessTokenHalfKey)`; `SS` from `clientKeyEncryptedSharedSecret`.
3. Load the `AppTokenApps` rows. For each: load the registration via `db.AppRegistrations.GetAsync` +
   `AppRegistrationService.FromRecord`. Missing or revoked app → handled per **T5**. Otherwise decrypt
   `KSK_i` with `CK` and add
   `PermissionGroup("app:{appId}", AppKeyStore.PermissionSet, AppKeyStore.DriveGrants, KSK_i, AppKeyStore.KeyStoreKeyEncryptedIcrKey)`.
4. Add the anonymous-drives group (via `IDriveManager.GetAnonymousDrivesAsync`, the same way
   `UnifiedAuthenticationHandler.CreateAnonResult` does; `ExchangeGrantService`'s helper is private)
   and an implied-keys group (`PermissionKeyImplications.ResolveImpliedKeys` over the union of keys).
5. Resolve the **acting app** per **T3**.
6. Caller: `SecurityGroupType.Owner`, no master key,
   `OdinClientContext { AppId = actingAppId, ClientIdOrDomain = friendlyName, CorsHostName = primary app's (T4), AccessRegistrationId = tokenId }`.
7. `PermissionContext(groups, sharedSecretKey: SS, keyStoreKey: KSK of the acting app)`.
8. Cache with `OdinContextCache.GetOrAddContextAsync(token, factory, keySuffix: actingAppId)`, so each
   acting app gets its own cached context. App revoke and permission updates already call
   `ResetAsync`, which clears these too.

Pre-v13 identities have no `AppRegistrations` rows (`LegacyDefinitionStore`), and step 3 has no legacy
fallback, so bundle tokens refuse those identities. That's acceptable for a dark launch. Checking
`legacyStore.IsPreMoveAsync()` gives a clear error.

`AppBundleAuthPathHandler` (Odin.Hosting, `UnifiedV2/Authentication/Handlers/`, new): reads the
acting-app header (T3), calls the factory, sets `Caller` and `PermissionContext`, and returns
`AuthHandlerResult.Success`. It mirrors `AppAuthPathHandler`.

#### Issuance and management (new service + controller)

`AppTokenService` (Odin.Services, new), all methods owner-only (`AssertHasMasterKey`):
- `IssueAsync(primaryAppId, appIds, friendlyName)` checks that every app is registered, not revoked,
  not duplicated, within the size limit (T8), and includes the primary app. It then writes both
  tables and returns the `ClientAccessToken`.
- `BeginExchangeAsync(request, jwkBase64UrlPublicKey)` issues the token, then does the ECDH exchange
  and 5-minute cache exactly as `YouAuthUnifiedService.CreateClientAccessTokenAsync` does (about 25
  lines re-implemented; that method is not reusable because it registers a V1 client inside).
  `ExchangeAsync(secretDigest)` returns the encrypted token once.
- `ListAsync()`, `ListForAppAsync(appId)`, `RevokeAsync(tokenId)` / `AllowAsync(tokenId)` (set the
  flag **and** `OdinContextCache.ResetAsync`), `DeleteAsync(tokenId)`, `RemoveAppAsync(tokenId, appId)` (T6).
- `DeleteCurrentAsync(ctx)` is logout for the calling bundle token.

`V2AppTokenController` (new), rooted at `/api/v2/app-tokens`:

| Verb | Route | Auth | Method |
|---|---|---|---|
| `POST` | `/` | Owner | `BeginExchangeAsync` → `{exchangePublicKey, salt}` |
| `POST` | `/exchange` | Anonymous (as YouAuth `token`) | `ExchangeAsync` |
| `GET` | `/` · `/by-app/{appId}` | Owner | list |
| `POST` | `/{tokenId}/revoke` · `/{tokenId}/allow` | Owner | revoke / allow |
| `DELETE` | `/{tokenId}` · `/{tokenId}/apps/{appId}` | Owner | delete token / remove app |
| `DELETE` | `/current` | the bundle token itself | logout |

#### Browser flow

The dark launch ships the **owner API issuance only** (above). That's enough for owner-console and
native clients, and for tests. Browser-initiated issuance for third-party apps needs a multi-app
consent screen, and that touches YouAuth. It's phase 2, laid out here so its cost is visible:

- New `ClientType` (for example `appBundle`) or new fields on `YouAuthAppParameters` (additional app
  ids and per-app drives/circles). ⚠️ Changes to `YouAuthAppParameters`, `YouAuthUnifiedController.Authorize`,
  `YouAuthUnifiedService.CreateClientAccessTokenAsync` / `AppNeedsRegistration` (per app).
- **Or** a new V2 authorize controller as new classes. This avoids touching the YouAuth classes, but
  `UnifiedAuthenticationHandler.HandleChallengeAsync` only redirects to login for the **V1**
  authorize path. ⚠️ A new path needs that method changed, or the controller must use the owner-cookie
  scheme the V1 controller uses; I haven't checked which scheme's challenge applies there.
- The owner-console registration and consent pages are outside this repo, and a multi-app version is
  front-end work in either case.

Recommendation for phase 2: the new-controller route, taking the one ⚠️ in `HandleChallengeAsync`,
over threading a second client type through the YouAuth classes.

### Decisions: tokens (proposed; recommendation shown)

| # | Question | Recommendation |
|---|---|---|
| T1 | New `ClientTokenType` value, or reuse `App`? | **New value `AppBundle`.** Reusing `App` means `AppAuthPathHandler` and the WebSocket controller would each have to fall back to the new tables on a miss, which changes existing handlers. A new type isolates it and makes V1 reject the token by type as well as by lookup. It costs the ⚠️ edits listed below. |
| T2 | Can a single-app client use this? | **Yes.** A bundle of one is valid. It's the path for clients that want the new revocation behaviour before they need a second app. |
| T3 | Which app is "acting" (`OdinClientContext.AppId`)? | **Per request, from a header** (`X-Odin-App-Id`), which must name a member app; default is `primaryAppId`. **Permissions are the union regardless of acting app; identity is one app at a time.** The 13 ownership checks stay correct unmodified (chat acting as vault manages vault's circles only when it says so). The alternative, making `AppId` a set, means changing those 13 sites. ⚠️ Listed below as out of scope. |
| T4 | CORS host | **The primary app's** `CorsHostName`. A client runs on one origin. |
| T5 | A member app revoked or deleted after issuance | **Drop that app's group and keep the token working** for the rest. If the **primary** is revoked or deleted, the token fails. The cache reset already done by `RevokeAppAsync` makes this take effect immediately. |
| T6 | Adding an app to an existing token | **Not possible; issue a new token.** Wrapping another KSK needs `CK`, which only exists when owner (master key) and client (client half) are both present, and no flow has both. Removing an app is just a row delete, so it is supported. |
| T7 | Lifetime | **365 days**, like V1, with the expiry column enforced by the factory. No sliding extension in the dark launch. |
| T8 | Maximum apps per token | **16.** It bounds per-request decrypt and group-building cost, and can be raised later. |
| T9 | Logout and revoke | **Revoke and delete always reset the context cache** (fixing, in new code, the V1 client-revoke gap). V1's `RevokeClientAsync` is left as is (existing code), and the gap is noted in *Existing code touched*. |

### Tests

- **Services tests (`Odin.Services.Tests`), `AppTokenKeyFactory`:** round-trip issuance → context. Each group decrypts its own drive's storage key; a group's KSK can't decrypt another app's storage key.
- **Migration tests (`MigrationTests`):** both tables create and roll back on SQLite and Postgres (the pattern commit `67732fcee` added).
- **`DataImporter`:** the existing import tests pass with both tables registered.
- **`Odin.Hosting.Tests.V2`:**
  - issue a bundle of chat-like + vault-like apps; one token reads/writes both apps' owned drives; transit works with an ICR key from either app
  - acting app: no header → primary; header naming a member → that app's ownership checks apply (for example, creating a circle for app B succeeds only with `X-Odin-App-Id: B`); header naming a non-member → 401
  - T5: revoke member → its drives are gone on the next request, others remain; revoke primary → 401
  - `UpdateAppPermissionsAsync` on a member → the new drive is visible through the existing bundle token on the next request
  - revoke token → 401 on the **next** request (no cache window)
  - T6: remove an app from a token → its drives are gone
  - expiry → 401
  - **V1 fails closed:** a bundle token sent to a V1 app endpoint and to `GetCallingAppIdAsync`-backed endpoints → 401 / not found
  - WebSocket: per the ⚠️ decision below (either works, or is rejected with "Invalid Client Token Type")
  - owner-only issuance: app and guest tokens → 401/403

## UI and clients: installing an app and signing in

The server side above is inert until something calls it. This section covers the callers: the owner
console and web apps in **odin-js** (`/Users/todd/src/odin/odin-js`, HEAD `76421fab`), and the native
client **chat-kmp** (`/Users/todd/src/odin/chat-kmp`). **homebase-id-app** (React Native "Feed & Chat",
last commit 2026-03-12, V1-only via `bx0900` header) is out of scope.

The same additive rule applies: new routes, components, hooks and exports. Edits to existing
front-end or client files are listed in [Existing client code touched](#existing-client-code-touched).

### How it works today (read in code; spot-checked where noted)

**Web app sign-in (odin-js)**
- Apps call js-lib `getRegistrationParams(...)` (`libs/js-lib/src/auth/providers/AuthenticationProvider.ts:108-159`),
  which builds `permission_request` = JSON `{n, appId, as, fn, o, p, cp, d, cd, c, return}`. `d`/`cd`
  are JSON arrays of `{a, t, n, d, p, r, s, at, ds, ts}` (drive alias, type, name, description,
  permission, anon-read, subscriptions, attributes, drive slug, type slug).
- The app redirects to `/api/owner/v1/youauth/authorize` (`chat-app/.../LoginBox.tsx`, `common-app/src/auth/YouAuthLoginBox.tsx`).
  An unregistered app goes to the owner console `/owner/appreg`, then consent `/owner/youauth/consent`,
  then back to the app's finalize route, which calls `finalizeAuthentication` → `POST /api/owner/v1/youauth/token`.
- The token and shared secret go into localStorage (`BX0900[_appKey]`, `APSS`/`APPS_{appKey}`). Requests
  send header `bx0900` to `/api/apps/v1` (`common-app/src/hooks/auth/useDotYouClient.ts:79-92`).

**Owner console app registration (`owner-app/src/templates/AppDefinition/RegisterApp.tsx`)**
- Shows permissions, "drives you already have", "and requests these new drives", and optionally a
  circle picker for circle drive grants. It has **no notion of drives or circles the app will own.**
- Save (`hooks/apps/useApp.ts:37-73`, spot-checked): for **every requested drive that has a name in
  its metadata**, `ensureDrive` creates it **with `appId` = the registering app** if no drive with that
  alias/type exists (`DriveProvider.ts:98-104`, spot-checked), then `POST appmanagement/register/app`.
  - **So today the console already stamps ownership, client-side: whichever app first asks for a drive
    that doesn't exist yet becomes its owner.** community-app, for example, requests Chat's drive with
    the `chat` slugs. Built-in drives already exist, which limits this in practice, but the rule is
    "first requester owns". V2 replaces it with an explicit owned/accessed split, checked on the server
    (D2, confused-deputy rule).
- Extending access later: `/owner/appupdate` (`ExtendAppPermissions.tsx`), which calls
  `updateapppermissions` + `updateauthorizedcircles`. Reached from js-lib `getExtendAppRegistrationParams`
  / common-app `useMissingPermissions`.
- App management (`templates/Apps/AppDetails/AppDetails.tsx`, `AppsDashboard.tsx`) already shows
  owned drives and circles (odin-js #923), clients (revoke/allow/remove), and set-owner/reassign-owner
  dialogs. All calls are V1.

**chat-kmp (native)**
- `YouAuthFlowManager.kt` + `AppAuthorizationParams.kt` go through the same `youauth/authorize` flow.
  They use the custom scheme `homebase-fchat://{appId}/authorization-code-callback` (Android Custom Tab,
  iOS ASWebAuthenticationSession), loopback on desktop, and a popup or seamless owner-cookie redirect
  on web. **They never send `as` or `cancel`.**
- The token is sent as `Authorization: Bearer` to **`/api/v2`** for nearly all data calls
  (`OdinApiProviderBase.kt:82-83`). Sign-in, `auth/verifytoken`, `auth/logout` and `notify/preauth`
  are still V1. The notification WebSocket uses the `Sec-WebSocket-Protocol` bearer.
- **`APP_ID` is `2d781401…`, which is `SystemAppConstants.ChatAppId`, the built-in Chat app**
  (checked against `SystemAppConstants.cs:36`).
- **It is already a bundle in all but name.** Through `/owner/appupdate` under the Chat app id, it
  adds feed + public channel, email, vault, webdrop, location, stickers and moments drives
  (`AppConfig.kt:124-473`, `PermissionExtensionManager.kt:56-163`). Every one of those belongs to a
  **built-in app that is already registered on every identity** (`BuiltinApps.Builtin`). Vault's and
  Location's GUIDs in `AppConfig.kt` are marked placeholders.

**Server facts that shape the UI**
- V2 unified auth reads the token from `Authorization: Bearer`, the owner cookie, the `BX0900` app
  cookie or the XToken cookie. It does **not** read a `bx0900` *header* (`UnifiedAuthenticationHandler.TryFindClientAuthToken`).
  Web clients of bundle tokens therefore send Bearer.
- `GET /api/v2/auth/context` exists (`V2AuthController.cs:63`). chat-kmp already uses it to compute
  missing permissions, and it can stand in for `verifytoken` on bundle tokens.
- Browsers can't set custom headers on a WebSocket upgrade, so `X-Odin-App-Id` (T3) can't reach the
  notification socket as a header.

### What V2 needs in the UI

There are four user-facing flows. Two ship with the dark launch; two wait for phase 2 (the browser
authorize flow in *V2 app tokens → Browser flow*).

| # | Flow | Who sees it | Ships |
|---|---|---|---|
| F1 | **Install an app (V2 registration)**: owned drives and circles plus access | owner console | dark launch |
| F2 | **Update an installed app**: add owned drives/circles, change access | owner console | dark launch |
| F3 | **Manage bundle tokens**: list, revoke, delete, remove an app | owner console | dark launch |
| F4 | **Sign in with a bundle token**: one consent for several apps | app → owner console → app | phase 2 |

#### F1. Install page: new route `/owner/appinstall` (owner-app)

- **Input: a manifest, not the V1 terse params.** Query param `m` = base64url JSON shaped like
  `AppRegistrationRequestV2` (§Design 1): `appId, name, appSlug, corsHostName, permissionSet, drives,
  authorizedCircles, circleMemberPermissionGrant, ownedDrives[], ownedCircles[]`, plus `return` and `cancel`.
  - Owned circles carry definitions (drive grants, permissions, `GrantOn`, designation, emoji), which
    the comma-list `c` format can't express.
  - Size: a manifest with a few drives and circles is a few KB base64url. That's within practical URL
    limits, but see **UI1**.
- **Screen**, top to bottom:
  1. App name, origin, `/apps/{appSlug}` (as `RegisterApp.tsx` does today).
  2. **"This app will create and own"**: drives (name, slug, type slug, anonymous/subscriptions/CDN
     flags) and circles (name, emoji, designation, what members get, `GrantOn` in plain words, e.g.
     "new connections are added automatically").
  3. **"It also needs access to"**: drives it doesn't own, each labelled with its **owning app**
     (from `GetDrivesByAppIdAsync` / drive `appId`, as `DriveDetails.tsx` already resolves), and
     permission level. Owned drives appear here as "full access (owned)" per D1.
  4. **"Identity-wide permissions"**: `PermissionView`.
  5. **"Circles whose members can use this app"**: `CircleSelector`, as today.
  6. **Blocking problems**, before Allow is enabled, pre-checked with the same rules the server
     enforces: tree app (D4), already installed (D3), slug taken, owned drive/circle already exists
     under another app or no app (D2), circle granting a drive the app neither owns nor accesses
     (confused deputy). The server still re-validates; the page only avoids a round trip that fails.
     **UI3**
- **Allow** → `POST /api/v2/app-registrations` → redirect to `return`. **Cancel** →
  `cancel?error=cancelled-by-user`, matching today.
- **How an app gets here (dark launch):** the app opens `/owner/appinstall?m=…&return={its authorize URL}`
  **before** starting sign-in. Once installed, `AppNeedsRegistration` returns false and the normal V1
  authorize flow issues a token with no YouAuth change. After phase 2, the authorize flow redirects
  here itself (**UI2**).
- New files: `templates/AppInstall/AppInstall.tsx`, `templates/AppInstall/manifest.ts` (decode + client-side checks),
  `provider/app/AppRegistrationV2Provider.ts`, `hooks/apps/useAppInstall.ts`.

#### F2. Update page: new route `/owner/appinstall/update` (owner-app)

- Input: `appId` + a manifest **delta** (`m`): owned drives/circles to add, the new access list, and
  authorized circles. The page computes the diff against `GET /api/v2/app-registrations/{appId}` and
  shows only what changes: "will create", "will gain access to", "will lose access to".
- Each confirmed change calls the matching V2 update endpoint (§Design 3) in the order the plan's
  atomicity section implies: owned drives → owned circles → permissions → authorized circles. The
  page shows per-step progress so a partial failure is visible and retryable (every endpoint is
  retry-safe).
- Refused for tree apps and Mail (U1) with a message pointing at the existing `/owner/appupdate`.
- **chat-kmp and the built-in web apps keep using `/owner/appupdate`**: they're tree apps. F2 is for
  third-party apps.
- In-console editing (AppDetails "edit" dialogs) stays V1 for the dark launch. A V2 edit mode is **UI5**.

#### F3. Tokens page: new route `/owner/third-parties/apps/:appKey/tokens` and `/owner/app-tokens` (owner-app)

- Lists bundle tokens from `GET /api/v2/app-tokens` (and `/by-app/{appId}`): friendly name, primary
  app, member apps (with revoked/deleted members greyed, per T5), created, expires, revoked.
- Actions: revoke/allow, delete, remove one app (T6). Each has a confirm dialog that states the effect
  ("vault drives stop working for 'Todd's phone' immediately").
- **Reaching it** needs a link from `AppDetails.tsx` and the Apps nav (**existing file edits**, listed
  below). Until then the route works by URL, which is fine for a dark launch.
- New files: `templates/AppTokens/AppTokens.tsx`, `provider/app/AppTokenProvider.ts`, `hooks/apps/useAppTokens.ts`.

#### F4. Bundle sign-in (phase 2): app → consent → app

**Owner console: new route `/owner/app-tokens/consent`**
- One screen, grouped **by app**. Each app card shows name, owner-visible slug, and what the token
  will be able to do through that app (drives with levels, identity permissions). The **primary app** is
  marked, along with the origin its CORS allows (T4). The client friendly name is at the top.
- **Apps that aren't installed** show "Install first" inline, which opens F1 for that app and
  returns here (**UI4**). Tree apps are always installed, so chat-kmp's bundle
  (Chat + Feed + Email + Vault + Webdrop + Location + Moments + Contacts) never hits this.
- Revoked apps block Allow, with a link to the app page.
- Allow → posts the form to the new V2 authorize controller (phase 2 in *Browser flow*), mirroring
  how `YouAuthConsent.tsx` posts `return_url` today.

**js-lib: new exports in `libs/js-lib/src/auth/` (new files)**
- `AppManifestV2` and `BundleAuthorizationRequest` types.
- `getAppInstallUrl(host, manifest, returnUrl, cancelUrl)` → `/owner/appinstall?...`.
- `getBundleAuthorizeUrl(host, {primaryAppId, appIds, friendlyName, publicKey, state, redirectUri})`.
- `finalizeBundleAuthentication(identity, privateKey, publicKey, salt)`. It reuses the existing ECDH
  helpers (`EccKeyProvider.ts`), but posts the digest to the V2 exchange endpoint. The existing
  `exchangeDigestForToken` hard-codes `/youauth/token`, so it can't be reused.
- `createBundleClient(identity, token, sharedSecret, actingAppId?)` returns a `DotYouClient` sending
  `Authorization: Bearer` (V2 doesn't read the `bx0900` header) and `X-Odin-App-Id`.
  - **[inferred]** `DotYouClient` accepts a `headers` option (homebase-id-app passes one), so one
    client instance per acting app needs no change to `DotYouClient`. That needs confirming when
    implementing; if it's wrong, it's an existing-file edit.
- `verifyBundleToken` → `GET /api/v2/auth/context`; `logoutBundle` → `DELETE /api/v2/app-tokens/current`.
  **V1 `verifytoken`/`logout` fail for bundle tokens** (V1 fails closed), so clients must not call them.

**chat-kmp (phase 2, or native first using the owner-API issuance)**
- New `BundleAuthorizationParams.kt`, and a `BundleFlowManager` next to `YouAuthFlowManager`. Same
  redirect scheme and key exchange; different authorize and exchange URLs.
- Split `AppConfig` into per-app groups: chat (chat, stickers, contacts, profile), feed (feed, public
  channel), email, vault, webdrop, location, moments. The bundle's `appIds` come from those groups; the
  drive lists no longer need to be requested at all, because the built-in registrations already grant them.
- **Acting app per call:** providers need to say which app they act as (MailProvider as email, feed
  calls as feed, circle work as the circle's owning app). There is no central place for this today:
  `bearerAuth` is called in about 11 places in `OdinApiProviderBase.kt` plus three file providers.
  That's an existing-code change.
- Logout and verify move to the V2 endpoints above. `PermissionExtensionManager` becomes "missing
  app in bundle → re-issue token" (T6: apps can't be added to an existing token).
- Settle the placeholder Vault/Location GUIDs against `BuiltinDrives` first.

#### Notification socket and acting app

The web socket can't carry `X-Odin-App-Id`. Recommendation (**UI6**): an extra subprotocol entry
`odin.app.{appId}`, next to the existing `odin.notify.v1` and `odin.bearer.*`, read by the bundle
branch of `V2NotificationSocketController` (already a ⚠️ row below). The query string is the
alternative, but it ends up in access logs.

### Decisions: UI (proposed; recommendation shown)

| # | Question | Recommendation |
|---|---|---|
| UI1 | How the manifest reaches `/owner/appinstall` | **base64url JSON in the query.** Stateless, and works from custom tabs and ASWebAuthenticationSession. Revisit with a hosted manifest (`{origin}/.well-known/homebase-app.json`, fetched by the console) only if manifests outgrow URLs. A hosted manifest also needs origin trust rules. |
| UI2 | Dark launch: who sends the owner to F1 | **The app itself**, before authorize. No YouAuth change; the V1 authorize flow then issues the token. In phase 2 the new authorize controller redirects to F1 (or F4's inline install). |
| UI3 | Client-side validation on F1 | **Yes, mirror the server rules** for a clear screen. The server stays authoritative and its 400s are shown verbatim when the two disagree. |
| UI4 | Bundle consent with a not-installed app | **Inline "install first"** via F1 and back, instead of rejecting the whole sign-in. |
| UI5 | V2 editing inside AppDetails | **Not in the dark launch.** F2 handles app-initiated changes; owner-initiated edits stay V1 until V2 is the default. |
| UI6 | Acting app on WebSockets | **Subprotocol `odin.app.{appId}`.** |
| UI7 | V1 `RegisterApp.tsx` "first requester owns" behaviour | **Leave it** (existing code, V1 path). Record it as a known issue; F1 is the fix going forward. |

### Existing client code touched

| Repo / file | Change | Needed for |
|---|---|---|
| odin-js `owner-app/src/app/App.tsx` | ⚠️ Add routes `appinstall`, `appinstall/update`, `app-tokens`, `app-tokens/consent`, `third-parties/apps/:appKey/tokens` | F1–F4. Unavoidable: routes are declared in one place. |
| odin-js `owner-app/src/templates/Apps/AppDetails/AppDetails.tsx` + Apps nav | ⚠️ Link to the tokens page | F3 discoverability. Optional during the dark launch. |
| odin-js `libs/js-lib/src/auth/index` (exports barrel) | ⚠️ Export the new functions and types | F1, F4. |
| odin-js `libs/js-lib/src/core/DotYouClient.ts` | ⚠️ *Only if* the `headers` option doesn't exist or doesn't apply per request | F4 Bearer + `X-Odin-App-Id`. |
| chat-kmp `homebase-api/.../OdinApiProviderBase.kt` and three file providers | ⚠️ Carry an acting-app id and set `X-Odin-App-Id` | F4 native. |
| chat-kmp `homebase-common/.../config/AppConfig.kt` | ⚠️ Split into per-app groups; resolve Vault/Location placeholder GUIDs | F4 native. |
| chat-kmp login, logout, verify, extension (`LoginViewModel.kt`, `YouAuthProvider.kt`, `YouAuthFlowManager.kt`, `PermissionExtensionManager.kt`) | ⚠️ Switch to the bundle flow behind a flag | F4 native. |
| **Not touched:** `RegisterApp.tsx`, `ExtendAppPermissions.tsx`, `YouAuthConsent.tsx`, `useApp.ts`, js-lib `getRegistrationParams` / `finalizeAuthentication`, every web app's `useAuth.ts`, homebase-id-app | | |

### UI tests

- **owner-app:**
  - F1 renders each section from a sample manifest; blocking problems disable Allow; a 400 from the server is shown.
  - F2 shows the diff correctly.
  - F3 revoke/delete/remove call the right endpoints.
  (Uses the repo's existing test setup; I haven't checked what that is.)
- **End-to-end, against a dev identity:**
  - A sample app opens F1 → installs → V1 authorize issues a token → the token writes to an owned drive.
  - Owner-API bundle issuance → a bundle token reads chat and vault drives → revoke in F3 → 401 on the next call.
- **chat-kmp:** a bundle of built-in apps signs in, sends a chat message, writes to vault, and receives
  notifications with `odin.app.{appId}` on the socket.

## Existing code touched

Everything in the design is new files except the items below. **Each is a modification of an existing
file.** All are additive (they add registrations or constants and change no existing line's
behaviour), but they are listed so none of them slips in unannounced.

| File | Change | Risk |
|---|---|---|
| `src/apps/Odin.Hosting/TenantServices.cs` | ⚠️ Add `cb.RegisterType<AppInstallService>().AsSelf().InstancePerLifetimeScope();` | Additive; no existing registration changes. Unavoidable, because DI has to know the type. |
| `src/apps/Odin.Hosting/UnifiedV2/UnifiedApiRouteConstants.cs` | ⚠️ *Optional:* add `AppRegistrations = BasePath + "/app-registrations"` | Additive constant. Avoidable by keeping the route string on the controller. |
| `src/apps/Odin.Hosting/UnifiedV2/SwaggerInfo.cs` | ⚠️ *Optional:* add an `Apps` tag | Additive constant; only affects Swagger grouping. Avoidable. |
| **Tokens** | | |
| `src/apps/Odin.Hosting/TenantServices.cs` | ⚠️ Register `AppTokenService`, `AppTokenKeyFactory`, `AppTokenPermissionContextFactory` | Additive. |
| `src/services/Odin.Services/Authorization/ExchangeGrants/ClientTokenType.cs` | ⚠️ Add enum member `AppBundle` (T1), with a value not already used | Additive. The enum travels inside `ClientAuthenticationToken`; old servers and V1 handlers won't know the value, which is the intended fail-closed behaviour. |
| `src/apps/Odin.Hosting/UnifiedV2/Authentication/UnifiedAuthenticationHandler.cs` | ⚠️ `GetHandler`: add `case ClientTokenType.AppBundle: return AppBundleHandler;` and the static field | **Edits the V2 auth dispatcher.** One new case; existing cases are unchanged. Unavoidable for T1. |
| `src/apps/Odin.Hosting/UnifiedV2/Authentication/Policy/UnifiedPolicies.cs` | ⚠️ Add `AppBundle` to the claim lists of `OwnerOrApp` and `OwnerOrAppOrGuest` | **Edits V2 authorization policies.** Every V2 endpoint that accepts an app token will accept a bundle token. That is the point, but it widens both policies at once. |
| `src/apps/Odin.Hosting/UnifiedV2/Notifications/V2NotificationSocketController.cs` | ⚠️ *Decision:* add `case ClientTokenType.AppBundle` that calls the new factory; **or** leave it, and bundle tokens get "Invalid Client Token Type" on the notification socket | Without it, a bundle client can't receive live notifications over V2. Recommend making the change: one new case, which also reads the acting app from an `odin.app.{appId}` subprotocol entry (UI6). |
| `…/Identity/IdentityDatabase.Generated.cs`, `…/Identity/IdentityMigrator.Generated.cs` | ⚠️ Regenerated to add `AppTokens` and `AppTokenApps` | Generated; requires the **Odin-SQLite-Generator** PR to land first, or the next generator run reverts it (per commit `67732fcee`). |
| `src/core/Odin.Core.Storage/DatabaseImport/DataImporter.cs` | ⚠️ Register both tables | Additive; existing import tests fail without it. |
| *Phase 2 only:* YouAuth (`YouAuthAppParameters`, `YouAuthUnifiedController`, `YouAuthUnifiedService`) **or** `UnifiedAuthenticationHandler.HandleChallengeAsync` | ⚠️ Browser-initiated bundle issuance; see *Browser flow* | Not in the dark launch. |
| *Not planned:* `OdinClientContext.AppId` → set of app ids, and its 13 consumers | ⚠️ Only if T3's one-acting-app-per-request is rejected | Would change ownership checks in `CircleNetworkService`, `CircleMembershipService`, scheduled notifications, WebSockets and live relay. |
| *Not planned:* `AppRegistrationService.RevokeClientAsync` cache reset | ⚠️ The existing V1 60-minute revocation gap | Worth fixing, but it changes V1 behaviour, so it's a separate change. |

**Not touched, by design:** `AppRegistrationService`, `IAppRegistrationService`,
`AppRegistrationRequest`, `UpdateAppPermissionsRequest`, `UpdateAuthorizedCirclesRequest`,
`AppRegistrationController`, `CircleDefinitionService`, `CircleMembershipService`,
`CircleNetworkService`, `DriveManager`, `BuiltinProvisioner`, `BuiltinApps`, `ExchangeGrantService`,
`ClientRegistrationStorage`, `AppClientRegistration`, `AppAuthPathHandler`, `YouAuthAuthenticationHandler`,
`OdinClientContext`, `PermissionContext`, `PermissionGroup`, `OdinContextCache`, the YouAuth classes
(dark launch), and every existing table or migration.

If implementation turns up a reason to change any of those, **stop and add it to this table with a ⚠️
before making the change.** Candidates to watch for:
- `AppRegistrationService.AssignSlugAsync` is private. Checking the slug up front should reimplement
  the check using `db.AppRegistrations.GetAllAsync()` and `AppSlugGenerator`/`OdinSlug`, not make
  that method public. The pre-v13 legacy-store branch lives inside it too.
- `CircleDefinitionService.AssertValidAsync` / `AssertValidPermissionSet` are private. Only
  `AssertValidDriveGrantsAsync` and `AssertDepositOnlyIfAmbientAsync` are public. Pre-validating
  permission sets may need to be reimplemented rather than exposed.
- **Fixing the V1 ordering in `CircleNetworkService.UpdateCircleDefinitionAsync`** (members
  re-granted before full validation). V2 avoids it by pre-validating; fixing the method itself
  changes V1 behaviour and needs a ⚠️ entry.
- **`Name` / `CorsHostName` updates (U6).** They would need a new method on `AppRegistrationService`.
- **Removing an owned drive (U4).** It would need a new `DriveManager` delete.

## Out of scope

- Deleting or revoking an app cascading to its drives and circles.
- Deleting owned drives (U4).
- Renaming the app or changing CORS (U6).
- Apps (non-owner tokens) registering or updating themselves.
- Moving `BuiltinProvisioner` onto `AppInstallService`.
- Moving tree apps and Mail onto the V2 update endpoints.
- Browser-initiated (YouAuth) issuance of bundle tokens (phase 2; see *Browser flow*).
- Multiple acting apps per request (T3).
- Migrating existing V1 app clients to bundle tokens.
- homebase-id-app (legacy React Native client).
- Fixing the V1 owner console's "first requester owns a new drive" behaviour (UI7).
- The Mail/Email discrepancy in the tree.
