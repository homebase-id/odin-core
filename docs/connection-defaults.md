# Connection defaults: enrollment, default circles, and the review (part 2/2)

*Status: proposal for the phase **after** the drive-addressing / app-owned-circles work — for
discussion. The schema columns this doc relies on (`Enrollment` on `Circle`,
`AutoConnectDefaults` on `AppRegistrations`, plus `Designation` and `Emoji`) ship **dormant**
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

App-owned circles can absorb both, via two columns that ship (dormant) with the
drive-addressing schema work: `Circle.Enrollment`
(`NONE | AUTO_CONNECT | VERIFIED_CONNECT`, indexed) and
`AppRegistrations.AutoConnectDefaults`. **The DDL lives in `drive-addressing.md`**
(*Connection defaults* section) — this document defines only what the values mean.

**Semantics.**

- `AUTO_CONNECT` — a circle *eligible for unattended enrollment*: it may be granted without the
  owner reviewing anything. Two ways that happens. An **auto-connection** — an introduction, or a
  request auto-approved because the owner's settings permit it — carries no app context, so it
  enrolls the `AUTO_CONNECT` circles of every app with `AutoConnectDefaults` set **and enabled by
  the owner**: the flag is the app's *declaration*; a per-app toggle in the owner console is the
  owner's *consent* (seeded by the existing install-time registration consent). The effective set
  is declared ∧ enabled — the same declare/dispose pattern as the review-dialog toggles, as a
  standing policy instead of a per-contact choice. Toggling an app off affects future
  auto-connections only; already-enrolled identities keep their grants (bulk-revoke is a separate
  explicit action on the circle's member list). The whole mechanism sits under the existing global
  auto-accept settings — if auto-connections are disabled entirely, no app toggle matters. An accept that
  goes through a specific **app's consent flow** names its circles explicitly (the V2
  accept-request endpoint already takes circle grants in the body, #1599) — and only
  `AUTO_CONNECT`-designated circles are eligible there without review. A vendor app connecting to
  receive receipts therefore enrolls only its own circle: connected, deposit-capable, and *not*
  able to chat.
- `VERIFIED_CONNECT` — granted when the owner completes the connection review. Surfaced client-side
  as per-app toggles in the review dialog, checked by default, individually declinable.
- `NONE` — manual membership only. The default, and what every existing circle is.

**The deposit-only invariant.** An `AUTO_CONNECT` circle may grant only Write/React drive
permissions — no read beyond `AllowAnonymousReads` drives, and no permission keys. Enforced at
**definition-write time**, next to the confused-deputy validation (`drive-addressing.md`, *Circles*), and re-checked whenever
`Enrollment` changes. Rationale: the review is the key ceremony. Auto-connection hands out deposit
capability; storage keys for anything non-public are minted only by the owner's explicit act. An
identity that is never reviewed can *give* you things and *see* nothing — so the worst a
misbehaving `AutoConnectDefaults` app can achieve is unsolicited deposits into its own drive, never
exfiltration.

**Worked example — auto-connection.** `sam.dotyou.cloud` introduces `frodo.dotyou.cloud` to me;
my settings allow it, so the request auto-approves with no app context. Today that drops frodo
into the *Auto Connections* system circle, whose grant bundle is a platform constant. Under
enrollment, the connect pipeline instead asks the **app registry**: which apps set
`AutoConnectDefaults`, and what are their `AUTO_CONNECT` circles? Chat contributes its
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
`VERIFIED_CONNECT` circles as toggles, checked by default — and these, unlike auto circles, **may
in principle carry read grants**. Note the key context: the review runs **inside the app**, so
only App Keys are available — not the master key. That is sufficient, by construction: each
verified circle is owned by the app whose defaults it carries, an app circle may only reference
drives its app can already read (the definition-write rule — `drive-addressing.md`, *Circles*), and those are precisely the
drives whose storage keys its App Key can source. So a well-formed verified circle mints working
read grants in-app, and never a keyless one. Grants beyond any app's reach remain owner-console
territory (master key). The feed app's verified circle is the canonical case — secured-feed distribution is read-shaped, which is exactly why it must sit behind
the review rather than auto-enrollment. What frodo holds at each stage:

| Stage | Membership | Can | Cannot |
|---|---|---|---|
| auto-connection | chat/mail/moments `AUTO_CONNECT` circles | message me, mail me, react | read anything non-public |
| review: "Add to circles" (Friends ✓, feed toggle left on) | + Friends (personal), + feed `VERIFIED_CONNECT` | see Friends-visible profile fields, receive my secured feed | anything not granted to those circles |
| review: "Chat only" (all toggles off) | unchanged from row 1 | message me, mail me, react | still holds zero read keys |

The review is one atomic act: it confirms, enrolls the checked `VERIFIED_CONNECT` circles, and
grants any selected personal circles — no revoke/grant swap, no lockout, and a declined toggle
simply never mints.

**What this replaces.**

| Today | Becomes |
|---|---|
| Auto Connections system circle | per-app `AUTO_CONNECT` circles |
| Confirmed Connections system circle | per-app `VERIFIED_CONNECT` circles + explicit review toggles |
| `CircleNetworkUtils` origin→circle routing | flow-scoped enrollment |
| `ConfirmConnectionAsync` revoke/grant swap | `VERIFIED_CONNECT` enrollment at review time |
| `CannotGrantAutoConnectedMoreCircles` (3010) | deleted — review = confirm + grant in one step |

Migration note: legacy bare-`connected` file ACLs can be read as "member of any `AUTO_CONNECT`
circle" with **zero behaviour change** — today's evaluator already admits auto-connected callers to
`Connected` ACLs (`DriveAclAuthorizationService` folds `Connected` and `AutoConnected` into one
case, and no caller is ever stamped `AutoConnected`).

## Installing an app

Plainly, in five steps:

1. **The app asks** — for its drives, its permissions, and its **default circles**: each with a
   name, its grants, and *when it enrolls* (`AUTO_CONNECT` = on auto-connection,
   `VERIFIED_CONNECT` = at connection review).
2. **The owner approves** — one consent screen showing all of it.
3. **The server applies** — creates the drives, and creates each default circle **as a real row
   in the Circle table** with its declared enrollment value. Auto-connect circles are validated
   deposit-only right here.
4. **The approval seeds the owner-console toggle** for the app's auto-connect participation.
5. **From then on** — new auto-connections join the app's `AUTO_CONNECT` circle automatically;
   the review dialog offers its `VERIFIED_CONNECT` circle as a toggle. Existing connections are
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
(name, grants, `Enrollment`, `Designation`, `Emoji`): registering the chat app *creates* its
"Chat-only" circle with `Enrollment = AUTO_CONNECT` in the same consented act that today
authorizes the system circles on the chat drive. Why it belongs in the registration payload and
not a later mutation:

- **No gap** — `AUTO_CONNECT` means unattended enrollment; the first auto-connection after
  install must already know, or chat installs and introduced strangers cannot message until
  something else runs.
- **Consent coverage** — the install consent screen already renders the requested circle-drive
  access (`cd`); the enrollment declaration must sit inside the consented payload, and it seeds
  the owner-console toggle.
- The old fields remain for their other job: authorizing *someone else's* circles on the app's
  drives.

**Terminology first — a circle is three layers**, all existing in today's schema. The `Circle`
table holds **definitions** (one row per circle: name, `AppId`, `Enrollment`, the grant
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
     `Enrollment` = the declared value, grants = the declared grants.
  3. If `Enrollment = AUTO_CONNECT`, the insert is rejected unless the grants are deposit-only.
  4. On update: match incoming entries to existing rows by circle id — update those, insert the
     missing ones. Never duplicate.
  5. The request is also saved on the app registration row. It has two uses only: showing what
     the owner consented to, and re-creating the rows if a repair or migration needs to. It is
     never read on the hot path — the rows are the truth.

- **On auto-connect** (a connection is created with no app context):
  1. The server queries the **`Circle` table** — `WHERE Enrollment = AUTO_CONNECT` (this query
     is why `Enrollment` is an indexed column) — and keeps the circles whose app the owner's
     per-app toggle enables. The stored registration JSON is **not** read here; rows are the
     truth. (An accept that came through an app's consent flow skips this and names its circles
     directly, #1599.)
  2. It adds the new identity as a member of each.
  Nothing else happens. No circle definition changes.

- **On verify** (the owner completes the connection review — including approving a connection
  request, which is the review happening at accept time):
  1. The client sends the circles chosen in the dialog: the checked `VERIFIED_CONNECT` toggles,
     the selected personal circles — **and, for any checked app the contact is not yet a member
     of, its `AUTO_CONNECT` circle.** That covers the baselines auto-connect never granted: a
     manual accept (auto-connect never ran), a toggle that was off at the time, or an app
     installed after the connection. Without this, an owner-approved contact would hold no chat
     write at all. `AUTO_CONNECT` means *eligible for unattended enrollment*, not *only enrolled
     unattended*.
  2. The server adds the contact as a member of each. Enrollment is idempotent — already a
     member is a no-op.
  Nothing is removed — membership from auto-connect stays.

One refinement to "definitions only at app creation": apps may also create circles at **runtime**
through the same definition-write path (the feed app mints an `AUDIENCE` circle per encrypted
channel drive) — but membership still moves only at auto-connect, verify, or an explicit owner
edit.

Two steps that are easy to miss:

- **Anonymous-read drives**: the Read + storage-key grant that `HandleDriveAdded` today adds to
  *both system circles* is instead added to **the app's own `AUTO_CONNECT` circle** — that is how
  connections keep decrypting public-drive content once the system circles are gone.
- **Existing connections**: enrolling them into a newly installed app's auto circle is offered,
  never automatic — one-time prompt (default off) vs future-connections-only is the remaining
  detail of open question 5.

*Related:* the client-side circles proposal (chat-kmp PR #1062, `CIRCLES_VISIBILITY_PROPOSAL.md`)
wants two more per-circle fields on this same registration record: a `Designation`
(`PERSONAL | AUDIENCE | VENDOR` — contact-book presentation and filtering; default circles carry
none, their rendering keys off `Enrollment`) and an optional user-chosen `Emoji`.

## Sequencing

- The four columns land (dormant) with the drive-addressing schema work — regenerated CRUD +
  migration, everything defaulting to today's behavior.
- This phase then: apps register their default circles, the enrollment pipeline turns on, the
  system circles / origin routing / `ConfirmConnectionAsync` swap / 3010 lockout retire, and the
  clients ship the review UX (chat-kmp PR #1062).

## Open questions

1. ~~Install-time consent for `AutoConnectDefaults`.~~ **Resolved** by the declare/dispose split:
   the app's flag is only a declaration; the owner holds a per-app toggle in the owner console,
   seeded by the install-time registration consent. Remaining detail: does enabling an app later
   offer enrollment of *existing* connections (prompt once, default off), or future ones only?
2. **Do `VERIFIED_CONNECT` circles carry permission keys** (`AllowIntroductions`,
   `ReadWhoIFollow`)? Keys are identity-wide, not drive-scoped — either they are allowed on
   verified circles only (never auto — the deposit-only invariant forbids it), or they leave
   circles entirely and become per-connection settings toggled at review. Either way, **no new
   schema**: permission keys already live in circle definitions, and per-connection settings
   would ride the existing connection-registration record.
