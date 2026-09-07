# Running odin-core on more than one node

Findings from a two-node cluster run on 2026-09-06: two `Odin.Hosting` processes against one
Postgres and one Redis (L2 cache + backplane + pub/sub + distributed lock), serving the same
identities on 8443 (node A) and 8444 (node B).

Two topologies were run: first with a shared tenant data root, then the realistic one, with a
**separate local disk per node** and payloads in S3 (local MinIO). Both behave the same.

Most of the shared-state machinery already works. One thing actually broke, the identity registry,
and it is **fixed** as of the change described in `load-balancing-registry-fix-plan.md`; the section
below is kept because it explains what the fix has to keep working. Everything else here is a
constraint to configure correctly, not a defect.

## Reproducing

```bash
docker compose -p homebase-dev -f docker/compose.dev.yml up -d      # postgres, redis, minio
# then two hosts with the env in the table below, ports 8080/8443/4444 and 8081/8444/4445
dotnet test tests/apps/Odin.Hosting.Tests --filter FullyQualifiedName~LoadBalancerProbeTests
```

Both nodes need, identically: `Database:Type=postgres` and one connection string,
`Redis:Enabled=true` with one `Redis:Configuration`, and `Cache:Level2CacheType=Redis`. For the
realistic topology also set `S3Storage:Enabled=true` (MinIO: `ServiceUrl=http://localhost:9000`,
`minioadmin`/`minioadmin`, `ForcePathStyle=true`) and `S3Payload:Enabled=true` with a
`BucketName`; the host creates the bucket at startup. `Host:TenantDataRootPath` /
`SystemDataRootPath` are then per node. Nodes otherwise differ only in
`Host:IPAddressListenList` ports and `Admin:ApiPort`.

`tests/apps/Odin.Hosting.Tests/LoadBalancer/LoadBalancerProbeTests.cs` is `[Explicit]` and asks
the cluster questions no single-process test can. Three of its four tests pass today; the fourth
is the registry bug below.

## What works

| property | evidence |
|---|---|
| Owner session issued by A is accepted by B | probe test; tokens live in the shared DB and `OdinContextCache` invalidates over Redis pub/sub |
| A drive created through A is listed by B | probe test; `TableDrives` L2 entries and backplane invalidation observed in Redis |
| A file uploaded through A is queryable through B | probe test (header; it lives in the shared DB) |
| A **payload** uploaded through A downloads byte-identical from B | probe test, with per-node local disks and S3 payloads: the object was written under `odin-payloads/payloads/<tenant>/drives/...` in MinIO and neither node's local disk held a `.payload` file |
| Cache invalidation genuinely crosses nodes | `FusionCache.Backplane:v2` messages and a per-tenant `cache_invalidation` channel observed on the wire |
| Certificates | stored in `TableCertificates` (shared DB), so SNI selection works on every node |
| Scheduled jobs are not double-run | jobs are claimed with a conditional `UPDATE` on the shared `jobs` table; in this run node A claimed all six startup jobs and node B ran none |
| WebSocket notifications are *designed* to cross nodes | `AppNotificationDispatcher` publishes and subscribes drive/client notifications through `ITenantPubSub`, which is Redis-backed when Redis is enabled. The transport was observed carrying tenant messages; end-to-end socket delivery from a second node was **not** verified here |

## What broke, and how it is fixed

### The identity registry was a per-process cache with no cross-node invalidation

Registrations live in the shared system database, but `FileSystemIdentityRegistry` used to read
them **once at startup** into an in-memory trie and never again. `ToggleDisabled` (and enable, delete,
public-web-presence, new registrations) mutates that node's trie and persists to the DB, and no
other node is told.

Observed: disabling `frodo.dotyou.cloud` through node A's admin API returned 409 for requests to
node A, while node B kept answering 200 for the same tenant. Each node's own admin API disagreed
about the same tenant, node A reporting `enabled: false` and node B `enabled: true`.

Consequences behind a balancer, with N nodes:

- **Disabling a tenant does not take effect.** It is an abuse/incident kill switch and an
  export/migration safety step, and it only stops traffic on the one node that received the call.
- **A newly provisioned tenant is served by only that node**; requests routed elsewhere 404 until
  every other node restarts.
- **Deleting a tenant** leaves the other nodes serving it from cache.

**Fixed** with one mechanism and one event handler, and no timer. A registry version lives in
the system `Settings` row `registry-version`, bumped in the **same transaction** as every
registration write (the settings upsert sets `modified = MAX(modified+1, now)` in one statement,
so concurrent bumps serialise on the row lock and the value is strictly monotonic). After commit,
the node announces the new version over `ISystemPubSub`. A node that hears a version above its
own reconciles from the database; anything at or below is dropped, which makes duplicate and
out-of-order delivery harmless. Because pub/sub has no replay, the only way to miss an
announcement is to be disconnected, so each node re-reads the version once on startup and
whenever its Redis connection is restored (`IConnectionMultiplexer.ConnectionRestored`), and
reconciles if behind. The one accepted gap: a node that commits and then fails to announce logs
an **error** (after retries), and other nodes learn on the next registry change anywhere, a
reconnect, or a restart. Verified two ways: `TenantDisabledOnNodeA_IsAlsoBlockedOnNodeB` passes,
and `RegistryChangeMissedWhileRedisWasDown_ConvergesOnReconnect` stops Redis, changes the
registration with nothing able to announce it, confirms both nodes are stale, starts Redis and
asserts both converge.

## Notes and constraints (not breaks)

- **The IP rate limiter is per node, so the effective limit is `configured x nodes`.**
  `AddIpRateLimiter` builds an in-process `PartitionedRateLimiter` with nothing shared. Measured
  with `IpRateLimitRequestsPerSecond=5` and 20 rapid requests from one client: 5 allowed against
  one node, but 10 allowed when alternating across both, exactly double. This degrades a defence
  rather than breaking the system; either divide the configured value by the node count, or move
  the counter to Redis if a true cluster-wide limit is wanted.
- **Blob storage must be shared, and S3 satisfies that.** Verified: with a separate local disk per
  node and `S3Payload` enabled, a payload uploaded through A downloaded byte-identical from B, and
  no `.payload` file appeared on either local disk. Without S3 the tenant root must be on shared
  storage instead. Upload *staging* is always local disk by design and is per-request, so it needs
  nothing shared.
- **`Host:SystemProcessApiKey` defaults to a fresh GUID per process** and is not in the ansible
  template, so each node would generate its own. `SystemAuthenticationHandler` validates inbound
  calls against it and `SystemHttpClient` sends it. Its one caller is the registry's certificate
  status check (`FileSystemIdentityRegistry.InitializeCertificate`), which calls the tenant's own
  host and so may land on a different node behind a balancer and be rejected. Pin the value
  across the cluster.
- **Every node runs every background service** (43 each in this run), including the
  inbox/outbox reconciliation, orphan scan and temp-folder cleanup. Outbox and inbox are safe
  because items are checked out with a DB update, and no contention errors appeared in either
  node's log. The cleanup and scan services were not stress-tested for concurrent safety.
- **Peer-to-peer port**: `Host.DefaultHttpsPort` comes from the node's own listen list, and peer
  calls are addressed to `capi.<recipient>` on that port. Behind a balancer every node must
  present the same public port.
