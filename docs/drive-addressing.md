# Drive addressing: DriveId, Type, Slug, AppId

*Status: proposal, for discussion. Prompted by the app-owned-drives direction.*

Today a drive is addressed by Guids that both ends must hardcode. We want an app to name its own
drives, and a **remote** caller to address them, with short slugs — `/chat/messages`. Getting there
means untangling one overloaded field (`Type`). The API is what this is *for*, so it leads.

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
     /api/v2/drives/{appSlug}/{driveSlug}/files
/api/v2/peer/{odinId}/drives/{appSlug}/{driveSlug}/files
```

These **coexist without ambiguity** — the `:guid` route constraint means `/drives/chat/messages`
can never match `/drives/{driveId:guid}`. So slugs can be added without touching the Guid routes.

The remote case is the one that matters. Sending a message to another identity's chat drive today
requires both sides to share hardcoded Guid constants (`TargetDrive` = `DriveId + Type`, from
`SystemDriveConstants`). With slugs:

```
POST /api/v2/peer/frodo.dotyou.cloud/drives/chat/messages/files
                                            └app┘ └ drive ┘
```

Neither side shares a Guid. Authorization is unchanged — the sender still needs a drive grant via a
circle; the slug replaces only the *address*.

### Enumeration, and the feed example

Say the **feed app** (`AppSlug = feed`) invents a channel type Guid `ff42…` and owns *zero or more*
drives of that type — `news`, `photos`, … each with its own `DriveSlug`. One call finds them:

```
GET /api/v2/drives?app=feed&type=ff42…
→ [ { driveId, slug: "news",   type: "ff42…" },
    { driveId, slug: "photos", type: "ff42…" }, … ]
```

This is why `Type` must stay **out of any unique key** — the feed app has many drives sharing one
type, distinguished by slug.

**This retires `GET /api/v2/drives/metadata/channel-drives`.** That endpoint exists *only* because
`Type` is a global constant — it is hardcoded to `SystemDriveConstants.ChannelDriveType`. Once the
type belongs to the app, the generic filter above replaces it. (The lookup beneath it,
`driveManager.GetDrivesAsync(type, …)`, must likewise become `(appId, type)`; a bare `type` is
ambiguous once apps pick their own.)

### Scoping

A caller only ever sees drives **it can already reach** — its own, plus any cross-app grants. An app
asking for another app's drives gets the granted subset, never that app's full inventory; the owner
console sees all. Same constraint as the rest of the app-owned work: *an app gains nothing beyond
its own reach.*

Two consequences:

- `type` is **app-private**, so it means nothing to a non-owning caller — consider omitting it for
  them rather than returning a Guid they cannot interpret.
- `AppId` must be an **indexed column**, not a JSON field: this is a per-app enumeration on the
  startup path of every app.

### Slugs are resolved by the recipient

`/peer/{odinId}/drives/chat/messages` means *"whatever app **that identity** registered under
`chat`, and its `messages` drive."* That is a feature — it works across independent implementations
of a chat app, where a shared Guid constant would not — and it is a risk: a different app could
occupy the slug. A Guid is exact; a slug is a convention. Worth deciding how much we care.

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
| Categorize, app-privately | — | **`AppDriveType`** — a **Guid the app invents**; the feed app decides its channel drives are `ff42…`, nobody else need know |
| Address a drive (URL, and remotely) | — | **`DriveSlug`** — short (target ≤12 chars), `[a-z0-9-]`, unique **per app**, immutable |
| Address an app (URL, and remotely) | — | **`AppSlug`** — same rules, unique **per identity**; e.g. `chat`. `AppId` stays the internal Guid |
| Own / cascade-delete | — | **`AppId`** — nullable; `null` = not app-owned |

An app then names its drives `(AppDriveType, DriveSlug)` — e.g. `(ff42…, "news")` — and never
needs to know the `DriveId`.

**`Type` stays a Guid — the app just invents it.** Nothing about its representation changes; only
its *meaning* goes from "a value the whole system agrees on" to "a value the owning app chose."

**Integrity: should two apps be allowed the same `Type` Guid?** In principle yes — apps are
developed independently and drives are `AppId`-scoped, so no app can *use* another's drives. But
the realistic cause of a collision isn't chance (random Guids never collide), it's **copy-paste** —
someone forks the feed app and keeps its constants. That's worth rejecting. The rule would be:
*within an identity, a given `Type` Guid may belong to at most one `AppId`.* Note this is a
functional dependency, not a `UNIQUE` constraint — the feed app has many drives sharing that Type —
so enforce it at drive-create (reject if any drive on this identity has this `Type` under a
different `AppId`), which wants an index on `(identityId, DriveType)`. A registry table
`(identityId, Type) → AppId` is the heavier alternative, and would double as the lookup for the
legacy by-Type call paths.

## Schema

Two nullable columns on `Drives`, one on the app registration — plus the constraints that make slug
addressing safe.

```sql
-- Drives (existing table; two new nullable columns)
AppId      BYTEA,        -- owning app; NULL = not app-owned
DriveSlug  TEXT,         -- URL/wire segment; NULL when AppId is NULL

