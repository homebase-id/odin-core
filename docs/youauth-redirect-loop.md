# YouAuth Redirect Loop

## Status

- [x] **Fix #2 (`verifyToken` does real auth, matching cookie-delete options)** landed in commit on branch `youauth-loop`. Files touched:
  - `src/apps/Odin.Hosting/Controllers/OwnerToken/Auth/OwnerAuthenticationController.cs` (rewritten `VerifyCookieBasedToken`, fixed `Response.Cookies.Delete` options in `ExpireCookieBasedToken`).
  - `tests/apps/Odin.Hosting.Tests/OwnerApi/Authentication/ResetPasswordTests.cs::VerifyTokenReturnsFalseForStaleCookieAfterPasswordReset` covers the stale-KEK path; verified the test fails without the controller change.
- [ ] **Fix #1 (invalidate registrations on password rewrite)**: still open. Plan below is unchanged.
- [ ] **Fix #3 (client-side bounce counter)**: still open, optional.

## Symptom

Occasionally, when a browser hits `/api/owner/v1/youauth/authorize` to start a
YouAuth flow (for example the Homebase KMP app authorizing on
`frodo.baggins.demo.rocks`), the browser ping-pongs between
`/api/owner/v1/youauth/authorize` (returns `302` to login) and
`/owner/login?returnUrl=...` (immediately redirects back to authorize), several
times per second, until the browser eventually aborts (see
`youauth-redirect-loop.log`, 67 round trips in 32 seconds).

The same log shows three signals that pin the failure:

1. `GET /api/owner/v1/authentication/verifyToken` returns `200`.
2. `GET /api/owner/v1/security/account-status`,
   `POST /api/owner/v1/config/system/isconfigured`,
   `POST /api/owner/v1/transit/inbox/processor/process`, and
   `GET /api/owner/v1/youauth/authorize` all fail with
   `digital-identity-owner was not authenticated. Failure message: Key hash did not match`.
3. The frontend therefore thinks the user is logged in, but every protected
   endpoint disagrees.

## Root cause

### Where "Key hash did not match" comes from

It is thrown in exactly one place:
`src/core/Odin.Core.Cryptography/Data/SymmetricKeyEncryptedAes.cs:87` (inside
`DecryptKeyClone`).

It is reached from
`src/services/Odin.Services/Authentication/Owner/OwnerSecretService.cs:97`
(`GetMasterKeyAsync`):

```csharp
var kek = serverClientRegistration.TokenEncryptedKek.DecryptKeyClone(clientSecret); // SymmetricKeyEncryptedXor
var masterKey = pk.KekEncryptedMasterKey.DecryptKeyClone(kek);                       // SymmetricKeyEncryptedAes
```

The first decrypt uses `SymmetricKeyEncryptedXor` (its mismatch error is
`"Byte arrays don't match"`, see
`src/core/Odin.Core.Cryptography/Data/SymmetricKeyEncryptedXor.cs:63`), so that
one succeeded. The failure is on the second line: the KEK pulled out of the
registration cannot unwrap the password's `KekEncryptedMasterKey`.

This means the cookie itself is genuine for the registration row, but the row's
KEK does not match the current `PasswordData`.

### Why the KEK is stale

`PasswordData.KekEncryptedMasterKey` is rewritten by `SavePasswordAsync` in
`src/services/Odin.Services/Authentication/Owner/OwnerSecretService.cs:244`
(callers: `ResetPasswordAsync` at line 219, `ResetPasswordUsingRecoveryKeyAsync`
at line 213, and the first-run `SetNewPasswordAsync` at line 62). Each call goes
through `PasswordDataManager.SetInitialPassword` and produces a fresh
`KekEncryptedMasterKey` wrapped with a new KEK derived from the new password.

Neither `SavePasswordAsync` nor any of its callers touch
`ClientRegistrationStorage`. The existing `OwnerConsoleClientRegistration` rows
keep a `TokenEncryptedKek` that wraps the old KEK. Any browser still holding the
matching `DY0810` cookie now lands in this exact half-broken state:

- cookie half-key correctly decrypts the registration's old KEK,
- old KEK cannot decrypt the new `KekEncryptedMasterKey`,
- result: every owner-auth endpoint throws `OdinSecurityException("Key hash did not match")`.

This is the underlying defect: a password rewrite invalidates the cookies
cryptographically without invalidating the database rows. Every device that was
logged in before the password change is affected, including the device that
performed the change itself.

### Why the loop keeps running

The login frontend has two checks that decide whether to bounce back to the
returnUrl:

