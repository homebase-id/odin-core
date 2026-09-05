# Chat send performance profile

What it costs odin-core to move one 1:1 text chat message from the sender's upload to the
recipient's drive, measured in-process on SQLite, with the fixes that measurement justified.

## Method

Benchmark: `tests/apps/Odin.Hosting.Tests.V2/Peer/ChatSendBenchmarkTests.cs` (`[Explicit]`).
Frodo uploads a chat-shaped encrypted file (fileType 7878, uniqueId, groupId, ~300 B content,
ACL connected, Sam as recipient, app-notification options) and the pipeline is driven in three
timed legs:

| leg | what runs |
|---|---|
| 1-upload | `POST /api/v2/drives/{driveId}/files`: multipart parse, metadata decrypt, validation, header commit, MediatR fan-out, transfer-history + outbox enqueue |
| 2-outbox | `PeerOutboxProcessorBackgroundService.DrainAsync`: checkout, header read, multipart build, in-process `POST capi.sam/…/transit/host/upload`, recipient inbox insert + push enqueue, transfer-history update |
| 3-inbox | Sam's owner inbox endpoint: pop, ICR read + key-header decrypt, header commit into the drive, MediatR fan-out |

Per leg: wall clock, `DatabaseCounters` deltas (statements, connections) and every `[…Timing]`
log line via the in-memory Serilog sink. 500 warm-up iterations are discarded because tiered JIT
needs several hundred iterations before the numbers stabilise (20 warm-ups measured ~25 ms
end-to-end, 500 warm-ups ~11 ms for the same code).

```bash
export ODIN_BENCH_WARMUP=500 ODIN_BENCH_ITERATIONS=500 ODIN_BENCH_OUT=/tmp/bench ODIN_BENCH_VARIANT=mine
dotnet test tests/apps/Odin.Hosting.Tests.V2 --filter FullyQualifiedName~Peer.ChatSendBenchmarkTests --logger "console;verbosity=normal"
```

CPU: `dotnet-trace collect --diagnostic-port <sock> --providers Microsoft-DotNETCore-SampleProfiler`
against the test host (a runsettings `DOTNET_DiagnosticPorts` env var scopes the port to the test
host; the sampler counts idle threads, so the speedscope export was aggregated after dropping
every stack that contains a wait frame).

### What the harness does not measure

The in-process seams (`TestPeerHttpClientFactory`, `TestPeerCapiAuthenticationHandler`, TestServer)
remove: TLS handshakes (`DynamicHttpClientFactory` rotates handlers every 2 min, so a chat message
after an idle gap pays DNS + TCP + TLS to `capi.<recipient>`), the CAPI validate callback the
recipient makes on a session-cache miss (`PeerCapiAuthenticationHandler.cs`, an extra HTTPS round
trip inside the inbound request), real WebSocket delivery, and the push gateway. Those are the
latency items a user actually notices; this profile is about what the server itself burns.

Two client-side items outside this repo dominate perceived latency and are listed for
completeness: chat-kmp coalesces every WebSocket notification behind a 200 ms delay
(`OdinWebSocketClient.kt`, `NOTIFICATION_BURST_MS`), and it answers `inboxItemReceived` with a
`processInbox` command round trip before the recipient server emits `fileAdded`.

## Baseline (SQLite, Debug log level = production default)

| leg | p50 ms | p95 ms | statements (nonQuery / reader) | connections |
|---|---:|---:|---:|---:|
| 1-upload | 3.93 | 5.40 | 2 / 9 | 5 |
| 2-outbox | 3.34 | 4.90 | 3 / 8 | 9 |
| 3-inbox | 3.11 | 4.81 | 3 / 9 | 7 |
| **end-to-end** | **10.90** | 13.18 | 34 | 21 |

Log level Information instead of Debug: end-to-end p50 10.18 ms (−7%).

Statement census (slow-query threshold temporarily zeroed, one iteration): 11 / 11 / 12
statements per leg, all parametrised, no per-row loops for a text message (tags empty, ACL by
security group). Round-trip count is not the problem for this scenario.

### Where the CPU goes (busy samples only, 30 s window, steady state)

| share | what | why it is there |
|---:|---|---|
| ~23% | Autofac resolution (`ResolveOperation.GetOrCreateInstance` 8.7% self, `BoundConstructor.Instantiate` 21% incl.) | per-request child scope + every MediatR publish resolving 7 handlers and their graphs |
| ~9% | Serilog logger construction (`SerilogLoggerFactory.CreateLogger` 4.5%, `SafeAggregateEnricher..ctor` array copy 4.4%) | `DriveStorageServiceBase` called `ILoggerFactory.CreateLogger` in its ctor and was built per dependency, twice (Standard + Comment) per `FileSystemResolver` |
| ~8% | MediatR `PublishNotification` (4 publishes per message: sender commit, transfer-history init, transfer-history update, recipient commit) | `CollectionRegistrationSource.RegistrationsFor` 5.9% — handler enumeration per publish |
| ~8% | SQLite statement preparation (`PrepareAndEnumerateStatements` 6.0% self, `sqlite3_prepare_v2` 1.4%) | a fresh `SqliteCommand` per statement, so nothing is ever reused |
| ~7% | `TransactionalCache` / FusionCache `GetOrSetAsync` | cache-hit overhead on `GetDriveAsync` (called ~8× per request), header reads, uniqueId lookup |
| ~3% | `DateTime.UtcNow` | scattered: `SequentialGuid`, `UnixTimeUtc.Now`, FusionCache expiry checks, `IdentityConnectionRegistration.set_Status` |
| ~1% | `OdinId` ctor SHA-256 per construction | every string→OdinId conversion hashes, including JSON reads |

