# App registration V2: simplified plan

Status: **plan**, 2026-09-16. This is a simplified version of `app-registration-v2-plan.md`, produced
by a reuse / simplification / efficiency / altitude review. The original is unchanged. Every change
from it, and every behaviour change, is listed in [Changes from the original plan](#changes-from-the-original-plan).
Nothing is implemented.

## What V2 adds

1. **Owned resources.** An app's registration declares the drives and circles the app **owns**, and the
   server creates them with `AppId` set. This is `WellknownAppDefinition` at runtime.
2. **Updates** to a registration and to what it owns.
3. **Bundle tokens.** One client token maps to several registered apps. Its access is the union of their
   grants.
4. **UI**: one owner-console page to install or update an app, a tokens page, and later bundle sign-in.

**Ground rule: additive, dark launch.** V1 registration, update, tokens, YouAuth and the V1 owner-console
pages stay as they are. New code only runs when called; there is no feature flag. Every edit to existing
code is listed with ⚠️ in [Existing code touched](#existing-code-touched). If implementation turns up
another one, stop and add it there first.

## Background facts that shape the design (verified in code)

- **Registration today:**
  - `RegisterAppAsync` requires the master key. It builds `AppKeyStore`: a random key-store key (KSK)
    encrypted with the master key, plus drive grants whose storage keys are encrypted with the KSK.
  - Registration and V1 permission updates add the transient drive when transit is requested.
  - A permission update keeps the same KSK (`AppRegistrationService.cs:110`), so existing tokens
    survive grant changes. **Never regenerate a KSK** (TODO at line 107): it would break every token for
    that app.
- **Grants are stored, not derived.** A Read grant needs the drive's storage key escrowed under the KSK,
  which needs the master key. Access therefore has to be written at owner time and can't be computed per
  request.
- **Adopting or reassigning a circle adds that circle's drive grants to the owning app's grant**
  (`CircleNetworkService.GrantAppTheCirclesDrivesAsync`, :1174, called at :1129 and :1731). A permissions
  rebuild that ignores owned circles silently removes those grants.
- **Creating an anonymous-read drive re-grants both system circles to every connection**
  (`HandleDriveAdded` :2864 → `UpdateCircleDefinitionAsync` :983). That is the most expensive side
  effect here. `HandleDriveUpdated` (:2830) does nothing if the circles already grant the drive.
- **`UpdateCircleDefinitionAsync` rewrites every member's ICR before its full validation runs**
  (permission set and deposit-only live in `CircleDefinitionService.UpdateAsync`). An invalid request can
  leave members re-granted against a definition that was never saved.
- **The authorized-circles update always reconciles every member of every authorized circle**
  (`ReconcileAuthorizedCircles` :1957). It also force-keeps tree circles for Chat, Mail and Feed.
- **Mail** owns tree drives and circles (`BuiltinDrives.MailDrive`, `BuiltinCircles`, `BuiltinProvisioner.Registrations`)
  but has **no `BuiltinApps` entry**.
- **Token issuance:** `ExchangeGrantService.CreateClientAccessToken(keyStoreKey, tokenType, sharedSecret?)`
  is **public** (:105). `ServerHalfOfClientKey.DecryptUsingClientAuthenticationToken` is public.
  `PermissionGroup` carries its own KSK and ICR key, so one context can hold groups unlocked by different keys.
- **Token type** is checked in five places: `UnifiedAuthenticationHandler.GetHandler`, the
  `OwnerOrApp`/`OwnerOrAppOrGuest` policies, `V2NotificationSocketController`, and **`OdinControllerBase.cs:61`
  (owner-side viewer) and `:154` (Cache-Control)**.
- **`OdinClientContext.AppId` is single-valued.** WebSocket delivery matches `deviceSocket.AppId == TargetAppId`
  (`AppNotificationDispatcher.cs:176`), with `AppId` set once per socket.
- **`OdinContextCache`**: 60 min default, takes an optional `expiration` and `keySuffix`; `ResetAsync`
  clears the whole tenant cache.
- **`ClientRegistrationStorage.GetAsync<T>` ignores `catType`**, so a bundle token stored there would be
  found and deleted by V1 client calls. **Separate tables are what make V1 fail closed.**
- **New tables** come from the external Odin-SQLite-Generator (its PR must land first). They need
  regenerated `IdentityDatabase.Generated.cs` / `IdentityMigrator.Generated.cs` and `DataImporter`
  registration (commit `67732fcee`).
- **Owner console today** (`odin-js` `useApp.ts:37-73`, also on the extend path at `:112`): any requested
  drive that doesn't exist yet is created **owned by the requesting app**, so the first requester owns it.
- **chat-kmp's app id is `SystemAppConstants.ChatAppId`.** Everything it borrows (feed, email, vault,
  webdrop, location, moments, contacts drives) belongs to built-in apps already registered on every identity.

## Decisions

Settled = confirmed by you. Proposed = recommendation awaiting confirmation. *Changed* = differs from the
original plan (see the last section).

| # | Decision | Status |
|---|---|---|
| D1 | Owning a drive grants the app `ReadWrite` on it, unless an explicit grant for that drive says otherwise. Implemented once, by the **grant rebuild** rule. | Settled |
| D2 | A declared owned drive or circle that already exists is accepted only if it's **owned by this app and matches the declaration**; otherwise rejected. Adoption stays the owner's `set-owner` action. | Settled, *changed: "and matches"* |
| D3 | Registering an already-registered `AppId` is rejected; changes go through update endpoints. | Settled |
| D4 | **Reserved apps** (tree apps and Mail) can't be registered or updated through V2. | Settled, *changed: one predicate, now includes Mail* |
| D5 | `AppSlug`, and `DriveSlug`/`DriveTypeSlug` on owned drives, are required. | Settled |
| U1 | V2 updates work on any registered non-reserved app, including apps registered through V1. | Proposed |
| U2 | Updates are separate endpoints, each backed by existing methods; owned drives and circles are added in **one batch call**. | Proposed, *changed: batch add* |
| U4 | Owned drives can't be deleted (no drive delete exists); archive instead. | Proposed |
| U5 | An owned circle can be deleted only if it has no members and isn't in the app's authorized circles. | Proposed |
| U6 | Immutable in V2: `AppSlug`, app name, CORS host, drive name/slugs/`TargetDrive`/`OwnerOnly`. | Proposed |
| T1 | New token type `ClientTokenType.AppBundle`; a bundle of one app is valid. | Proposed |
| T3 | Permissions are the union of member apps; **one acting app per request** (header `X-Odin-App-Id`, default primary app). | Proposed |
| T4 | CORS comes from the primary app. | Proposed |
| T5 | A revoked or deleted member app drops out; if the primary is revoked or deleted, the token fails. | Proposed |
| T6 | Apps can be removed from a token, not added (re-issue instead). | Proposed |
| T7 | 365-day lifetime, enforced per request. | Proposed |
| T10 | **Notification sockets: one socket per acting app** in the dark launch (`odin.app.{appId}` subprotocol entry). Delivering every member app on one socket needs the ⚠️ `AppIds` change. | Proposed, *new* |
| UI1 | The install page takes a base64url JSON manifest in the URL. | Proposed |
| UI2 | Dark launch: the app opens the install page before V1 sign-in; phase 2's authorize flow redirects there itself. | Proposed |
| UI4 | Bundle consent installs missing apps inline. | Proposed |
| UI8 | **Server validates, UI renders**: a dry-run endpoint replaces client-side copies of server rules. | Proposed, *changed (was UI3)* |

## Shared rules (defined once, used everywhere)

**Reserved app.** `IsReserved(appId) = BuiltinApps.Get(appId) != null || appId == SystemAppConstants.MailAppId`.
One private static in the new service, used by register, update and validate.

**Ownership check** (D2), per declared owned drive or circle:

| Existing state | Result |
|---|---|
| missing | create |
| owned by this app, matches the declaration (drive: flags and slugs; circle: drive grants, permissions, `GrantOn`, designation) | accept as-is |
| owned by this app, differs | reject (use the owned-drive / owned-circle update) |
| unowned, or owned by another app | reject |

Update endpoints additionally require the target to exist and be owned by the route's app.

**Confused-deputy rule.** A circle's drive grants may name only drives the app owns (existing, or
declared in the same request) or drives in the app's explicit `Drives` grant list.