1. `useDotYouClientContext().isAuthenticated()` in
   `packages/apps/owner-app/src/templates/Login/Login.tsx:24`. This reduces to
   `!!getRawSharedSecret()` in
   `packages/libs/js-lib/src/core/DotYouClient.ts:69`, i.e. it only checks
   `localStorage.OWNER_SHARED_SECRET`. That entry was written by the prior
   successful login and is still present.
2. `useVerifyToken()` in
   `packages/apps/owner-app/src/hooks/auth/useVerifyToken.ts:6` which calls
   `hasValidOwnerToken()` in
   `packages/common/common-app/src/provider/auth/OwnerAuthenticationProvider.ts:10`,
   which `GET`s `/api/owner/v1/authentication/verifyToken`.

The backend `verifyToken` handler in
`src/apps/Odin.Hosting/Controllers/OwnerToken/Auth/OwnerAuthenticationController.cs:30`
only does:

```csharp
var value = Request.Cookies[OwnerAuthConstants.CookieName];
if (ClientAuthenticationToken.TryParse(value ?? "", out var result))
{
    var isValid = await authService.IsValidTokenAsync(result.Id);
    ...
    return new JsonResult(isValid);
}
```

And `IsValidTokenAsync` in
`src/services/Odin.Services/Authentication/Owner/OwnerAuthenticationService.cs:118`
only checks the registration row exists and is not expired. It never touches
the cookie's `AccessTokenHalfKey` or attempts the KEK chain. So a stale-KEK
cookie passes verifyToken.

The login page therefore behaves as if the user is authenticated:

- `useValidateAuthorization` in
  `packages/apps/owner-app/src/hooks/auth/useAuth.ts:60` only forces logout when
  `hasValidToken === false`. Here it is `true`, so it does nothing.
- The separate effect at
  `packages/apps/owner-app/src/templates/Login/Login.tsx:63` runs
  `checkRedirectToReturn()` (Login.tsx:111), which sets `window.location.href`
  to the returnUrl, i.e. back to the authorize endpoint.

The authorize endpoint runs through
`src/apps/Odin.Hosting/Authentication/Owner/OwnerAuthenticationHandler.cs:88`
(`HandleAuthenticateAsync`), which catches the `OdinSecurityException`, fails
auth, and `HandleChallengeAsync` (line 57) responds with `302` to login because
`/api/owner/v1/youauth/authorize` is in `RedirectPaths`. The loop closes.

### Why it is rare

All three need to be true:

- A previous successful owner login on this device (cookie `DY0810` plus
  `localStorage.OWNER_SHARED_SECRET` exist).
- `PasswordData` has been rewritten since that login (owner-initiated change via
  `ResetPasswordAsync`, or recovery-key reset via
  `ResetPasswordUsingRecoveryKeyAsync`).
- The registration row has not yet expired (default 6-month TTL, see
  `OwnerConsoleClientRegistration.TimeToLiveSeconds`).

There is also a delayed-onset effect from `OdinContextCache`
(`src/services/Odin.Services/Base/OdinContextCache.cs`, `DefaultDuration =
60 minutes`). The cache key is `token.AsKey()`, so any successful auth before
the password rewrite (including the password change itself) leaves a "valid"
context cached against the cookie. Until that entry is evicted (cache expiry,
server restart, or a registration `DeleteAsync` which calls `ResetAsync`),
requests with the stale cookie keep succeeding. The loop only kicks in after
the cache lets go. This is why the bug surfaces hours or a day after the
password change, not immediately.

That matches the reported "rare but happens" cadence: a small fraction of users
who hit password recovery or changed their password, then later open a
previously-logged-in browser/tab to do a YouAuth flow.

## Fix plan

Three changes. Implement #1 and #2; #3 is optional and defensive.

### 1. Invalidate owner registrations on password rewrite (root cause fix, backend)

Goal: after `SavePasswordAsync` writes new `PasswordData`, drop every
`OwnerConsoleClientRegistration` so that every device must re-login. This is
also the correct security posture for a password change or recovery.

Owner registrations are identifiable in the table:
`OwnerConsoleClientRegistration.Type == 100` and
`CategoryId == cc0b390d-ac32-450f-bbaa-0108debde248` (see
`src/core/Odin.Core.Cryptography/Data/OwnerConsoleClientRegistration.cs:28`).

Steps:

