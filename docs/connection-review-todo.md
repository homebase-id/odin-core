# Connection review — remaining work

Working list for the review programme. Design lives in `connection-defaults.md`; this is only what
is left to do and what blocks what.

**Shipped** (PR #1710): `ReviewedAt` set, read and round-tripped; review and un-review endpoints on
V1 and V2; stamping on accept and on the sender's side; the v15→v16 backfill; viewer-scoped
redaction on the connections list; `reviewedAt` on the owner-app connection detail screen
(odin-js branch `connection-review-support`).

## Now

- Full `Odin.Hosting.Tests` run, and CI green on **PostgreSQL** — only SQLite has been exercised
  locally.
- Audit the rest of the V2 `OwnerOrAppOrGuest` surface. `blocked` was reachable by a guest and had
  no permission check of its own; `GetCircleMembers` sits under the same class policy and has
  **not** been checked.
- Check whether any guest-side client reads `status` or `created` from the connections list —
  those now serialize as defaults for third-party viewers.

## Next: the per-app circle pipeline

Replaces the one hardcoded bundle every new connection gets with one small circle per app.
Everything else waits on this.

- Add the per-app owner-console toggle to tenant settings, seeded at app install. Does not exist.
- Call `CircleDefinitionService.GetCirclesByGrantOnAsync(Connect)` from the auto-connect path,
  filter by that toggle, enrol. **The method is written and indexed and has zero callers** — the
  declarations are live data with no consumer. `BuiltinCircles.ChatCircle` already says
  `GrantOn = Connect` and nothing reads it.
- Then re-read `ClearReviewAsync`: its ambient carve-out is theoretical today because nothing
  grants ambiently. It starts mattering the moment this lands.

Already done and needs nothing: the circle declarations (`BuiltinCircles`), their provisioning
(`BuiltinProvisioner.EnsureCirclesAsync`), and the deposit-only invariant
(`CircleDefinitionService.AssertDepositOnlyIfAmbientAsync`).

## On hold: the security ladder

Deliberately paused until the review is fully working.

- `ReviewedAt != null` → 777, else 444, at the three sites that hardcode `Connected`:
  `CircleNetworkService.cs:160`, `TransitAuthenticationService.cs:58`,
  `HomeAuthenticatorService.cs:357`.
- Delete `AutoConnected` (555) and collapse its case in `DriveAclAuthorizationService.cs:105`.
- Rename `Connected` → `Reviewed` in the enum and UX labels; the wire string `connected` stays on
  777.
- Release notes: bare-`connected` ACLs become reviewed-only, so unreviewed connections lose access.
- **Do not flip before v16 has run fleet-wide**, or previously-confirmed connections silently drop
  to `Authenticated`. How that is confirmed depends on the deploy process, which has not been
  looked at.

## On hold: retiring confirm and the two system circles

One job — `ConfirmConnectionAsync` exists only to swap people between those two circles.
Blocked on the pipeline above, on clients shipping the review dialog, and — for one piece — on the
ladder.

**Replace first**

- Origin→circle routing (`CircleNetworkUtils.cs:18,22`) → the `GrantOn` pipeline.
- Migrate existing members' grants to the per-app circles, or people lose chat/mail/feed access
  they already hold.
- Feed distribution (`FeedDriveDistributionRouter.cs:411`) and introduction eligibility
  (`CircleNetworkIntroductionService.cs:248,253,351`) both ask "are they in Confirmed?"
- *ConnectedIdentitiesCanViewConnections* and *WhoIFollow* are permission keys stored on the
  Confirmed circle (`TenantConfigService.cs:414-416`). They need the `Reviewed` tier to move to —
  **so this cannot finish before the ladder.**

**Then delete**

`ConfirmConnectionAsync` and its V1/V2 endpoints, the 3010 lockout
(`CircleNetworkService.cs:540`), `EnsureSystemCirclesExistAsync` (or they respawn),
`IsConfirmedConnection()` and its callers (`ShamirReadinessCheckerService.cs:52`,
`V5ToV6VersionMigrationService.cs:90`), `AutoFixConnections.cs:49`, the `IsSystemCircle` filters
(`CircleMembershipService.cs:124`, `CircleDefinitionService.cs:296`), the troubleshooting lookups
(`CircleNetworkService.cs:1618,1623,1668,1681`), `BuiltinProvisioner.SystemCircleCarryOverDrives`,
the system-circle carve-out in `ClearReviewAsync` (`CircleNetworkService.cs:1221`), and the
hardcoded GUIDs in odin-js `ConnectionSummary.tsx`.

**Two traps**

- Six historical migrations reference the constants (V0→V1, V3→V4, V5→V6, V7→V8, V8→V9, V9→V10).
  Keep the GUIDs even after the circles go.
- The v15→v16 backfill finds already-confirmed connections *through* the Confirmed circle. Delete
  the circles before a tenant runs v16 and those connections stay "Not yet reviewed" forever,
  silently.

## Client work

- chat-kmp #1062: the review dialog against `POST review` / `review/clear`.
- Move clients off `vetted` to `reviewedAt`; `vetted` is now only a compatibility alias.
- Surface New-vs-reviewed in the contact book.

## Known gaps in what shipped

- The un-review guard keys on `Designation == Personal`, but nothing anywhere sets `Audience` or
  `Vendor` — every circle in `BuiltinCircles` is `Personal`. Until designations are real the guard
  is blunter than the design describes.
- Nothing pages on `ReviewedAt` yet. Filtering "reviewed" and "New people" is the stated reason it
  is a column rather than a blob field, and no endpoint uses it.
- No cross-app pending enrolment queue for apps whose keys the reviewing client does not hold.
