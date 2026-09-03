# Drive addressing: DriveId, Type, Slug, AppId (part 1/4)

This is a continuation of the document docs/app-circle-mmembership-plan.md

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

### The drive public key — write-only deposits

Every drive can carry a **write-only keypair**: an ECC-384 public key whose only power is
*depositing* — encrypt to it and you can write; you can never read. The pattern and the type are
already shipped: `PeerKeyStore.WriteOnlyKeyPair` is an **`EccFullKeyData`** — one self-contained
object holding the public half in clear and the private half AES-encrypted under a symmetric key
(see `app-circle-membership-plan.md`, *Forward-looking: two keypairs, one pattern*). For a drive,
the escrow key is the drive's own **storage key**, so deposit-collection custody equals existing
read access — for free.

A remote writer retrieves it by slug:

```
GET /api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/public-key
```

This endpoint is where slugs earn their keep: the whole point of the Drive PK is a writer with
**no prior relationship** — no shared Guid constants, possibly no connection at all. A stranger
deposit needs nothing but a hostname and two slugs. This key is what makes secretless 3 a.m.
receiving possible — consumed by part 3's `weak-key-retirement.md` (transfer envelopes and
pre-connection deposits).

**Who may ask:** serving the key discloses the drive's existence, and *Scoping* above promises
callers only see drives they can reach. ~~Resolution by the doc's own capability-flag pattern: a
drive opts in with a new **`AllowDeposits`** flag (sibling of `AllowAnonymousReads` /
`AllowSubscriptions`), and the public-key endpoint answers only for drives that set it —
existence is disclosed exactly where the owner opted into being writable.~~ The deposit mechanics
themselves (ECIES envelope, conversion on next read) are out of scope here; this doc carries only
the address and the key.

> **Built:** the gate is **Write on the drive**, not a per-drive flag — a caller without it gets a
> security exception, not a 404, since resolving the address already proved the drive is there. A
> reader has no use for the key, so nothing is disclosed to anyone who could not already write.
> This narrows the paragraph above: a writer with **no** grant at all cannot obtain the key, so the
> unconnected-stranger deposit is not reachable yet and waits on part 3.

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

**The type pair rides on `Drives` — denormalized on purpose.** `DriveTypeGuid` and
`DriveTypeSlug` are one-to-one *within an app*, and many drives share them, so a `DriveTypeSlug`
column on `Drives` repeats the pair per drive. Acceptable here: a single server code path writes
drives, slugs are immutable after creation (*Open questions*), and drives number in the tens — a
registry table would buy integrity nobody is positioned to violate, at the price of a join on
every enumeration, while the denormalized column makes `?type=channel` a direct filter. The two
invariants become **drive-creation-time validation**, in the same spirit as the other
definition-write checks:

- *Pair consistency* — a new drive carrying an already-used `DriveTypeGuid` (same identity) must
  carry the same `DriveTypeSlug`, and vice versa.
- *No cross-app reuse* — a `DriveTypeGuid` already used by another app on this identity is
  rejected. Random Guids never collide; **copy-paste does** (someone forks the feed app and keeps
  its constants). No `UNIQUE` on `Drives` can express this — many drives legitimately share the
  type — but a lookup over tens of rows at creation time enforces it exactly.

The validation is what makes this equivalent to a registry table. The realistic mistake — the
developer renames the slug in code ("channel" → "funnel") without shipping rename logic — fails
**loudly in both designs, at the same moment**: here the pair-consistency check rejects the first
creation under the new name; with a table, the `UNIQUE` on the type row rejects the same call.
Neither design can write the rename migration for the developer. The only residual difference is
rename mechanics — one row vs. an `UPDATE … WHERE DriveTypeGuid` over tens of rows in one
transaction — which does not justify a table.

## Schema

Four nullable fields on `Drives`, a new `AppRegistrations` table — plus the constraints that
make slug addressing safe.

```sql
-- Drives (existing table; four new nullable fields)
AppId            BYTEA,   -- owning app; NULL = not app-owned
DriveSlug        TEXT,    -- URL/wire segment; NULL when AppId is NULL
DriveTypeSlug    TEXT,    -- readable form of DriveType, e.g. "channel"; NULL when AppId is NULL
WriteOnlyKeyPair BYTEA,   -- serialized EccFullKeyData: Drive PK, private half escrowed under the
                          -- drive's storage key. NULL only on a drive that predates the field and
                          -- escaped v14 -> v15; every drive is minted one at creation

, UNIQUE(identityId, AppId, DriveSlug)   -- one "news" per app
-- index (identityId, DriveType)         -- legacy by-type lookups

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
- **No `DriveTypes` table.** The `DriveTypeGuid` ↔ `DriveTypeSlug` pair rides on `Drives`,
  denormalized; pair consistency and *a type Guid belongs to at most one app* are enforced at
  drive-creation time (*The type pair rides on `Drives`*) — a lookup over tens of rows, in the
  same spirit as the other definition-write validations.
- **`UNIQUE(identityId, DriveId, DriveType)` can be dropped** once resolution is by `DriveId` — it
  is already redundant against `UNIQUE(identityId, DriveId)`.

One trap:

- **NULLs are distinct in a unique index** (SQLite *and* Postgres). So
  `UNIQUE(identityId, AppId, DriveSlug)` constrains **nothing** for system drives, where
  `AppId IS NULL` — two of them could both claim `profile`. Enforce the invariant **`AppId` and
  `DriveSlug` are set together, or both `NULL`** — then every slug-bearing row is covered, and
  system drives are simply not slug-addressable.

Mechanics: nullable columns (`Guid? AppId`, `string DriveSlug`, `string DriveTypeSlug`), `TEXT` with a C#-side length check
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

## Connection defaults (separate document)

The connection-default model — per-app grant-on-connect / grant-on-review circles, the
deposit-only invariant, app installation, and the retirement of the system circles — is specified
in **`connection-defaults.md`**, next to this file. It is a **later phase**: nothing from it is
implemented with the addressing work. What rides along here is **schema only** — all dormant,
every default preserving today's behavior:

```sql
-- Circle (three more columns)
GrantOn      SMALLINT NOT NULL DEFAULT 0,   -- 0 None | 1 Connect | 2 OwnFlowConnect | 3 Review
Designation  SMALLINT NOT NULL DEFAULT 1,   -- 1 PERSONAL | 2 AUDIENCE | 3 VENDOR
Emoji        TEXT,                          -- optional user-chosen emoji; store the full string (may be a multi-codepoint ZWJ sequence)
-- index (identityId, GrantOn)

