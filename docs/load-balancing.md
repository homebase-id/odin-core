# Running odin-core on more than one node

Findings from a two-node cluster run on 2026-09-06: two `Odin.Hosting` processes against one
Postgres, one Redis (L2 cache + backplane + pub/sub + distributed lock) and one shared tenant
data root, serving the same identities on 8443 (node A) and 8444 (node B).

Most of the shared-state machinery already works. Two things break, one of them silently.

## Reproducing

```bash
docker compose -p homebase-dev -f docker/compose.dev.yml up -d      # postgres, redis, minio
# then two hosts with the env in the table below, ports 8080/8443/4444 and 8081/8444/4445
dotnet test tests/apps/Odin.Hosting.Tests --filter FullyQualifiedName~LoadBalancerProbeTests
```

Both nodes need, identically: `Database:Type=postgres` and one connection string,
`Redis:Enabled=true` with one `Redis:Configuration`, `Cache:Level2CacheType=Redis`, and the same
`Host:TenantDataRootPath` / `Host:SystemDataRootPath`. They differ only in
`Host:IPAddressListenList` ports and `Admin:ApiPort`.

`tests/apps/Odin.Hosting.Tests/LoadBalancer/LoadBalancerProbeTests.cs` is `[Explicit]` and asks
the cluster questions no single-process test can. Three of its four tests pass today; the fourth
is the registry bug below.

## What works

| property | evidence |
|---|---|
| Owner session issued by A is accepted by B | probe test; tokens live in the shared DB and `OdinContextCache` invalidates over Redis pub/sub |
| A drive created through A is listed by B | probe test; `TableDrives` L2 entries and backplane invalidation observed in Redis |
| A file uploaded through A is queryable through B | probe test |
| Cache invalidation genuinely crosses nodes | `FusionCache.Backplane:v2` messages and a per-tenant `cache_invalidation` channel observed on the wire |
| Certificates | stored in `TableCertificates` (shared DB), so SNI selection works on every node |
| Scheduled jobs are not double-run | jobs are claimed with a conditional `UPDATE` on the shared `jobs` table; in this run node A claimed all six startup jobs and node B ran none |
| WebSocket notifications are *designed* to cross nodes | `AppNotificationDispatcher` publishes and subscribes drive/client notifications through `ITenantPubSub`, which is Redis-backed when Redis is enabled. The transport was observed carrying tenant messages; end-to-end socket delivery from a second node was **not** verified here |

## What breaks

### 1. The identity registry is a per-process cache with no cross-node invalidation

Registrations live in the shared system database, but `FileSystemIdentityRegistry` reads them
**once at startup** into an in-memory trie. `ToggleDisabled` (and enable, delete,
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

The pieces to fix it are already present: the tenant pub/sub channel that carries cache
invalidation, or `INodeLock`/Redis. A registry-changed message that makes other nodes reload the
affected registration would close it.

### 2. The IP rate limiter is per-node, so the limit multiplies by node count

`AddIpRateLimiter` builds an in-process `PartitionedRateLimiter`. Nothing is shared, so each node
independently allows the configured rate.

Measured with `Host:IpRateLimitRequestsPerSecond=5` and 20 rapid requests from one client:

| target | allowed | limited |
|---|---:|---:|
| node A only | 5 | 15 |
| node B only | 5 | 15 |
| alternating A/B, as a balancer would spread one client | **10** | 10 |

Exactly double, and it scales with the cluster. This is the same limiter the PROXY-protocol work
restored the real client IP for; getting the address right does not help if the budget is per
node. A shared counter (Redis) is the fix, or accept and document that the real limit is
`configured × nodes`.

## Deployment constraints (not bugs, but they will bite)

- **Blob storage must be shared.** This run used one tenant data root for both nodes. With
  per-node local disks, a file uploaded through A is not readable through B. Either enable
  `S3Payload`/`S3Storage`, or put the tenant root on shared storage. Note that upload *staging* is
  always local disk by design, which is fine, but the long-term payload location must be shared.
- **`Host:SystemProcessApiKey` defaults to a fresh GUID per process** and is not in the
  ansible template, so each node would generate its own. `SystemAuthenticationHandler` validates
  inbound calls against it and `SystemHttpClient` sends it, so any cross-node system call would
  fail. `SystemHttpClient` currently has no callers, so this is latent rather than broken. Pin the
  value across the cluster before that changes.
- **Every node runs every background service** (43 each in this run), including the
  inbox/outbox reconciliation, orphan scan and temp-folder cleanup. Outbox and inbox are safe
  because items are checked out with a DB update, and no contention errors appeared in either
  node's log. The cleanup and scan services were not stress-tested for concurrent safety.
- **Peer-to-peer port**: `Host.DefaultHttpsPort` comes from the node's own listen list, and peer
  calls are addressed to `capi.<recipient>` on that port. Behind a balancer every node must
  present the same public port.
