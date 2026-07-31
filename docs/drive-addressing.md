# Drive addressing: DriveId, Type, Slug, AppId

*Status: proposal, for discussion. Prompted by the app-owned-drives direction.*

Today a drive is addressed by Guids that both ends must hardcode. We want an app to name its own
drives, and a **remote** caller to address them, with short slugs — `/apps/chat/drives/messages`.
Getting there means untangling one overloaded field (`Type`). The API is what this is *for*, so it
leads.

Apps also need to own the **circles** they create, which turns out to need the same `AppId` column —
see *Circles* below.

## Addressing & API (V2)

A drive ends up with two names, for two different jobs:

- **`DriveId`** — the internal handle. Callers can still address a drive by it, exactly as today.
- **`(AppSlug, DriveSlug)`** — the portable name. This is the payoff: it is how a **remote** caller
  addresses a drive **on another identity**.

Today's V2 routes take the Guid:

```
     /api/v2/drives/{driveId:guid}/files
/api/v2/peer/{odinId}/drives/{driveId:guid}/files
```

The slug form slots in beside them:

```
          /api/v2/apps/{appSlug}/drives/{driveSlug}/files
/api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/files
```

Note the shape: **every segment names what follows it.** The slug tree hangs off a new `/apps` root
(free today) rather than burrowing under `/drives`, where `/drives/feed` would read as *"the drive
called feed."* The two trees are simply separate, so slugs can be added without touching the Guid
routes and without any route-constraint trickery.

The remote case is the one that matters. Sending a message to another identity's chat drive today
requires both sides to share hardcoded Guid constants (`TargetDrive` = `DriveId + Type`, from
`SystemDriveConstants`). With slugs:

```
POST /api/v2/peer/frodo.dotyou.cloud/apps/chat/drives/messages/files
                                          └app┘        └ drive ┘
```

Neither side shares a Guid. Authorization is unchanged — the sender still needs a drive grant via a
circle; the slug replaces only the *address*.

### Enumeration, and the feed example

The **feed app** (`AppSlug = feed`) invents a channel type — `DriveTypeSlug = channel`, backed by
`DriveTypeGuid = ff42…` — and owns *zero or more* drives of that type, each with its own
`DriveSlug`. An app's drives are a collection, so they get a resource of their own:

```
GET /api/v2/apps/feed/drives                 → every drive the feed app owns
GET /api/v2/apps/feed/drives?type=channel    → just its channel drives

→ [ { "driveSlug": "news",   "driveTypeSlug": "channel" },
    { "driveSlug": "photos", "driveTypeSlug": "channel" } ]
```

Note what is *absent*: no Guids. An app names its own drives entirely in slugs, all the way down to
`/api/v2/apps/feed/drives/news/files`.

**The Guids are backwards compatibility.** During the transition each entry also carries `driveId`
and `driveTypeGuid`, because existing clients still assemble `TargetDrive = (DriveId, Type)` for the
Guid routes. Both are scaffolding: once `TargetDrive` retires, they drop out of the response
entirely — see *Cost & sequencing*.

This is also why the **type** must stay **out of any unique key**: the feed app has many drives
sharing one type, distinguished by `DriveSlug`. `feed/drives/news` names a *drive*; `channel` names
its *category*.

**This retires `GET /api/v2/drives/metadata/channel-drives`.** That endpoint exists *only* because
`Type` is a global constant — it is hardcoded to `SystemDriveConstants.ChannelDriveType`. Once the
type belongs to the app, `GET /api/v2/apps/feed/drives?type=channel` replaces it. (The lookup under
`driveManager.GetDrivesAsync(type, …)`, must likewise become `(appId, type)`; a bare `type` is
ambiguous once apps pick their own.)

### Scoping

A caller only ever sees drives **it can already reach** — its own, plus any cross-app grants. An app
asking for another app's drives gets the granted subset, never that app's full inventory; the owner
console sees all. Same constraint as the rest of the app-owned work: *an app gains nothing beyond
its own reach.*

Two consequences:

- `type` is **app-private**: a non-owning caller cannot interpret it. *Long term* it is a pure
  category — something to filter on, not part of any address — and could be omitted for callers
  that don't own the drive. **Not during the transition**, though: until the slug pair replaces
  `TargetDrive`, `type` is still half of the address every existing client builds, so it must keep
  being returned to everyone. See *Cost & sequencing*.
- `AppId` must be an **indexed column**, not a JSON field: this is a per-app enumeration on the
  startup path of every app.

