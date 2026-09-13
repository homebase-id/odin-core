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

Re-measured on main at the time of merge (500 warm-up, 500 measured): baseline **10.77 ms**,
with the file-system scope fix **9.85 ms**, and with the unmerged command cache on top **8.78 ms**.

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

### 1. Build the drive file-system graph once per scope (merged)

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

### 2. Reuse prepared SQLite commands per connection (measured, deliberately NOT merged)

`ScopedConnectionFactory.CommandWrapper` creates a fresh `SqliteCommand` per statement, so every
execution re-runs `sqlite3_prepare_v2`, which the CPU sample put at ~8% of busy time. A prototype
parks the command on the physical connection keyed by SQL text and lets a later command with the
same text take it over, disposing cached commands with the connection. SQLite only; Npgsql prepares
on its own.

It works and it is the larger single win, but it is **held back on purpose**. It introduces an
invariant nothing enforces: a caller that disposes its command while its reader is still open
leaves the cached command holding a dangling reader, and the next reuse throws "DataReader already
open". Today no call site does that (readers never outlive their commands, and the registry's
`BumpMonotonicAsync` uses scalars only), and the full suites pass with it applied, but future code
has to keep obeying a rule no test states. Weighed against the bottom line below, a ~1 ms saving on
a path that is not what a user waits on does not justify that, so it waits for either a host that
is genuinely CPU-bound or a wrapper that refuses to cache a command whose reader is still open.

### Bottom line

Steady-state, the server spends about **11 ms of CPU-bound work** on a 1:1 text message on SQLite
(sender request ≈ 4 ms, peer delivery ≈ 3.3 ms, recipient inbox ≈ 3.1 ms). The merged fix takes
that to ≈ 9.9 ms, and the unmerged command cache would take it to ≈ 8.8 ms. None of that is what a
user waits on. The wall-clock a user sees is dominated by
the network legs the harness bypasses (TLS to `capi.<recipient>`, the CAPI validate callback), the
recipient client's 200 ms notification coalescing delay and its `processInbox` round trip, and,
for media, the synchronous disk→S3 copy in the upload request. Those are the levers for perceived
latency; the CPU fixes are throughput/host-load wins for multi-tenant servers.

## Over the real wire: Kestrel, certificates, loopback TLS

`tests/apps/Odin.Hosting.Tests/_Universal/Outbox/Performance/TlsChatSendBenchmarkTests.cs`
(`[Explicit]`) repeats the profile on `WebScaffold`: real Kestrel on 8443 with the dev
certificates, the production peer HTTP client (`OdinHttpClientFactory` →
`DynamicHttpClientFactory`) delivering to `capi.sam.dotyou.cloud` over loopback HTTPS, the
production `PeerCapiAuthenticationHandler`, and the outbox background service running on its own
(the test polls the outbox every 1 ms instead of the 100 ms helper). An in-process
`EventListener` on the `System.Net.*` event sources counts DNS lookups, connections and TLS
handshakes per message and times the handshakes. Two idle probes send one message after 70 s and
after 130 s of silence.

```bash
export ODIN_BENCH_WARMUP=200 ODIN_BENCH_ITERATIONS=300 ODIN_BENCH_IDLE_SECONDS=70,130 ODIN_BENCH_OUT=/tmp/bench
dotnet test tests/apps/Odin.Hosting.Tests --filter FullyQualifiedName~TlsChatSendBenchmarkTests --logger "console;verbosity=normal"
```

Steady state (messages back to back), 300 measured iterations:

| stage | p50 ms | p95 ms |
|---|---:|---:|
| 1-upload | 6.69 | 10.34 |
| 2-delivery (upload return → outbox empty) | 8.59 | 12.12 |
| 3-inbox | 6.14 | 9.45 |
| **end-to-end** | **22.57** | 35.32 |

Per message: 1.00 peer request, 0 DNS lookups, 0 new connections, 0 TLS handshakes, 0 CAPI
validate callbacks. Under load the connection pool is reused exactly as intended, and the
2-minute handler lifetime never bit inside the 15 s loop.

After a pause it looks different:

| probe | upload ms | delivery ms | inbox ms | DNS | new connection | TLS handshake | handshake ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| after 70 s idle | 67.0 | **206.6** | 10.2 | 1 | 1 | 1 | 6.7 |
| after 130 s idle | 37.3 | **83.9** | 9.9 | 1 | 1 | 1 | 5.8 |

The event timeline of the 130 s probe: `ResolutionStart capi.sam.dotyou.cloud` at 0.2 ms,
`ResolutionStop` at 74.1 ms, connect + handshake done at 80.0 ms, response at 81.3 ms. The
70 s probe reached `HandshakeStop` at 198 ms with a 6.7 ms handshake, so roughly 190 ms of it
was name resolution. `ConnectionClosed` events for the pooled connections appear ~61 s after
the last message: that is `SocketsHttpHandler.PooledConnectionIdleTimeout` (default 1 minute)
closing them, well before the 2-minute handler lifetime.

Why the lookup is slow here: `capi.*` names are not in this machine's hosts file; they resolve
through real DNS as a CNAME chain (`capi.sam` → `sam` → `localhost`, TTL 59 s), 62 ms per
uncached query on this resolver. The size of that number is environment-specific, but the
mechanism is not: with the idle timeout at 60 s and the DNS TTL at 59 s, every message after a
minute of silence does an uncached lookup, a TCP connect and a full TLS handshake before the
recipient sees a byte. On a real network that is one DNS round trip plus three network round
trips for TCP + TLS 1.3, tens to a few hundred milliseconds depending on distance, on the one
message a person is actually waiting for.

What is amiss in this part of the code, as verified above:

- **Idle timeout is the .NET default, 1 minute, and the handler lifetime is absolute.**
  `DynamicHttpClientFactory` builds an `HttpClientHandler`, which does not expose
  `PooledConnectionIdleTimeout`; `HandlerEntry.IsExpired` is creation time + 2 min regardless
  of use. Switching to `SocketsHttpHandler` allows a long idle timeout (10–15 min suits chat
  cadence) with `PooledConnectionLifetime` covering DNS rotation, and the handler lifetime can
  then be long or removed. The receiving Kestrel has no `KeepAliveTimeout` configured in this
  repo, so its default of 130 s (ASP.NET documentation, not verified in code) would close the
  connection first; both ends are odin-core, so both can be raised together.
- **No client-side DNS cache.** .NET resolves per new connection through the OS; keeping the
  connection alive is the fix, a resolver cache is the fallback.
- **Dead mTLS remnant.** `ServerCertificateSelector` in `Program.cs` sets
  `ClientCertificateRequired = true` for every `capi.*` host name, so the server asks the peer for
  a client certificate on every handshake, but `OdinHttpClientFactory` never sends one (auth is
  the `X-CAPI-Session-Id` header). Harmless to the handshake result, but it is a leftover.
- **CAPI validate callback cadence.** The sender rotates its session id every 10 min
  (`Host:CapiSessionLifetimeMinutes`), so the recipient's 20-min cache is refreshed by a fresh
  callback roughly every 10 min per sender-recipient pair, not every 20. Out of scope here as
  agreed, measured at 0 per message in the loop.

Server-side cost per handshake is fine: the certificate selector resolves the tenant from the
in-memory registry and the certificate from `CertificateStore`'s in-memory cache; the peer
handshakes measured 5.8–6.7 ms on loopback.

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
