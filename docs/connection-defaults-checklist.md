# Connection defaults — implementation checklist

Line-by-line extraction of every imperative in `connection-defaults.md` (part 2/4), grouped into
implementation categories. Line refs are `connection-defaults.md` as of 2026-08-19.

*This is a derived working checklist, not a spec. `connection-defaults.md` remains the source of
truth; where this file and that one disagree, that one wins.*

## Cat 0 — Schema (done)

`GrantOn` / `Designation` / `Emoji` / `AppId` on Circle, `ReviewedAt` on Connections, plus indexes
— L26-29, L207-210. Landed dormant in `TableCircleMigrationV202608040942` /
`TableConnectionsMigrationV202608040942` / `TableDrivesMigrationV202608040942` (commits
`67732fce`, `5c4de4a0`, `a728bc75`). Verified by reading the generated CRUD; not verified against a
live migration run on either dialect.

## Cat 1 — The review stamp

| # | Item | Line |
|---|---|---|
| 1.1 | Storage plumbing: the column is the only at-rest home — map in/out in `CircleNetworkStorage`, exclude from the ICR blob | L283-287 |
| 1.2 | One atomic review endpoint: stamp + enroll in a single call, gated by `ManageCircleMembership` | L314-316 |
| 1.3 | The review must also enroll `Connect` circles for any checked app the contact is not yet a member of | L199-205 |
| 1.4 | Redacted shape: `Vetted` becomes `reviewedAt`; V1 compat serves `vetted = reviewedAt != null` | L316-319 |
| 1.5 | Un-review rejected while the contact holds any `PERSONAL`-designated membership | L324-326 |
| 1.6 | Viewer-scoped redaction — two shapes from one record (owner/app full, third party identity-only) | L294-307 |
| 1.7 | Decide `omitContactData`'s fate — the doc calls it "the wrong axis" | L311 |
| 1.8 | Contact API: explicitly no change | L320 |
| 1.9 | Explicitly *no* server-side "in any personal circle" endpoint | L321-324 |

Backfill (`vetted == true` → reviewed) is specified in part 4's *Migration / legacy*, not here.

## Cat 2 — Ladder recut

| # | Item | Line |
|---|---|---|
| 2.1 | Assignment only: `ReviewedAt != null` → 777, else 444, at transit/YouAuth context build | L277-279 |
| 2.2 | Delete `AutoConnected` (555) | L273 |
| 2.3 | Wire string `connected` keeps the 777 slot; enum member and every UX label become `Reviewed` | L281-282 |
| 2.4 | "Connected-but-unreviewed" survives as an internal caller classification only | L279-280 |
| 2.5 | Move `ConnectedIdentitiesCanViewConnections` / `WhoIFollow` off Confirmed-circle permission keys | L257-260 |
| 2.6 | ACL migration + release note — see the conflict below | L288-292 |

The evaluator and the `BETWEEN 0 AND callerLevel` range query are untouched.

## Cat 3 — Enrollment model