Harness overhead visible in the same trace and excluded from the ranking: owner-login PBKDF2
(fixture setup), Refit `RestService.For` per call in the test clients (3.2%), the test-side
JSON content serializers rebuilding `JsonSerializerOptions` (5%). Production has the same
`RestService.For<T>` per peer send in `OdinHttpClientFactory.CreateClient` (0.8% here).

## Fixes tried

### 1. Build the drive file-system graph once per scope (committed)

`StandardFileSystem`, `CommentFileSystem`, their storage/query services and `FileSystemResolver`
were `InstancePerDependency`; `DriveStorageServiceBase` took `ILoggerFactory` and created a logger
per construction. Registered them `InstancePerLifetimeScope` (they hold only injected
dependencies) and inject `ILogger<DriveStorageServiceBase>`, which the MS `Logger<T>` singleton
caches.

| variant | 1-upload p50 | 2-outbox p50 | 3-inbox p50 | end-to-end p50 |
|---|---:|---:|---:|---:|
| baseline | 3.93 | 3.34 | 3.11 | 10.90 |
| fs-scope | 3.51 / 3.55 | 2.89 / 2.81 | 2.82 / 2.75 | **9.59 / 9.35** (−13%) |

Statement counts unchanged.

### 2. Reuse prepared SQLite commands per connection (committed)

`ScopedConnectionFactory.CommandWrapper` created a fresh `SqliteCommand` per statement, so every
execution re-ran `sqlite3_prepare_v2`. The wrapper now parks the command on the physical
connection (keyed by SQL text, `PreparedCommandCache`) instead of disposing it, and a later
command with the same text takes it over with parameters cleared. Commands are disposed together
with the connection when `DbConnectionPool` closes it. SQLite only; Npgsql prepares on its own.

| variant | 1-upload p50 | 2-outbox p50 | 3-inbox p50 | end-to-end p50 |
|---|---:|---:|---:|---:|
| fs-scope | 3.51 / 3.55 | 2.89 / 2.81 | 2.82 / 2.75 | 9.59 / 9.35 |
| fs-scope + cmd-cache | 3.30 / 3.18 | 2.67 / 2.63 | 2.65 / 2.59 | **8.89 / 8.72** (−19% vs baseline) |

Caveat: a caller that disposes its command while its reader is still open would leave the cached
`SqliteCommand` with a dangling reader reference and the next reuse would throw
"DataReader already open". Every call site follows `await using cmd` / `await using rdr` in that
order and the storage, V2 and V1 drive/peer suites pass, but that is the failure mode to look for.

### Bottom line

Steady-state, the server spends about **11 ms of CPU-bound work** on a 1:1 text message on SQLite
(sender request ≈ 4 ms, peer delivery ≈ 3.3 ms, recipient inbox ≈ 3.1 ms), and the two fixes cut
that to ≈ 8.8 ms. None of that is what a user waits on. The wall-clock a user sees is dominated by
the network legs the harness bypasses (TLS to `capi.<recipient>`, the CAPI validate callback), the
recipient client's 200 ms notification coalescing delay and its `processInbox` round trip, and,
for media, the synchronous disk→S3 copy in the upload request. Those are the levers for perceived
latency; the CPU fixes are throughput/host-load wins for multi-tenant servers.

## Not done, with the evidence

Text-message scenario never hits these; they are the payload-path findings from the code trace:

- `Host:FileWriteChunkSizeInBytes` defaults to **1024** (`OdinConfiguration.cs`) and is the buffer
  of the staging write loop in `FileReaderWriter.WriteStreamInternalAsync`. Config-only fix.
- `CommitNewFile` copies payloads and thumbnails to long-term storage **sequentially and inside
  the request** (`DriveStorageServiceBase.CopyPayloadsAndThumbnailsToLongTermStorage` →
  `S3FileStore.CopyFromAsync` → `TransferUtility.UploadAsync`). The client's "Finalizing" spinner
  is this copy. Parallelising the per-object copies is mechanical; acking before the S3 move is a
  durability decision.
- `AssertPayloadsExistOnFileSystemAsync` does an existence probe per payload and per thumbnail
  right after the copy (an S3 HEAD each) only to log; the throw is commented out.
- Per-recipient loop in `PeerOutgoingTransferService.SendFile`: `InitiateTransferHistoryAsync`
  re-publishes `DriveFileChangedNotification` (all 7 drive handlers) per recipient, plus its own
  transaction. Measured 0.7% CPU with one recipient; scales linearly with group size.
- `CircleNetworkService.GetIcrAsync` reads the ICR twice when `tryUpgradeEncryption` is left at
  its default (once per recipient on send, once per inbox item on receive).
- `PeerInboxProcessor` pops one item per query inside its batch loop and counts pending items
  twice per call.
- `PeerAppNotificationHandler` and `AppNotificationHandler` both handle `DriveFileAddedNotification`
  and both serialise the header; they feed different dispatchers, so verify both channels are
  consumed before calling it a duplicate.
- Serilog `MinimumLevel:Default` is `Debug` in `appsettings.json`; measured 7% end-to-end.
- PostgreSQL: Npgsql only auto-prepares when `Max Auto Prepare` is set in the connection string
  (default 0). The SQLite finding above suggests checking that in the Postgres deployments.
