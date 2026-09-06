# Plan: make the identity registry coherent across nodes

Fixes the one real break found in the two-node run (see `load-balancing.md`): registrations are
shared in Postgres but cached per process, so a registry change on one node is invisible to every
other node until it restarts. Disabling a tenant on node A left node B serving it, and the two
nodes' admin APIs disagreed about the same tenant.

## Where the state actually lives

`FileSystemIdentityRegistry` is a system-scoped singleton holding two in-memory structures:

- `_trie` — a `Trie<IdentityRegistration>` used by `ResolveIdentityRegistration(domain)` for
  hierarchical host lookup, so `capi.frodo.dotyou.cloud` resolves to frodo's registration.
- `_cache` — `ConcurrentDictionary<Guid, IdentityRegistration>` backing `GetList` / `GetTenants`.

Both are filled once by `LoadRegistrations()` at startup from `systemDatabase.Registrations`.
Every mutation funnels through exactly two methods, which is what makes this tractable:

| path | callers |
|---|---|
| `SaveRegistrationInternal` (upsert) | `AddRegistration`, `MarkRegistrationComplete`, `ToggleDisabled`, `SetPublicWebPresenceAsync`, `MarkForDeletionAsync`, `UnmarkForDeletionAsync` |
| `DeleteRegistration` | admin delete |

Both already write to Postgres. Neither tells any other node.

## Two designs that look right and are not

**Read from Postgres per request.** `ResolveIdentityRegistration` runs on every HTTP request and
inside the Kestrel SNI callback on every TLS handshake. A database round trip there is not
affordable, and the trie's hierarchical lookup is not a key-value get. The in-memory structure has
to stay; the question is only how it gets invalidated.

**Redis pub/sub alone.** `IPubSub` is documented as *fire-and-forget, at-most-once*. A node that is
starting up, briefly disconnected, or paused when the message goes out misses it and stays stale
**forever**, because nothing ever re-checks. That is the current bug with a smaller window, and the
failure is silent. The same objection applies to putting registrations in the FusionCache L2: its
backplane is also at-most-once, and a missed invalidation is only repaired when the entry's TTL
expires, which is a mechanism we would then depend on anyway.

## The design: Postgres is truth, Redis is speed, a sweep is the guarantee

Three parts, each with one job:

1. **Postgres remains the single source of truth.** Already the case. Every correctness claim is
   anchored here, never in a message.
2. **Redis pub/sub (`ISystemPubSub`) carries a change *signal*** so propagation is near-instant in
   the normal case. It is an optimisation, and the system is correct without it.
3. **A periodic reconciliation sweep against Postgres** bounds staleness to one interval no matter
   what pub/sub did. This is what turns "usually fast" into "eventually correct", and it is the
   part that makes the fix safe to rely on for a kill switch.

**The design decision that makes ordering irrelevant: a message carries an identity id and a change
kind, never the new state.** The receiver always re-reads that row from Postgres. Two concurrent
edits, out-of-order delivery and duplicate delivery then all converge on current database truth,
and no node can overwrite newer state with older state from a stale message.

## Work breakdown

Each phase is shippable on its own. Phase 2 is the one that actually closes the hole; phase 1
without phase 2 is an improvement that still fails silently.

### Phase 1 — broadcast changes (latency)

- Add `RegistryChangeMessage { Guid IdentityId, string PrimaryDomain, RegistryChangeKind Kind, Guid OriginNodeId }`
  with `Kind ∈ { Upserted, Deleted }`, on a system channel such as `registry-changed`.
- Publish from the two funnels: at the end of `SaveRegistrationInternal` (`Upserted`) and
  `DeleteRegistration` (`Deleted`), after the database write commits, never before.
- Subscribe in `FileSystemIdentityRegistry`, and subscribe **before** `LoadRegistrations()` so the
  startup window is narrow; the handler is idempotent so an early message is harmless.
- Handler:
  - `Upserted` → `TableRegistrations.GetAsync(identityId)` (single indexed read, the CRUD method
    already exists) and re-apply to `_trie` + `_cache`.
  - `Deleted` → `_trie.RemoveDomain(domain)` then `UnloadRegistration(registration)`, which removes
    it from `_cache`, stops that tenant's background services and drops its DI scope.
  - Ignore messages whose `OriginNodeId` is this node.
- **Do not** reuse `DeleteRegistration` on the receiving side: it also deletes the registration
  directory and payloads, which the originating node has already done. Remote nodes must only
  forget the tenant locally. This is the sharpest correctness trap in the change.
- `CacheIdentityAsync` runs `TenantConfigService.InitializeAsync()` and carries an explicit warning
  about not opening database transactions on the caller's scope. The subscriber runs on a pub/sub
  callback thread with no ambient scope, so it must create its own lifetime scope, exactly as
  `SaveRegistrationInternal` does.

### Phase 2 — reconciliation sweep (correctness)

- New `RegistryReconciliationBackgroundService` next to the other system services, registered and
  started in `BackgroundServiceExtensions` beside `UpdateCertificatesBackgroundService`.
- Each pass: `Registrations.GetAllAsync()`, then diff against `_cache` and apply
  - rows absent from `_cache` → add,
  - rows whose `modified` is newer than the cached copy → replace,
  - cached entries absent from the result → unload.
- Confirm `UpsertAsync` actually bumps `modified` on update; the column exists on
  `RegistrationsRecord`, but the sweep's update detection depends on it, and if it is not bumped the
  sweep silently degrades to add/remove only. If it is not, either bump it in SQL or compare a hash
  of the mapped fields.
- Interval via `BackgroundServices:RegistryReconciliationIntervalSeconds`, defaulting to something
  short enough for an operational kill switch, on the order of 30–60s. The query returns one row
  per tenant on the host, so this is cheap; it is a list the process already holds in full.
- Log at info when a pass actually changes something, and at warning when it repairs a change that
  pub/sub should have delivered. That second log is the signal that Redis delivery is unhealthy.

### Phase 3 — prove it

- `LoadBalancerProbeTests.TenantDisabledOnNodeA_IsAlsoBlockedOnNodeB` already exists and fails
  today; it should pass on phase 1.
- Add a probe that proves phase 2 independently of Redis: stop the Redis container (or point one
  node at a dead Redis), change a registration on node A, and assert node B converges within the
  sweep interval. Without this the sweep is untested and will rot.
- Add a probe for a **new** registration appearing on the other node, and for delete, since those
  are different code paths from disable and are the ones that 404 or serve a deleted tenant.

## Risks and edges

- **Deletion is destructive and now partly remote-triggered.** The receiving handler must be
  incapable of touching the filesystem or payload store. Worth a unit test that asserts the remote
  path does not call the file-deleting methods.
- **Sweep versus in-flight mutation.** A sweep that reads mid-mutation may briefly apply older
  state; the next sweep or the pub/sub message corrects it. Because messages carry no state, this
  converges rather than flapping.
- **Startup ordering.** `LoadRegistrations` creates the tenant scopes; the subscriber must tolerate
  a message for an identity it has not loaded yet, by treating `Upserted` as an upsert rather than
  an update.
- **In-process mode still works.** `ISystemPubSub` resolves to the in-process broker when Redis is
  disabled, so a single-node deployment keeps working unchanged and the sweep becomes a no-op that
  finds nothing.

## What this does not fix

The per-node rate limiter, which is a note in `load-balancing.md` rather than a break: the limit is
per node, so the effective budget is `configured × nodes`. Unrelated mechanism, separate decision.