### Slugs are resolved by the recipient

`/peer/{odinId}/apps/chat/drives/messages` means *"whatever app **that identity** registered under
`chat`, and its `messages` drive."*

**`AppSlug` names a particular app by a particular author** — it is a package name, not a role. A
second chat implementation does not get to call itself `chat`; it picks its own slug (`chatty`). It
can still *reference* `chat`'s `messages` drive — addressing the first app's drive — provided it is
file-format compatible and has been granted access. **Interop happens by referencing another app's
drive, never by occupying its name.**

`UNIQUE(identityId, AppSlug)` makes resolution unambiguous, and **authorization is unchanged**: the
sender needs a drive grant on whatever the slug resolves to. So this is not an access-control hole.

What remains is a **namespace** question, and it is real:

- **There is no global registry.** `AppSlug` is unique per *identity*. Nothing stops a different
  author from shipping an app that registers itself as `chat` on an identity where the real one is
  not installed — a sender's `/apps/chat/drives/messages` then resolves to the impostor's drive. It still
  cannot be written to without a grant, so this is a naming problem, not an access one.
- **Registration is first-come.** On a given identity the *second* app wanting `chat` cannot have
  it. So an app cannot assume its own preferred slug is available.
- **The slug is quietly doing global-identifier duty.** For a remote `/apps/chat/…` to mean
  anything, sender and recipient must agree on what `chat` is. That is a lot of weight for a flat,
  unowned, ≤12-character namespace. `AppId` is the thing that is actually globally unique.

## Why this needs a model change

A drive's `Type` currently does two unrelated jobs, and that's what blocks the above:

1. **Part of the address** — a drive is looked up by `DriveId + Type`, the pair that `TargetDrive`
   carries on the wire.
2. **A cross-identity vocabulary** — `SystemDriveConstants.ChannelDriveType` is how a follower
   *on another identity* discovers that a drive is a channel.

Job 1 turns out to be an illusion. `DriveId` is **already unique per identity**
(`UNIQUE(identityId, DriveId)`), so `Type` contributes nothing to resolution: the two-argument
lookup can only ever match on `DriveId`, and `UNIQUE(identityId, DriveId, DriveType)` is redundant.

*(In today's code `TargetDrive.Alias` **is** the `DriveId` — same value, two names. This doc says
`DriveId` throughout.)*

Job 2 is real — and it's what makes "Type is app-private" dangerous.

## The model

Split the one overloaded `Type` into things with distinct jobs:

| Concern | Today | Proposed |
|---|---|---|
| Resolve a drive | `DriveId + Type` | **`DriveId`** alone — the drive's identifier |
| Federate ("subscribable", "public") | `Type == ChannelDriveType` | **capability flags** — `AllowSubscriptions`, `AllowAnonymousReads` (already exist) |
| Address that drive (URL, and remotely) | — | **`DriveSlug`** — e.g. `news`; short (≤12 chars), `[a-z0-9-]`, unique **per app**, immutable |
| Categorize it, app-privately | — | **`DriveTypeGuid`** — a **Guid the app invents**; the feed app decides its channel drives are `ff42…` |
| …readably | — | **`DriveTypeSlug`** — e.g. `channel`; the same category, spelled for humans and query strings |
| Address an app (URL, and remotely) | — | **`AppSlug`** — e.g. `chat`, unique **per identity** |
| Own / cascade-delete | — | **`AppId`** — nullable; `null` = not app-owned |

**Everything comes in pairs — an exact Guid for the system, a slug for humans and URLs:**

| | Guid | Slug |
|---|---|---|
| the app | `AppId` | `AppSlug` |
| the drive | `DriveId` | `DriveSlug` |
| the drive's category | `DriveTypeGuid` | `DriveTypeSlug` |

An app then names its drives `(DriveTypeSlug, DriveSlug)` — e.g. `("channel", "news")` — and never
needs to know a Guid at all.

**`Type` stays a Guid — the app just invents it.** Nothing about its representation changes; only
its *meaning* goes from "a value the whole system agrees on" to "a value the owning app chose."

**The type pair needs its own row, which conveniently fixes integrity.** `DriveTypeGuid` and
`DriveTypeSlug` are one-to-one *within an app*, and many drives share them — so neither can be a
column on `Drives` without denormalising and inviting drift. Give types a small registry table (see
*Schema*). It then answers a question that was otherwise awkward:

