# App registration V2 and bundle tokens: API contract

The wire contract for `docs/app-registration-v2-plan-simplified.md`, as implemented on branch
`app-registration-v2`. Clients: the owner console (odin-js `owner-app`) and any app that wants a
bundle token.

## Conventions

- JSON is camelCase. Enums are **camelCase strings** on output and accept strings or numbers on input.
  - Flags combine as `"read, write"`; named combinations serialize by name (`"readWrite"`).
  - `DrivePermission` values: `none`=0, `read`=1, `write`=2, `react`=4, `comment`=8,
    `conditionalTemporalRead`=16, `readWrite`=15 (read|write|comment|react).
- GUIDs are strings (dashed on output; dashed or undashed accepted on input). `UnixTimeUtc` is a number (ms).
- **Owner endpoints** use the owner session (cookie). They are shared-secret encrypted exactly like
  `/api/owner/v1`: js-lib's `DotYouClient` does this automatically, with the request sent to an absolute
  `https://{identity}/api/v2/...` URL.
- **Bundle-token requests** send `Authorization: Bearer <base64 ClientAuthenticationToken>` and are
  shared-secret encrypted with the token's shared secret. The optional header `X-ODIN-APP-ID: <appId>`
  picks the acting app; the default is the primary app. V2 does **not** read the `bx0900` header.
- Errors: 400 as ProblemDetails -- the message is in `title`, the Odin error code in `errorCode` -- and
  shared-secret encrypted like any other owner response; 401/403 from auth.

### Shared shapes

```ts
type TargetDrive = { alias: string; type: string };
type PermissionedDrive = { drive: TargetDrive; permission: DrivePermission | number; temporalReadWindowSeconds?: number };
type DriveGrantRequest = { permissionedDrive: PermissionedDrive };
type PermissionSet = { keys: number[] };
type PermissionSetGrantRequest = { drives?: DriveGrantRequest[]; permissionSet?: PermissionSet };
type CircleGrantOn = 'none' | 'connect' | 'ownFlowConnect' | string;   // see odin-core CircleEnrollment.cs
type CircleDesignation = 'personal' | 'audience' | string;
```

## App registration: `/api/v2/app-registrations` (owner only)

### Manifest

```ts
type AppManifestV2 = {
  appId: string;
  name: string;
  appSlug: string;                    // required, OdinSlug: [a-z0-9-], 1..14, immutable
  corsHostName?: string;              // "host" or "host:port", immutable
  permissionSet?: PermissionSet;      // identity-wide keys the app holds
  drives?: DriveGrantRequest[];       // explicit access; owned drives need not be listed
  authorizedCircles?: string[];
  circleMemberPermissionGrant?: PermissionSetGrantRequest;
  ownedDrives?: OwnedDrive[];
  ownedCircles?: OwnedCircle[];
};

type OwnedDrive = {
  name: string; targetDrive: TargetDrive; metadata?: string;
  allowAnonymousReads: boolean; allowSubscriptions: boolean; allowCdn: boolean; ownerOnly: boolean;
  driveSlug: string; driveTypeSlug: string;          // both required
  attributes?: Record<string, string>;
};

type OwnedCircle = {
  id: string; name: string; description?: string;
  driveGrants?: DriveGrantRequest[]; permissions?: PermissionSet;
  grantOn: CircleGrantOn; designation: CircleDesignation; emoji?: string;
};
```

Access rule: the app ends up with `explicit drives ∪ ReadWrite on every owned drive ∪ whatever its owned
circles grant`. An explicit entry for an owned drive wins over the implicit ReadWrite.

### Endpoints

| Verb | Path | Body | Response |
|---|---|---|---|
| `GET` | `/` | | `AppRegistrationV2[]`: every registered app, with what it owns |
| `GET` | `/{appId}` | | `AppRegistrationV2`, or 404 |
| `POST` | `/validate` | `AppManifestV2` | `AppRegistrationValidationResult` (dry run; writes nothing) |
| `POST` | `/` | `AppManifestV2` | `AppRegistrationV2`, or 400 listing every problem |
| `POST` | `/{appId}/owned` | `{ ownedDrives?, ownedCircles? }` | `AppRegistrationV2` |
| `PUT` | `/{appId}/permissions` | `{ permissionSet?, drives? }` | 204 |
| `PUT` | `/{appId}/authorized-circles` | `{ authorizedCircles?, circleMemberPermissionGrant? }` | 204 (no-op if unchanged) |
| `PATCH` | `/{appId}/owned-drives/{driveId}` | `{ allowAnonymousReads?, allowSubscriptions?, allowCdn?, isArchived?, metadata?, attributes? }` | 204 |
| `PUT` | `/{appId}/owned-circles/{circleId}` | `OwnedCircle` (route id wins) | 204 |
| `DELETE` | `/{appId}/owned-circles/{circleId}` | | 204; 400 if it has members or is an authorized circle |

Built-in apps (Chat, Feed, Mail, …) are **reserved**: registering or updating them is refused.

