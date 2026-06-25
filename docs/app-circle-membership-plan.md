# Plan: Allow apps to add OdinIds to a circle

## Problem statement

Currently you must have the master key in order to add an OdinId to a circle. We
need to change this so that an app can add OdinIds into a circle — **within the scope of drives that the App already has access to.**

The guiding principle: an app can only hand out what it can already reach. The
keyStoreKey / storage-key layering is, in effect, just indirection so that the
master key can always get to everything; an app sits below that, holding only the
keys for the drives it was granted. So the feature is less "give apps a new
power" and more "let the grant code build a grant from the keys the app already
has, instead of assuming the master key is the only source."

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
   (`CircleNetworkRequestService.cs`): it ECC-encrypts the connection's keyStoreKey
   and CAT to the **identity-level** online/offline key (`TempWeakKeyStoreKey` /
   `TemporaryWeakClientAccessToken`), leaves `MasterKeyEncryptedKeyStoreKey` **null**
   (`RequiresMasterKeyEncryptionUpgrade()` ⇒ true), pins the connection to
   **`AutoConnectionsCircle` only** until "confirmed" from the console, and later
   runs a deferred **upgrade** to re-encrypt the keyStoreKey under the master key
   when the owner appears. It leans on an `overrideHack` parameter bypassing the
   `ReadConnections` check (`//HACK: DOING THIS WHILE DESIGNING x-token - REMOVE THIS`)
   and flags the open question in-line: `//TODO: should we validate all drives are
   write-only ?`.

   *Why it's the same problem.* That stop-gap already does the work of **Blocker #3**
   — it escrows the keyStoreKey under an alternative (identity-level) key — but
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

3. **The app cannot decrypt the target member's keyStoreKey.** Adding a circle
   grant to an existing connection means writing a grant encrypted under *that
   connection's* keyStoreKey, which is stored only as
   `AccessExchangeGrant.MasterKeyEncryptedKeyStoreKey`. This is the **member's**
   secret, not a drive secret — read-scoping the app does not help, because the
   app's drive access says nothing about another member's keyStoreKey. There is no
   app-accessible copy today. *(Cryptographic — this is the real cost; see callout.)*

4. **The grant builder sources the drive storage key only via the master key.** A
   circle grant that includes drive reads must re-encrypt each drive's storage key
   under the new member's keyStoreKey. The grant builder currently obtains the
   plaintext storage key one way only:
   `drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey)`
   (`ExchangeGrantService.cs`). With a null master key it silently produces a grant
   with a **null** storage key — a member who is "in the circle" but cannot read any
   of its drives.

   **But this is not a missing-secret problem.** For any drive the app already has
   **read** access to, the app *holds a usable plaintext storage key at request
   time* — reconstructed without the master key via
   `client token → app keyStoreKey (AccessRegistration) → DriveGrant.KeyStoreKeyEncryptedStorageKey → storage key`
   (`PermissionGroup.GetDriveStorageKey`). The blocker is that
   `CreateExchangeGrant` was written assuming the master key is the only source. A
   code path that reuses the storage key already in the app's permission context
   dissolves this for read-scoped grants — **no data upgrade needed.** *(Cryptographic
   in appearance, but really a code-path assumption.)*

> **#3 and #4 are not the same kind of blocker — this is the key correction.**
> Blocker #4 (drive storage keys) dissolves the moment we scope "an app may only
> grant drives it can already read": the app already holds those storage keys, so it
> needs no new escrowed copies and no migration. Blocker #3 (the member's
> keyStoreKey) does **not** dissolve — it is a member-level escrow node the app
> genuinely cannot see. **#3 is the actual cryptographic cost of this feature.**

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
- **#3 (crypto — the real cost):** give the app a way to encrypt into the target
  member's grant. Either (a) require the master key be present for this op after
  all (defeats the purpose), or (b) introduce an app-accessible copy of the
  connection keyStoreKey for connections the app is allowed to manage. This is the
  one place that needs a data upgrade for existing connections.
- **#4 (crypto — free under scoping):** add a code path in `CreateExchangeGrant`
  that uses the storage key already in the app's permission context for drives the
  app can read, instead of decrypting `MasterKeyEncryptedStorageKey`. No upgrade.

Net: the broad "escrow every drive's storage key under a host key" framing is
**not** required if we accept read-scoping. The remaining genuine cryptographic
work is #3 (the member keyStoreKey), not #4.

## App-owned drives (committed direction, timing TBD)

