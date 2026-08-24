# Weak key retirement — implementation checklist

Line-by-line extraction of every imperative in `weak-key-retirement.md` (part 3/4), grouped into
implementation categories. Line refs are `weak-key-retirement.md` as of 2026-08-19.

*This is a derived working checklist, not a spec. `weak-key-retirement.md` remains the source of
truth; where this file and that one disagree, that one wins.*

The doc introduces **no schema** (L7-8). Everything below is behavior.

## Cat 0 — Prerequisites from part 1 (not done)

Part 3 consumes two things part 1 was supposed to deliver (L6-7). The columns exist; the runtime
does not.

| # | Item | Line |
|---|---|---|
| 0.1 | Mint `Drives.WriteOnlyKeyPair` — lazily on first request, or backfilled in a VersionUpgrade pre-pass as the `PeerKeyStore` keypair was | L6, part 1 *Schema* |
| 0.2 | `AllowDeposits` capability flag on the drive's existing flags | L6-7 |
| 0.3 | Drive public-key endpoint (anonymous tier) — part 1's *The drive public key* | L30-33 |

Verified: `Drives.WriteOnlyKeyPair` exists as a column with zero usages outside generated CRUD, and
`AllowDeposits` does not appear anywhere in `src/`. The identity-scoped
`PeerKeyStore.WriteOnlyKeyPair` and its deposit machinery (`DepositedGrant`, sealed storage keys,
the V11→V12 backfill) **are** shipped — that is the pattern to copy, not the thing part 3 needs.

Nothing in Cat 1 can start until 0.1-0.3 land.

## Cat 1 — Move 1: transfers ride the Drive PK

| # | Item | Line |
|---|---|---|
| 1.1 | Outbox seals the KeyHeader to the **destination drive's** `WriteOnlyKeyPair` public half at enqueue, using the shipped ECIES primitive (`DepositedGrant.Seal`, envelope `EccEncryptedPayload`) | L79-83 |
| 1.2 | Replaces the CAT-shared-secret encryption in `CreateTransferInstructionSet` | L82-83 |
| 1.3 | Receive: unseal inline when the storage key is in scope — the same S1100 gate that decides direct-write today | L84-86 |
| 1.4 | Otherwise the sealed envelope rides the inbox row and drain unseals it (drain already requires the storage key) | L86-87 |
| 1.5 | **Required reorder:** the direct-write path currently decrypts *before* it checks; the unseal must move inside the storage-key branch | L88-90 |
| 1.6 | Same envelope change covers **file updates** | L90 |
| 1.7 | An **unencrypted-file variant** is needed | L90-91 |
| 1.8 | **Comments stay connected-senders-only** — their inbox fallback throws by design; they can never be pure deposits | L91-92 |

## Cat 2 — Move 2: the ICR key is the escrow

| # | Item | Line |
|---|---|---|
| 2.1 | At an attended accept, wrap the CAT **and the KSK** under the ICR key — not an App Key | L96 |
| 2.2 | Do *not* App-Key-wrap: every outbox enqueue site resolves the CAT via `GetIcrKey`, so it would silently break cross-app sending | L96-98 |
| 2.3 | Use the same storage format as the strong path (`EncryptedClientAccessToken`); stays owner-recoverable (master → ICR) | L98-99 |
| 2.4 | Fix the in-code TODO ("read ICR key from app?") — the fallback to the weak key exists only because the check tests for the *master* key specifically | L47-51 |
| 2.5 | Fix outbox rows storing peer CATs in **plaintext** (in-code TODO) — the doc says do it alongside Move 2 | L140-141 |

No change needed to background sending: the outbox captures everything at enqueue and the drain
worker only deserializes and sends, so it needs no key context once enqueue was attended
(L100-103).

## Cat 3 — No unattended minting: pending accepts

| # | Item | Line |
|---|---|---|
| 3.1 | Introductions and auto-connects store a **pending** request instead of minting secrets | L107-108 |
| 3.2 | The accept completes at the next attended app session | L108 |
| 3.3 | Companion fix: seal all connection-request payloads to `OnlineIcrEncryptedKey` (owner *and* transit apps) instead of the OfflineKey | L112-115 |
| 3.4 | Companion fix: populate `TempEncryptedIcrKey` **unconditionally** on outgoing requests, or the requester-side establish path re-mints weak escrow while unattended | L116-118 |

Deadlock-free by construction: completion needs one attended session per side, not simultaneity —
the existing temp-key round-trip lets the requester-side `EstablishConnection` finalize strong and
unattended (L108-110).

## Cat 4 — Wire compatibility and rotation

Filed as open questions 1 and 2, but Cat 1 cannot ship without them.

