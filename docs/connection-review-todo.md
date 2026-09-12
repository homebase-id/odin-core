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
- ~~Move clients off `vetted` to `reviewedAt`~~ — done by deleting `vetted` outright; it had no
  reader in the server or in any client in the tree, so there was nothing to transition.
- Surface New-vs-reviewed in the contact book.

## Known gaps in what shipped

- The un-review guard keys on `Designation == Personal`, but nothing anywhere sets `Audience` or
  `Vendor` — every circle in `BuiltinCircles` is `Personal`. Until designations are real the guard
  is blunter than the design describes.
- Nothing pages on `ReviewedAt` yet. Filtering "reviewed" and "New people" is the stated reason it
  is a column rather than a blob field, and no endpoint uses it.
- No cross-app pending enrolment queue for apps whose keys the reviewing client does not hold.

## Open problem: finding connections that have work queued on them

**Not decided. Written down so the next person does not rediscover it.**

Deposited grants and pending enrollments both live inside `Connections.data`, the per-connection
JSON blob. That satisfied "no new schema", and it is fine for writing — the code always already has
the connection in hand. Reading is the problem: a blob field cannot be queried, so *"which
connections have work waiting?"* has no answer except selecting every connection row and
deserialising every blob.

Where that costs something today:

- `ProcessPendingEnrollmentsForAppAsync` — the sharp one. It runs on every `ProcessEnrollments`
  socket command and every `POST connections/enrollments/process`, so an app that connects with
  nothing pending still pays a full scan to discover that.
- `GetPendingCircleMembersAsync` / `GetAllPendingCircleMembersAsync` — the owner-facing read behind
  the pending-members UI.
- `ConvertDepositedGrantsForConnectedIdentitiesAsync` and the enrollment pre-pass both scan too, but
  they run once during an upgrade with the owner present, which is the one place a scan is fine.

Nothing is broken. It is a scan per connect where an indexed read would do, and it gets worse with
connection count rather than with the amount of work outstanding — which is backwards, because the
outstanding set is small and drains itself.

### Candidate: a `PendingWork` flags column on `Connections`

The same move `ReviewedAt` already made on this table, and for the same stated reason
(`drive-addressing.md`: point lookups did not need a column, filtering did).

```sql
PendingWork INT NOT NULL DEFAULT 0    -- 1 = deposited grants, 2 = pending enrollments, ...
-- index on (identityId, PendingWork), ideally partial on PendingWork != 0
```

**Flags, not a boolean.** A single `HasPendingWork` bit shared by several producers cannot be
cleared safely: no drain could clear it without first proving every *other* kind of work was also
finished, which couples them permanently. A bit per kind lets each producer set its own and each
drain clear only its own, off one column and one index.

What it does and does not buy: it narrows to connections-that-have-work, not to *this app's* work.
An app still reads the rows with the enrollment bit set and filters on `OwningAppId` from the blob.
That is enough — the set is small — but it is a coarse filter, not an index on ownership. Per-app
precision would need a real index, which is a different and larger decision.

**The rule that keeps it safe.** The blob stays authoritative; the column is a hint about where to
look. The two failure directions are not equal: set-but-empty costs one wasted read, while
clear-but-not-empty strands work silently and indefinitely. So set the flag eagerly in the same
write that queues the work, and clear it only when a drain has just proved the set empty. Have the
upgrade pass rebuild the column from the blobs, which gives a self-heal path needing no repair
tooling.

**The trap to write into the code.** `ToConnectionsRecord` performs a full-row write, so any caller
that forgets the new column nulls it. `ReviewedAt` was silently clobbered exactly this way earlier
in this work, which is why that method takes it as a parameter; this column needs the same treatment
and the same comment.

**Rejected alternative.** A marker row in the `KeyValue` table holding the set of app ids with
outstanding work. It avoids a migration, but only answers "is it worth scanning at all" — the moment
the answer is yes you are back to a full scan — and it introduces a second at-rest copy of the same
fact with no natural place to reconcile it.