, UNIQUE(identityId, AppId, DriveSlug)   -- one "news" per app
-- index (identityId, DriveType)         -- one-app-per-Type check; legacy by-type lookups

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
Applies to **`AppSlug`** and **`DriveSlug`** alike:

- `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$` — lowercase letters, digits, internal hyphens only.
- **No** spaces, `/`, `.`, `%`, `?`, `#`, `&`, `:`, `@`, and no uppercase. Nothing that needs
  percent-encoding; nothing readable as a path separator or as `.` / `..`.
- 1–12 characters — capped in the C# validator, not in the column type.
- **Not a reserved route segment.** The slug lands inside
  `/api/v2/drives/{appSlug}/{driveSlug}/files`, so it must not collide with a literal sibling at
  that position. Today that is exactly one word — **`metadata`**
  (`/api/v2/drives/metadata/channel-drives`) — and the denylist must grow whenever a literal
  segment is added under `/drives`. (Retiring `channel-drives`, as proposed above, removes this
  one.)

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
  segments, so `/feed/news` and `/chat/news` may coexist. A drive slug only has to be unique
  *within its app*.
- **That unique index doubles as the enumeration index.** Its `(identityId, AppId)` prefix serves
  `GET /drives?app=feed`; the `type` filter then runs over that app's handful of drives.
- **`(identityId, DriveType)` index** backs the integrity rule (*a Type Guid belongs to at most one
  `AppId` within an identity*), which is a functional dependency and therefore cannot be a `UNIQUE`.
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
  keeping a chat app out of the family circle.
- A circle definition written by an app may reference **only drives the app can already read** —
  the same constraint that governs granting. Otherwise an app could mint a circle containing the
  banking drive and hand it out.

*Open:* should an app-owned circle also carry a slug, so an app finds "my contacts circle" without
storing a Guid? The symmetry with drives says yes; nothing in a URL needs it yet, so it can wait.

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
- Existing drives keep their `SystemDriveConstants` Type and `AppId = null`.

## Open questions

1. **Is `(AppSlug, DriveSlug)` the successor to `TargetDrive` on the wire, or a parallel name?**
   If remote callers address drives by slug, the slug pair *is* the wire address and `TargetDrive`
   should retire. Otherwise a drive carries three names (`DriveId`, `TargetDrive`, slug pair) with
   no stated precedence — the old "TargetDrive reconciliation" question, now unavoidable.
2. **How much do we care that a slug is a convention, not a guarantee?** A remote `/chat/messages`
   resolves against whatever the *recipient* registered as `chat`. Nothing stops a different app
   from occupying that slug. Do we need a reserved/well-known slug list, or is recipient-side
   resolution simply the correct semantics?
3. **Are system drives (`AppId IS NULL`) slug-addressable at all?** Under the invariant in *Schema*
   they are not — `/profile` would need a reserved app slug, or system drives keep Guid addressing
   forever. Probably fine, but say so out loud.
4. **Immutability.** Once a slug is both a URL segment *and* a wire address, renaming breaks links
   and breaks remote senders. Presumably immutable after creation.