| # | Item | Line |
|---|---|---|
| 4.1 | Key-id in the envelope — sealed envelopes carry only a key CRC today, so a rotated Drive PK makes them undecryptable | L122-123 |
| 4.2 | Typed NACK at receive; sender re-fetches the key and re-seals (the retry pattern exists at `InvalidateRecipientEccPublicKeyAsync`) | L123-125 |
| 4.3 | Recipient-side key history for already-inboxed rows | L125-126 |
| 4.4 | Capability signal: a successful fetch of the drive public-key endpoint doubles as it | L127-129 |
| 4.5 | Fall back to CAT-shared-secret for legacy peers; receive accepts both during the transition | L129-130 |

## Cat 5 — Deposit abuse controls

Open question 3. All unspecified beyond the list.

| # | Item | Line |
|---|---|---|
| 5.1 | Never anonymous — CAPI-authenticated identity required, else `SenderOdinId` is spoofable | L131-132 |
| 5.2 | Per-sender quotas | L132 |
| 5.3 | Deposit TTL | L132 |
| 5.4 | Size caps | L132 |
| 5.5 | Block-list checks | L132 |

## Cat 6 — Pending-flow edge rules

Open question 4. All undecided.

| # | Item | Line |
|---|---|---|
| 6.1 | Crossing-request convergence — the existing tie-break assumed short windows | L133-134 |
| 6.2 | Withdrawal racing in-flight deposits | L134 |
| 6.3 | Pending TTL and count caps | L134 |
| 6.4 | Sender-side UX truthfulness — deposits shown "delivered" to an identity that may never accept | L134-135 |

## Cat 7 — What stays weak

Open question 5, but these are scope statements with work attached.

| # | Item | Line |
|---|---|---|
| 7.1 | Feed distribution still seals raw KeyHeaders to the OfflineKey — the largest remaining weak-at-rest surface. Sketch: the same Move-1 envelope sealed to the *feed drive's* PK | L136-139 |
| 7.2 | The OfflineKey decrypt path must stay alive until all existing `TempWeak*` rows have ridden the existing upgrade paths | L139-140 |

The OfflineKey is **not** retired by this design. The weak key ends up unemployed on the connection
path (L63), not deleted.

## Dependencies

- Cat 0 → Cat 1. No Drive PK, no envelope.
- Cat 4 → Cat 1 ships. The envelope changes a host-to-host contract with no flag day.
- Cat 2 and Cat 3 are independent of Cat 1 — both are escrow fixes, not wire changes.
- Cat 3.3 and 3.4 are the two back doors; without them the weak key re-enters even after Cat 1-2.
- Part 2's deposit-only invariant is a hard input to the whole design (L26-28). Part 2's
  `ReviewedAt` and Authenticated-tier assignment are what make "mint nothing at 3 a.m." safe
  (L41-44).

## Code refs — spot-checked

All file:line references in the doc resolve in current `src/` (checked 2026-08-19):

| Ref | Lands on |
|---|---|
| `PublicPrivateKeyService.cs:43` | `OfflinePrivateKeyEncryptionKey = { 0, 0, ... }` — exact |
| `PeerOutgoingTransferService.cs:534-551` | `CreateTransferInstructionSet` |
| `PeerOutgoingTransferService.cs:574-615` | outbox capture region |
| `PeerDriveIncomingTransferService.cs:366` | `DecryptKeyHeaderWithSharedSecret(...)` — the decrypt-before-check |
| `PeerDriveIncomingTransferService.cs:526-556` | `CanDirectWriteFile` |
| `PeerServiceBase.cs:82` | `CreateClientAccessToken(...GetIcrKey())` |
| `CircleNetworkRequestService.cs:806-810` | shared-secret reuse at accept |
| `CircleNetworkRequestService.cs:1077-1091` | `if (originalRequest.TempEncryptedIcrKey == null)` |
| `CircleNetworkRequestService.cs:1736-1741` | `GetPublicPrivateKeyType(origin)` — Offline-vs-Online selection |
| `SendFileOutboxWorkerAsync.cs:105-107` | CAT deserialize in the drain worker |
| `ServerHalfOfClientKey.cs:35-44` | `DecryptUsingClientAuthenticationToken` |
| `FeedDriveDistributionRouter.cs:234` | `EccEncryptPayloadForRecipientAsync(...)` |

`PublicTransitAuthScheme`, `OnlineIcrEncryptedKey`, `DepositedGrant`, and `TempWeak*` all exist as
named. I verified these symbols resolve at roughly the cited locations; I did not verify that each
surrounding block still does what the doc claims about it.

## Not in scope here

Part 2 (`connection-defaults.md`) — see `connection-defaults-checklist.md`.
Part 4 (chat-kmp `CIRCLES_VISIBILITY_PROPOSAL.md`) — all client work.