a. Add a bulk-delete helper on `ClientRegistrationStorage` at
   `src/services/Odin.Services/Authorization/ClientRegistrationStorage.cs`:

   ```csharp
   public async Task DeleteAllByTypeAsync(int catType)
   {
       var records = await clientRegistrationsTable.GetCatsByTypeAsync(catType);
       foreach (var record in records)
       {
           await clientRegistrationsTable.DeleteAsync(record.catId);
       }
       await odinContextCache.ResetAsync();
   }
   ```

   `GetCatsByTypeAsync` and `DeleteAsync(catId)` are already on
   `TableClientRegistrations` (see
   `src/core/Odin.Core.Storage/Database/Identity/Table/TableClientRegistrations.cs:31`
   and `:36`). The existing single-row `DeleteAsync` already calls
   `odinContextCache.ResetAsync()`; reset once at the end here to avoid a flood.

b. Inject `ClientRegistrationStorage` into `OwnerSecretService`. Today's
   constructor (`OwnerSecretService.cs:23`) doesn't have it, so add it as a
   primary-constructor parameter and update the DI registration site if needed
   (it is constructor-injected per Autofac convention; no explicit registration
   change should be required).

c. In `SavePasswordAsync`
   (`src/services/Odin.Services/Authentication/Owner/OwnerSecretService.cs:244`),
   after `PasswordDataStorage.UpsertAsync(...)` succeeds, call:

   ```csharp
   await clientRegistrationStorage.DeleteAllByTypeAsync(
       OwnerConsoleClientRegistrationType);
   ```

   Define `OwnerConsoleClientRegistrationType = 100` either as a `const` next
   to the other ids near the top of the class, or expose
   `OwnerConsoleClientRegistration.Type` as a `public const int Type = 100;` and
   reference it. Prefer exposing the constant on the class that owns it.

d. Do not delete inside the same DB transaction as the password update if there
   isn't already one wrapping both; if there is a transactional scope at the
   call site, keep the delete inside it.

e. The very session that calls `ResetPasswordAsync` will be logged out by this
   change. The frontend `useChangePassword` flow should already navigate to
   login after success. If it doesn't, follow up by checking
   `packages/apps/owner-app/src/hooks/auth/useAuth.ts` (`changePassword`) and
   the calling component, but that is out of scope for breaking the loop.

f. Note: `SavePasswordAsync` is also called from `SetNewPasswordAsync`
   (first-run only, guarded by `IsMasterPasswordSetAsync` being false). On first
   run there are no registrations, so the bulk delete is a no-op and safe.

g. Tests: add a unit/integration test that
   1) authenticates as owner, captures the `DY0810` cookie,
   2) calls `ResetPasswordAsync`,
   3) re-uses the captured cookie on an owner endpoint and asserts `401`.
   And mirror it for `ResetPasswordUsingRecoveryKeyAsync`.

### 2. Make `verifyToken` actually verify (DONE)

Without this, every user currently stuck in the loop would stay stuck until
their registration row expires, because their cookie still passes
`IsValidTokenAsync`. `verifyToken` now performs a real authentication.

Shipped changes (commit on branch `youauth-loop`):

- `src/apps/Odin.Hosting/Controllers/OwnerToken/Auth/OwnerAuthenticationController.cs`
  - `VerifyCookieBasedToken` now builds an `OdinClientContext` and calls
    `OwnerAuthenticationService.UpdateOdinContextAsync` inside
    `try { ... } catch (OdinSecurityException) { isValid = false; }`. That
    exercises the full chain (`GetDotYouContextAsync` ->
    `GetMasterKeyAsync` -> both `DecryptKeyClone`s) and mirrors the catch in
    `OwnerAuthenticationHandler.HandleAuthenticateAsync`. `ExtendTokenLife` and
    `AddUpgradeRequiredHeaderAsync` only run on success.
  - `ExpireCookieBasedToken` now deletes the cookie with options matching the
    set: `HttpOnly = true, Secure = true, SameSite = Strict, Path = "/"`.
    Bundled into the same patch because `logoutOwnerAndAllApps` on the client
    relies on the cookie actually being gone (some browsers refuse to delete a
    `Secure` cookie via a non-`Secure` delete).
- `tests/apps/Odin.Hosting.Tests/OwnerApi/Authentication/ResetPasswordTests.cs`
  - New test `VerifyTokenReturnsFalseForStaleCookieAfterPasswordReset`:
    1) login with an isolated `CookieContainer`,
    2) assert `verifyToken` returns `true` against the fresh cookie,
    3) reset the password via the existing scaffold owner client,
    4) reset the tenant-scoped `OdinContextCache` (resolved via
       `IMultiTenantContainer.GetTenantScope(...).Resolve<OdinContextCache>()`)
       to simulate cache expiry; without this step the cached pre-reset
       context masks the staleness and the test passes for the wrong reason,
    5) assert `verifyToken` now returns `false` for the same cookie.
  - Verified: passes with the fix, fails (at the post-reset assertion) when the
    controller change is reverted.

