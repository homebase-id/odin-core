# Retiring the weak key — the 3 a.m. problem (part 3/4)

Previous doc: odin-core PR#1589 docs/connection-defaults.md
Next doc: chat-kmp PR#1062 CIRCLES_VISIBILITY_PROPOSAL.md

*Status: proposal, for discussion. Consumes part 1's Drive PK (`Drives.WriteOnlyKeyPair` +
`AllowDeposits`) and part 2's deposit-only auto-connect invariant. Introduces **no schema** —
part 1's complete-surface claim stands.*

## The plain story

**The requirement:** a stranger — someone who was just introduced to me — must be able to send me
a message at 3 a.m., when nobody is home. No password typed, no app open.

**Why the weak key exists today:** the current code believes that to *receive* from someone, it
must first build the *whole connection* — mint them a token (the CAT), create the peer key, set
up grants — right now, at 3 a.m. Those are secrets, and secrets must be stored encrypted. But at
that moment the server holds no strong key to encrypt them with: the master key needs the owner's
password, app keys need a logged-in device. So it seals them to the "offline key", whose private
half is protected by a **hard-coded key of all zeros** (`PublicPrivateKeyService.cs:43`) —
effectively plaintext, with a sticky note saying "encrypt properly when the owner shows up."

The weakness is not the cryptography. It is the **calendar**: we mint secrets at the one moment
nothing exists to protect them — because receiving was welded to connection-building.

**The insight, from part 2's own invariant:** auto-connect is **deposit-only**. No read keys are
in play until the owner reviews the connection. And accepting a deposit requires **no secrets on
my side at all**:

- The message arrives sealed to my **chat drive's public key** (part 1). The sender needs no
  shared secret with me, and my server needs no key to *accept* a sealed envelope — it just
  stores it. It unlocks when my app next opens (the private half sits under the drive's storage
  key).
- The sender's identity is vouched for by their own server at the perimeter — CAT-free
  authentication already exists and is deployed (`PublicTransitAuthScheme` on the
  follow/invitation/public-key endpoints). Deposits are never anonymous.
- Enrolling them in the deposit-only default circle needs no keys either: write permissions are
  plaintext on the grant; only *read* grants carry storage keys, and deposit-only means there are
  none.

**So the 3 a.m. job becomes: store a sealed envelope, record the sender as New 👋. Mint nothing.
Store no secrets.** There is nothing left for a weak key to protect. (New = `ReviewedAt == null`
on the connection registration; until the review, the caller ranks as **Authenticated** on the
recut security ladder — part 2 — so they can read nothing beyond any stranger identity.)

**The connection's real secrets** — the token for talking back, the read grants — are created
later, when someone is actually there. The next app session completes the connection: apps with
transit permission **already carry a strong key for exactly this** (the ICR key, via
`PermissionContext.GetIcrKey()`); the code falls back to the weak key today only because it
checks for the *master* key specifically — the in-code TODO ("read ICR key from app?") names the
fix. Read keys are first minted at the review, exactly where part 2 says the key ceremony lives.

**Why it must be the *drive* key.** The identity-level alternatives fail:

- The **OfflineKey** *is* the weakness — zero-key escrow (it is also what feed distribution seals
  to today; see *What stays weak*).
- The ICR-scoped identity key is **identity-wide**: any transit app could unseal any deposit at
  drain — the mail app reading chat messages — breaking the per-app scoping this whole series is
  built on.
- Only a **per-drive** key makes a deposit unlock exactly and only where read access already
  exists: its private half sits under that drive's storage key.

The weak key isn't fixed by any of this. It's **unemployed**.

## Technical backing (code-audited)

Corrections to folklore, established by audit:

- Transit KeyHeaders are encrypted with the **CAT shared secret** — not the Peer Key. Both sides
  hold one secret because accept literally reuses the requester's
  (`CircleNetworkRequestService.cs:806-810`).
- The Peer Key's only runtime job is unlocking read grants
  (`KeyStoreKeyEncryptedStorageKey`), and it is always reconstructible from the *presented* CAT
  (`ServerHalfOfClientKey.cs:35-44`) — its escrow never mattered on peer-facing paths. The
  escrows exist for the owner's own server (grants, upgrades, sending).

**Move 1 — transfers ride the Drive PK.**

- The outbox seals the KeyHeader to the **destination drive's** `WriteOnlyKeyPair` public half at
  enqueue, using the shipped ECIES primitive (throwaway P-384 key + 16-byte salt + HKDF → 16
  bytes + AES-GCM; `DepositedGrant.Seal`, envelope = `EccEncryptedPayload` carrying the ephemeral
  public key + salt) — replacing the CAT-shared-secret encryption
  (`PeerOutgoingTransferService.cs:534-551`).