**Grant rebuild.** An app's drive grants are always written as
`explicit ∪ owned drives (ReadWrite) ∪ drive grants of circles the app owns`, merged per drive with
permission flags OR-ed and explicit entries winning for owned drives. It is applied through the
existing `UpdateAppPermissionsAsync`, which adds the transient drive and resets the cache. Where
"explicit" comes from:
- **permissions update**: the request (so access can be removed);
- **register**: the manifest;
- **add owned / owned-circle update**: the app's current grant list (the union only grows).

This single rule replaces the separate D1, U3 and add-drive merges, and keeps circle-derived grants.

**Validation helpers (reused, no edits):**

| Check | Helper |
|---|---|
| slug format | `OdinSlug.AssertValidOrNull` |
| app slug taken | `IAppRegistrationService.GetAppRegistrationBySlugAsync` |
| target drive | `OdinValidationUtils.AssertIsValidTargetDriveValue` |
| CORS | `AppUtil.AssertValidCorsHeader` |
| circle permission keys | `PermissionKeyAllowance.IsValidCirclePermission` |
| grants on existing drives | `CircleDefinitionService.AssertValidDriveGrantsAsync` |
| deposit-only rule on existing drives | `AssertDepositOnlyIfAmbientAsync` |

Two checks are re-done in memory because the drives they refer to don't exist yet: owner-only and
deposit-only rules on declared (not-yet-created) drives, and the owner-only/anonymous/subscriptions flag
combinations (use `DriveManager`'s error codes). The legacy slug branch is unnecessary: **pre-v13
identities are refused** (`LegacyDefinitionStore.IsPreMoveAsync`).

## Registration and updates

### Types (new)

- **`AppManifestV2`** carries `appId, name, appSlug, corsHostName, permissionSet, drives` (explicit
  grants), `authorizedCircles, circleMemberPermissionGrant, ownedDrives[], ownedCircles[]`.
- **`OwnedDrive`** is `CreateDriveRequest` minus `AppId`. **`OwnedCircle`** is `CreateCircleRequest`
  minus `AppId`. The owner always comes from the manifest or route, so a request can't name another
  owner.
- **`UpdateOwnedDrive`** has nullable `AllowAnonymousReads, AllowSubscriptions, AllowCdn, IsArchived,
  Metadata, Attributes`.
- For the circle update body (`OwnedCircle`), the route `circleId` wins, and a mismatching body `Id` → 400.

### Service `AppRegistrationV2Service` (new, `Odin.Services/Apps/`)

Dependencies: `IDriveManager`, `CircleDefinitionService`, `CircleMembershipService`, `CircleNetworkService`,
`IAppRegistrationService`. Nothing depends on it, so it can't create a cycle.

All methods: `AssertHasMasterKey`; refuse reserved apps and pre-v13 identities.

| Method | Steps |
|---|---|
| `ValidateAsync(manifest)` | Everything below that can be checked without writing, **all problems collected**, plus the diff against the current registration (will create / gain / lose). Used by register, by the batch add, and by the UI. |
| `RegisterAsync(manifest)` | Validate (throw on any problem); **create owned drives**; create owned circles via `CircleMembershipService.CreateCircleDefinitionAsync`; register via `RegisterAppAsync` with `Drives` = grant rebuild. |
| `GetAsync(appId)` | Registration + `GetDrivesByAppIdAsync` + `GetCirclesAsync(false)` filtered on `AppId`. |
| `AddOwnedAsync(appId, ownedDrives, ownedCircles)` | Validate the same way; create drives, then circles; one grant rebuild. |
| `UpdatePermissionsAsync(appId, permissionSet, drives)` | Grant rebuild with the request as explicit. |
| `UpdateAuthorizedCirclesAsync(appId, circles, memberGrant)` | **No-op if unchanged**; otherwise the existing `UpdateAuthorizedCirclesAsync`. |
| `UpdateOwnedDriveAsync(appId, driveId, update)` | Check flag combinations first, then the existing per-field `DriveManager` setters (`SetDriveReadModeAsync`, `SetDriveAllowSubscriptionsAsync`, `SetDriveAllowCdnAsync`, `SetArchiveDriveFlagAsync`, `UpdateMetadataAsync`, `UpdateAttributesAsync`). |
| `UpdateOwnedCircleAsync(appId, circleId, circle)` | Validate **fully first** (confused-deputy, permission keys, deposit-only). If only name, description, emoji, designation or `GrantOn` changed: `CircleMembershipService.UpdateAsync` + publish `CircleDefinitionChangedNotification(Updated)`, **no member re-grant**. Otherwise `CircleNetworkService.UpdateCircleDefinitionAsync`, then a grant rebuild. |
| `DeleteOwnedCircleAsync(appId, circleId)` | U5 check, then `CircleNetworkService.DeleteCircleDefinitionAsync` (refuses when the circle has members). |

**Creating owned drives cheaply.** A naive loop over K anonymous-read drives costs 2K passes over every
connection. Instead:
1. Create every drive with `AllowAnonymousReads = false`.
2. Add all K read grants to each system circle in **one** `UpdateCircleDefinitionAsync` per circle.
3. Call `SetDriveReadModeAsync(true)` per drive. `HandleDriveUpdated` then sees the grant and does nothing.

That is two passes total. The same order applies when an update turns anonymous reads on for several
drives. Turning it on for a single drive is inherently one pass per system circle.

### Endpoints: `V2AppRegistrationController` (new), `[UnifiedV2Authorize(UnifiedPolicies.Owner)]`

Root `/api/v2/app-registrations`, declared in `UnifiedApiRouteConstants` like every other V2 controller.

| Verb | Route | Method |
|---|---|---|
| `POST` | `/validate` | `ValidateAsync` |
| `POST` | `/` | `RegisterAsync` |
| `GET` | `/{appId}` | `GetAsync` |
| `POST` | `/{appId}/owned` | `AddOwnedAsync` |
| `PUT` | `/{appId}/permissions` | `UpdatePermissionsAsync` |
| `PUT` | `/{appId}/authorized-circles` | `UpdateAuthorizedCirclesAsync` |
| `PATCH` | `/{appId}/owned-drives/{driveId}` | `UpdateOwnedDriveAsync` |
| `PUT` · `DELETE` | `/{appId}/owned-circles/{circleId}` | update / delete |

Revoke, allow, app delete and V1 client management stay on V1.

### Failure and retry (the one place this is described)

Nothing here is transactional end to end. Drive creation makes directories and publishes notifications,
circle updates rewrite member ICRs one by one, and authorized-circle updates reconcile through a
notification handler. So:

- **All client-caused failures happen before the first write** (validation collects everything).
- **Every endpoint is safe to retry** after a server fault:
  - register and add: the ownership check accepts matching resources that already exist, and D3 stops
    a register that already completed;
  - permissions and authorized circles: full replacement;
  - owned-drive update: the setters are per field;
  - owned-circle update: re-mints from the same definition.
- **A retry with a *different* manifest can't silently diverge:** the ownership check rejects an
  existing resource that doesn't match.
- **No outer transaction**, because the notification handlers would run inside it (lock scope, and the
  scoped-connection "Parallelism detected" rule).
- **Accepted race:** two concurrent grant rebuilds on one app can lose an update. This is owner-only traffic.

## Bundle tokens

### Tables (new; generator PR first)

```sql
BundleTokens
  identityId, tokenId                    -- PK (identityId, tokenId); tokenId = ClientAuthenticationToken.Id
  primaryAppId                           NOT NULL
  friendlyName                           NOT NULL
  accessRegistration   TEXT NOT NULL     -- ServerHalfOfClientKey json (server half, SS, bundle key, IsRevoked, Created)
  expiresAt, created, modified

BundleTokenApps
  identityId, tokenId, appId             -- PK
  bundleKeyEncryptedKeyStoreKey TEXT NOT NULL
  created
  INDEX (identityId, appId)
```

Dedicated tables (your preference) are also what make V1 fail closed; see Background.

### Keys: reuse the existing client-token code

**Issue** (owner, master key), for apps `A1..An`:
1. Random 16-byte bundle key `BK`.
2. `(accessReg, token) = exchangeGrantService.CreateClientAccessToken(BK, ClientTokenType.AppBundle)`.
   This is the existing public overload. It does the XOR split, the shared secret, and escrows `BK`
   under the client key.
3. For each app: `KSK_i = AppKeyStore.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey)`; store
   `new SymmetricKeyEncryptedAes(BK, KSK_i)`; wipe.
4. Save `accessReg` + rows. Return `token`.

**Per request:** `(BK, SS) = accessReg.DecryptUsingClientAuthenticationToken(token)`, then
`KSK_i = row.DecryptKeyClone(BK)`.

No crypto is re-implemented.

### Per-request context: `BundleTokenContextFactory` (new)

1. Load the token row; missing or revoked → fail.
2. Decrypt `BK`, `SS`.
3. Load member registrations with **one** `db.AppRegistrations.GetAllAsync()` filtered by member ids,
   using `AppRegistrationService.FromRecord` (internal, same assembly).
4. Apply T5: skip revoked or missing members; fail if the primary is revoked or missing.
5. Build one `PermissionGroup` per member app (its `AppKeyStore` permissions, drive grants, `KSK_i`, ICR
   key), plus the anonymous-drives group and an implied-keys group.
6. **Cache one context per token** (no suffix), with
   `expiration = min(60 min, expiresAt − now)`, so expiry is enforced without a per-request read.

`BundleAuthPathHandler` (new, Hosting) gets the cached context, validates `X-Odin-App-Id` against the
member list (default primary app), and sets a **fresh** `CallerContext` with that `AppId`, the primary
app's CORS host and `AccessRegistrationId = tokenId`. The cached instance is never mutated (the WebSocket
controller already had to clone for that reason).

