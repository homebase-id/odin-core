# Plan: Allow apps to add OdinIds to a circle

## Problem statement

Currently you must have the master key in order to add an OdinId to a circle. We
need to change this so that an app can add OdinIds into a circle — **without
letting an app grant access it does not itself already hold.**

The guiding principle: an app can only hand out what it can already reach. The
keyStoreKey / storage-key layering is, in effect, just indirection so that the
master key can always get to everything; an app sits below that, holding only the
keys for the drives it was granted. So the feature is less "give apps a new
power" and more "let the grant code build a grant from the keys the app already
has, instead of assuming the master key is the only source."

## What we want to achieve (worked examples)

These three examples define the intended behavior. Each is decided purely by what
keys the calling app already holds.

1. **Banking drive — must remain impossible.** A chat app must never be able to
   grant a *working* right to the user's private banking drive. It has no grant on
   that drive, so it holds no banking storage key — and shouldn't. Any grant it
   tried to mint would be empty/non-working. **This is enforced by cryptography,
   not just policy:** no storage key in hand ⇒ no usable grant can be produced. The
   policy boundary and the cryptographic boundary are the same line.

2. **GPS drive — what we want to newly enable.** A chat app has **read** access to
   the user's GPS drive (e.g. a location/temporal API). It needs to add a contact
   to a circle that grants read access to that GPS drive. Because the app already
   holds the GPS drive's storage key (it can read the drive), it has everything
   needed to mint that drive's portion of the grant for the new member ⇒ the member
   gets a **working** grant. **This is the case we cannot do today** — not because
   the secret is missing, but because the grant builder only knows how to source
   the storage key via the master key (see Blocker #4).

3. **Write-only drive — contribute without reading (proposed mechanism).** An app
   with **write-only** access does *not* hold the drive's storage key (and doesn't
   need it to write). Today such a writer deposits data encrypted with the
   per-client **shared secret**, which the server re-keys with the storage key on
   the way in. A proposed addition is a **per-drive public key**: the write-only app
   could leave a drive-public-key-encrypted copy in the drive's inbox. This is good
   because the app can contribute data while remaining *cryptographically* unable to
   read the drive — write-only becomes a real cryptographic property, not just an
   ACL flag. (Note: no per-drive keypair exists today — see Blocker #4 / open
   questions.)

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

## Alternative approach (exploratory): app-owned drives

> **Status: exploratory.** Not a committed direction — captured for comparison.

Instead of escrowing secrets broadly, give a drive an app-scoped root of trust. An
app already holds a stable per-app `keyStoreKey` at request time, reconstructed
from its client token with no master key
(`AccessRegistration.DecryptUsingClientAuthenticationToken`). "App owns a drive"
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
- **System drives:** likely out of scope for app ownership.
- **Per-drive public key (write-only deposits):** none exists today — drives are
  purely symmetric; only identity-level ECC keys exist (`PublicPrivateKeyService`).
  Enabling Example #3's drive-public-key deposit is a separate mechanism to design.