*Should two apps be allowed the same type Guid?* In principle yes — apps are developed
independently and drives are `AppId`-scoped, so no app can *use* another's drives. But the
realistic cause of a collision isn't chance (random Guids never collide), it's **copy-paste** —
someone forks the feed app and keeps its constants. Worth rejecting. As a bare rule it is an
awkward functional dependency (*within an identity, a type Guid belongs to at most one `AppId`*)
that no `UNIQUE` on `Drives` can express, since the feed app has many drives sharing that type. On
the registry table it is simply `UNIQUE(identityId, DriveTypeGuid)`.

## Schema

Two nullable columns on `Drives`, one on the app registration — plus the constraints that make slug
addressing safe.

```sql
-- Drives (existing table; two new nullable columns)
AppId      BYTEA,        -- owning app; NULL = not app-owned
DriveSlug  TEXT,         -- URL/wire segment; NULL when AppId is NULL

, UNIQUE(identityId, AppId, DriveSlug)   -- one "news" per app
-- index (identityId, DriveType)         -- legacy by-type lookups

-- DriveTypes (NEW table; the DriveTypeGuid ↔ DriveTypeSlug pair, per app)
identityId     BYTEA NOT NULL,
AppId          BYTEA NOT NULL,
DriveTypeGuid  BYTEA NOT NULL,   -- the Guid the app invented
DriveTypeSlug  TEXT  NOT NULL,   -- readable form, e.g. "channel"

, UNIQUE(identityId, DriveTypeGuid)          -- a type Guid belongs to at most one app
, UNIQUE(identityId, AppId, DriveTypeSlug)   -- one "channel" per app

-- AppRegistrations (NEW table; see below)
identityId   BYTEA NOT NULL,
AppId        BYTEA NOT NULL,   -- the app's stable Guid
AppSlug      TEXT  NOT NULL,   -- URL/wire segment
Name         TEXT  NOT NULL,   -- human title, unchanged semantics
CorsHostName TEXT,
grantJson    TEXT  NOT NULL,   -- today's registration payload (KeyStore, authorized circles, …)
detailsJson  TEXT,             -- reserved; nothing writes it yet
created, modified

, UNIQUE(identityId, AppId)
, UNIQUE(identityId, AppSlug)            -- recipient-side resolution must be unambiguous
```

**Include `detailsJson` from day one, even unused.** Adding a column later costs a generated-CRUD
regen plus an `AbstractMigrator` migration exercised across two dialects; adding an unused nullable
`TEXT` column now costs nothing. `Drives.detailsJson` is the precedent — and the reason the drive
side has somewhere to put new attributes without touching schema. Give the app side the same
escape hatch.

**`AppRegistrations` must become a real table.** App registrations live today in the shared
`KeyThreeValue` / `ThreeKeyValueStorage` blob, where `UNIQUE(identityId, AppSlug)` cannot be
expressed at all — slug uniqueness would be a best-effort code check over opaque rows. Since the
slug is a **wire address** that other identities resolve against, best-effort is not good enough.
Move registrations into their own table (columns above), migrated by a one-time shadow-table copy
(cf. `TableDrivesMigrationV202510311515`); no master key needed. It also gives `Drives.AppId` a real
FK target, and makes *delete app ⇒ delete its drives* expressible in SQL rather than
load-all-deserialize-filter.

*(The app-circle-membership plan proposes this same table for unrelated reasons. There it is
optional; here it is required.)*

### Slug format

A slug is a URL path segment *and* a wire address, so it must survive both with no encoding.
Applies to **`AppSlug`**, **`DriveSlug`** and **`DriveTypeSlug`** alike:

- `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$` — lowercase letters, digits, internal hyphens only.
- **No** spaces, `/`, `.`, `%`, `?`, `#`, `&`, `:`, `@`, and no uppercase. Nothing that needs
  percent-encoding; nothing readable as a path separator or as `.` / `..`.
- 1–12 characters — capped in the C# validator, not in the column type.
- **Not a reserved route segment.** A slug must never collide with a literal sibling at its
  position. Rooting the slug tree at `/apps` buys this almost for free: `{appSlug}` sits under
  `/apps` and `{driveSlug}` under `/apps/{appSlug}/drives`, and neither has a literal sibling
  today. (Under the rejected `/drives/{appSlug}/…` shape, `metadata` was a live collision —
  `/api/v2/drives/metadata/channel-drives`.) Keep a denylist anyway, and grow it whenever a
  literal segment is added at either position.