> **Status: committed direction.** We are certain we need app-owned drives; the
> open question is *when* to build it, not *whether*. The mechanics below are the
> design sketch, not a fixed spec.

Drives will be owned by the app that creates them: delete the app and you delete
its drives (cross-app drive grants stay possible), and to a lesser extent apps will
own app circles they can create and delete. Cryptographically this means giving a
drive an app-scoped root of trust. An app already holds a stable per-app
`keyStoreKey` at request time, reconstructed from its client token with no master
key (`AccessRegistration.DecryptUsingClientAuthenticationToken`). "App owns a drive"
means re-rooting that drive's storage key onto the owning app's keyStoreKey.

Concretely it would require:

- An `OwningAppId` association on the drive (none exists on `StorageDrive` today).
- A second storage-key copy, e.g. `AppKeyStoreKeyEncryptedStorageKey`, alongside
  the existing `MasterKeyEncryptedStorageKey`.

Payoff: the owning app can read/write the drive and **grant it to circles and
identities without the master key** — scoped to that app rather than escrowing all
drives broadly. (Note this mainly addresses #4-style drive access, which
read-scoping already handles; it does not by itself solve #3, the member
keyStoreKey.) The policy blockers (#1, #2) still apply, but the check becomes "is
the caller the owning app?"

Open questions:

- **Owner access:** co-owned (keep the master-key copy so the owner can recover —
  preferred) vs app-exclusive (owner cannot read the drive; stronger isolation but
  risks unrecoverable data).
- **Migration:** making an existing drive app-owned still needs the master key once
  (owner online) to mint the app-key copy.
- **App removal/revocation:** policy for what happens to a drive when its owning app
  is deleted.
- **Cross-app isolation:** only the owning app's keyStoreKey may decrypt the drive.
- **System drives:** out of scope for app ownership (see taxonomy below).
- **Per-drive public key:** none exists today — drives are purely symmetric; only
  identity-level ECC keys exist (`PublicPrivateKeyService`). It's a separate
  mechanism to design; its relation to the four use cases is in the closing section.

## Drive ownership taxonomy

Ownership is **lifecycle authority, not authorship.** "Which app created the drive"
is provisioning; ownership is decided by one question:

> If you delete an app — or swap to a different client — should this drive's data
> die with it, or persist?

That sorts every drive into three buckets:

- **System drives — the true core.** Only what the most basic identity functionality
  requires. The clear case is the **Profile drive**: it is integral to YouAuth —
  being able to query a user's attributes. The bar is "the identity itself cannot
  function without it," and very little clears it. Owner is the platform/identity
  (master-key rooted); always present.
- **App-owned drives.** Lifecycle bound to one installed app; deleted with it;
  app-specific format. **Chat** is the clear case — delete the chat app and its
  history going with it is expected.
- **Virtual-app / appless drives.** Owned by a *stable logical owner that is not a
  currently-installed client*, so the data survives swapping clients while staying
  modular and grantable cross-app. This is the home for user data an app *manages*
  but does not *own*. **Vault** is the poster child — reinstall a different chat
  client and your vault must still be there. **Stickers** and possibly **Moments** are
  candidates, depending on whether they should be reusable / outlive the client.

**"Built-in" ≠ "system."** Today `SystemDriveConstants` hardcodes Chat, Moments,
Stickers, Wallet (Vault), Mail, Contacts, Feed, Channels and Profile alike as
built-in drives — but that is an implementation convenience, not a claim that they
are system drives. By the bar above, **Feed, Channels, Mail and the rest are bolt-on
apps**; only Profile (and a minimal core) is truly system. The move to app-owned /
virtual-app drives reclassifies most of today's built-ins out of "system."

This taxonomy is **orthogonal to the write-only keypair.** Because the per-drive key
is escrowed under the drive's storage key (not an app), it works identically for a
system, app-owned, or virtual-app drive — so the ownership bucket can be decided per
drive, on product grounds, without blocking the crypto. A per-*app* key would have
forced the bucket to be resolved first, just to know where the key lives.

## Per-drive public keys: relation to each use case

None exists today (see above). Drive public keys are currently proposed for two
main purposes: writing to a drive with no connection (#3), and bootstrapping the
write side of a safe connection request (#4). Everything else is solved by
symmetric keys the app already holds.

A per-drive keypair would be a **write-only root of trust** for a drive: anyone can
encrypt *to* the drive's public key and deposit the result, but only the holder of
the drive's private key (escrowed under the storage / master key) can read it back.
That one property — *the public key lets you write, never read* — is what decides
its relevance to each use case:

- **#1 Banking — neutral, and must stay that way.** A drive public key confers no
  read access, so publishing one can never breach the banking boundary. A chat app
  could at most *deposit* into a drive it cannot read; it still cannot read the
  banking drive. Relevance here is a property to **preserve**, not a feature:
  "knows the public key" must never imply "can read."

- **#2 GPS read grant — not needed.** The app already holds the GPS storage key (it
  has read access), so it mints the member's read grant from the symmetric key it
  already has (the Blocker #4 code path). Drive public keys add nothing; this case
  is pure symmetric reuse.

- **#3 Write-only deposit — the core use case.** This is what per-drive public keys
  are *for*. Today write-without-read works only between already-connected parties
  (shared secret); a per-drive public key extends the same guarantee to **any**
  writer with no shared secret yet, by encrypting to the drive's public key into its
  inbox.

- **#4 App-driven connection — the enabler for "strong within app scope."** The new
  connection's *read* grants are solvable like #2 wherever the accepting app holds
  the storage key. Per-drive public keys cover the rest: they let the app bootstrap
  the new peer's **write** access to specific drives at accept time, **per drive**,
  in place of the current identity-wide ECC escrow + AutoConnections jail + deferred
  master-key upgrade. The escrow becomes drive-scoped, so the connection is strong
  enough within the app's scope immediately, without the master key.

**In short:** essential to **#3** and the write side of **#4**, **not needed** for
**#2**, and must remain **read-neutral** for **#1**.

## Per-drive vs per-app keypair — recommendation: per-drive

> **Status: exploratory question, with a recommendation.** App-owned drives is
> committed (above); *where the write-only keypair lives* is the open call. This
> section recommends **per-drive**.

Even with app-owned drives committed, the write-only root of trust should be **per
drive, not per app** — with the drive's private key escrowed under that **drive's
storage key**.

The deciding lens: a write-only public key answers *"where do I write,"* and its
private key answers *"who collects and reads the deposit."* The collector is
whoever holds the **storage key** — which is exactly the existing read-access
principal. So wrap each drive's private key under its storage key
(`StorageKeyEncryptedDrivePrivateKey`, alongside the existing keys) and custody
falls out for free: read access ⇒ can collect deposits; no read access ⇒ can only
write to the public key. Write-only stays intact, because the writer has no storage
key to unwrap the private key.

Why per-drive wins:

- **One uniform mechanism — no fallback.** Every drive gets the property, app-owned
  or not. System drives and owner-created drives are covered by the same code.
  Per-app cannot reach unowned drives, so it would need a *second* per-drive /
  per-identity fallback — two mechanisms instead of one.
- **Cross-app grants stay un-coupled.** The keypair travels with the drive, so a
  second app granted the drive (holding the storage key via its grant) can collect
  deposits independently. A per-app key couples deposit-decryption to the owning
  app, which a cross-app grantee would have to route through.
- **Blast radius is isolated.** Compromise of one drive's private key exposes only
  that drive; a per-app key exposes every one of the app's drives at once.
- **Custody needs no new principal.** It is just the storage key the system already
  manages. (A per-app keypair would instead extend the app's `keyStoreKey`
  principal — workable, but unnecessary here.)

What per-app would have offered, and why it doesn't flip the call:

- **Lifecycle is not unique to per-app.** Per-drive keys on app-owned drives die when
  the drive dies, which is when the app dies — the same delete cascade.
- **#4's app-scoped bootstrap fan-out is cheap.** "Strong within the app's scope"
  spans a handful of drives; encrypting a small bootstrap payload to each in-scope
  drive's public key is trivial — and it is *better* least-privilege, since each
  drive is bootstrapped and revoked independently.
- **The one real per-app argument is a product one, not crypto:** a single durable
  *"write to this app"* address, independent of how many drives sit behind it. If we
  ever want that as a product primitive, revisit — but it does not change the
  cryptographic recommendation.

Open questions (per-drive):

- **Private-key escrow beyond the storage key:** storage key alone is enough for any
  read-access holder; do we also want a master-key copy for owner recovery if a
  drive's storage key is ever lost?
- **Rotation:** does the drive keypair ever rotate, and what happens to deposits
  encrypted to the old public key?
- **Drive deletion:** what happens to in-flight deposits addressed to a deleted
  drive's public key?
- **App circles:** app ownership of circles is a separate concern — it does not need
  this write-only keypair.
