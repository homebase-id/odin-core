# Contact Enrichment — Phase 2 (deferred automation)

**Status:** Phase 1 ships with **client-driven enrichment only**. The app calls
`POST /api/v2/contacts/sync/{odinId}` to create/refresh a contact from an identity's profile.
All *automatic* (lifecycle-driven) enrichment has been removed; this document captures why, what
code already exists to build on, and what Phase 2 needs to add.

This supersedes the "connection/introduction lifecycle" and "ContactReconcileJob" portions of
`docs/contact-api-plan.md` — those describe the intended end state, not what currently ships.

---

## The problem

Automatic enrichment was wired to MediatR connection-lifecycle notifications
(`ContactLifecycleService`, removed in this branch — recover it with
`git show f5a84a543:src/services/Odin.Services/Contacts/ContactLifecycleService.cs`). It tried to
create a contact stub and pull the peer's profile the moment a relationship appeared. Three issues
made that unworkable as a v1:

### 1. The write needs the owner key; most lifecycle events don't have it

Persisting a contact is the hard constraint, **not** fetching profile data. Contact files are
`IsEncrypted = true`, `AccessControlList.OwnerOnly`, so every write
(`ContactService.EnsureExistsAsync`, `MergeAsync`, `UpsertAsync`) must resolve the **ContactDrive
storage key** via `writeContext.PermissionsContext.GetDriveStorageKey(DriveId)`. That key is only
available when the calling context carries the **owner master key** (an owner session) or an app's
ContactDrive grant.

Mapping that to where the notifications are published:

| Notification | Publish site | Context | Has write key? |
|---|---|---|---|
| `ConnectionFinalizedNotification` (acceptor side) | `CircleNetworkService.cs:394` via `AcceptConnectionRequestAsync` (`CircleNetworkRequestService.cs:744`) | owner (interactive accept) | **Yes** |
| `ConnectionFinalizedNotification` (requester side) | `CircleNetworkService.cs:394` via `EstablishConnection`→`ConnectAsync` (`CircleNetworkRequestService.cs:977`) | peer | No |
| `ConnectionFinalizedNotification` (introduction auto-accept) | same, via `OdinContextUpgrades.UsePermissions` | permissions-only upgrade, no master key | No |
| `ConnectionRequestReceivedNotification` | `CircleNetworkRequestService.cs:550` | peer-incoming | No |
| `IntroductionsReceivedNotification` | `CircleNetworkIntroductionService.cs:431` | peer-incoming | No |

So the only lifecycle event that could actually write a contact was the **owner-interactive
accept**. Every other relationship-formation path (inbound request, introduction, requester-side
finalize, auto-accept) physically cannot persist a contact in-context. Fetching public profile data
is keyless and fine; there's just nowhere to write the result.

### 2. The one event that *could* write fetched the wrong data

On the acceptor side, `ConnectionFinalizedNotification` fires from `ConnectAsync`
(`CircleNetworkService.cs:394`) **before** the acceptor calls `EstablishConnection` on the requester
(`CircleNetworkRequestService.cs:755`/`:787`). The requester only creates its ICR for the acceptor
when it handles that `EstablishConnection` call (its own `ConnectAsync` at `:977`). So a connected
peer-query at finalize-time runs before the requester recognizes the acceptor as connected → the
query is served under the anonymous/public fallback (ProfileDrive allows anonymous reads), returning
only public attributes while the code believes it got connected data. A 403 carrying an ICR-issue on
that path can also revoke the just-created ICR (`PeerDriveQueryService`).

### 3. No backfill

The design leaned on "owner-driven `/sync` converges it later" plus a reconcile job — but the
reconcile job was never built, and inline handlers can't reach pre-existing connections. So missed
events were effectively permanent.

Net: automatic enrichment was a single working path (owner-accept) that fetched degraded data on a
blocking hot path, plus several dead paths. Phase 1 removes it entirely.

---

## Phase 1 behavior (what ships now)

- **No lifecycle subscriptions.** `ContactLifecycleService` and its three `INotificationHandler`
  registrations are gone (`TenantServices.cs`).