| # | Item | Line |
|---|---|---|
| 3.1 | `DefaultCircles` on `AppRegistrationRequest` (name, grants, `GrantOn`, `Designation`, `Emoji`) | L151-153 |
| 3.2 | App create: insert a Circle row per entry | L177-181 |
| 3.3 | App update: match incoming entries to existing rows by circle id, update-or-insert, never duplicate | L182-183 |
| 3.4 | Persist the request JSON on the app row — consent display and repair only, never read on the hot path | L184-186 |
| 3.5 | Deposit-only validation at definition-write time; re-check whenever `GrantOn` changes | L59-62 |
| 3.6 | Auto-connect pipeline: query `WHERE GrantOn = Connect`, filter by the owner's per-app toggle | L188-194 |
| 3.7 | An accept with app context bypasses the default set and names its own circles | L44-49, L192-193 |
| 3.8 | Owner-console per-app toggle + settings storage (existing per-tenant store, no schema) | L36-37 |
| 3.9 | The toggle is seeded by the install-time registration consent | L136 |
| 3.10 | Toggling an app off affects future connections only; already-granted identities keep their grants | L39-41 |
| 3.11 | Bulk-revoke as a separate explicit action on the circle's member list | L40-41 |
| 3.12 | The whole mechanism sits under the existing global auto-accept settings | L41-43 |
| 3.13 | Apps may also create circle definitions at runtime (feed's per-channel AUDIENCE circle) | L225-228 |

## Cat 4 — Cross-app enrollment

| # | Item | Line |
|---|---|---|
| 4.1 | Pending-enrollment queue riding the connection registration record — no new schema | L213-220 |
| 4.2 | Per-app drain on the next App-Key run; idempotent and additive | L218-220 |
| 4.3 | Client surfaces the app's toggle as *pending* until processed | L220 |
| 4.4 | Owner-console (master key) path for grants beyond every app's reach | L95-97 |

## Cat 5 — Retirement

| # | Item | Line |
|---|---|---|
| 5.1 | Auto Connections system circle → per-app `Connect` circles | L114 |
| 5.2 | Confirmed Connections system circle → per-app `Review` circles + review toggles | L115 |
| 5.3 | `CircleNetworkUtils` origin→circle routing → flow-scoped enrollment | L116 |
| 5.4 | `ConfirmConnectionAsync` revoke/grant swap → `Review` enrollment at review time | L117 |
| 5.5 | `CannotGrantAutoConnectedMoreCircles` (3010) → deleted | L118 |
| 5.6 | `HandleDriveAdded`'s anon-read Read + storage-key grant moves to the app's own `Connect` circle | L232-234 |
| 5.7 | JS clients stop passing the two hardcoded system-circle GUIDs (`getExtendAppRegistrationParams`) | L143-148 |

## Dependencies

- Cat 1 → Cat 2. The tier has nothing to key on until the stamp is stored and written.
- Cat 1 → Cat 3. Enrollment at review needs the stamp, but not the tier.
- Cat 3 → Cat 5. The system circles are load-bearing until per-app defaults replace them.
- Cat 1.6 should land before or with Cat 2 — the tier is what makes the connections list
  ACL-expressible, which is when oversharing it starts to matter.

Cat 1 changes no behavior. Cat 2 is the first phase that does.

## Conflict to resolve before Cat 2

**L120-123 and L288-292 disagree about the same ACLs.**

- L120-123: legacy bare-`connected` file ACLs can be read as "member of any `Connect` circle" with
  **zero behaviour change**.
- L288-292: existing bare-`connected` ACLs — e.g. today's "Vetted" profile fields — become
  **reviewed-only**, "a deliberate tightening", explicitly *not* behavior-identical.

Profile attributes are stored as files, so the two sets overlap. The readings give opposite
outcomes for an unreviewed connection reading a Vetted profile field.

**Likely resolution: L288 is current, L120 is stale.** L288 says of itself "*(this revises part 4's
earlier mapping)*", and part 4's OQ9 (`CIRCLES_VISIBILITY_PROPOSAL.md` L805-809) does state the
behavior-identical version — so L288 is knowingly superseding the older plan. L120-123 appears to
be residue of that same older plan, left in place. Worth confirming with the author rather than
assuming, since the two give opposite access outcomes.

*(Inference from reading the three passages together — none of the docs flags it as a conflict.)*

## Open questions that become work

1. Does enabling an app later offer enrollment of *existing* connections (prompt once, default
   off), or future connections only? — L235-237, L338-341
2. Do `Review` circles carry permission keys (`AllowIntroductions`, `ReadWhoIFollow`), or do those
   leave circles and become per-connection settings? — L342-347

Neither is in a category above because neither is decided.

## Not in scope here

Part 3 (`weak-key-retirement.md`) — Drive PK transfers, pending accepts, the ICR-key escrow.
Part 4 (chat-kmp `CIRCLES_VISIBILITY_PROPOSAL.md`) — all client work, contact states, the review
dialog, the visibility picker. Part 4 must move together with Cat 1-3 (L12-14).