**Validate and reject at write; never coerce.** The value is immutable and ends up in *other
identities'* URLs, so silently lowercasing or stripping a character produces an address the caller
did not ask for.

### Make it a type, not a convention

**C# has nothing built in.** `Uri.IsWellFormedUriString` and `Uri.EscapeDataString` do not validate
a path-segment charset, and `Uri.CheckHostName` is for hosts. A rule enforced by "everyone
remembers to call the validator" will be violated.

So wrap it, exactly the way `OdinId` wraps a domain name — a `readonly struct` that **validates in
its constructor** and cannot exist in an invalid state:

```
OdinSlugValidator   — the rules (cf. AsciiDomainNameValidator)
readonly struct OdinSlug   — validates on construction; throws OdinClientException
OdinSlugConverter   — JsonConverter<OdinSlug>, reads/writes a plain string (cf. OdinIdConverter)
```

Then any `OdinSlug` anywhere in the system is URL-kosher **by construction**, which is the same
guarantee `OdinId` already gives for domains. Route binding needs nothing special: V2 handlers
already take `[FromRoute] string odinId` and construct the value type immediately — do the same.
The column stays `TEXT`; convert at the storage boundary.

**We already have the bug this prevents.** `ChannelDefinition.Slug` and `PostContent.Slug` are
plain client-supplied `string`s, and `HomebaseSsrService` interpolates them straight into a path —
`ToSsrUrl($"/posts/{channelKey}/{content.Slug}")` — where `SsrUrlHelper.ToSsrUrl` only *trims*; it
does not escape. A slug containing `/`, `..`, or `%` yields a malformed or mis-pointing link today.
Worth fixing on its own, and a good reason to land the type before more slugs exist.

Why these shapes:

- **`UNIQUE(identityId, AppId, DriveSlug)` — per app, not per identity.** The URL carries both
  segments, so `feed/drives/news` and `chat/drives/news` may coexist. A drive slug need only be unique
  *within its app*.
- **That unique index doubles as the enumeration index.** Its `(identityId, AppId)` prefix serves
  `GET /api/v2/apps/feed/drives`; the `?type=` filter then runs over that app's handful of drives.
- **The `DriveTypes` table** carries the `DriveTypeGuid` ↔ `DriveTypeSlug` pair and enforces
  *a type Guid belongs to at most one app* as a plain `UNIQUE`, which no constraint on `Drives`
  could express.
- **`UNIQUE(identityId, DriveId, DriveType)` can be dropped** once resolution is by `DriveId` — it
  is already redundant against `UNIQUE(identityId, DriveId)`.

One trap:

- **NULLs are distinct in a unique index** (SQLite *and* Postgres). So
  `UNIQUE(identityId, AppId, DriveSlug)` constrains **nothing** for system drives, where
  `AppId IS NULL` — two of them could both claim `profile`. Enforce the invariant **`AppId` and
  `DriveSlug` are set together, or both `NULL`** — then every slug-bearing row is covered, and
  system drives are simply not slug-addressable.

Mechanics: nullable columns (`Guid? AppId`, `string DriveSlug`), `TEXT` with a C#-side length check
per convention (never `char(n)` — Postgres blank-pads it, SQLite ignores it). The CRUD is generated
with a version header, so this is regenerate + an `AbstractMigrator` migration, exercised on **both**
SQLite and Postgres.

## Circles

An app should be able to **create and modify its own circles** — the app-circle-membership plan
already assumes this ("apps own app circles they can create and delete"). That needs the same
ownership column drives get:

```sql
-- Circle (existing table; one new nullable column)
AppId  BYTEA,        -- owning app; NULL = owner circle
-- index (identityId, AppId)
```

**A column, not a field in the JSON.** `TableCircle` keeps the definition in an opaque
`data BYTEA` — worse than `Drives`, which at least exposes `DriveType` / `DriveName`. Bury `AppId`
in that blob and:

- *delete app ⇒ delete its circles* becomes load-all-deserialize-filter-delete, instead of one
  `DELETE … WHERE identityId = ? AND AppId = ?`;
- listing an app's circles deserializes **every** circle on the identity;
- the ownership check on *every* app write to a circle costs a blob parse;
- and no constraint or FK can ever reference it.

Circles number in the tens per identity, so this is not about speed. It is about being able to
express ownership at all.

**The rules that come with it:**

