# Live Relay — Generic Ephemeral Group Streaming

## Context

We want a **generic, app-agnostic primitive** for sharing *live* ephemeral data among a group of
already-connected identities, each running their own server. The motivating use case is 10 friends
on vacation sharing live GPS for a week, but the server must treat the carried bytes as opaque (it
happens to be GPS; it could be anything).

> **Naming:** we avoid "payload"/"data" for the carried bytes — those collide with Homebase **file
> payloads**. The opaque field is **`Blob`** throughout; types use a `LiveRelay` prefix.

Design principles settled in discussion:
- **Decentralized mesh, no relay/master.** Each participant is the authoritative publisher of their
  own stream; the group is just a recipient list the sender holds. No leader election, no single
  point of failure. (A future WebRTC *calling* feature will use a participant-relay — a deliberately
  different data plane under the same session idea — out of scope here.)
- **Ephemeral, last-value-wins state — not durable events.** No drive writes, no outbox, no
  notification DB. "If you're offline, the data is just gone." A missed packet is harmless because
  the next one supersedes it.
- **Reuse the existing ephemeral publish lane.** `ITenantPubSub.PublishAsync` →
  `AppNotificationDispatcher` → websocket already delivers events to clients with zero DB writes
  (12 of 13 current notification types use it). We surface our opaque blob via a new
  `IClientNotification` variant — same pattern as `NewFollowerNotification`.
- **Multi-instance correct.** A tenant may be served by several server instances. The **live** path
  is already cross-instance: `ITenantPubSub` uses Redis PUBLISH/SUBSCRIBE (`RedisPubSub`), so a blob
  published on instance A reaches subscribed dispatchers on B/C and their local sockets. The only
  cross-instance state we add — the **retained last-value store** — rides the transparent layer-2
  cache (`ITenantLevel2Cache`: in-memory L1 + Redis L2, auto memory-only when Redis is off), as a
  single per-app snapshot the server reads whole to auto-flush every sender's last point to a
  newly-connecting client.

### Three transport hops (all decided)

1. **App → its own server** — **HTTP POST**. Chosen for **background reliability**: a week-long
   share runs mostly backgrounded, where the OS tears down persistent sockets; discrete HTTP uploads
   map onto iOS background `URLSession` / Android foreground-service/`WorkManager`. chat-kmp's
   `LocationTrackUploaderService` already does background HTTP upload — mirror it.
2. **Sender server → each recipient server** — **ephemeral fire-and-forget HTTP POST**, no outbox,
   drop on failure. Mutual-TLS peer auth.
3. **Recipient server → its connected clients** — **existing websocket publish lane**, with a new
   per-AppId socket filter.

### Authorization & routing (layers)

- **Connection trust (sender identity):** the recipient server knows the sending server's identity
  for certain from the mutual-TLS cert (`GetCallerOdinIdOrFail`). It accepts a blob only if that
  sender is a **Connected** identity (`CircleNetworkService`). No server-side per-channel consent.
- **App isolation (server-enforced):** the **AppId is inferred from the authenticated caller** on
  hop 1 (the app cannot self-declare/spoof it), carried in the hop-2 envelope, and used on hop 3 to
  deliver **only to sockets of apps with the matching AppId** — a chat GPS blob can never reach a
  different Homebase app (e.g. a health app).
- **Session routing (client):** the client filters by `channelKey` to route to the correct *open
  share session*. Server enforces *which app*; client picks *which session*.
- **Forwarded to the client with every blob:** the **sending server's OdinId** and the
  **server-side received timestamp** (`ReceivedAt`, stamped at receipt) — so the client can show
  freshness/staleness, especially for a blob flushed on connect.

**AppId is free to obtain:** the app-token auth path (`AppRegistrationService.GetAppPermissionContextAsync`
→ `ValidateClientAuthTokenAsync`, `:278–302`) already loads the `AppRegistration`/`AppClientRegistration`
(`AppClientRegistration.AppId` / `CategoryId`). It's just not copied onto the context — surfacing it
adds **no extra DB read**.

---

## Backend changes (odin-core)

### New types