- Receive: unseal inline when the storage key is in scope — the **same S1100 gate** that decides
  direct-write today (`PeerDriveIncomingTransferService.cs:526-556`) — otherwise the sealed
  envelope rides the inbox row and drain unseals it. Drain **already requires** the storage key
  (it re-keys the header to it), so nothing new is demanded of drain contexts.
- Required reorder: the current direct-write path decrypts *before* it checks
  (`PeerDriveIncomingTransferService.cs:366`) — the unseal must move inside the storage-key
  branch. The same envelope change covers **file updates**; an unencrypted-file variant is
  needed; **comments stay connected-senders-only** (their inbox fallback throws by design — they
  can never be pure deposits).

**Move 2 — the ICR key is the escrow; the App Key is just how an app reaches it.**

- At an attended accept, wrap the CAT (and the KSK) under the **ICR key** — not an App Key.
  App-Key wrapping would silently break cross-app sending: every outbox enqueue site resolves the
  CAT via `GetIcrKey` (`PeerServiceBase.cs:82`). ICR-wrap uses the same storage format as the
  strong path (`EncryptedClientAccessToken`) and stays owner-recoverable (master → ICR).
- The outbox captures everything at enqueue (instruction set, sealed KeyHeader, CAT bytes:
  `PeerOutgoingTransferService.cs:574-615`); the background drain worker deserializes and sends
  (`SendFileOutboxWorkerAsync.cs:105-107`) — so background sending needs no key context at all
  once enqueue was attended.

**No unattended minting — pending accepts.**

- Introductions and auto-connects store a **pending** request instead of minting secrets; the
  accept completes at the next attended app session. Deadlock-free: completion needs one attended
  session per side, not simultaneity — the existing temp-key round-trip lets the requester-side
  `EstablishConnection` finalize **strong and unattended**.
- Two companion fixes so the weak key cannot re-enter through back doors:
  1. Seal all connection-request payloads to `OnlineIcrEncryptedKey` (decryptable by the owner
     *and* transit apps) — pending payloads otherwise sit effectively-plaintext for the whole
     pending window (today they are sealed to the OfflineKey,
     `CircleNetworkRequestService.cs:1736-1741`).
  2. Populate `TempEncryptedIcrKey` **unconditionally** on outgoing requests — otherwise the
     requester-side establish path re-mints weak escrow while unattended
     (`CircleNetworkRequestService.cs:1077-1091`).

## Open questions

1. **Envelope staleness/rotation** — sealed envelopes carry only a key CRC; if the Drive PK
   rotates, they are undecryptable. Needed: key-id + typed NACK at receive (sender still holds
   the plaintext KeyHeader and can re-fetch + re-seal — the retry pattern exists at
   `InvalidateRecipientEccPublicKeyAsync`), and recipient-side key history for already-inboxed
   rows.
2. **Wire-format negotiation** — the sealed-KeyHeader envelope changes the host-to-host contract
   with no flag day. Proposed: a successful fetch of the drive public-key endpoint doubles as the
   capability signal; fall back to CAT-shared-secret for legacy peers; receive accepts both
   during transition.
3. **Deposit abuse controls** — never anonymous (CAPI-authenticated identity required, else
   `SenderOdinId` is spoofable); per-sender quotas, deposit TTL, size caps, block-list checks.
4. **Pending-flow edge rules** — crossing-request convergence (the tie-break assumed short
   windows), withdrawal racing in-flight deposits, pending TTL and count caps, and sender-side UX
   truthfulness (deposits shown "delivered" to an identity that may never accept).
5. **What stays weak — scope honesty.** This design does *not* retire the OfflineKey: feed
   distribution still seals raw KeyHeaders to it (`FeedDriveDistributionRouter.cs:234` — the
   largest remaining weak-at-rest surface; follow-up sketch: the same Move-1 envelope sealed to
   the *feed drive's* PK), and the OfflineKey decrypt path must stay alive until all existing
   `TempWeak*` rows have ridden the existing upgrade paths. Also in the at-rest inventory: outbox
   rows store peer CATs in **plaintext** today (in-code TODO) — fix alongside Move 2.

One synergy worth naming: under part 2's deposit-only invariant, the "keyless grants" defect of
the weak path stops being a defect at all — `AUTO_CONNECT` circles are write/react and need no
storage keys. The invariant and this design close over each other.
