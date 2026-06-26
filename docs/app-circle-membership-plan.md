# Plan: Allow apps to add OdinIds to a circle

## The app key we already have

We do **not** need to invent a new app key to let an app act without the master key —
**the app key already exists.** It is the shared, per-app hub key on the app's
exchange grant (today buried under the generic name `keyStoreKey`), and every
logged-in client (device) already reaches it with no master key. The trouble is purely
the names. So we lead with the naming and propose a rename.

| Concept | Today (obscure) | Proposed |
|---|---|---|
| Per-device key, one per login | `accessKeyStoreKey` / "access key" (`AccessRegistration`) | **App Client Key** *(a.k.a. App Device Key)* |
| The shared per-app hub key | grant `keyStoreKey` (`ExchangeGrant`) | **App Key** |
| The per-connection hub key — a connected peer's grants | `keyStoreKey` on `AccessExchangeGrant` (the ICR) | **Peer Key** |
| App Key wrapped per device | `AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey` | **AppClientKeyEncryptedAppKey** |
| App Key wrapped for the owner | `MasterKeyEncryptedKeyStoreKey` (app grant) | **MasterKeyEncryptedAppKey** |
| Peer Key wrapped for the owner | `MasterKeyEncryptedKeyStoreKey` (`AccessExchangeGrant`) | **MasterKeyEncryptedPeerKey** |
| A drive's storage key wrapped under its grant's hub key | `KeyStoreKeyEncryptedStorageKey` | **AppKeyEncryptedStorageKey** / **PeerKeyEncryptedStorageKey** |

**Why the old name misleads:** the code calls *every* grant's hub key the same thing —
`keyStoreKey` — whether it belongs to an app or to a peer connection. That one reused
name is exactly what hid them as distinct principals. The rename gives each its own: an
app grant's hub key is the **App Key**; a peer connection's hub key is the **Peer
Key**. Blocker #3 then states simply: *the app holds its App Key but not the target's
Peer Key.*