## Open decision: converting members of the two system circles

**Not decided.** Retiring *Auto Connections* and *Confirmed Connections* means moving everyone
already in them onto the per-app circles that replace them. The drive mapping is exact and needs no
judgement — every target already exists via `BuiltinProvisioner` and carries an identical grant:

| System circle grant | Becomes |
|---|---|
| Chat Write\|React | `ChatCircle` |
| Lists Write\|React | `ListsCircle` |
| Moments Write\|React | `MomentsCircle` |
| Mail Write\|React | `MailCircle` |
| Feed Write\|React | `FeedCircle` |
| ShardRecovery Write *(Confirmed only)* | `RecoveryCircle` — but see the next section |

So Auto members map to five circles, Confirmed members to six. One connection becomes five or six
`CircleMember` rows and as many circle grants where there was one; fine for an ordinary identity,
worth a thought for the feed's million-connection case the addressing doc raises.

**What does not map: `AllowIntroductions`.** No built-in circle carries a permission key, and
whether verified circles may carry them at all is `connection-defaults.md` open question 2. Worse,
`TenantConfigService.UpdateSystemCirclePermissionAsync` writes *ConnectedIdentitiesCanViewConnections*
and *WhoIFollow* onto the Confirmed circle at runtime, so it carries keys its static definition does
not show. Confirmed cannot be fully retired until those have somewhere to live.

**Preserve, do not normalise.** A faithful conversion gives every auto-connected identity all five
circles. What the new model *would* have granted them is narrower -- ambient enrolment only grants
`GrantOn = Connect` circles, and today only `ChatCircle` declares it -- so normalising would silently
strip mail, moments, lists and feed write from existing contacts. The design already says an app
toggle affects future connections only; the same principle applies here.

**Additive first, revoke later.** Adding the new memberships while leaving the system-circle grants
in place is reversible: people hold duplicate grants to the same drives, which is untidy and
harmless. Revoking in the same pass is tidier and one-way. One consequence of waiting: the 3010
lockout keys off Auto membership, so it keeps firing until the revoke happens.

**Ordering.** Must run *after* v15 -> v16, which reads Confirmed membership to backfill `ReviewedAt`.
Fan out and revoke first and that backfill loses its source. It does not depend on the ambient
enrolment pipeline, so it can land well before that does.

**Shape.** A version-upgrade pass with the master key present, so grants mint with real storage keys.
Idempotent by construction, since enrolment already no-ops on existing membership.

## Open decision: when someone enters the recovery circle

**Not decided.** `RecoveryCircle` conflates two facts. Its grant is `ShardRecoveryDrive: Write` --
*may deposit a shard on me*, which is eligibility. Its description says *members hold a shard of your
recovery key*, which is selection. Today they coincide only by accident: Confirmed granted that write
to every confirmed contact, and `ShamirReadinessCheckerService.VerifyRemotePlayerReadiness` gates on
`IsConfirmedConnection()` rather than on the grant.

- **On review** -- reviewed implies membership. Faithful to today, no behaviour change, but leaves a
  drive open to every contact with no part in the owner's recovery, and the description stays untrue.
- **On selection** -- the grant is minted when the owner picks shard holders. Least privilege, and the
  description becomes true. No chicken-and-egg: readiness is checked against review state, not the
  grant, so verify -> select -> grant -> distribute still works.
- **Split them** -- circle means eligibility (renamed), separate marker for holders. Most honest, most
  work, and it is not clear what it buys over selection.

Selection is the better end state; nothing else in the system hands write access to everyone by
default, and recovery is the last place to start. But the *migration* should still preserve rather
than strip: everyone confirmed already holds that grant, and removing it in the conversion pass would
quietly change who can take part in an in-flight recovery setup. Convert additively, tighten as its
own change with its own release note.

Either way `ShamirReadinessCheckerService.cs:52` is one of the last two `IsConfirmedConnection()`
callers and has to move to `ReviewedAt != null` when the Confirmed circle retires, or readiness
checks start failing for everyone.