- **`/sync` is the only trigger.** `POST /api/v2/contacts/sync/{odinId}`
  (`V2ContactsController.Sync`) runs, in order:
  1. `ContactService.EnsureExistsAsync(id, ctx)` — creates an `{ odinId }` stub if absent (so the
     call deterministically yields a contact even when the peer has no profile data).
  2. `ContactEnrichmentService.EnrichAsync(id, ctx)` — best-effort profile pull + merge.
- The caller is the owner or an app holding `ManageContacts` (which now implies `ReadConnections`,
  `ReadConnectionRequests`, `ReadCircleMembership` — see `PermissionKeyImplications`), so the write
  key is always present on this path.

The app is responsible for calling `/sync` after a connection is established (and, in Phase 2, this
becomes automatic again).

---

## Existing code to build on

These are intact and are the Phase 2 building blocks:

- **`ContactEnrichmentService.EnrichAsync(OdinId, IOdinContext)`** — the enrichment engine.
  - Chooses source by live status (`CircleNetworkService.IsConnectedAsync`): **connected** →
    `PeerDriveQueryService.GetBatchAsync` on the peer's `ProfileDrive`; **not connected / 403** →
    keyless `pub/profile` fetch (`TryBuildFromPublicProfileAsync`, 10s timeout).
  - Selects among multiple same-type attributes by authored `Priority` (lower wins), taking the
    first **non-empty** value per field — see `ProfileAttribute` (owned locally, not the SSR
    `ProfileBlock`) and `ContactProfileAttributes`.
  - Best-effort: swallows peer/profile failures and leaves the contact unchanged.
- **`ContactService`** — the write authority (asserts `ManageContacts`, writes via an ACL-bypass
  upgrade):
  - `EnsureExistsAsync` — create-if-absent stub (local, fast, needs the write key).
  - `MergeAsync` — server-internal create-or-merge keyed on `ToContactUniqueId(odinId)` (= md5 of
    the domain, byte-compatible with odin-js); retries once on a version-tag race.
  - `UpsertAsync` — client create/update with optimistic concurrency.
  - Merge rules: empty/whitespace means "leave alone" (`Coalesce`), so enrichment/API never wipe a
    stored value; overwrites are recorded to the encrypted `merge_log` payload; content IV rotates
    on every update.
- **`V2ContactsController`** — `POST /contacts` (upsert), `DELETE /contacts/{uniqueId}`,
  `POST /contacts/sync/{odinId}`.
- **MediatR notifications** available to re-subscribe to (with the context caveats in the table
  above): `ConnectionFinalizedNotification`, `ConnectionRequestReceivedNotification`,
  `IntroductionsReceivedNotification`.

---

## What Phase 2 needs to build

1. **A deferred, owner-keyed write path.** Because the peer-context events can't write, capture the
   work (e.g. an outbox/job item carrying the target odinId) and execute it under an owner-keyed
   context — either a captured-owner-token job, or a sweep that runs at the next owner session.
   This is the core unlock; everything else depends on it.
2. **Move enrichment off the accept hot path.** Even on the owner-accept event, don't block the
   accept response on cross-server HTTP. Enqueue enrichment; run it after the handshake completes so
   the connected peer-query actually sees an established ICR (avoids the degraded-data + ICR-revoke
   problem in §2).
3. **A reconcile/backfill job** (`ContactReconcileJob` in the plan). Scan ICRs + pending/sent
   requests + received introductions and ensure a contact exists for each. Heals missed events and
   pre-existing relationships that predate this feature.
4. **Full event coverage.** Re-introduce stub creation for inbound requests, introductions, and the
   requester side — all routed through the deferred owner-keyed path from (1).

---

## Related: unlinked contacts (#10) — partly shipped, link flow deferred

**Shipped (phase 1):** the contact write API was split from a single `Upsert` into an explicit
**create / update** pair, which fixes the addressability and version-tag halves of #10:

- `POST /api/v2/contacts` — **create**. Server derives the uniqueId (`md5(odinId)` if present, else
  random) and returns it in `ContactWriteResponse`; a collision returns **409** (update instead).
- `PUT /api/v2/contacts/{uniqueId}` — **update**. `VersionTag` is required and enforced (**409** on
  stale, **404** if missing); the route's uniqueId identifies the file and the call never re-keys.

