# Connection defaults: enrollment, default circles, and the review (part 2/4)

Previous doc: odin-core PR#1589 docs/drive-addressing.md
Next doc: odin-core PR#1589 docs/weak-key-retirement.md

*Status: proposal for the phase **after** the drive-addressing / app-owned-circles work — for
discussion. The schema columns this doc relies on (`GrantOn` on `Circle`,
plus `Designation` and `Emoji`) ship **dormant**
with the drive-addressing schema work (see `drive-addressing.md`, *Schema*); every default is
"off", so nothing in this document changes behavior until this phase is implemented.*

*This is the **server half** of the phase. The client/product half — contact states, the review
dialog, per-app presentation — is chat-kmp PR #1062 (`CIRCLES_VISIBILITY_PROPOSAL.md`); the two
documents cross-reference and must move together.*


Two hardcoded system circles (`CircleConstants.cs`) currently decide what every new connection can
do. *Auto Connections* and *Confirmed Connections* carry an **identical** drive-grant bundle —
Write|React on the chat/lists/moments/mail/feed drives; confirmed adds only ShardRecovery write,
`AllowIntroductions`, and feed-distribution eligibility. Two problems: the bundle is a platform
constant that is really the **chat suite's** default (connecting with a receipts vendor should not
invite them to chat), and "confirm" is a special-cased swap between the two circles
(`ConfirmConnectionAsync`: revoke auto, grant confirmed) guarded by a lockout
(`CannotGrantAutoConnectedMoreCircles`, 3010).

App-owned circles can absorb both, via one indexed column that ships (dormant) with the
drive-addressing schema work: **`Circle.GrantOn`** (`None | Connect | OwnFlowConnect | Review`).
**The DDL lives in `drive-addressing.md`** (*Connection defaults* section) — this document
defines only what the values mean.

**Semantics.**