Once `verifyToken` returns `false` for a stale-KEK cookie, the existing client
path triggers cleanup: `useValidateAuthorization` in
`packages/apps/owner-app/src/hooks/auth/useAuth.ts:60` sees `hasValidToken ===
false` while `hasSharedSecret === true` and calls
`logoutOwnerAndAllApps(capturedReturnUrl)`, which clears localStorage and
cookies and routes to a clean login page with the original `returnUrl`
preserved. Loop ends, user logs in, KMP app gets its authorize redirect.

Caveat noticed while testing: the existing `OdinContextCache` keys context by
cookie composite key (`token.AsKey()`), so the unblocking only happens once the
cached entry is evicted (cache TTL default 60 min, or a server restart, or a
registration delete). Fix #1 will side-effect-flush this via
`ClientRegistrationStorage.DeleteAsync` -> `OdinContextCache.ResetAsync`. Until
fix #1 lands, a currently-stuck user will see the loop end the next time their
cached context expires.

### 3. Defensive loop breaker on the client (optional, frontend)

Even with #1 and #2, a future regression that silently makes verifyToken too
permissive would resurface the loop. Cheap belt-and-braces:

In `packages/apps/owner-app/src/hooks/auth/useAuth.ts` (or directly in
`Login.tsx`), keep a small counter in `sessionStorage` keyed by the current
returnUrl. Whenever the login page mounts with a returnUrl, increment. If the
counter passes a threshold (e.g. 3) within a short window (e.g. 30s), call
`logoutOwnerAndAllApps(returnUrl)` instead of `checkRedirectToReturn()`. Reset
the counter on successful login (in `authenticate()` after
`invalidateVerifyToken`) and on a fresh navigation without a returnUrl.

Sketch in `useAuth.ts`:

```typescript
const REDIRECT_COUNTER_KEY = 'owner-login-bounce';
const REDIRECT_LIMIT = 3;
const REDIRECT_WINDOW_MS = 30_000;

const trackAndMaybeBreakLoop = (returnUrl: string): boolean => {
  try {
    const raw = sessionStorage.getItem(REDIRECT_COUNTER_KEY);
    const now = Date.now();
    const state = raw ? JSON.parse(raw) : null;
    const fresh = !state || state.returnUrl !== returnUrl || (now - state.firstAt) > REDIRECT_WINDOW_MS;
    const next = fresh
      ? { returnUrl, firstAt: now, count: 1 }
      : { ...state, count: state.count + 1 };
    sessionStorage.setItem(REDIRECT_COUNTER_KEY, JSON.stringify(next));
    return next.count > REDIRECT_LIMIT;
  } catch {
    return false;
  }
};
```

Use it inside `checkRedirectToReturn` before navigating, and clear the key
inside `authenticate()` after `invalidateVerifyToken`.

This is small and self-contained but only valuable as a safety net; it is not a
substitute for #1 and #2.

## Order of work

1. ~~Fix #2~~ DONE. Currently-stuck users get out of the loop once their
   cached context expires (or immediately on next request after fix #1 ships,
   since that flushes the cache).
2. Land fix #1 next. It removes the source of the problem so users no longer
   land in the bad state, and as a side effect immediately unblocks anyone
   still in a cached-context grace period.
3. Optionally land fix #3 as a regression guard.

## Out-of-scope follow-ups noticed while investigating

These are not required to break the loop, but they are adjacent and worth a
ticket each.

- `SymmetricKeyEncryptedAes.DecryptKeyClone`
  (`src/core/Odin.Core.Cryptography/Data/SymmetricKeyEncryptedAes.cs:88`)
  always sets `IsRemoteIcrIssue = true` on the thrown exception, even for
  owner-flow failures that have nothing to do with ICR. Misleading for triage.
- ~~`OwnerAuthenticationController.ExpireCookieBasedToken` cookie-delete
  options not matching the set options.~~ Fixed alongside fix #2.
- Login.tsx (`packages/apps/owner-app/src/templates/Login/Login.tsx:48-61`)
  auto-sets the first password to `'a'` when the master password is not set.
  This relies on a `FirstRunToken` and only succeeds in dev/preconfigured
  tenants, but the unconditional call is a footgun.