- An app may create / modify / delete **only** circles whose `AppId` is its own.
- `AppId IS NULL` marks an **owner circle**. Apps must never touch those — that is the boundary
  keeping a chat app out of system circles.

  > **NOTE:** right now we don't see a use case for `NULL`-`AppId` circles. User-created circles
  > (Friends, Family) are minted **under the profile app** — their grants are profile-attribute
  > reads, and that is where users manage them; other apps reference the membership list by circle
  > id (e.g. moments distribution) without owning it. The rule above stays as written in case a
  > use appears.
- A circle definition written by an app may reference **only drives the app can already read** —
  the same constraint that governs granting. Note *why*: not because the app could otherwise
  decrypt those drives itself. It cannot — reaching the banking drive's storage key needs the
  master key, or that drive's owning app's App Key (via *its* App Client Key), and our app has
  neither. Any grant it minted would come out **keyless**: a member "in the circle" who can decrypt
  nothing.

  The real hazard is **confused deputy**. An app can *plant* a definition naming the banking drive,
  and the next time the **owner** grants that circle — master key present — the grant machinery
  sources the storage key from the master key and mints a fully *working* banking grant on the
  app's behalf. So validate at definition-write time, not only at grant time.

## Connection defaults — enrollment

Two hardcoded system circles (`CircleConstants.cs`) currently decide what every new connection can
do. *Auto Connections* and *Confirmed Connections* carry an **identical** drive-grant bundle —
Write|React on the chat/lists/moments/mail/feed drives; confirmed adds only ShardRecovery write,
`AllowIntroductions`, and feed-distribution eligibility. Two problems: the bundle is a platform
constant that is really the **chat suite's** default (connecting with a receipts vendor should not
invite them to chat), and "confirm" is a special-cased swap between the two circles
(`ConfirmConnectionAsync`: revoke auto, grant confirmed) guarded by a lockout
(`CannotGrantAutoConnectedMoreCircles`, 3010).

App-owned circles can absorb both. `Circle` gets one more column:

```sql
Enrollment  SMALLINT NOT NULL DEFAULT 0,   -- 0 NONE | 1 AUTO_CONNECT | 2 VERIFIED_CONNECT
-- index (identityId, Enrollment)
```

and `AppRegistrations` one flag:

```sql
AutoConnectDefaults  BOOLEAN NOT NULL DEFAULT FALSE   -- enroll this app's AUTO_CONNECT circles on auto-connections
```

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
**definition-write time**, next to the confused-deputy validation above, and re-checked whenever
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
drives its app can already read (the definition-write rule above), and those are precisely the
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

### Installing an app (follow-up-PR behavior)

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
successor of that field pair. In the follow-up PR the request grows a `DefaultCircles` list
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

- **On verify** (the owner completes the connection review):
  1. The client sends the circles chosen in the dialog: the checked `VERIFIED_CONNECT` toggles
     plus the selected personal circles.
  2. The server adds the contact as a member of each.
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

### Implementation scope — what lands with this PR, and what does not