- `Connect` — granted at **any** connection establishment, ambient included: an
  **auto-connection** (an introduction, or a request auto-approved because the owner's settings
  permit it) carries no app context, so the pipeline grants every `Connect` circle whose app the
  owner has **enabled** — the circle's `GrantOn` is the app's *declaration*; a per-app toggle in
  the owner console is the owner's *consent* (seeded by the install-time registration consent).
  The effective set is declared ∧ enabled — the same declare/dispose pattern as the review-dialog
  toggles, as a standing policy instead of a per-contact choice. Toggling an app off affects
  future connections only; already-granted identities keep their grants (bulk-revoke is a
  separate explicit action on the circle's member list). The whole mechanism sits under the
  existing global auto-accept settings — if auto-connections are disabled entirely, no app toggle
  matters.
- `OwnFlowConnect` — granted **only** when the connection is created through the **owning app's
  own consent flow**, never ambiently. The accept names its circles explicitly (the V2
  accept-request endpoint already takes circle grants in the body, #1599). This is the vendor
  case: the receipts app's circle is granted when the hotel connects through its consent screen —
  connected, deposit-capable, and *not* able to chat — while a friend introduced through the
  chat app never touches it.
- `Review` — granted when the owner completes the connection review. Surfaced client-side as
  per-app toggles in the review dialog, checked by default, individually declinable.
- `None` — manual membership only. The default, and what every existing circle is.

*One column, not two.* An earlier design split this policy — a per-circle enum plus an
app-level "participate in ambient connects" flag — which put half the answer in each of two
tables and could not express an app holding one ambient circle *and* one own-flow-only circle.
`GrantOn` says it all in one place.

**The deposit-only invariant.** An `Connect` circle may grant only Write/React drive
permissions — no read beyond `AllowAnonymousReads` drives, and no permission keys. Enforced at
**definition-write time**, next to the confused-deputy validation (`drive-addressing.md`, *Circles*), and re-checked whenever
`GrantOn` changes. Rationale: the review is the key ceremony. Auto-connection hands out deposit
capability; storage keys for anything non-public are minted only by the owner's explicit act. An
identity that is never reviewed can *give* you things and *see* nothing — so the worst a
misbehaving grant-on-connect app can achieve is unsolicited deposits into its own drive, never
exfiltration.

**Worked example — auto-connection.** `sam.dotyou.cloud` introduces `frodo.dotyou.cloud` to me;
my settings allow it, so the request auto-approves with no app context. Today that drops frodo
into the *Auto Connections* system circle, whose grant bundle is a platform constant. Under
`GrantOn`, the connect pipeline instead asks the **app registry**: which enabled apps declare
`GrantOn = Connect` circles? Chat contributes its
chat-defaults circle (ChatDrive Write|React), mail and moments likewise. Frodo can message me
immediately — the bootstrapping property survives — while holding **zero read keys**, by the
deposit-only invariant. This is how the app registry *replaces* the connected circle: the system
circle was a frozen union of every app's defaults, compiled into `CircleConstants.cs`; enrollment
computes the same union at connect time from registrations — and an accept with app context
bypasses the default set entirely, naming its own circles. The proof it's better:
`hotel.example.com` accepted through the receipts app's consent screen enrolls in the receipts
circle only — connected, able to deposit purchase history, unable to chat. A frozen union cannot
express that; per-app registration makes it the default. And nothing is left for a bare
`connected` ACL tier to do: content targets circles, baseline capability comes from membership,
and "is connected" survives only as the wire-level perimeter check.

**Worked example — the review.** Later I review frodo (the review UX — adaptive button, per-app
toggles, personal-circle selection — is specified client-side in chat-kmp's
`CIRCLES_VISIBILITY_PROPOSAL.md`, chat-kmp PR #1062). The dialog surfaces each app's
`Review` circles as toggles, checked by default — and these, unlike auto circles, **may
in principle carry read grants**. Note the key context: the review runs **inside the app**, so
only App Keys are available — not the master key. That is sufficient, by construction: each
verified circle is owned by the app whose defaults it carries, an app circle may only reference
drives its app can already read (the definition-write rule — `drive-addressing.md`, *Circles*), and those are precisely the
drives whose storage keys its App Key can source. So a well-formed verified circle mints working
read grants in-app, and never a keyless one. Grants for apps whose keys the reviewing
client does not hold are not lost — they queue as pending enrollments and complete when that app
next runs (see *Cross-app verified enrollment*); only grants beyond *every* app's reach remain
owner-console territory (master key). The feed app's verified circle is the canonical case — secured-feed distribution is read-shaped, which is exactly why it must sit behind
the review rather than auto-enrollment. What frodo holds at each stage:

| Stage | Membership | Can | Cannot |
|---|---|---|---|
| auto-connection | chat/mail/moments `Connect` circles | message me, mail me, react | read anything non-public |
| review: "Add to circles" (Friends ✓, feed toggle left on) | + Friends (personal), + feed `Review` | see Friends-visible profile fields, receive my secured feed | anything not granted to those circles |
| review: "Chat only" (all toggles off) | unchanged from row 1 | message me, mail me, react | still holds zero read keys |

The review is one atomic act: it confirms, enrolls the checked `Review` circles, and
grants any selected personal circles — no revoke/grant swap, no lockout, and a declined toggle
simply never mints.

**What this replaces.**

| Today | Becomes |
|---|---|
| Auto Connections system circle | per-app `Connect` circles |
| Confirmed Connections system circle | per-app `Review` circles + explicit review toggles |
| `CircleNetworkUtils` origin→circle routing | flow-scoped enrollment |
| `ConfirmConnectionAsync` revoke/grant swap | `Review` enrollment at review time |
| `CannotGrantAutoConnectedMoreCircles` (3010) | deleted — review = confirm + grant in one step |

Migration note: legacy bare-`connected` file ACLs can be read as "member of any `Connect`
circle" with **zero behaviour change** — today's evaluator already admits auto-connected callers to
`Connected` ACLs (`DriveAclAuthorizationService` folds `Connected` and `AutoConnected` into one
case, and no caller is ever stamped `AutoConnected`).

## Installing an app

Plainly, in five steps:

1. **The app asks** — for its drives, its permissions, and its **default circles**: each with a
   name, its grants, and *when it enrolls* (`Connect` = on auto-connection,
   `Review` = at connection review).
2. **The owner approves** — one consent screen showing all of it.
3. **The server applies** — creates the drives, and creates each default circle **as a real row
   in the Circle table** with its declared `GrantOn` value. Grant-on-connect circles are validated
   deposit-only right here.
4. **The approval seeds the owner-console toggle** for the app's auto-connect participation.
5. **From then on** — new auto-connections join the app's `Connect` circle automatically;
   the review dialog offers its `Review` circle as a toggle. Existing connections are
   *not* enrolled automatically — that is offered separately.

The detail behind each step:

Today's registration request **already declares circle access at install time**:
`AppRegistrationRequest` carries `AuthorizedCircles` ("circles whose members can work with your
identity via this app") plus `CircleMemberPermissionGrant` (what those members get) — and the JS
clients pass exactly the two hardcoded system-circle GUIDs there (chat's `useAuth`:
`c = [AUTO_CONNECTIONS, CONFIRMED]`, `cd = [ChatDrive Write|React]`, via
`getExtendAppRegistrationParams`).

**So the enrollment declaration must be expressible at registration time** — it is the direct
successor of that field pair. In this phase the request grows a `DefaultCircles` list
(name, grants, `GrantOn`, `Designation`, `Emoji`): registering the chat app *creates* its
"Chat-only" circle with `GrantOn = Connect` in the same consented act that today
authorizes the system circles on the chat drive. Why it belongs in the registration payload and
not a later mutation:

- **No gap** — `Connect` grants ambiently; the first auto-connection after
  install must already know, or chat installs and introduced strangers cannot message until
  something else runs.
- **Consent coverage** — the install consent screen already renders the requested circle-drive
  access (`cd`); the enrollment declaration must sit inside the consented payload, and it seeds
  the owner-console toggle.
- The old fields remain for their other job: authorizing *someone else's* circles on the app's
  drives.

**Terminology first — a circle is three layers**, all existing in today's schema. The `Circle`
table holds **definitions** (one row per circle: name, `AppId`, `GrantOn`, the grant
*specification* — no people in it). `CircleMember` holds **membership** (one row per circle +
identity). And joining a circle **mints the definition's grants into that identity's connection
registration**, keys included — that per-connection grant is what actually opens drives; the
definition row is its recipe. "Add as a member" below always means both writes: the
`CircleMember` row and the minted grant.

**Three moments, and what happens at each.** Circle definitions and default membership change at
exactly three events — nothing happens between them:

- **On app creation** (register or update):
  1. The server reads the `DefaultCircles` list from the registration request.
  2. For each entry it inserts a row in the `Circle` table: `AppId` = the app,
     `GrantOn` = the declared value, grants = the declared grants.
  3. If `GrantOn = Connect`, the insert is rejected unless the grants are deposit-only.
  4. On update: match incoming entries to existing rows by circle id — update those, insert the
     missing ones. Never duplicate.
  5. The request is also saved on the app registration row. It has two uses only: showing what
     the owner consented to, and re-creating the rows if a repair or migration needs to. It is
     never read on the hot path — the rows are the truth.

- **On auto-connect** (a connection is created with no app context):
  1. The server queries the **`Circle` table** — `WHERE GrantOn = Connect` (this query
     is why `GrantOn` is an indexed column) — and keeps the circles whose app the owner's
     per-app toggle enables. The stored registration JSON is **not** read here; rows are the
     truth. (An accept that came through an app's consent flow skips this and names its circles
     directly, #1599.)
  2. It adds the new identity as a member of each.
  Nothing else happens. No circle definition changes.

- **On verify** (the owner completes the connection review — including approving a connection
  request, which is the review happening at accept time):
  1. The client sends the circles chosen in the dialog: the checked `Review` toggles,
     the selected personal circles — **and, for any checked app the contact is not yet a member
     of, its `Connect` circle.** That covers the baselines auto-connect never granted: a
     manual accept (auto-connect never ran), a toggle that was off at the time, or an app
     installed after the connection. Without this, an owner-approved contact would hold no chat
     write at all. `Connect` marks a circle *eligible to be granted without a review*, not
     *only granted ambiently*.
  2. The server adds the contact as a member of each. Enrollment is idempotent — already a
     member is a no-op.
  3. The server stamps **`ReviewedAt`** on the connection registration (a nullable column on the
     Connections table — DDL in part 1's *Schema*). This is what promotes the caller's security level (see *The security ladder,
     recut* below) and what clients derive the New-vs-reviewed state from.
  Nothing is removed — membership from auto-connect stays.

**Cross-app verified enrollment — the pending queue.** The reviewing client can only mint
read-bearing grants for apps whose App Keys it holds — its own suite. A checked toggle for any
*other* app (mail reviewed from the chat client, a third-party vendor app) cannot enroll
immediately: the keys are not in the process. So the review **records the decision and enqueues a
pending enrollment** (connection + circle), riding the existing connection-registration record —
no new schema. The next time that app runs with its App Key context, it processes its pending
queue: mints the grants, enrolls the connection in its `Review` circle, clears the
entry — idempotent and additive like all enrollment. Until processed, the app's toggle shows as
*pending* rather than active. (`Connect` enrollment is unaffected: deposit-only grants need
no read keys — exactly what the invariant guarantees — so the server completes those at any
time, from any context.)

One refinement to "definitions only at app creation": apps may also create circles at **runtime**
through the same definition-write path (the feed app mints an `AUDIENCE` circle per encrypted
channel drive) — but membership still moves only at auto-connect, verify, or an explicit owner
edit.

Two steps that are easy to miss:

- **Anonymous-read drives**: the Read + storage-key grant that `HandleDriveAdded` today adds to
  *both system circles* is instead added to **the app's own `Connect` circle** — that is how
  connections keep decrypting public-drive content once the system circles are gone.
- **Existing connections**: enrolling them into a newly installed app's auto circle is offered,
  never automatic — one-time prompt (default off) vs future-connections-only is the remaining
  detail of open question 5.

*Related:* the client-side circles proposal (chat-kmp PR #1062, `CIRCLES_VISIBILITY_PROPOSAL.md`)
wants two more per-circle fields on this same registration record: a `Designation`
(`PERSONAL | AUDIENCE | VENDOR` — contact-book presentation and filtering; default circles carry
none, their rendering keys off `GrantOn`) and an optional user-chosen `Emoji`.

## The security ladder, recut

The last gap: after a review, the owner needs ACL-expressible distinctions like *"who can see my
connections list — reviewed people"*, while a freshly introduced contact must see **nothing**
extra. Today's ladder cannot say this.

**Today** (`SecurityGroupType.cs`): `Anonymous = 111`, `Authenticated = 444`,
`AutoConnected = 555`, `Connected = 777`, `Owner = 999`, `System = 1`. Three audited facts drive
the recut:

- The ACL evaluator folds `Connected`/`AutoConnected` into one case, and **no caller is ever
  assigned 555** — a `connected` ACL admits every connection, unreviewed included. The tier's
  promise has always been broken.
- The "ConnectedIdentitiesCanViewConnections / WhoIFollow" tenant settings are implemented as
  permission keys **on the Confirmed circle** (`TenantConfigService.cs:293-309`) — the system
  already treats *reviewed* as the operative tier for exactly this use case; it just has no
  first-class name, and the Confirmed circle retires with this series.
- DB filtering is `requiredSecurityGroup BETWEEN 0 AND callerLevel`; the numeric ordering is
  load-bearing and must be preserved.

**The end state:**

| Level (value) | Who | Use cases |
|---|---|---|
| `Anonymous` (111) | anyone on the internet | public profile card, public posts, the drive public-key endpoint, initiating a connection request |
| `Authenticated` (444) | any logged-in Homebase identity **and every unreviewed connection** | commenting/reacting on public posts; authenticated-tier profile attributes. The must-not case lands here **by construction**: a 3 a.m. introduction can deposit (via circles) but *reads* nothing beyond any stranger identity |
| `Reviewed` (777 — today's `Connected` slot, recut) | connections the owner completed the review for; **includes every circle member by construction** (adding to a circle requires the review) | "who can see my connections list" / "who I follow" (replaces the Confirmed-circle permission keys — the settings' labels finally match their semantics); react/comment on secured posts; low-sensitivity social metadata. **Not** for high-sensitivity data — the home address stays on enumerated personal-circle ACLs |
| `circleIdList` (orthogonal, unchanged) | members of named circles | birthday, home address, per-circle photos, feed channels — everything part 4's visibility picker covers |
| `Owner` (999) / `System` (1) | unchanged | drafts, settings, contact records / internal |
| ~~`AutoConnected` (555)~~ | **deleted** | never assigned to a caller; its accidental ACL semantics are documented in part 4 |

Mechanics:

- **Assignment, not evaluation, changes.** At transit/YouAuth context build:
  `ReviewedAt != null` → 777, else → 444. The evaluator and the DB range query are untouched.
- **"Connected-but-unreviewed" survives as an internal caller classification** (perimeter
  checks, deposit eligibility) but is not an ACL-targetable level.
- **Wire compatibility:** the serialized string `connected` keeps the 777 slot; the enum member
  and every UX label become **Reviewed**.
- **The reviewed fact lives server-side**: stamped on the connection registration at the review
  (step 3 above; a real column — part 1's *Schema* — because the contact book filters and pages
  on it at audience scale, where blob fields fail). It is the owner's own recorded act — the ambient-authority objection that
  killed designation-qualified ACLs (part 4) does not apply. Owner-private, never sent to the
  peer, visible to all owner clients via GetConnectionInfo.
- **Migration is intent-restoring, not behavior-identical** (this revises part 4's earlier
  mapping): existing bare-`connected` ACLs — e.g. today's "Vetted" profile fields — become
  reviewed-only. A deliberate tightening: unreviewed connections lose access they never should
  have had, and "Vetted" finally delivers what it always claimed. Call it out as a behavior
  change in release notes.

**Viewer-scoped redaction — a required companion.** The connections-list API currently
contradicts the "owner-private, never sent to the peer" promise: `CircleNetworkController` is
mounted on **both the app route and the guest (YouAuth) route**, and both return the same
`Redacted()` shape — which includes, per connection, the legacy `Vetted` flag (becoming
`reviewedAt`), `introducerOdinId`, `connectionRequestOrigin`, and a redacted grant with circle
info. The guest path is gated only by `ReadConnections`, which anonymous viewers can hold when
the tenant setting allows. So anyone permitted to see the list today also sees the owner's
*judgments* about each contact. Fix: **two shapes from one record** —

- **Owner/app viewers** (the owner's own clients): the full redacted shape — `reviewedAt`-derived
  state, origin, introducer, grants. This is what the contact book runs on.
- **Third-party viewers** (guest, reviewed peers, anonymous where enabled): an **identity list
  only** — odinId plus the public contact card, nothing else.

*The connections list a peer may see is a list of identities, never a list of my judgments.* The
"who can see my connections" setting decides **whether** third parties see the list; viewer-scoped
redaction decides **what** they see — without it, enabling the setting overshares regardless of
tier. (Today's `omitContactData` flag is the wrong axis: it strips the harmless part and keeps
the sensitive part.)

**API surface.** The review is **one atomic endpoint on the connections API** — stamp
`ReviewedAt` + enroll the checked circles in a single call (the accept-with-circle-grants
endpoint, #1599, is the precedent) — gated by `ManageCircleMembership`: the same trust level as
granting a circle, which implies a review anyway. The owner/app redacted connection shape
replaces the legacy `Vetted` boolean with **`reviewedAt`** (V1 compat: `vetted` served as
`reviewedAt != null` during transition). The server-side **contact** API needs nothing: contact
records are owner-only and carry no review state — review is connections-API domain, and it
should stay that way. Likewise **no server endpoint** for "is this contact in any personal
circle" — clients compute it from connection info ∩ circle definitions; a server-side
personal-circle predicate is the rejected designation-qualified ACL by another door. The review
endpoint's one designation consultation is **invariant enforcement**: un-review (clearing
`ReviewedAt`) is rejected while the contact holds any `PERSONAL`-designated membership —
`circleIdList` ACLs check membership, not tier, so membership must imply review.

## Sequencing

- The four columns land (dormant) with the drive-addressing schema work — regenerated CRUD +
  migration, everything defaulting to today's behavior.
- This phase then: apps register their default circles, the enrollment pipeline turns on, the
  system circles / origin routing / `ConfirmConnectionAsync` swap / 3010 lockout retire, and the
  clients ship the review UX (chat-kmp PR #1062).

## Open questions

1. ~~Install-time consent for `GrantOn`.~~ **Resolved** by the declare/dispose split:
   the app's flag is only a declaration; the owner holds a per-app toggle in the owner console,
   seeded by the install-time registration consent. Remaining detail: does enabling an app later
   offer enrollment of *existing* connections (prompt once, default off), or future ones only?
2. **Do `Review` circles carry permission keys** (`AllowIntroductions`,
   `ReadWhoIFollow`)? Keys are identity-wide, not drive-scoped — either they are allowed on
   verified circles only (never auto — the deposit-only invariant forbids it), or they leave
   circles entirely and become per-connection settings toggled at review. Either way, **no new
   schema**: permission keys already live in circle definitions, and per-connection settings
   would ride the existing connection-registration record.