| Type | Path | Role |
|---|---|---|
| `AppLiveRelayController` | `Controllers/ClientToken/App/Notifications/AppLiveRelayController.cs` | HOP 1 ingress. `[AuthorizeValidAppToken]`, `[HttpPost("relay")]`. Mirror `AppPeerNotificationController`. |
| `LiveRelayRequest` | `src/services/Odin.Services/LiveRelay/` | DTO: `Guid ChannelKey`, `List<string> Recipients`, `string Blob` (opaque base64). **No AppId** — inferred server-side. |
| `LiveRelayService` | `src/services/Odin.Services/LiveRelay/LiveRelayService.cs` | Sender fan-out. Extends `PeerServiceBase`; inject `ILifetimeScope`. Asserts `UseTransitWrite`; reads AppId from caller context. |
| `ILiveRelayHttpClient` | `src/services/Odin.Services/LiveRelay/ILiveRelayHttpClient.cs` | Refit client for HOP 2. Mirror `IPeerAppNotificationHttpClient` in `IPeerReactionHttpClient.cs`. |
| `LiveRelayPeerEnvelope` | `src/services/Odin.Services/LiveRelay/` | Wire DTO: `ChannelKey`, `Blob`, `AppId`. **No sender identity** (from cert). |
| `PeerLiveRelayController` | `Controllers/PeerIncoming/LiveRelay/PeerLiveRelayController.cs` | HOP 2 ingress. `[Authorize(Policy = IsInOdinNetwork, AuthenticationSchemes = TransitCapiAuthScheme)]`. Mirror `PeerAppNotificationsPreAuthController`. |
| `PeerLiveRelayReceiverService` | `src/services/Odin.Services/LiveRelay/PeerLiveRelayReceiverService.cs` | Recipient: connected-check → stamp `ReceivedAt` → retained store → publish. |
| `LiveRelayRetainedStore` | `src/services/Odin.Services/LiveRelay/LiveRelayRetainedStore.cs` | **Per-tenant singleton** over **`ITenantLevel2Cache`** (transparent L1 + Redis L2; memory-only when Redis off). `Put(...)` RMWs a per-app snapshot under a per-app `SemaphoreSlim`; `GetAllForApp(appId)` reads it for auto-flush. |
| `LiveRelayAppSnapshot` | `src/services/Odin.Services/LiveRelay/` | **Named record** cached once per app: `Dictionary<string, LiveRelayRetainedEntry> Entries` keyed `"{channelKey}:{senderDomain}"`. Enumerable in one read. |
| `LiveRelayRetainedEntry` | `src/services/Odin.Services/LiveRelay/` | **Named record** (STJ-serializable): `string Blob`, `Guid AppId`, `Guid ChannelKey`, `string SenderDomain`, `long ReceivedAtMs`. |
| `LiveRelayNotification` | `src/services/Odin.Services/AppNotifications/ClientNotifications/LiveRelayNotification.cs` | New `IClientNotification` + `IAppTargetedClientNotification`. Carries `SenderOdinId`, `ChannelKey`, `Blob`, `ReceivedAt`, `TargetAppId`. |
| `IAppTargetedClientNotification` | `src/services/Odin.Services/AppNotifications/ClientNotifications/` | Marker: `IClientNotification` + `Guid TargetAppId { get; }`. Lets the dispatcher filter sockets by app. |

### Surfacing AppId onto the context + socket

- **`OdinClientContext.cs`** — add `public GuidId? AppId { get; set; }`; ensure `Clone()` copies it.
- **`AppRegistrationService.GetAppPermissionContextAsync` (~`:253–267`)** — set `AppId = appReg.AppId`
  on the `OdinClientContext` it builds (value already in scope; no extra lookup).
- **`DeviceSocket.cs`** — add `public Guid? AppId { get; set; }`.
- **`AppNotificationHandler.cs`** `EstablishConnectionRequest` handler (~`:241–279`) — after
  `deviceSocket.DeviceOdinContext = odinContext.Clone()`, set
  `deviceSocket.AppId = odinContext.Caller.OdinClientContext?.AppId`.

### App-targeted delivery in the dispatcher