**There is no "key store" object.** A hub key does not wrap one bundle — it wraps each
key beneath it **individually** (each drive's storage key, the ICR/transit key). "The
app's keys" is a description, not a key; nothing to build.

### How the App Key fits together

```
 owner  ── master key ──────────────►  APP KEY        MasterKeyEncryptedAppKey
 device ── App Client Key ──────────►  APP KEY        AppClientKeyEncryptedAppKey
                                         │            (same key; one wrapping per device)
                                         ▼  unlocks (each wrapped individually)
                              drive storage keys   +   ICR / transit key

 per device:  device token-half  ⊕  server-stored half  →  App Client Key  →  unwraps App Key
```

- **App Client Key** — per device, reconstructed each request from the device's
  token-half ⊕ a server-stored half. No master key.
- **App Key** — one per app. Every device's App Client Key unwraps the *same* App Key,
  so *N* devices are just *N* wrappings of one key and all clients share identical
  access. The owner reaches that same App Key via the master key.

### How the Peer Key fits together

Each connection (ICR) to another identity has its own hub key — the **Peer Key** —
which unlocks the grants that peer is given (circle grants → the storage keys for the
drives that peer may read). Today **only the owner can reach it**:

```
 owner ── master key ──────────────►  PEER KEY        MasterKeyEncryptedPeerKey
 app   ──        (no path today) ───►  PEER KEY        ◄── Blocker #3
                                         │  unlocks
                                         ▼
                             this peer's circle / drive grants
```

**Blocker #3, in one line:** an app reaches its **App Key** but has no path to a peer's
**Peer Key**, so it cannot write into that peer's grants. "Solving #3" (below) is
exactly *give the app a master-key-free path to the Peer Key.*

**Takeaway:** the app already has a durable, all-clients-shared, master-key-free key
(the App Key). Anything we want an app to do without the master key should anchor on
the App Key — not a newly invented one.

### Proposed rename — do this first

**Before any of the feature work below, do the mechanical rename in the code** —
adopt the **Proposed** column across the codebase (pure rename, no behavior change).
Two reasons it comes first: the current names actively mislead (they hid the fact
that the App Key already exists), and every design below is far easier to state — and
to review — in terms of *which hub key a principal can reach*. The rest of this
document already uses the new vocabulary (**App Key**, **App Client Key**, **Peer
Key**); the table above is the only place the old names appear.

## Problem statement

Currently you must have the master key in order to add an OdinId to a circle. We
need to change this so that an app can add OdinIds into a circle — **within the scope of drives that the App already has access to.**

The guiding principle: an app can only hand out what it can already reach. The
hub-key / storage-key layering (the **App Key** for apps, the **Peer Key** for
connections — see above) is, in effect, just indirection so that the master key can
always get to everything; an app sits below that, holding only the keys for the
drives it was granted. So the feature is less "give apps a new power" and more "let
the grant code build a grant from the keys the app already has, instead of assuming
the master key is the only source."

## What we want to achieve (worked examples)

These four examples define the intended behavior. The first three are decided
purely by what keys the calling app already holds; the fourth is the connection
flow that motivates the whole feature.

1. **Banking drive — must remain impossible.** A chat app must never be able to
   grant a *working* right to the user's private banking drive. It has no grant on
   that drive, so it holds no banking storage key — and shouldn't. Any grant it
   tried to mint would be empty/non-working. **This is enforced by cryptography,
   not just policy:** no storage key in hand ⇒ no usable grant can be produced. The
   policy boundary and the cryptographic boundary are the same line.

2. **GPS drive — what we want to newly enable.** A chat/location app has **temporal read** access to
   the user's GPS drive (e.g. a location/temporal API). It needs to add a contact
   to a circle that grants read access to that GPS drive. Because the app already
   holds the GPS drive's storage key (it can read the drive), it has everything
   needed to mint that drive's portion of the grant for the new member ⇒ the member
   gets a **working** grant. **This is the case we cannot do today** — not because
   the secret is missing, but because the grant builder only knows how to source
   the storage key via the master key (see Blocker #4).

3. **Write-only drive — contribute without reading.** Write-without-read is
   **already a real cryptographic property today**, not merely an ACL flag: I can
   write to Todd's chat drive yet I am *cryptographically* unable to read it,
   because the writer never holds that drive's storage key. An app with
   **write-only** access is in the same position — it does *not* hold the drive's
   storage key (and doesn't need it to write). Today the writer deposits data
   encrypted with the per-client **shared secret**, which the server re-keys with
   the storage key on the way in. The catch: **the shared secret only exists
   between parties that are already connected** — so this path covers established
   connections, not an arbitrary writer who has no shared secret for that drive. A
   proposed addition is a **per-drive public key**: a writer could encrypt to the
   drive's public key and leave that copy in the drive's inbox, extending the same
   write-without-read guarantee to cases where no shared secret exists yet — while
   the writer remains cryptographically unable to read the drive. (Note: no
   per-drive keypair exists today — see Blocker #4 / open questions.)

4. **App-driven connection — the motivating case.** Today a *safe* connection can
   only be accepted from the **owner console**, i.e. with the master key online. We
   want a user of the chat client to send a connection request that is received and
   **accepted on the other person's chat client** — no console, no master key in the
   loop — and the resulting connection should be **strong enough to work within that
   app's scope** (chat, Moments, location, shared lists), not a weak placeholder that
   only starts working once the owner later comes online.

   *Prior art — the unsafe stop-gap we already built.* To make app-side accept
   possible at all, the no-master-key path improvises
   (`CircleNetworkRequestService.cs`): it ECC-encrypts the new **Peer Key**
   and CAT to the **identity-level** online/offline key (`TempWeakKeyStoreKey` /
   `TemporaryWeakClientAccessToken`), leaves the connection's
   `MasterKeyEncryptedPeerKey` **null** (`RequiresMasterKeyEncryptionUpgrade()`
   ⇒ true), pins the connection to **`AutoConnectionsCircle` only** until "confirmed"
   from the console, and later runs a deferred **upgrade** to re-encrypt the
   Peer Key under the master key when the owner appears. It leans on an
   `overrideHack` parameter bypassing the `ReadConnections` check
   (`//HACK: DOING THIS WHILE DESIGNING x-token - REMOVE THIS`) and flags the open
   question in-line: `//TODO: should we validate all drives are write-only ?`.

   *Why it's the same problem.* That stop-gap already does the work of **Blocker #3**
   — it escrows the Peer Key under an alternative (identity-level) key — but
   coarsely and only as a temporary state finalized by the master key. It does **not**
   solve **Blocker #4**: real **read** access to specific drives still needs each
   drive's storage key, which is exactly why the connection stays weak /
   AutoConnections-only until upgraded. A **per-drive keypair** is what would make the
   accepted connection genuinely strong within the app's scope without the master key
   — granting real read access to exactly that app's drives at accept time, retiring
   both the AutoConnections jail and the deferred-upgrade dance.

## The four blockers

Two are policy (who is allowed) and two are cryptographic (can the app physically
build the grant). The two cryptographic blockers are **not** equivalent — one
dissolves under read-scoping, the other is the real cost. See the callout after #4.

1. **The auth gate refuses the app outright.** `GrantCircleAsync` (and
   `RevokeCircleAccessAsync`) call `AssertHasMasterKey()` as their first line
   (`CircleNetworkService.cs`). An app context is built with `masterKey: null`
   (`OdinContextMiddleware.cs`), so the call throws before anything else runs. This
   is the visible blocker. *(Policy.)*

2. **There is no permission an app could present instead.** App operations are
   authorized by named permission keys the app was granted (e.g. `ReadConnections`,
   `ReadCircleMembership`), checked via `HasPermission(...)`. The add-to-circle path
   has no such check — its only authorization is the master key. The circle
   permission set only contains `ReadCircleMembership` (read-only); there is no
   "manage circle membership" key, and nothing in the app-allowed set maps to this
   operation. So even with the gate in #1 removed, the system has no token of
   authority to distinguish an allowed app from a disallowed one. *(Policy — needs a
   new permission key, e.g. `ManageCircleMembership`.)*

3. **The app cannot reach the target's Peer Key.** Adding a circle grant to an
   existing connection means writing a grant sealed under *that connection's*
   **Peer Key**, stored only as
   `AccessExchangeGrant.MasterKeyEncryptedPeerKey` — reachable via the master
   key alone. This is the **connection's** secret, not a drive secret — read-scoping
   the app does not help, because the app's drive access says nothing about another
   connection's Peer Key. There is no app-reachable copy today. *(Cryptographic
   — this is the real cost; see callout.)*

4. **The grant builder sources the drive storage key only via the master key.** A
   circle grant that includes drive reads must re-encrypt each drive's storage key
   under the new connection's **Peer Key**. The grant builder currently obtains
   the plaintext storage key one way only:
   `drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey)`
   (`ExchangeGrantService.cs`). With a null master key it silently produces a grant
   with a **null** storage key — a member who is "in the circle" but cannot read any
   of its drives.

   **But this is not a missing-secret problem.** For any drive the app already has
   **read** access to, the app *holds a usable plaintext storage key at request
   time* — reconstructed without the master key via
   `App Client Key → App Key → AppKeyEncryptedStorageKey → storage key`
   (`PermissionGroup.GetDriveStorageKey`). The blocker is that
   `CreateExchangeGrant` was written assuming the master key is the only source. A
   code path that reuses the storage key already in the app's permission context
   dissolves this for read-scoped grants — **no data upgrade needed.** *(Cryptographic
   in appearance, but really a code-path assumption.)*

> **#3 and #4 are not the same kind of blocker — this is the key correction.**
> Blocker #4 (drive storage keys) dissolves the moment we scope "an app may only
> grant drives it can already read": the app already holds those storage keys, so it
> needs no new escrowed copies and no migration. Blocker #3 (the **Peer Key**)
> does **not** dissolve — it is a connection-level escrow node the app genuinely
> cannot see. **#3 is the actual cryptographic cost of this feature.**

## The silent failure (fix regardless of this feature)

The line
`var storageKey = masterKey == null ? null : drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey);`
silently yields a grant with a null storage key when no master key is present — a
member who is in the circle but can read nothing. Silent is bad. Independent of
this feature, this path should **throw / refuse** rather than mint a broken grant.

## What it takes (revised)

Scoping the feature to "an app grants only drives it can already read" collapses
the work to:

- **#1 (policy):** stop gating on the master key for the app path; gate on the new
  permission instead.
- **#2 (policy):** add a `ManageCircleMembership` (or similar) permission key and put
  it in the app-allowed set.
- **#3 (crypto — the real cost):** give the app a way to encrypt into the target's
  grant. We introduce an app-reachable copy of the **Peer Key** for connections
  the app is allowed to manage — via a **per-app management key** (detailed in the
  next section). This is the one place that needs a data upgrade for existing
  connections.
- **#4 (crypto — free under scoping):** add a code path in `CreateExchangeGrant`
  that uses the storage key already in the app's permission context for drives the
  app can read, instead of decrypting `MasterKeyEncryptedStorageKey`. No upgrade.

Net: the broad "escrow every drive's storage key under a host key" framing is
**not** required if we accept read-scoping. The remaining genuine cryptographic
work is #3 (the **Peer Key**), not #4.

## Solving #3: the per-app management key

Blocker #3 is the only piece read-scoping does not dissolve. Adding a member to a
circle writes into *that connection's* grant, sealed under the **Peer Key**
(`AccessExchangeGrant.MasterKeyEncryptedPeerKey`) — reachable only via the master key
today. We need to give an authorized app a master-key-free path to each managed
**Peer Key**.

> **Isn't this just the App Key?** Almost — and that is the right instinct. The app
> already reaches its **App Key** with no master key; the only thing missing is a hop
> from the App Key to each managed **Peer Key**. There are two ways to add that hop:
>
> - **Direct (simplest):** store the Peer Key wrapped straight under the App Key —
>   `AppKeyEncryptedPeerKey`, one per managed connection. **No new key.** (Alternative
>   C below.)
> - **Via a management key (described here):** introduce a separate per-app secret —
>   the *management key* — itself wrapped under the App Key, and hang the Peer-Key
>   spokes off *it* instead of off the App Key directly.
>
> Both reach the Peer Key with no master key. The management key buys exactly one
> thing: a **revocation / rotation seam** — you can revoke or rotate an app's
> *circle-management authority alone* by touching one key, without disturbing the App
> Key or the rest of the app. If that independent seam isn't worth the extra layer,
> collapse this whole section to the direct form (`AppKeyEncryptedPeerKey`). **This is
> the one open design choice in the plan.**

### What it is (management-key variant)

- A per-app random secret (16 bytes), **one per app** — the app's durable authority
  to manage circle membership on the owner's behalf **without the master key**.
- It is the **hub**: one management key fans out to one *spoke* on every connection
  that app manages (the spoke being that **Peer Key** wrapped under the
  management key).

### When it's created, and what unlocks it

- **Created** at app registration, or when the app is granted `ManageCircleMembership`
  — i.e. while the master key is present.
- **Stored** wrapped under the **App Key**, as `AppKeyEncryptedManagementKey` on the
  app registration. It has **no own master-key copy** — the owner reaches it
  transitively (master key → `MasterKeyEncryptedAppKey` → App Key → management key).
- **Unlocked at request time, no master key:** **App Client Key → App Key** → decrypt
  `AppKeyEncryptedManagementKey` → **management key**.

### How it shows up on the ICR, and why

New field on `AccessExchangeGrant`, alongside the existing
`MasterKeyEncryptedPeerKey`:

```csharp
Dictionary<Guid, SymmetricKeyEncryptedAes> AppManagementKeyEncryptedPeerKey
```

- **key** = managing AppId; **value** = this **Peer Key** wrapped under that
  app's management key.
- **Why on the ICR:** the Peer Key is unique per connection, so the
  app-reachable copy must live per connection — it is the spoke.
- **Why a dictionary keyed by AppId:** apps and connections are orthogonal — one app
  manages many connections, and one connection may be managed by more than one app,
  each with its own management key (its own wrapping). Usually one entry. Keying by
  AppId makes revocation clean.

### How a spoke gets minted

- **App-accepted connections:** minted **at accept, no master key** — the app holds
  its management key (chain above) and the just-generated **Peer Key**, so it
  wraps one under the other on the spot. No migration, no AutoConnections jail.
- **Pre-existing connections:** one-time **migration** while the owner is online
  (master key present) — for each connection the app may manage, wrap its Connection
  Key under the management key and add the entry.

### What this gives us (the payoff)

Reaching the management key yields the one missing ingredient — the target's
**Peer Key** in the clear, exactly what `CircleNetworkService.cs:458` produces
from the master key today. From there the real `GrantCircleAsync` body runs with **no
master key**:

- **Permission-only parts of the circle: fully granted** — the `PermissionSet` is
  stored in the clear in the grant; it just needs the Peer Key to be written.
- **Drive parts: granted for the drives the app can read** — each drive's storage key
  is sourced from the app's own permission context (#4) and wrapped under the
  Peer Key. The member gets real read access to *(circle's drives ∩ drives the
  app can read)*.
- **Drives the app can't read stay empty** — no storage key in hand ⇒ non-working
  portion. Banking drive stays impossible, by the same cryptographic line.

The same recovered Peer Key also unblocks the other two member-grant mutations
for the app path: `UpdateCircleDefinitionAsync` and `ReconcileAuthorizedCircles`.

**Full chain:** App Client Key → App Key → management key → Peer Key → write
circle grant.

### How it maps to the four goals

The management key gives the app exactly one thing — the target's **Peer Key**
(the envelope every grant is sealed under) — and *no* drive storage keys. What you may
put *in* the envelope stays bounded by the storage keys the app already holds (#4).

- **#1 Banking — neutral, by design.** The management key hands over the Connection
  Key (the envelope), not any drive's storage key. Granting banking *read* would still
  need the banking storage key the app doesn't hold, so that portion stays
  empty/non-working. The boundary remains enforced by #4 — the management key never
  widens drive reach.
- **#2 GPS — needs both halves.** #4 supplies the GPS storage key (the app reads the
  drive); the management key supplies the Peer Key. Together they produce a
  *working* read grant with no master key. The management key is the missing half that
  turns "app holds the storage key" into "member gets a working grant."
- **#3 Write-only — management key alone suffices.** A write grant carries no storage
  key (storage keys embed only for Read/ConditionalTemporalRead), so writing a
  write-only drive grant needs only the Peer Key — even for drives the app can't
  read. (The per-drive public key is about *depositing data*; the management key is
  about *granting* the write membership.)
- **#4 App-driven connection — durable manageability.** At accept the app generates
  the new **Peer Key** itself, so it can build initial grants on the spot (reads
  via #4, write bootstrap via the per-drive keypair) without the management key. The
  management key's role is to mint the spoke at accept so the Peer Key stays
  reachable *afterward* — letting the app add/modify circles in later sessions without
  the master key, retiring the AutoConnections-jail + deferred-upgrade dance.

Throughline: the management key solves the *envelope* (#3) for every case; what goes
*in* it is still bounded by #4.

### Revocation

- **One app:** remove its `AppManagementKeyEncryptedPeerKey` entries and its
  `AppKeyEncryptedManagementKey`.
- **Fully:** rotate the management key.

### Outstanding decision

When a circle includes a drive the app **cannot** read, do we **(a)** grant the
partial / permission-only result silently, or **(b)** reject the add as out of the
app's scope? This is a policy call, not a cryptographic one — **undecided**.

### Alternatives considered

The per-app management key (above) is the chosen approach. These were weighed and
set aside. (Excluded entirely from consideration: anything that requires the master
key *at the add operation*, or that defers the add until the owner is next online —
those defeat the purpose. Like the chosen approach, every option below still needs
the master key *once* for setup/migration of pre-existing connections.)

- **B — Identity-level online/ICR-key escrow.** Wrap the Peer Key under a
  single identity-wide online/ICR key; the spoke is one non-app-specific wrapping per
  connection, not keyed by AppId. *How it differs from the chosen per-app key:* one
  key for the whole identity vs one per app — so authority is coarse ("any app
  holding the identity key," conflating with transit-write apps that already hold the
  ICR key), you cannot revoke one app without rotating the shared key, and a single
  key compromise exposes every connection for every app. Its only upside is less new
  code — it reuses the key the no-master-key accept path already uses for
  `TempWeakKeyStoreKey`. *Rejected for coarse authority and blast radius.*

- **C — Direct App-Key wrapping (no management key).** Wrap the Peer Key
  straight under each app's **App Key**, skipping the management-key layer. Simpler by
  one indirection, but loses the rotation seam and forces re-wraps whenever the App
  Key rotates.

- **D — Per-app ECC keypair (encrypt-to-public-key).** Owner/accept flow encrypts
  the spoke to the app's *public* key; lets the spoke be minted even when the app is
  offline, app decrypts with its private key. More crypto surface than a symmetric
  management key.

- **E — Derive instead of store (KDF).** Peer Key =
  KDF(management key, connectionId), so no per-ICR spoke is stored. Saves storage but
  couples every connection to one key by construction, and still needs a re-mint for
  existing connections.

- **F — Host/server-held escrow key.** The server holds a key that recovers the
  Peer Key without the owner present; real-time and no per-app plumbing, but
  shifts trust to the host.

- **I — Proxy re-encryption.** Owner issues a re-encryption key (once) so the server
  transforms the master-key-encrypted Peer Key into app-readable form without
  ever revealing it; elegant, but heavy and novel crypto.

## App-owned drives (committed direction, timing TBD)

> **Status: committed direction.** We are certain we need app-owned drives; the
> open question is *when* to build it, not *whether*. The mechanics below are the
> design sketch, not a fixed spec.

Drives will be owned by the app that creates them: delete the app and you delete
its drives (cross-app drive grants stay possible), and to a lesser extent apps will
own app circles they can create and delete. Cryptographically this means giving a
drive an app-scoped root of trust. An app already holds a stable **App Key** at
request time, reconstructed from its App Client Key with no master key
(`AccessRegistration.DecryptUsingClientAuthenticationToken`). "App owns a drive"
means re-rooting that drive's storage key onto the owning app's **App Key**.

Concretely it would require:

- An `OwningAppId` association on the drive (none exists on `StorageDrive` today).
- A second storage-key copy, e.g. `AppKeyEncryptedStorageKey`, alongside the
  existing `MasterKeyEncryptedStorageKey`.

Payoff: the owning app can read/write the drive and **grant it to circles and
identities without the master key** — scoped to that app rather than escrowing all
drives broadly. (Note this mainly addresses #4-style drive access, which
read-scoping already handles; it does not by itself solve #3, the **Connection
Key**.) The policy blockers (#1, #2) still apply, but the check becomes "is the
caller the owning app?"

### Implementation: storage changes

Two dedicated, code-first CRUD tables (same pattern as `TableDrivesCRUD`):

**1. New `AppRegistrations` table.** Move app registrations off the shared
`KeyThreeValue` / `ThreeKeyValueStorage` blob (`AppRegistrationService.cs:39,93`)
into their own table. Columns: `identityId`, `AppId` (PK), `Name`, `CorsHostName`,
JSON columns for `AuthorizedCircles`, `CircleMemberPermissionGrant`, and `Grant`
(now carrying `AppKeyEncryptedManagementKey`), `created`/`modified`.
*Migration:* shadow-table copy (cf. `TableDrivesMigrationV202510311515`) — create the
table, copy each `KeyThreeValue` row where `key3 = AppRegistrationDataType`,
deserialize `data` into columns, verify counts, retire old rows. One-time, no master
key.

**2. New drives table, scoped under an app.** A brand-new table — *not* an evolution
of the existing `Drives` table. A drive belongs to exactly one app; an app owns many
drives (**one-to-many** via `AppId` FK → `AppRegistrations.AppId`). It deliberately
**does not use `TargetDrive`** (no Alias/Type Guids); a drive is identified within
its app by **string `Type`** and **string `Label`**. Columns: `identityId`,
`DriveId` (PK), `AppId` (owning app), `DriveType` (string), `DriveLabel` (string),
`AppKeyEncryptedStorageKey`, optionally `MasterKeyEncryptedStorageKey`
(co-owned recovery — see Open questions), `created`/`modified`.

**Transition strategy.** The new table ships first and backs app-created drives going
forward. Existing drives in the legacy `Drives` table are moved over in a **follow-up
data migration** that maps each old `TargetDrive` (Alias+Type Guids) onto the new
`AppId` + string `Type`/`Label` model — its own deferred task, since deciding which
app owns a pre-existing/system drive (and its string type/label) is non-trivial.

Open questions:

- **TargetDrive reconciliation:** drives are addressed everywhere today by
  `TargetDrive` (Alias+Type Guids); how do the new string-addressed, app-scoped
  drives coexist with — or replace — `TargetDrive`-based APIs and stored references?

- **Owner access:** co-owned (keep the master-key copy so the owner can recover —
  preferred) vs app-exclusive (owner cannot read the drive; stronger isolation but
  risks unrecoverable data).
- **Migration:** making an existing drive app-owned still needs the master key once
  (owner online) to mint the app-key copy.
- **App removal/revocation:** policy for what happens to a drive when its owning app
  is deleted.
- **Cross-app isolation:** only the owning app's App Key may decrypt the drive.
- **System drives:** out of scope for app ownership (see taxonomy below).
- **Per-drive public key:** none exists today — drives are purely symmetric; only
  identity-level ECC keys exist (`PublicPrivateKeyService`). It's a separate
  mechanism to design; its relation to the four use cases is in the closing section.

## Forward-looking: drive keys and ownership

Three related forward-looking threads, condensed. None of this blocks the near-term
feature above.

**Drive ownership = lifecycle, not authorship.** Litmus: *delete the app or swap the
client — should the drive's data die or persist?* Three buckets:

- **System (true core):** only what basic identity functionality needs — **Profile**
  (integral to YouAuth attribute queries). Very little qualifies.
- **App-owned:** lifecycle bound to one app, deleted with it — **Chat**.
- **Virtual-app / appless:** a stable logical owner that survives client swaps,
  grantable cross-app — **Vault** (poster child); **Stickers / Moments** candidates.

"Built-in" ≠ "system": most of today's `SystemDriveConstants` (Feed, Channels, Mail,
Chat, Stickers, Wallet) are bolt-on apps hardcoded for convenience, not system drives.

**Per-drive public key — purpose.** A write-only root of trust for a drive (none
exists today; drives are purely symmetric). *The public key lets you write, never
read.* Two proposed uses: writing to a drive with **no connection** (#3), and
bootstrapping the **write side** of a safe connection request (#4). Reads (#2) need no
keypair; banking (#1) stays read-neutral by construction.

**Recommendation: per-drive, not per-app** — with the private key escrowed under that
drive's **storage key**, so deposit-collection custody = existing read access, for
free. Per-drive beats per-app: one uniform mechanism (covers system / unowned drives,
no fallback), cross-app grants stay un-coupled, blast radius isolated. It is also
**orthogonal to ownership** — the key rides the storage key regardless of bucket, so
the taxonomy above need not be settled to ship it. Per-app's only real draw is a
product-level single "write to this app" address.

Open questions (per-drive keypair): escrow beyond the storage key (a master-key copy
for owner recovery?); rotation and what happens to deposits under an old public key;
in-flight deposits to a deleted drive's key. App-owned *circles* are a separate
concern and do not need this keypair.
