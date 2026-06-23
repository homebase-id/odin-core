# Live Relay — chat-kmp Client Plan

## Context

The backend for **Live Relay** (generic, ephemeral, app-agnostic live data sharing — e.g. live GPS
among connected friends) is implemented and merged-pending in PR
[#1572](https://github.com/homebase-id/odin-core/pull/1572). Design: `docs/live-relay-plan.md`.
Reference contract test (read this first):
`tests/apps/Odin.Hosting.Tests/_V2/Tests/LiveRelay/LiveRelayShareGpsExampleTest.cs`.

This plan covers the **chat-kmp client** side. Goal: a friend can share their live location with a
chosen set of connected identities; recipients see it live on a map and instantly on (re)connect.
Nothing is stored durably — "if you're offline, the data is just gone."

> **This file is the handoff doc. On approval it is written to `docs/live-relay-client-plan.md` on
> the `live-relay` branch and pushed (so it travels with the PR). The actual client implementation
> should be done in a fresh session pointed at this doc + the example test.**

---

## Wire contract (already live on the server)

**Send (hop 1, app → its own server)** — `POST /api/v2/live-relay`, app-authenticated, request body
shared-secret-encrypted like every other V2 JSON POST:
```json
{ "channelKey": "<guid>", "recipients": ["sam.dotyou.cloud", ...], "blob": "<base64>" }
```
- `blob` is **opaque to the server** (GPS today, anything tomorrow). The app owns its meaning/encoding.
- `appId` is **inferred server-side** from the app token — the client never sends it.
- Response: `204 No Content`. Fire-and-forget — unreachable/non-connected recipients are silently dropped.

**Receive (hop 3, server → client websocket)** — a `LiveRelay` client notification over the existing
notification socket. Its `Data` payload:
```json
{ "senderOdinId": "frodo.dotyou.cloud", "channelKey": "<guid>", "blob": "<base64>", "receivedAt": 1718800000000 }
```
- `senderOdinId` is authoritative (server knows it from the peer cert). `receivedAt` = server-received
  time (ms) — use for staleness on the map.
- **Server enforces app isolation**: only sockets of the matching app receive it.
- **Flush-on-connect is automatic**: on (re)connect/foreground the server pushes every retained
  sender's last point for this app — the client sends nothing extra; just filter by open `channelKey`s.

---

## Permissions — no change needed

The relay endpoint requires the calling app to have `UseTransitWrite` (backend key **210**). The chat
app **already requests it** as `AppPermissionType.SendDataToOtherIdentitiesOnMyBehalf(210)` in
`homebase-common/.../core/config/AppConfig.kt` (`appPermissions`) — same key it already uses to send
files peer-to-peer. No new permission, no re-authorization. (Verify at runtime: the relay also needs
ICR access to connected recipients, which the app already has via its connection grants.)

---

## Touch points (chat-kmp)

All paths under `/home/seifert/odin/chat-kmp/`.

**Receive**
- `homebase-api/.../client/websockets/ClientNotificationType.kt` — add `liveRelay(6001)` (match the
  backend enum value; existing values are explicit ints e.g. `fileAdded(101)`).
- `homebase-api/.../client/websockets/OdinWebSocketClient.kt` — `dispatchNotification()` (~line 412):
  add a `ClientNotificationType.liveRelay ->` case that deserializes `notification.data` into a new
  DTO and `eventBus.emit(BackendEvent.LiveRelayReceived(...))`. Mirror the `introductionsReceived`
  case (~lines 470–479).
- `homebase-api/.../client/eventbus/BackendEvent.kt` — add `LiveRelayReceived(senderOdinId, channelKey,
  blob, receivedAt)` (new `DataEvent`/category). Consumers collect from `eventBus`.

**Send**
- `homebase-api/.../client/OdinApiProviderBase.kt` — reuse `encryptedPostJson(url, token, jsonBody,
  secret)` (~lines 396–425); responses auto-decrypt. Template provider:
  `homebase-api/.../client/peer/PeerNotificationProvider.kt`.
- New `LiveRelayProvider : OdinApiProviderBase` with `relay(channelKey, recipients, blob)` →
  `POST /api/v2/live-relay`.

**Connections (share picker)**
- `homebase-api/.../client/connections/ConnectionNetworkProvider.kt` — `getConnected(count, cursor)`;
  `ConnectionService` for cached lists.

**Location capture / encoding (reuse)**
- `homebase-common/.../core/location/tracking/LocationTracker.kt` — `start/setMode/stop`
  (platform impls `.android/.apple/.jvm/.web`). Already background-capable.
- `homebase-core/.../ui/screens/location/model/LocationTrackContent.kt` — `LocationTrackCodec`
  (compact point encoding) as a template for the live-point blob.
- `homebase-core/.../ui/screens/location/LocationTrackUploaderService.kt` — background send pattern
  to mirror for the throttled live sender.
- `homebase-common/.../core/config/AppConfig.kt` — `APP_ID = 2d78140138044b57b4aad8e4e2ef39f4`.

---

## Implementation steps

1. **Receive plumbing**: `liveRelay(6001)` enum value → `BackendEvent.LiveRelayReceived` →
   `dispatchNotification` case (decode Data, emit). Smallest, do first; unblocks UI work.
2. **Send provider**: `LiveRelayProvider.relay(channelKey, recipients, blob)` over `encryptedPostJson`.
3. **Live sender service**: while a share is active, take `LocationTracker` output, **throttle/coalesce
   to a few-second cadence** (last-value-wins — not raw sensor rate), encode the live-point blob, and
   call `relay(...)` to the channel's recipient set. Mirror `LocationTrackUploaderService`'s background
   handling. Distinct from the durable hourly track files — this is ephemeral.
4. **Session + picker**: a "live share" = a `channelKey` (GUID) + a recipient set chosen via
   `getConnected()`. Persist active shares locally; tie to a conversation/group if desired.
5. **Map / dashboard**: subscribe to `BackendEvent.LiveRelayReceived`; keep **last value per
   `senderOdinId`** for channels the user has open; render position + `receivedAt` freshness.
6. **Lifecycle**: start/stop the live sender with the share toggle and app foreground/background.

---

## Open decisions (resolve at the start of the client session)

1. **Blob encoding & encryption.** The server sees the blob at each hop (it's "opaque", not
   encrypted, unless the app encrypts it). Decide: (a) plaintext compact codec — simplest, fine if
   server-visible-to-your-own-infra is acceptable; or (b) app-level E2E encryption with a per-channel
   symmetric key shared among participants. **Recommend (a) for v1**, a trimmed `LocationTrackCodec`
   point (lat/lon E5, heading, speed, ts); layer (b) later if needed.
2. **Send cadence.** Pick the coalesce interval (e.g. 2–5s) and movement threshold; battery vs liveness.
3. **Session/membership model.** Standalone "live share" sessions vs riding an existing group
   conversation's membership. Determines who's on `recipients` and which `channelKey`s the watcher opens.
4. **Multi-channel UI.** If a user is in several live shares, how the map disambiguates by `channelKey`.

---

## Verification

- **Contract reference**: `LiveRelayShareGpsExampleTest.TwoFriendsShareLiveGps` (backend) shows the
  exact request/response/notification shapes the client must match.
- **Unit (KMP commonTest)**: live-point codec round-trip; `dispatchNotification` parses a `LiveRelay`
  Data JSON into `BackendEvent.LiveRelayReceived` with the right fields.
- **Manual, two identities (same app, connected)**: one shares → the other sees live updates on the
  map; background the sharer (HTTP keeps flowing); kill+reopen the watcher → it immediately re-hydrates
  to everyone's current position (flush-on-connect); stop sharing → updates cease.
- **Negative**: share to a non-connected identity → no delivery, no client error.

## Out of scope (client)

- Group calling (WebRTC) — separate effort.
- Server changes — backend is complete in PR #1572; this is client-only.
