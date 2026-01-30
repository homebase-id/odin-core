# Scalability Issues: Multi-Instance Deployment Analysis

## SQL Transactions: Mostly Safe

The transaction architecture is well-designed for multi-instance:

- **Reference-counted stacked transactions** via `ScopedConnectionFactory` provide clean nesting
- **Short-lived transactions** minimize lock hold times
- **Job scheduling** (`TableJobs.GetNextScheduledJobAsync`) uses `BEGIN IMMEDIATE` (SQLite) and `FOR UPDATE` (PostgreSQL) — properly serializes across instances
- **Inbox pop** (`TableInbox.PopSpecificBoxAsync`) uses a `popstamp` mechanism that correctly prevents double-processing
- **Drive index upserts** use version tag (`hdrVersionTag`) checks to prevent lost updates
- **Consistent ordering** in multi-table deletes (ACL → Tags → MainIndex) avoids deadlocks

No cross-table deadlock risks were identified. The SQL layer should handle multi-instance fine.

---

## Background Services Running on Every Instance (Critical)

Every instance will independently run all background services with no leader election or distributed coordination:

- **`JobRunnerBackgroundService`** — calls `GetNextScheduledJobAsync()` which does use DB-level locking, so it is *probably* safe, but multiple instances polling adds contention
- **`PeerOutboxProcessorBackgroundService`** — uses `popstamp` DB mechanism, likely safe but wasteful
- **`InboxOutboxReconciliationBackgroundService`** — `RecoverDeadAsync()` on all instances simultaneously could cause duplicate recovery
- **`UpdateCertificatesBackgroundService`** — all instances renewing certs could hit ACME rate limits
- **`TempFolderCleanUpBackgroundService`** — file system race conditions if instances share storage
- **`SecurityHealthCheckBackgroundScheduler`** — schedules duplicate jobs on startup

### Recommendation

Add leader election or use the existing job system to ensure only one instance runs periodic tasks. Alternatively, use a "run on all but be idempotent" strategy where the DB-level locking already handles it (jobs/outbox), and disable filesystem-dependent services on non-primary instances.

---

## In-Memory Caches Not Shared Across Instances (High)

- **`FileSystemIdentityRegistry`** (`Odin.Services/Registry/FileSystemIdentityRegistry.cs`) — `ConcurrentDictionary` + `Trie` of registrations is process-local. Registration changes won't propagate.
- **`CertificateStore`** (`Odin.Services/Certificate/CertificateStore.cs`) — `ConcurrentDictionary<string, X509Certificate2>` per instance
- **`LastSeenService`** (`Odin.Services/LastSeen/LastSeenService.cs`) — batches updates in local `ConcurrentDictionary` before periodic DB flush
- **`Level1Cache`** (`Odin.Core.Storage/Cache/Level1Cache.cs:24-25`) — explicitly skips distributed cache: `SkipDistributedCacheRead = true`, `SkipDistributedCacheWrite = true`
- **`GenericMemoryCache`** (`Odin.Core/Cache/GenericMemoryCache.cs`) — factory lock is process-local

### Recommendation

The FusionCache + Redis backplane is already set up but `Level1Cache` explicitly opts out. Consider enabling distributed cache reads/writes, or add a Redis pub/sub invalidation channel. The `FileSystemIdentityRegistry` cache/trie needs a distributed invalidation mechanism.

---

## Process-Local Locks Guarding Shared Resources (Critical)

- **`PublicPrivateKeyService`** (`Odin.Services/EncryptionKeyService/PublicPrivateKeyService.cs`) — `static SemaphoreSlim` for key creation. Two instances could create duplicate keys simultaneously.
- **`BackgroundServiceManager`** (`Odin.Services/Background/BackgroundServiceManager.cs`) — `AsyncReaderWriterLock` is process-local (Nito.AsyncEx)
- **`InProcPubSubBroker`** (`Odin.Core.Storage/PubSub/InProcPubSubBroker.cs`) — entirely in-memory channels. Messages won't cross instance boundaries.

### Recommendation

Replace `static SemaphoreSlim` in `PublicPrivateKeyService` with the existing `RedisLock` mechanism. The `InProcPubSubBroker` needs a Redis-backed alternative for cross-instance messaging.

---

## WebSocket/Device Notifications (High)

- **`SharedDeviceSocketCollection<>`** — registered as `SingleInstance` in DI (`Odin.Hosting/TenantServices.cs`), meaning each process has its own collection. Push notifications via WebSocket only reach devices connected to that specific instance.

### Recommendation

Add a Redis pub/sub backplane so notifications published on one instance reach WebSocket connections on all instances.

---

## Summary

| Category | Severity | Status |
|---|---|---|
| Request-scoped SQL transactions | Safe | No changes needed |
| Job claiming (`FOR UPDATE`) | Safe | No changes needed |
| Inbox pop (`popstamp`) | Safe | No changes needed |
| Drive index upserts (version tag) | Safe | No changes needed |
| Background services (duplicate execution) | Critical | Needs leader election or idempotency |
| Process-local locks on shared resources | Critical | Needs distributed locks |
| In-memory caches (no cross-instance invalidation) | High | Needs distributed cache or invalidation |
| WebSocket notifications (per-instance only) | High | Needs Redis pub/sub backplane |
| In-proc pub/sub broker | High | Needs Redis-backed alternative |