- **`AppNotificationDispatcher.cs`** — in `PublishClientNotificationAsync`, if the notification is
  `IAppTargetedClientNotification`, stamp `ClientNotificationMessage.TargetAppId` (new nullable
  `Guid?`, default null = broadcast as today). In `WsPublishAsync(ClientNotificationMessage)`
  (~`:145–155`), when `TargetAppId` is set, filter `sockets.Where(ds => ds.AppId == targetAppId)`
  (mirrors the drive filter at `:183–185`); null → unchanged broadcast.
- Add `SendRetainedToSocketAsync(DeviceSocket, IEnumerable<LiveRelayNotification>)` that serializes
  like `PublishClientNotificationAsync` and calls `SendMessageAsync(socket, json, encrypt: true)`
  directly to one socket (bypassing pub/sub, for flush-on-connect). `LiveRelayNotification` must
  **not** be in the `shouldEncrypt` exclusion list.

### Retained store — single `ITenantLevel2Cache` impl, per-app snapshot

The store must be **enumerable** so the server can auto-flush on reconnect without the client naming
anyone. `ITenantLevel2Cache` (FusionCache: in-memory L1 + Redis L2, **transparently memory-only when
Redis is off**) handles single- vs multi-server for us — **one implementation, no InProc/Redis
split**. It's pure key→value and can't list keys, so we pack all of an app's senders into **one
snapshot value** that we can read whole.

- **`LiveRelayRetainedStore`** — **per-tenant singleton**, injects `ITenantLevel2Cache` and holds a
  `ConcurrentDictionary<Guid appId, SemaphoreSlim>` of async locks:
  - **Key:** one entry per app, `liverelay:{appId}` → `LiveRelayAppSnapshot` whose
    `Entries["{channelKey}:{senderDomain}"]` holds each sender's last `LiveRelayRetainedEntry`.
  - **`Put`:** under the app's `SemaphoreSlim` — `GetOrDefaultAsync` snapshot → set this sender's slot
    → `SetAsync(key, snapshot, TTL ~5 min)`. Last-value-wins; TTL auto-evicts (no leak, no timer). L2
    enforces ≥2s TTL — fine. (A `SemaphoreSlim`, not `lock {}`, because the RMW spans `await`s.)
  - **`GetAllForApp(appId)`:** one read → all senders; **filter stale by `ReceivedAtMs`** before use.
  - Memory bounded by (channels × senders) per app — tiny for a friend group.
- **Concurrency — resolved:**
  - *Same instance* (incl. single-server / Redis-off): the per-app async lock fully serializes the
    RMW → **no clobber**. This is the common deployment.
  - *Across instances:* the FusionCache **Redis backplane is already enabled** whenever L2 is Redis
    (`FusionCacheWrapperExtensions.cs:83–90`) — it invalidates each instance's L1 on write, so reads
    aren't systematically stale. The only residual is a rare two-instance concurrent-write window,
    which is self-healing (the clobbered sender reappears next tick). Acceptable for ephemeral
    last-value data; no distributed lock or Redis-hash needed.

### Flush-on-connect (hydration) — fully automatic, no client input

- In `AppNotificationHandler`’s `EstablishConnectionRequest` handler, after `DeviceOdinContext`/
  `AppId` are set and the handshake reply is sent: call `store.GetAllForApp(deviceSocket.AppId)`,
  build one `LiveRelayNotification` per returned entry (preserving original `ReceivedAt`), and
  `dispatcher.SendRetainedToSocketAsync(...)` to this single socket. The reconnecting/foregrounding
  client immediately receives every sender's last data point and filters by its open `channelKey`s
  client-side. **No `EstablishConnectionOptions` change, no client-supplied identity/channel lists.**

### Other edits

- **`ClientNotificationType.cs`** — add `LiveRelay = 6001`.
- **`TenantServices.cs`**:
  - Add `.As<INotificationHandler<LiveRelayNotification>>()` to the `AppNotificationHandler`
    registration (~`:149–164`). **Most error-prone step — miss it and the notification publishes but
    silently never reaches the websocket.**
  - Register `LiveRelayService`, `PeerLiveRelayReceiverService` as `InstancePerLifetimeScope()`
    (alongside `PeerAppNotificationService`, ~`:373`). Register `LiveRelayRetainedStore` as a
    per-tenant **`SingleInstance()`** (it holds the per-app async locks); it injects
    `ITenantLevel2Cache` (already registered via `AddTenantCaches`, `:395`) — no new DI extension.