### Issuance and management: `BundleTokenService` + `V2BundleTokenController` (new)

Root `/api/v2/bundle-tokens`.

| Verb | Route | Auth | Does |
|---|---|---|---|
| `POST` | `/` | Owner | Validate members (registered, not revoked, primary included, ≤ 16), issue, ECDH-encrypt, cache 5 min → `{exchangePublicKey, salt}` |
| `POST` | `/exchange` | Anonymous (like YouAuth `token`; V1's `client-ecc-exchange` is owner-authenticated, so this is a deliberate choice) | Return the encrypted token once |
| `GET` | `/?appId=` | Owner | List, with member app names and state joined server-side |
| `POST` | `/{tokenId}/revoke` · `/{tokenId}/allow` | Owner | Flip `IsRevoked` + `OdinContextCache.ResetAsync` |
| `DELETE` | `/{tokenId}` · `/{tokenId}/apps/{appId}` | Owner | Delete token / remove member + reset cache |
| `DELETE` | `/current` | the bundle token | Logout |

The ECDH step uses the public primitives (`EccFullKeyData`, `AesCbc`, `EncryptedTokenExchange`) with its
own cache (`ITenantLevel2Cache<BundleTokenService>`). Writing into YouAuth's cache instead would couple
the new code to a private key format.

### Browser sign-in (phase 2)

The dark launch issues tokens only through the owner API above. Browser sign-in for third-party apps
needs a new authorize controller plus a consent page. ⚠️ `UnifiedAuthenticationHandler.HandleChallengeAsync`
only redirects the V1 authorize path to login. I haven't checked which scheme's challenge a new
controller gets.

## UI and clients

### Owner console (odin-js `owner-app`), new routes

**`/owner/app-registration?m={manifest}&return=&cancel=`** (one page for install **and** update):
- Calls `POST /api/v2/app-registrations/validate` with the manifest. The response's `isRegistered` picks
  install or update wording; its diff drives the sections below; its problems disable Allow. There is no
  client copy of server rules.
- Sections:
  1. app header (name, origin, `/apps/{appSlug}`);
  2. **will create and own** (drives, circles);
  3. **will gain / lose access to** (each drive labelled with its owning app);
  4. identity permissions;
  5. authorized circles.
- Reuse `AppOverviewParts.tsx`: `CircleOverview` (build a `CircleDefinition` from the manifest circle),
  `DriveGrantList`, `DriveLabel`, `PermissionKeyList`, `GRANT_ON_LABELS/HINTS`, `DESIGNATION_LABELS/HINTS`.
  Also `PermissionView`, `DrivePermissionRequestView`, `CircleSelector`.
- Allow:
  - install: `POST /`;
  - update: `POST /{appId}/owned` → `PUT permissions` → `PUT authorized-circles`, only the calls with
    a non-empty diff, with per-step progress.
  - Then redirect to `return`. Cancel goes to `cancel?error=cancelled-by-user`.
- Refused for reserved apps; built-in web apps and chat-kmp keep using `/owner/appupdate`.
- Dark-launch entry: the app opens this page before starting V1 sign-in (UI2).

**`/owner/bundle-tokens?appId=`**: a list plus revoke/allow/delete/remove-app with confirmations. Model
it on `ClientView` (not exported from `AppDetails.tsx`, so copy it) and `useAppClients.ts`.

**Phase 2: `/owner/bundle-tokens/consent`**: one screen grouped by app, primary app and origin marked,
missing apps installed inline via the registration page (UI4).

### js-lib (new exports)

- `AppManifestV2` type.
- `getAppRegistrationUrl(host, manifest, returnUrl, cancelUrl)`.
- Phase 2: `getBundleAuthorizeUrl`, `finalizeBundleAuthentication` (reuses `EccKeyProvider` helpers; posts
  to `/api/v2/bundle-tokens/exchange`).
- A bundle client is a `DotYouClient` with `headers: {Authorization: 'Bearer …', 'X-Odin-App-Id': …}`.
  `headers` already exists (`DotYouClient.ts:18,109`). V2 calls need a per-call `baseURL`, since
  `getEndpoint()` returns V1 roots; `exchangeDigestForToken` already does this. V2 auth doesn't read the
  `bx0900` header.
- Verify through `GET /api/v2/auth/context`; logout through `DELETE /api/v2/bundle-tokens/current`.
  V1 `verifytoken`/`logout` fail for bundle tokens.

### chat-kmp (phase 2, or earlier via owner-API issuance)

- A `BundleFlowManager` next to `YouAuthFlowManager`, with the same redirect scheme and key exchange.
- The bundle is `[Chat, Feed, Email, Vault, Webdrop, Location, Moments, Contacts]`. All are
  built-in and registered, so no install and no drive requests.
- Acting app per call: `encryptedPostJson` already takes `extraHeaders`; the other ~10 request methods don't.
- One notification socket per acting app that needs live events (T10).
- Settle the placeholder Vault/Location GUIDs in `AppConfig.kt` first.

## Existing code touched

### Dark launch

| Repo / file | Change |
|---|---|
| odin-core `TenantServices.cs` | ⚠️ Register `AppRegistrationV2Service`, `BundleTokenService`, `BundleTokenContextFactory` |
| odin-core `UnifiedApiRouteConstants.cs` | ⚠️ Add `AppRegistrations`, `BundleTokens` roots (every V2 controller uses this file) |
| odin-core `ClientTokenType.cs` | ⚠️ Add `AppBundle` (an unused value) |
| odin-core `UnifiedAuthenticationHandler.GetHandler` | ⚠️ Add a case for `AppBundle` |
| odin-core `UnifiedPolicies.cs` | ⚠️ Add `AppBundle` to `OwnerOrApp` and `OwnerOrAppOrGuest`. **Widens both policies.** |
| odin-core `OdinControllerBase.cs:61, :154` | ⚠️ Treat `AppBundle` like `App` (owner-side viewer, Cache-Control). **Missing from the original plan; without it bundle tokens get guest-side file behaviour.** |
| odin-core `V2NotificationSocketController.cs` | ⚠️ Add an `AppBundle` case; read the acting app from the `odin.app.{appId}` subprotocol entry |
| odin-core `IdentityDatabase.Generated.cs`, `IdentityMigrator.Generated.cs` | ⚠️ Regenerated for the two tables (generator PR first) |
| odin-core `DataImporter.cs` | ⚠️ Register both tables |
| odin-js `owner-app/src/app/App.tsx` | ⚠️ Add routes `app-registration`, `bundle-tokens` (+ `bundle-tokens/consent` in phase 2) |
| odin-js `libs/js-lib/src/auth` exports | ⚠️ Export the new functions and types |

### Deferred or not planned (each would be an existing-code edit)

| Item | Where | Why it's deferred |
|---|---|---|
| Member **app set** on `OdinClientContext`; socket delivery by set | `OdinClientContext`, `AppNotificationHandler`, `AppNotificationDispatcher` | Removes T10's one-socket-per-app |
| Token-type registry / `IsAppLike` helper shared by the five type checks | auth handler, socket controller, policies, `OdinControllerBase` | Makes the next token type a one-place change |
| Validate before re-granting in `UpdateCircleDefinitionAsync` | `CircleNetworkService` | Fixes V1's partial ICR rewrite; V2 avoids it by validating first |
| Reset the cache on V1 client revoke/allow/delete | `AppRegistrationService` | Closes V1's up-to-60-minute revoked-client window |
| Store explicit grants separately; one internal grant rebuild used by V1 and adoption too | `AppRegistrationService`, `CircleNetworkService` | Removes the three merge rules in V1 |
| Expose `BuiltinProvisioner`'s managed-app set; run the provisioner on the V2 service | `BuiltinProvisioner` | Removes the hand-kept reserved-app predicate and the second installer |
| Browser bundle sign-in | new authorize controller + ⚠️ `HandleChallengeAsync` | Phase 2 |
| Stop "first requester owns" | odin-js `useApp.ts` register and extend paths | V1 path |
| js-lib `odin.app.{appId}` socket option | `WebsocketProvider.ts` (`useV2`) | Needed when web clients use bundle sockets |
| chat-kmp acting-app header, `AppConfig` split, bundle login/logout | `OdinApiProviderBase.kt` + file providers, `AppConfig.kt`, auth flow | chat-kmp adoption |
| Name/CORS updates (U6), owned-drive delete (U4) | `AppRegistrationService`, `DriveManager` | Not needed yet |

**Out of scope (no code planned):** cascade delete of an app's drives and circles; apps registering
themselves; migrating V1 clients to bundle tokens; homebase-id-app; the Mail/Email naming overlap in
the tree.

## Tests

- **Registration** (`Odin.Hosting.Tests.V2`, new `OwnerAdmin.AppsV2.cs`):
  - validate reports every problem at once;
  - install creates owned drives/circles with `AppId`, grants per the rebuild, and publishes circle-created notifications;
  - D2 cases (match / differ / unowned / other app), with nothing created on any 400;
  - D3, D4 (tree app **and** Mail), D5;
  - confused deputy;
  - pre-v13 refused;
  - K anonymous owned drives cost two system-circle passes (assert via logs or a counter).
- **Updates**:
  - adopt a circle, then a permissions update keeps its drive grants;
  - omitting an owned drive keeps `ReadWrite`, and an explicit lower permission sticks;
  - metadata-only circle update leaves member ICRs untouched;
  - an invalid circle update leaves member ICRs untouched;
  - authorized circles unchanged → no reconcile;
  - owned-circle delete refused with members or while authorized;
  - V1-registered app is updatable.
- **Bundle tokens**:
  - one token reads/writes two apps' drives, and transit works from either app's ICR key;
  - `X-Odin-App-Id` scopes ownership checks, and a non-member → 401;
  - T5 member vs primary revoke;
  - a member's permission update is visible on the next request;
  - revoke → 401 on the next request;
  - expiry honoured without a cache wait;
  - `OdinControllerBase` owner-side behaviour matches an `App` token;
  - V1 endpoints reject bundle tokens;
  - socket delivery per acting app.
- **Storage**: `MigrationTests` for both tables on SQLite and Postgres; `DataImporter` tests.
- **UI**: the registration page renders a validate response (install and update); token list actions;
  end to end: app → registration page → V1 sign-in → write to an owned drive.

## Confirm during implementation (not verified)

- Whether the drive/circle notification handlers run synchronously (affects the retry reasoning).
- Whether the drive setters other than archive skip writes when nothing changes.
- Which authentication challenge applies to a new V2 authorize controller (phase 2).
- Whether bearer clients open the V2 notification socket through `ws-token` or the subprotocol only
  (affects where the acting app is read).
- Whether `CircleOverview` renders a synthetic manifest circle cleanly.
- The owner-app test setup.

## Changes from the original plan

**Behaviour changes (need your confirmation):**
1. **D2 now requires existing owned resources to *match* the declaration**, not just share the `AppId`.
   Prevents a retried install with a different manifest from silently keeping old definitions.
2. **D4 and U1 merged into one reserved-app predicate that includes Mail.** V2 can no longer register
   Mail even if its registration was deleted. Before, V2 could register Mail but not update it.
3. **Adding owned drives or circles is one batch endpoint** that ends in a grant rebuild. The separate
   add-drive and add-circle endpoints are gone.
4. **The grant rebuild now includes drive grants of circles the app owns.** The original U3 merge would
   have removed grants added by circle adoption/reassignment.
5. **Install and update are one page taking a full manifest.** Access the manifest omits shows as
   "will lose", consistent with V1's replace semantics. The original F2 used a delta.
6. **Client-side validation (UI3) is replaced by a server dry-run endpoint.**
7. **V2 no longer refuses system circles in authorized circles** (V1 parity). The original plan added
   that stricter rule with a note to maybe drop it.
8. **Bundle tokens cache one context per token** and apply the acting app per request. Expiry is now
   honoured within the cache window.
9. **Notification sockets deliver only the acting app's events** (T10). This was missing from the
   original plan.

**Structure and reuse (no behaviour change):**
- Token crypto reuses `ExchangeGrantService.CreateClientAccessToken` + `ServerHalfOfClientKey` (one JSON
  column) instead of re-implementing the key split.
- Slug and validation checks use the existing public helpers; pre-v13 identities are refused, so no
  legacy branch is copied.
- Owned-circle updates skip member re-grants when grants didn't change; unchanged authorized-circle
  updates are skipped.
- Anonymous owned drives are created with two system-circle passes instead of two per drive.
- One set of names: "app registration" (service, controller, route, UI route), "bundle token" (type,
  tables, service, routes, UI).
- **Added ⚠️ `OdinControllerBase.cs:61, :154`** (missed originally).
- The route constants are now a normal ⚠️ row (V2 convention); the Swagger-tag row is dropped.
- One decisions table. D6 is folded into the ground rule. T2, T8 (≤ 16, now a constant) and UI3/5/7 are
  folded into the design or the deferred table.
- One failure and retry section; one deferred table (replacing "Not planned" rows, "Candidates to
  watch" and most of "Out of scope"); one unverified list.
- Removed: repeated statements of the V1 revoke gap, the circle-update ordering bug and Chat/Mail/Feed
  force-keep; stale draft notes; long "how it works today" UI background (kept only the facts that
  drive decisions).

**Considered and not applied:**
- Merging the two token tables into one JSON column: you prefer dedicated tables, and the app index serves revoke-by-app.
- Writing into YouAuth's token cache to reuse `/youauth/token`: couples to a private cache-key format.
- Building the permission context through `ExchangeGrantService.CreatePermissionContext`: needs mutating an internal dictionary.
- One-step token issuance without the exchange endpoint: phase-2 browser flow and js-lib expect the two-step shape.
- Dropping un-revoke (`allow`).
- Making private validators `internal` (⚠️): the public helpers are enough.
