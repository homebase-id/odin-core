# Plan: Allow apps to add OdinIds to a circle

## Goal & status

Today you must hold the **master key** to add an OdinId to a circle. We want an **app**
(e.g. the chat client) to do it too — **but only within the scope of drives the app
already has access to**, gaining no access, read or write, beyond its own reach. The
motivating case is app-driven connections: a chat client sends a connection request,
another chat client accepts it, and the resulting connection works within that app's
scope — without the owner console or the master key online.

**Status.** The key model, the naming, and three of the four blockers are settled. The
hard part is **Blocker #3:** an app cannot write into a peer's grant store without also
gaining read access to the peer's entire scope. A **candidate solution** — a write-only
deposit via a Peer Key Store keypair — is now in hand but not yet built (see *The current
blocker*).

## Key model & naming

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
| The connected peer's per-CAT client key (≥1 per connection) | `AccessRegistration` on the ICR | **Peer Client Key** |
| The app's grant container (what the App Key unlocks) | `ExchangeGrant` (app grant) | **App Key Store** |
| A peer's grant container (what the Peer Key unlocks) | `AccessExchangeGrant` (the ICR) | **Peer Key Store** |
| App Key wrapped per device | `AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey` | **AppClientKeyEncryptedAppKey** |
| App Key wrapped for the owner | `MasterKeyEncryptedKeyStoreKey` (app grant) | **MasterKeyEncryptedAppKey** |
| Peer Key wrapped for the owner | `MasterKeyEncryptedKeyStoreKey` (`AccessExchangeGrant`) | **MasterKeyEncryptedPeerKey** |
| A drive's storage key wrapped under its grant's hub key | `KeyStoreKeyEncryptedStorageKey` | **AppKeyEncryptedStorageKey** / **PeerKeyEncryptedStorageKey** |
| The transit/ICR key under a hub key | `KeyStoreKeyEncryptedIcrKey` | **AppKeyEncryptedIcrKey** / **PeerKeyEncryptedIcrKey** |
| *(#3 candidate)* the Peer Key Store's public key, in clear | *(new)* | **`PeerKeyStorePublicKey`** *(prose: Peer Key Store PK)* |
| *(#3 candidate)* its private key, Peer-Key-encrypted | *(new)* | **`PeerKeyEncryptedStorePrivateKey`** |
| *(#3 candidate)* a grant deposited via the PK, awaiting conversion | `EccEncryptedPayload` (reuse) | **Deposited Grant** |

**Why the old name misleads:** the code calls *every* grant's hub key the same thing —
`keyStoreKey` — whether it belongs to an app or to a peer connection. That one reused
name is exactly what hid them as distinct principals. The rename gives each its own: an
app grant's hub key is the **App Key**; a peer connection's hub key is the **Peer
Key**. Blocker #3 then states simply: *the app holds its App Key but not the target's
Peer Key.*

**The App Key *is* the key store** (and likewise the Peer Key). It is the indirection
that lets each access be stored **once**: every drive/transit key the app can reach is
wrapped a single time under the App Key — *not* duplicated per device, and *not* wrapped
again under the master key — while the App Key itself carries the two top-level
wrappings (`MasterKeyEncryptedAppKey` + one `AppClientKeyEncryptedAppKey` per device).
So there is no *separate* store object to build. Beneath it the leaves stay wrapped
**individually**, so each principal reaches only its own subset. The one other copy of a
drive's storage key — `MasterKeyEncryptedStorageKey` on the drive — is the drive's own
canonical root (a different level), not an app duplicate.

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
drives that peer may read). It *already* has a master-key-free reconstruction path
today — but only for the **peer themselves**, via their CAT, using the exact same
split-key pattern as App Client Key → App Key (a peer may even hold several, one per
device):

```
 owner ── master key ────────────────►  PEER KEY       MasterKeyEncryptedPeerKey
 peer  ── Peer Client Key (its CAT) ──►  PEER KEY       (split-key, like App Client Key)
 app   ── (no path today) ────────────►  PEER KEY       ◄── Blocker #3
                                           │  unlocks
                                           ▼
                              this peer's circle / drive grants
```

**Blocker #3, in one line:** the owner's app has no path to the Peer Key — and must
**not** simply be handed one, because the symmetric Peer Key cannot be given write-only.
That is the feature's one unsolved blocker; full treatment in *The current blocker*.

**Takeaway:** the app already has a durable, all-clients-shared, master-key-free key
(the App Key). Anything we want an app to do without the master key should anchor on
the App Key — not a newly invented one.

### Do the rename first

**Before any of the feature work below, do the mechanical rename in the code** —
adopt the **Proposed** column across the codebase (pure rename, no behavior change).
Two reasons it comes first: the current names actively mislead (they hid the fact
that the App Key already exists), and every design below is far easier to state — and
to review — in terms of *which hub key a principal can reach*. The rest of this
document already uses the new vocabulary (**App Key**, **App Client Key**, **Peer
Key**); the table above is the only place the old names appear.

## What we want (worked examples)

These four examples define the intended behavior. The first three are decided
purely by what keys the calling app already holds; the fourth is the connection
flow that motivates the whole feature.

1. **Banking drive — must remain impossible.** A chat app must never be able to
   grant a *working* right to the user's private banking drive. It has no grant on
   that drive, so it holds no banking storage key — and shouldn't. Any grant it
   tried to mint would be empty/non-working. **This is enforced by cryptography,
   not just policy:** no storage key in hand ⇒ no usable grant can be produced. The
   policy boundary and the cryptographic boundary are the same line.

2. **GPS drive — what we want to newly enable.** A chat/location app has **temporal read**
   access to the user's GPS drive (e.g. a location/temporal API). It needs to add a
   contact to a circle that grants read access to that GPS drive. Because the app already
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
   **per-drive public key** would extend the same write-without-read guarantee to a
   writer with no shared secret yet (defined in *Forward-looking*; none exists today).

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
   AutoConnections-only until upgraded. What would make the accepted connection genuinely
   strong within the app's scope — without the master key — is that at accept the app
   holds the new **Peer Key** (it generates it), so it grants **read** access to exactly
   its drives directly (#4); the per-drive public key covers the **write** side of the
   request bootstrap. Together these retire the AutoConnections jail and the
   deferred-upgrade dance.

## The blockers and where each stands

Four things block an app today — two are policy (who is allowed), two are
cryptographic (can the app build the grant). Each, with its status:

**#1 — The auth gate refuses the app (policy → tractable).** `GrantCircleAsync` /
`RevokeCircleAccessAsync` call `AssertHasMasterKey()` as their first line
(`CircleNetworkService.cs`); an app context has `masterKey: null`
(`OdinContextMiddleware.cs`), so the call throws immediately. *Resolution:* stop gating
the app path on the master key; gate on the permission from #2.

**#2 — No permission an app could present (policy → tractable).** App operations
authorize via named permission keys (`HasPermission(...)`); the circle set only has
`ReadCircleMembership` (read-only), and nothing maps to "manage membership." *Resolution:*
add a `ManageCircleMembership` permission key and put it in the app-allowed set.

**#4 — The grant builder sources storage keys only via the master key (crypto → free
under read-scoping).** Building a circle grant re-encrypts each drive's storage key
under the new connection's **Peer Key**, and today the builder gets that storage key one
way only: `drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey)`
(`ExchangeGrantService.cs`). With a null master key it silently mints a grant with a
**null** storage key. *But this is a code-path assumption, not a missing secret:* for
any drive the app can already read, the app holds the storage key at request time
(`App Client Key → App Key → AppKeyEncryptedStorageKey`, via
`PermissionGroup.GetDriveStorageKey`). *Resolution:* add a code path in
`CreateExchangeGrant` that reuses that already-held storage key — no master key, no data
upgrade.

> *Incidental fix (do regardless of this feature):* that silent null-storage-key line
> should **throw / refuse** rather than mint a member who is "in the circle" but can read
> nothing.

**#3 — The app cannot reach the target's Peer Key (crypto → THE BLOCKER).** Adding a
member writes a grant sealed under that connection's **Peer Key**
(`AccessExchangeGrant.MasterKeyEncryptedPeerKey`), reachable today only via the master
key (and by the peer themselves). Read-scoping does not help — it is the *connection's*
secret, not a drive secret. **And it cannot simply be handed to the app: the Peer Key is
symmetric, so any copy that lets the app write also lets it read the peer's entire
scope.** This is the unsolved problem the feature is stuck on — full treatment next.

> **#3 ≠ #4 — the key distinction.** #4 dissolves the moment we scope "an app grants only
> drives it can already read": the app already holds those storage keys, so no escrow and
> no migration — the broad "escrow every storage key under a host key" framing is
> unnecessary. #3 does **not** dissolve: it is a connection-level secret the app cannot
> see, and the obvious fixes break the scope constraint. **#3 is the blocker.**

## ⛔ The current blocker: #3 — write-without-read into the peer store

> **This is the open problem the whole feature is stuck on.** The App Key story, the
> #1 / #2 / #4 mechanics, and read-scoping all work. **#3 does not.** Until it is solved,
> an app cannot add an OdinId to a circle. A **candidate solution** is below (write-only
> deposit); it is not yet built.

**The constraint we will not break:** an app may grant a peer access *only* to things
the app itself already has — and it must gain **no** access, read or write, to anything
beyond its own reach in the process. (#4 already enforces the *write* side for drive
keys: the app can only mint a working grant for a drive whose storage key it holds.)

**The blocker.** To add a peer to a circle, the app must **write into that peer's grant
store** — `E(Peer Key, SK_drive)`. That write requires the **Peer Key** in plaintext.
But:

- The app **has the drive key** (#4) — that part is fine.
- The app **does not have the Peer Key**, and **must not** have it. The peer's store is
  symmetric, so any key that lets the app *write* one grant also lets it *read* every
  other grant in it — every drive the owner or any other app ever granted that peer.
  That would hand the app access to anything *any* of your peers can access.

So even though the app holds the drive key, **it has no way to update the peer's store
without also gaining read access to the peer's entire scope.** That is the blocker: the
missing piece is not the drive key (we have it) — it is a *safe* path to write the peer
store.

**Reaching the Peer Key is the wrong goal.** Every way of *handing* the app the Peer Key
delivers the same symmetric key and the same read-all (see *Rejected approaches*). The
fix is not to reach the Peer Key at all — it is to **write without reading it**.

### Candidate solution: write-only deposit via the Peer Key Store PK

Give every **Peer Key Store** (a peer's grant container — the `AccessExchangeGrant`) an
**ECC-384 keypair**: the **Peer Key Store PK** (`PeerKeyStorePublicKey`, in clear) and its
private key **Peer-Key-encrypted** (`PeerKeyEncryptedStorePrivateKey`). That keypair is
the write-without-read primitive:

- **App deposits a grant** (to a circle, for drives it holds the storage key to — #4) by
  ECIES-encrypting it to the Peer Key Store PK with a fresh **ECC-384 temp key**; the temp
  public key is stored beside the ciphertext (needed to decrypt). The app needs no Peer
  Key and can read nothing — it only writes.
- **Access converts it.** Whenever the Peer Key Store is next accessed with the Peer Key in
  scope (peer login via CAT, or owner online), the private key is recovered (Peer Key → its
  encrypted copy) and each **Deposited Grant** is rewritten as a normal
  Peer-Key-encrypted grant.

Because the app gains **no read capability at all**, this needs **neither per-circle keys
nor App circles** to bound it — those drop to optional product features, not requirements
for #3. *Validation:* keep each grant's `{ circleId, driveId, permission }` in clear so the
server checks `driveId ∈ the depositing app's drives` at deposit time; only the storage key
is sealed to the PK.

**Status: candidate solution — not yet built.**

### Policy question (downstream of the above)

Once a write-without-read mechanism exists: when a circle includes a drive the app
**cannot** read, do we **(a)** grant the partial / permission-only result, or **(b)**
reject the add as out of the app's scope? A policy call — **undecided**.

### Rejected approaches (for the record)

Every option below hands the app the symmetric **Peer Key**, so every one grants
read-all and fails the constraint — they differ only in *how* the copy is delivered:

- **App-Key wrapping (`AppKeyEncryptedPeerKey`)** — simplest delivery; was briefly the lead.
- **Per-app management key** — a single-cut revocation seam; no isolation (sits under the App Key).
- **Identity-level online/ICR-key escrow** — one key for the whole identity; coarse, identity-wide blast radius.
- **KDF derivation** — Peer Key = KDF(App Key, connectionId); couples every connection to one key.
- **Host/server-held escrow key** — works without the owner, but shifts trust to the host.
- **Proxy re-encryption** — still yields the full Peer Key, and heavy novel crypto.

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
read-scoping already handles; it does not by itself solve #3, the **Peer
Key**.) The policy blockers (#1, #2) still apply, but the check becomes "is the
caller the owning app?"

### Implementation: storage changes

Two dedicated, code-first CRUD tables (same pattern as `TableDrivesCRUD`):

**1. New `AppRegistrations` table.** Move app registrations off the shared
`KeyThreeValue` / `ThreeKeyValueStorage` blob (`AppRegistrationService.cs:39,93`)
into their own table. Columns: `identityId`, `AppId` (PK), `Name`, `CorsHostName`,
JSON columns for `AuthorizedCircles`, `CircleMemberPermissionGrant`, and `Grant`,
`created`/`modified`.
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
  identity-level ECC keys exist (`PublicPrivateKeyService`). Defined below; it is also
  the most promising direction for Blocker #3.

## Forward-looking: drive keys and ownership

Three related forward-looking threads, condensed. None of this blocks the near-term
feature above — except that the **per-drive public key** below is also the leading
candidate for solving Blocker #3.

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
read.* Uses: writing to a drive with **no connection** (Example #3) and bootstrapping the
**write side** of a safe connection request (Example #4). Reads (Example #2) need no
keypair; banking (Example #1) stays read-neutral by construction.

**Two keypairs, one pattern.** The Drive PK and the **Peer Key Store PK** (#3's candidate
solution) share this write-only pattern — an ECC keypair with its private key escrowed
under a symmetric key — but they are **distinct keypairs with distinct jobs**, and
neither subsumes the other:

| | Drive PK | Peer Key Store PK |
|---|---|---|
| Lives on | a **drive** | a **Peer Key Store** (the ICR) |
| Private key escrowed under | the drive's **storage key** | the **Peer Key** |
| Lets a writer deposit | **data** into a drive | a **grant** into a peer's store |

Both are needed for the full scope: the Peer Key Store PK is what unblocks **#3**; the
Drive PK is what enables the write-only *data* deposits of Examples #3 and #4.

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