```ts
type AppRegistrationV2 = {
  registration: RedactedAppRegistration;   // the V1 shape: appId, appSlug, name, isRevoked, created, modified,
                                           // grant { isRevoked, permissionSet, driveGrants[{permissionedDrive, hasStorageKey}] },
                                           // authorizedCircles, circleMemberPermissionSetGrantRequest, corsHostName
  isReserved: boolean;
  ownedDrives: { driveId, targetDrive, name, driveSlug, driveTypeSlug, allowAnonymousReads,
                 allowSubscriptions, allowCdn, ownerOnly, isArchived }[];
  ownedCircles: RedactedCircleDefinition[];   // id, name, description, appId, grantOn, designation, emoji, driveGrants, permissions{keys}
  created: number;
};

type AppRegistrationValidationResult = {
  isValid: boolean;
  isRegistered: boolean;            // false → install wording, true → update wording
  problems: { code: string; subject: string; message: string }[];
  diff: {
    drivesToCreate: OwnedDrive[]; drivesAlreadyOwned: OwnedDrive[];
    circlesToCreate: OwnedCircle[]; circlesAlreadyOwned: OwnedCircle[];
    driveAccess: DriveAccessEntry[];          // the app's access afterwards
    driveAccessGained: DriveAccessEntry[]; driveAccessLost: DriveAccessEntry[];
    permissionKeysGained: number[]; permissionKeysLost: number[];
    authorizedCirclesAdded: string[]; authorizedCirclesRemoved: string[];
  };
};

type DriveAccessEntry = { targetDrive: TargetDrive; permission: DrivePermission; driveName?: string;
                          owningAppId?: string; owningAppName?: string };
```

Problem codes: `appIdRequired`, `reservedApp`, `identityNotUpgraded`, `alreadyRegistered`, `nameRequired`,
`invalidSlug`, `slugTaken`, `immutable`, `invalidCorsHostName`, `invalidTargetDrive`, `invalidDriveFlags`,
`duplicateDrive`, `duplicateSlug`, `driveOwnedElsewhere`, `ownedDriveDiffers`, `driveSlugTaken`,
`driveNotFound`, `circleIdRequired`, `reservedCircle`, `duplicateCircle`, `circleGrantsNothing`,
`invalidPermissionKey`, `keysOnAmbientCircle`, `driveNotGrantable`, `ownerOnlyDrive`,
`readOnAmbientCircle`, `circleNotFound`, `circleOwnedElsewhere`, `ownedCircleDiffers`.

**Applying an update** (the app is registered and `validate` found no problems):
1. `POST /{appId}/owned` with `diff.drivesToCreate` / `diff.circlesToCreate`, if any.
2. `PUT /{appId}/permissions` with the manifest's `permissionSet` and `drives`, if keys or access changed.
3. `PUT /{appId}/authorized-circles` with the manifest's circles, if they changed.

## Bundle tokens: `/api/v2/bundle-tokens`

| Verb | Path | Auth | Body | Response |
|---|---|---|---|---|
| `POST` | `/` | owner | `{ primaryAppId, appIds[], friendlyName, jwkBase64UrlPublicKey, redirectUri? }` | `{ tokenId, exchangePublicKeyJwkBase64Url, exchangeSalt64 }` |
| `POST` | `/exchange` | anonymous, **not** encrypted | `{ secret_digest }` | `{ base64SharedSecretCipher, base64SharedSecretIv, base64ClientAuthTokenCipher, base64ClientAuthTokenIv }` or 404 |
| `GET` | `/?appId=` | owner | | `RedactedBundleToken[]` |
| `POST` | `/{tokenId}/revoke` · `/{tokenId}/allow` | owner | | 204 |
| `DELETE` | `/{tokenId}` | owner | | 204 |
| `DELETE` | `/{tokenId}/apps/{appId}` | owner | | 204 (the primary app can't be removed) |
| `DELETE` | `/current` | the bundle token | | 204 (logout) |

Rules for issuing:
- Every app must be registered and not revoked; the primary app is always a member; at most 16 apps.
- If the primary app has a `corsHostName`, `redirectUri` must be on that host (`host[:port]`).
- A token lasts 365 days.
- Revoking or deleting a token takes effect on the next request.
- A revoked member app drops out of the token; a revoked primary app makes the token fail.

```ts
type RedactedBundleToken = {
  tokenId: string; friendlyName: string; primaryAppId: string; isRevoked: boolean;
  created: number; expiresAt: number;
  apps: { appId: string; name: string; appSlug: string; isPrimary: boolean; isRevoked: boolean; isMissing: boolean }[];
};
```

### The exchange (same crypto as YouAuth)

1. The client makes an ECC P-384 key pair and sends its public key as JWK base64url (js-lib `createEccPair`
   / `exportEccPublicKey`, the same value YouAuth's `public_key` carries).
2. The owner console calls `POST /` and hands `exchangePublicKeyJwkBase64Url` and `exchangeSalt64` to the client.
3. The client derives the shared secret with ECDH + the salt (js-lib `getEccSharedSecret`), then
   `digest = base64(SHA-256(sharedSecret))`, then `POST /exchange { secret_digest: digest }`.
4. The client AES-CBC-decrypts the two ciphers with the shared secret. `clientAuthToken` bytes base64 =
   the bearer token; `sharedSecret` = the key for request encryption. This is exactly js-lib
   `finalizeAuthentication`, except that it posts to `/api/v2/bundle-tokens/exchange`.

## Browser sign-in (owner console)

**Registration page:** `https://{identity}/owner/app-registration?m={base64url(JSON AppManifestV2)}&return={url}&cancel={url}`
- Calls `validate`; shows install or update; on Allow applies it (register, or the update steps above),
  then redirects to `return`.
- Cancel goes to `cancel?error=cancelled-by-user`.

**Bundle consent page:** `https://{identity}/owner/bundle-tokens/authorize?p={base64url(JSON)}` where JSON is
`{ primaryAppId, appIds[], friendlyName, publicKey, redirectUri, state }`.
- On Allow: `POST /api/v2/bundle-tokens`, then redirect to
  `redirectUri?identity={identity}&public_key={exchangePublicKeyJwkBase64Url}&salt={exchangeSalt64}&state={state}`
  (the same parameter names YouAuth uses).
- Cancel goes to `redirectUri?error=cancelled-by-user&state={state}`.