- **`PeerApiPathConstants.cs`** — add `LiveRelayV1`.

### End-to-end flow (one packet)

1. App POSTs `/api/apps/v1/notify/liverelay/relay` (app token) → `AppLiveRelayController` →
   `LiveRelayService.RelayAsync`. Assert `UseTransitWrite`; **read `appId` from
   `ctx.Caller.OdinClientContext.AppId`**; validate channel + recipients.
2. **Fan-out:** per recipient,
   `OdinHttpClientFactory.CreateClientAsync<ILiveRelayHttpClient>(recipient).Relay({ ChannelKey, Blob, AppId })`
   (pattern: `SendPeerPushNotificationOutboxWorker.cs:65`). Try/catch per recipient: **log + drop**.
   No outbox.
3. Recipient `PeerLiveRelayController` (cert auth) → sender = `WebOdinContext.GetCallerOdinIdOrFail()`
   → `PeerLiveRelayReceiverService.ReceiveAsync`.
4. **Auth:** `GetIcrAsync(caller, ctx, overrideHack: true).IsConnected()` (pattern:
   `PeerAppNotificationService.cs:62`). Reject if not connected.
5. **Stamp + store:** `receivedAt = UnixTimeUtc.Now()`;
   `store.Put(appId, channelKey, sender, entry)` → RMW the per-app `LiveRelayAppSnapshot` (overwrite
   this sender's slot; last data point per sender) and `SetAsync` with ~5 min TTL.
6. **Publish:** `mediator.Publish(new LiveRelayNotification { SenderOdinId = caller, ChannelKey,
   Blob, ReceivedAt = receivedAt, TargetAppId = appId })` → `AppNotificationHandler.Handle` →
   `PublishClientNotificationAsync` (stamps `TargetAppId`) → `ITenantPubSub.PublishAsync` (Redis,
   cross-instance) → each instance’s `WsPublishAsync` → **only sockets with `AppId == appId`**,
   encrypted per-socket.
7. Client receives `ClientNotificationPayload`, decrypts, reads `{ senderOdinId, channelKey, blob,
   receivedAt }`, **filters by `channelKey`**, renders. Zero matching sockets = no-op (entry still in
   the store for TTL, flushed on next connect).

### Parallelism / scoping

- Fan-out: **parallel with child scopes** (avoids head-of-line blocking on an unreachable peer).
  Pattern: `CircleNetworkIntroductionService.ProbeRecipientAsync` (`:249–271`) +
  `PeerOutboxProcessorBackgroundService.ProcessItemThread` (`:119`). Per recipient:
  `await using var childScope = _lifetimeScope.BeginLifetimeScope($"LiveRelay:{Guid.NewGuid()}")`,
  resolve DB-touching services from it, swallow per-recipient errors. (Sequential `foreach` is an
  acceptable fallback.)
- Recipient auth, store write, and flush run in their own request/socket scope; the store is
  thread-safe (Redis `IDatabase` / `ConcurrentDictionary`).

### Gotchas

- `Blob` is opaque base64 inside `GetClientData()` JSON — do **not** interpret or double-encrypt; the
  websocket shared-secret encryption is applied generically by `SendMessageAsync`.
- Never trust a sender id or AppId from a request **body** — sender from the cert
  (`GetCallerOdinIdOrFail`); AppId inferred from the app token on hop 1, only then placed in the
  hop-2 envelope by the trusted sender server.
- `LiveRelayAppSnapshot`/`LiveRelayRetainedEntry` are STJ-serialized into the L2 cache — keep them
  **named records** (no `ValueTuple`/anonymous types, per `CacheTypeGuard`); store `OdinId` as its
  domain string. Filter stale slots on read by `ReceivedAtMs` (TTL bounds growth).
- RMW is serialized by a per-app `SemaphoreSlim` (not `lock {}` — the RMW spans `await`s). The
  FusionCache Redis backplane is already enabled with L2 Redis (`FusionCacheWrapperExtensions.cs:83`),
  so L1 stays coherent across instances — no extra config needed.
- `OdinClientContext.AppId` is on a widely-cloned type — keep it a plain nullable field; confirm
  `Clone()` copies it.
- `channelKey` = a per-share-session `Guid` the app generates; only participants know it. Chat AppId
  is `2d781401-3804-4b57-b4aa-d8e4e2ef39f4` (`SystemAppConstants.ChatAppId`).

---

## Client changes (chat-kmp)

- **Receive:** add `LiveRelay` to `ClientNotificationType` and a case in
  `OdinWebSocketClient.dispatchNotification()` → emit a `BackendEvent`
  (e.g. `LiveBlobReceived(senderId, channelKey, blob, receivedAt)`) on `EventBus`. Reuse AES-CBC
  shared-secret decode. Use `receivedAt` for staleness; `senderId` is authoritative.
- **Send:** new provider method (reuse `OdinApiProviderBase.post`) POSTing the encrypted blob to
  `/notify/liverelay/relay` with `{channelKey, recipients, blob}` (no appId — server infers).
  Drive from `LocationTracker`, **throttled/coalesced** to a few-second cadence (last-value-wins) —
  not raw sensor rate. Mirror background-upload handling from `LocationTrackUploaderService`.
- **Connect:** nothing extra to send — on (re)connect/foreground the server **automatically flushes**
  every retained sender's last data point for this app; the client just filters by its open
  `channelKey`s. (No `EstablishConnectionRequest` changes.)
- **Share picker:** reuse `ConnectionNetworkProvider.getConnected()` / `ConnectionService`; generate
  a `channelKey` GUID per share session.
- **Map/dashboard:** consume `EventBus`, keep last-value per friend (`senderId`), show `receivedAt`
  freshness; reuse GPS capture + `LocationTrackCodec` for compact blob encoding.

---

## Verification

- **Unit (NUnit):**
  - `LiveRelayRetainedStore` over an in-memory `ITenantLevel2Cache`: per-sender put/overwrite
    (last-value-wins), multiple senders coexist in the snapshot, `GetAllForApp` returns all live
    senders, stale slots filtered by `ReceivedAtMs`, snapshot round-trips through STJ.
  - `LiveRelayNotification.GetClientData()`: JSON includes `senderOdinId`, `channelKey`, `blob`,
    `receivedAt`; `Blob` byte-identical.
  - Sender fan-out: mock `IOdinHttpClientFactory`; one `Relay` per recipient with inferred AppId in
    the envelope; a throwing recipient is swallowed and doesn’t block others.
- **Integration (`WebScaffold`, frodo + sam):**
  1. Connect frodo↔sam; sam opens app websocket + `EstablishConnectionRequest`.
  2. frodo POSTs relay `Recipients=[sam]` → sam’s socket gets a `LiveRelay` notif; decrypt; `Blob`
     byte-identical; `senderOdinId == frodo`; `receivedAt` present/recent.
  3. **App isolation:** a second sam socket under a *different* AppId must **not** receive it.
  4. **Auth negative:** block/disconnect → relay rejected, no socket message.
  5. **Auto-flush on connect:** relay while sam has no socket; sam then connects → frodo's last entry
     is flushed immediately (no client-supplied list) with original `receivedAt`; after TTL → nothing.
  6. **Zero sockets:** relay with no sam sockets → HTTP success, no error (store-only).
- **Manual / multi-instance:** two hosting instances behind the tenant, two chat-kmp clients; confirm
  a blob received on instance A reaches a client socketed to instance B (Redis pub/sub) and that a
  client connecting to a *different* instance than the writer auto-flushes from the L2 snapshot.

## Out of scope

- WebRTC group calling (participant-relay data plane; separate effort).
- Server-enforced per-channel consent (client gates the *session*; server enforces the *app*).
- Promoting the send hop (HOP 1) to a websocket command — future optimization only if POST cadence
  proves a bottleneck.