So an unlinked (no-odinId) contact is now addressable by the uniqueId its create returned, edits no
longer duplicate, and `VersionTag` is honest. (`ContactService.CreateAsync` / `UpdateAsync`;
server-internal `MergeAsync` used by `/sync` is unchanged.)

**Deferred (phase 2) — the link / re-key flow.** Associating an odinId with an *existing* unlinked
contact is **not** an update: a linked contact's identity is `uniqueId = md5(odinId)`
(`ToContactUniqueId`), which is the file's immutable key, but the unlinked contact lives under a
random uniqueId. Setting `odinId` in its content via `PUT` would leave it findable-by-nobody
(enrichment/`/sync`/lifecycle all look up `md5(odinId)`, and `BuildTags` tags by uniqueId), so a
later connection would create a *second*, canonical file → duplicate.

It needs its own operation that re-keys, reusing existing primitives:

1. Read the unlinked file's content, set `OdinId = X`.
2. `MergeAsync(content, …)` — keys on `md5(X)`, already create-or-merge: creates the canonical
   linked file, or merges into one if the person connected in the meantime (the `Coalesce` +
   `merge_log` rules keep it non-destructive).
3. Soft-delete the old random-keyed file (`DeleteByUniqueIdAsync`).

Suggested surface: `POST /api/v2/contacts/{uniqueId}/link` with `{ odinId }` → returns the canonical
uniqueId + version tag. Two decisions to settle: (a) **precedence** when a canonical file already
exists — default to the user's hand-entered values winning, with old values logged to `merge_log`;
(b) keep it **explicit/client-initiated** (matches the plan's "layered, no auto-merge" stance,
`b18316d47`) — the server should not guess that "Barliman" is `barliman.dotyou.cloud`.

---

## Related: profile image (#1) — shipped, preview-thumbnail deferred

**Shipped (phase 1):** the contact image is a payload (replace semantics), so it gets a dedicated
sub-resource off the JSON merge/version path, version-tag gated like `update`:

- `PUT /api/v2/contacts/{uniqueId}/image` — set/replace. Body
  `SetContactImageRequest { versionTag, contentType, content, thumbnails[] }`. **200** + new tag,
  **404** if the contact is missing, **409** (`ContactWriteConflict`) on a stale tag.
- `DELETE /api/v2/contacts/{uniqueId}/image?versionTag=…` — remove it. **200/404/409**.

Because this API is server-key-managed (the client never holds the file's AES key), the client sends
**plaintext** image + thumbnail bytes over the shared-secret transport and the **server encrypts at
rest** under the file key — one IV for the payload and all its thumbnails — stored as the
`prfl_pic` payload (`ContactService.ProfileImagePayloadKey`). The client still *generates*
thumbnails; only the encryption moved server-side. `SetImageAsync`/`DeleteImageAsync` reuse the
merge-log `UpdateBatchAsync` + staging plumbing, preserving the contact content (re-encrypted under a
rotated content IV) and the `merge_log` payload. (Fixed in passing: `WriteContentWithMergeLogAsync`
now carries forward other payloads, so a field edit no longer drops the image.)

**Deferred (phase 2):**
- **Inline header preview-thumbnail.** The payload + its thumbnails have an independent IV and
  survive content updates untouched, but `AppData.PreviewThumbnail` is encrypted with the *content*
  key-header IV — so it would need decrypt+re-encrypt on **every** field update (content-IV rotation)
  across all write paths. Left out for now; clients render avatars via `pub/image` or the payload
  thumbnails. Add later with that re-encryption handled on each content write.
- **Header-content overflow (`shouldEmbedInHeader`).** Large contact JSON should spill to a content
  payload instead of `appData.content`; the server has the plaintext so it can decide this itself,
  but the client reader must also read content back from the payload. Use the same
  `MaxHeaderContentBytes` threshold as the client.
- **The `source` field** (`'contact' | 'public' | 'user'`). The client persists it; the server's
  `ContactContent` deliberately omits it, so it's dropped on write/merge. Decide: drop it
  client-side, or add it back to `ContactContent` and round-trip it.