-- Connections (existing table; one new nullable column)
ReviewedAt  BIGINT,   -- UnixTimeUtc of the owner's review; NULL = New. Set at the review
                      -- (part 2, "on verify" step 3); drives the Reviewed caller tier
-- index (identityId, status, ReviewedAt)
```

`WriteOnlyKeyPair` is **one field, not split columns** — `EccFullKeyData` self-contains the
public half and the storage-key-encrypted private half, copying the shipped
`PeerKeyStore.WriteOnlyKeyPair` pattern (name kept for symmetry). It is not passive DDL like the
other columns: populating it means *minting* a keypair with the storage key in scope — ~~lazily on
first request~~, or backfilled in the VersionUpgrade pre-pass exactly as the `PeerKeyStore` keypair
was. ~~`AllowDeposits` is a capability flag stored with the drive's existing flags, not a column.~~

> **Built:** minted at drive creation for every drive, and backfilled for the rest by v14 → v15.
> Lazy minting is struck because it cannot work: minting needs the drive's storage key, and the
> caller who triggers the first request — a remote writer fetching the public half — never holds it.
> `AllowDeposits` is struck with it. It existed to say which drives get a keypair; with every drive
> getting one there is nothing left for it to gate.
On rotation, `WriteOnlyKeyPair` always holds only the **current** key; the **previous** key and a
rotation timestamp ride the drive's existing `jsonDetails` column (cold path — consulted only
when an old-key envelope arrives during the grace window). Still no schema change.

`GrantOn` replaces an earlier design that split this policy across two tables (a per-circle
enum *plus* an app-level "participate in ambient connects" flag). One column now says it all:
`Connect` grants at **any** connection establishment, ambient introductions included;
`OwnFlowConnect` grants **only** when the connection is created through the owning app's own
consent flow (a vendor circle enrolled when the hotel connects via the receipts flow, never when
a friend is introduced). An app can hold one of each — which the deleted boolean could not
express. Semantics in `connection-defaults.md`.

`Designation` and `Emoji` semantics are client-side (chat-kmp PR #1062: contact-book presentation
and filtering); `GrantOn` semantics are in `connection-defaults.md`.
The deposit-only validation ships with the `GrantOn` column (unreachable until something sets
either `Connect` value). The owner's per-app toggle needs no schema — it persists in the
existing per-tenant settings store (like the `ConnectedIdentitiesCanView*` flags today).

`ReviewedAt` must be a **column, not a blob field**, for the same reason `GrantOn` is: it is
queried on filtering paths — the contact book pages "reviewed connections"
(`ReviewedAt IS NOT NULL`) and "New people" (unreviewed ∩ people-app auto-circle membership, via
`CircleMember`) over a table that may hold a feed's million unreviewed audience connections.
Point lookups alone (caller-tier assignment) would not have needed it; pagination does. The
vetted backfill becomes a plain UPDATE.

**One at-rest copy — promote, don't duplicate.** The ICR is serialized into this table's data
blob, so the column must be the *only* at-rest home: `CircleNetworkStorage` maps it into the
in-memory ICR object on read and back to the column on write — exactly the existing
`WeakClientAccessToken` / `WeakKeyStoreKey` pattern on this same table — and the field is
**excluded from the blob serialization** (ignore-on-persist), or a naive `Serialize(icr)` into
`data` silently mints a second copy that drifts. A drifted `ReviewedAt` is not cosmetic: the
pagination query (column) and the caller's security tier (hydrated object) would disagree.

**Together with the addressing fields above (including `WriteOnlyKeyPair` and
`Connections.ReviewedAt`), this is the complete schema surface for both phases** —
`connection-defaults.md` and the client proposal introduce no further schema.

## What this depends on

Only four places read `Type` as a global vocabulary. Each should become a **capability check**:

- `FollowerService` — asks a *remote* identity for channel drives by Type → should ask for the Feed app's 
  *subscribable* drives (and / or by feed drive type channel).
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
5. **Drive PK — owner recovery.** Should the keypair's private half also carry a master-key escrow
   copy, or is the storage-key escrow (recoverable via the drive's master-key root) sufficient?
6. **Drive PK — rotation.** What happens to deposits encrypted to an old public key after
   rotation — drain-then-rotate, or accept both for a window?
7. **Drive PK — deleted drives.** In-flight deposits addressed to a deleted drive's key: reject,
   or tombstone-and-bounce?