Implementing
the app-circles work must **not** drag in the full connection-model change (chat-kmp PR #1062).
The boundary is clean because every new column defaults to today's behavior:

*In this PR:*

- The **columns only**: `Enrollment` on `Circle` (default `NONE`), `AutoConnectDefaults` on
  `AppRegistrations` (default `FALSE`), plus the client proposal's `Designation` and `Emoji`
  fields. Regenerated CRUD + migration, both databases.
- The **deposit-only validation** on definition writes — it lands with the column so the invariant
  is never violable, but it is unreachable until something sets `Enrollment = AUTO_CONNECT`.

*Explicitly NOT in this PR (the follow-up that implements PR #1062):*

- **No startup / provisioning changes.** `CircleConstants`, `EnsureSystemCirclesExistAsync`, and
  both system circles keep existing and being granted exactly as today.
- **No connection-establishment changes.** The `CircleNetworkUtils` origin→circle routing, the
  `ConfirmConnectionAsync` swap, and the 3010 lockout all stay.
- **No app registers a default circle yet**; `AutoConnectDefaults` stays `FALSE` everywhere; no
  owner-console toggle.
- **No `SecurityGroupType` / ACL changes** — `connected`/`autoconnected` evaluation untouched, no
  ACL sweep.
- **No connection-review UX changes** in any client.

With every default at "off", the system behaves identically until the follow-up PR registers
default circles, flips the enrollment pipeline on, and retires the system-circle machinery — at
which point the schema is already waiting for it.

*Related:* the client-side circles proposal (chat-kmp PR #1062, `CIRCLES_VISIBILITY_PROPOSAL.md`)
wants two more per-circle fields on this same registration record: a `Designation`
(`PERSONAL | AUDIENCE | VENDOR` — contact-book presentation and filtering; default circles carry
none, their rendering keys off `Enrollment`) and an optional user-chosen `Emoji`.

## What this depends on

Only four places read `Type` as a global vocabulary. Each should become a **capability check**:

- `FollowerService` — asks a *remote* identity for channel drives by Type → should ask for
  *subscribable* drives.
- `FollowerPerimeterService` — validates an *incoming* follow request against `ChannelDriveType`
  → should validate `AllowSubscriptions`.
- `FeedDriveDistributionRouter` — already checks `AllowSubscriptions && Type == ChannelDriveType`;
  the flag is doing the real work.
- `FeedNotificationMapper` — same.

Once those are capability checks, nothing cross-identity reads `Type`, and it is free to become
app-private.

## Cost & sequencing

- **Making `Type` app-scoped needs no DDL and no wire change at all.** `Type` stays a `Guid`,
  `DriveId` already resolves, and `TargetDrive` keeps its shape — so the `DriveType BYTEA` column,
  the peer wire, and every client SDK are untouched. This is a pure change of *meaning*. Do it
  first; it is nearly free.
- **The DDL** — two nullable columns on `Drives`, plus a new `AppRegistrations` table (see
  *Schema*). Regenerated CRUD + `AbstractMigrator`, exercised on both SQLite and Postgres. This is
  the only genuinely expensive piece.
- **Who mints the `DriveId` is a later step.** Today it is a well-known constant supplied by the
  caller. Eventually the system should mint a **random Guid**, so apps address purely by
  `(AppSlug, DriveSlug)` and never see it. We are not ready for that yet — and nothing above
  depends on it.
- **What the slug actually replaces.** Not `Type` — the slug pair replaces `TargetDrive`, *both* of
  its components, as the **address**. `Type` survives, demoted from "half the address" to "the
  app-private category you filter on" (`?type=channel`). So "slug instead of Type" is a half-truth;
  it is *slug instead of `DriveId + Type` as an address, with `Type` kept as a category.*
- **Transition: `Type` stays in responses until `TargetDrive` retires.** Making `Type` app-private
  changes its *meaning*, not its *exposure*. Every existing client still builds
  `TargetDrive = (DriveId, Type)` to address a drive, so responses must keep returning `type` until
  slug addressing has replaced `TargetDrive` end to end. Only then can `type` be hidden from
  non-owning callers. Two independent steps, in this order — do not conflate them.
- Existing drives keep their `SystemDriveConstants` Type and `AppId = null`.

## Open questions

1. **Is `(AppSlug, DriveSlug)` the successor to `TargetDrive` on the wire, or a parallel name?**
   If remote callers address drives by slug, the slug pair *is* the wire address and `TargetDrive`
   should retire. Otherwise a drive carries three names (`DriveId`, `TargetDrive`, slug pair) with
   no stated precedence — the old "TargetDrive reconciliation" question, now unavoidable.
2. **Does `AppSlug` need a global registry?** Remote addressing only works if sender and recipient
   agree on what `chat` is, yet the slug is a flat, unowned, per-identity name — while `AppId` is
   the value that is actually globally unique. Do we reserve well-known slugs, bind the slug to the
   `AppId` at publish time, or accept that `/chat/…` is only as trustworthy as the recipient's
   registration?
3. **Are system drives (`AppId IS NULL`) slug-addressable at all?** Under the invariant in *Schema*
   they are not — `/profile` would need a reserved app slug, or system drives keep Guid addressing
   forever. Probably fine, but say so out loud.
4. **Immutability.** Once a slug is both a URL segment *and* a wire address, renaming breaks links
   and breaks remote senders. Presumably immutable after creation.
5. ~~Install-time consent for `AutoConnectDefaults`.~~ **Resolved** by the declare/dispose split:
   the app's flag is only a declaration; the owner holds a per-app toggle in the owner console,
   seeded by the install-time registration consent. Remaining detail: does enabling an app later
   offer enrollment of *existing* connections (prompt once, default off), or future ones only?
6. **Do `VERIFIED_CONNECT` circles carry permission keys** (`AllowIntroductions`,
   `ReadWhoIFollow`)? Keys are identity-wide, not drive-scoped — either they are allowed on
   verified circles only (never auto — the deposit-only invariant forbids it), or they leave
   circles entirely and become per-connection settings toggled at review.
