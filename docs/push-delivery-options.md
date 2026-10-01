# Push delivery options: expiry, collapse, silent, time-sensitive

A push notification used to be fire-and-remember: once enqueued it was delivered whenever the
device next came online, however late, and nothing could retract it. That is fine for a chat
message and wrong for a ring or a "send me your location now". These four fields on
`AppNotificationOptions` make a push time-bound and replaceable, and are honoured on this server's
outbox, on the push relay, and on the device platforms.

| field | type | meaning |
|---|---|---|
| `timeToLiveSeconds` | int?, 1..86400 | Dropped instead of delivered once this many seconds have passed since enqueue. Null: no expiry (today's behaviour). |
| `collapseId` | string?, ≤ 64 chars | Platform collapse key. A later push with the same id replaces an undelivered earlier one. |
| `silent` | bool | Background delivery, no alert. Existed before but was never read; it now drives the iOS push type. |
| `timeSensitive` | bool | iOS `interruption-level: time-sensitive`. Needs the app's Time Sensitive Notifications entitlement, otherwise APNs downgrades it. |

The notification-list row on the recipient is written for every push, silent or not, as before.
A ring therefore leaves a missed-call entry; a silent wake-up also leaves a row, which is accepted
for now and is the next follow-up if it proves noisy.

## Where they are honoured

**Outbox.** `PushNotificationOutboxRecord.IsExpired(now)` is checked at the top of every attempt in
both push workers: the local one (`SendPushNotificationOutboxWorker`) and the peer hop
(`SendPeerPushNotificationOutboxWorker`). An expired item is completed without sending. On the peer
hop a failed attempt is rescheduled ten minutes out, so an item that expires after a failure is
dropped on that next run rather than retried. The record's timestamp is re-stamped at enqueue on
each hop, so on the two-hop peer route the worst case is about twice the TTL. The push that rides
on a LiveRelay message has no outbox hop before the recipient's server, so its TTL starts there.

**Validation.** The bounds are what the outbox and the relay accept, so
`PushDeliveryOptionsValidation.AssertDeliveryBounds` runs where every push is enqueued
(`PushNotificationService.EnqueueNotificationInternalAsync`), whichever route it came in on, and
an out-of-range value is refused there instead of being dropped by the relay later. The limits
themselves are constants on `DevicePushNotificationRequestV1` in Odin.Core, the one type the host
and the relay both see, so the two validators cannot drift. The LiveRelay push additionally
requires a `TypeId` at its ingress (`AssertValid`).

**Expiry lives in the two push workers today.** A deeper shape would be an `ExpiresAt` on the
outbox item state, checked once in the outbox processor for every item type; that is a follow-up
once something other than a push wants a deadline.

**Relay request.** `DevicePushNotificationRequestV1` carries the four fields as optional
properties. The version stays 1: an older relay ignores them, a newer relay treats missing ones as
null/false. The FCM `data` dictionary the app reads is unchanged.

## Platform mapping (push relay, `PushNotification.BuildMessage`)

| input | Android (FCM) | iOS (APNs via FCM) |
|---|---|---|
| always | data-only, `priority: high` | `content-available: 1`, `mutable-content: 1` |
| `timeToLiveSeconds` | `ttl` | `apns-expiration` = now + ttl (unix seconds) |
| `collapseId` | `collapse_key` | `apns-collapse-id` |
| `silent: false` | unchanged | `apns-push-type: alert`, `apns-priority: 10`, alert with title and body (as before) |
| `silent: true` | unchanged; the app decides what to show | `apns-push-type: background`, `apns-priority: 5`, no alert |
| `timeSensitive` | unchanged | `aps.interruption-level: time-sensitive` on an alert push; ignored when `silent` |

Sound and badge are left to the app, as before. The explicit `apns-push-type` header is the only
change for existing pushes; APNs requires it to match the payload on iOS 13 and later, and
requires priority 5 for background pushes.

**WebPush (browsers).** TTL maps to the `TTL` header, `timeSensitive` to `Urgency: high`, and
`collapseId` to `Topic` when it fits RFC 8030's 32 URL-safe characters. `silent` has no WebPush
equivalent; the service worker decides.

## Kind: the one knob an app sets

`kind` says what the push is for. The server derives the delivery fields from it; the four fields
above become overrides that a calling app never has to touch.

| kind | derived | what the device gets |
|---|---|---|
| `notify` (default) | nothing | today's behaviour |
| `ring` | TTL 45 s, `collapseId` = `call-<tagId>`, time-sensitive, not silent | Android: high-priority data message, the app shows its own call UI. iOS with a registered PushKit token and an APNs key on the relay: a VoIP push (below). iOS otherwise: a time-sensitive alert. |
| `wake` | silent | a background wake-up with no UI; the app acts on the LiveRelay blob |

For a call the app sends `push: { "kind": "ring", "typeId": ..., "tagId": <callId>, "unEncryptedMessage": "Frodo is calling" }`
and nothing else. The hangup before answer is a second push with the same `tagId` (so the same
collapse id) and `kind: notify`, which replaces the ring on the lock screen.

### Ring on iOS: PushKit VoIP

Ringing an iOS app that is not running needs a PushKit VoIP push. It is a different APNs push
type (`voip`), goes to a different device token than the normal one, and cannot be sent through
Firebase. So:

- **Registration.** The app registers its PushKit token in the optional `voipDeviceToken` field of
  `POST …/notify/push/subscribe-firebase`, next to the normal token, and can verify it with
  `GET …/notify/push/subscription`. Only a build that handles CallKit may register one.
- **Decision.** The host carries `kind` and both tokens to the relay and decides nothing. The relay
  (`PushRouter`) sends a VoIP push when the kind is `ring`, the device has a VoIP token, and the
  Apple key is configured; otherwise it sends the ordinary alert and, when a VoIP token was there,
  logs `VoIP push wanted … but APNs is not configured`. That log line is the signal that the Apple
  key is the only missing piece.
- **Payload.** The VoIP push body carries the same five keys as the FCM data message
  (`correlationId`, `id`, `data`, `timestamp`, `version`) at the top level next to an empty `aps`,
  so the app parses one shape on both paths. `data` is the serialized push payload; its
  `options.tagId` is the call id.
- **The app's obligation.** On every VoIP push the app must call CallKit's `reportNewIncomingCall`
  immediately. iOS stops delivering VoIP pushes to an app that does not, and can terminate it.
  That is why the server sends VoIP only for `kind: ring`.
- **What Apple needs** (an admin on the developer account, no review): an APNs authentication key
  (`.p8`) with its key id, the team id, the app's bundle id, and on the app target the Push
  Notifications capability plus the Voice over IP background mode. The relay reads them from its
  `Apns` config section; see `src/apps/Odin.PushNotification/README.md`. Dev-signed builds only
  receive from Apple's sandbox host, so the dev relay uses `Environment: sandbox`.

Until the key exists the client team can register a token, send a ring, watch the fallback log,
and build PushKit and CallKit against the payload above using Xcode's simulated push
(`xcrun simctl push <device> <bundle> payload.apns`), which needs no key.

## Platform limits worth knowing

- iOS background pushes are throttled by the OS (a few per hour) and are not delivered to an app
  the user force-quit or in Low Power Mode. A ring must not rely on a silent push. Ringing a killed
  iOS app needs a PushKit VoIP push, which is a different token and a different APNs push type that
  cannot be sent through FCM; it is deliberately not part of this.
- Android keeps at most four collapse keys per device; APNs collapse ids are limited to 64 bytes.
- `apns-expiration` and FCM TTL bound how long the platform holds the push for an offline device.
  They do not recall a push already shown; that is what a second push with the same `collapseId`
  does.

## Deploying

Host and relay can go in either order. The relay image is built by
`.github/workflows/push-notification-build-and-deploy.yml` (manual dispatch) and deployed from the
ops repo with ansible. Until the relay is redeployed, the host's new fields are ignored and every
push behaves as before.

## Where the code is

- `src/services/Odin.Services/Peer/Outgoing/Drive/AppNotificationOptions.cs`: the fields.
- `src/services/Odin.Services/AppNotifications/Push/PushDeliveryOptionsValidation.cs`: the limits.
- `src/services/Odin.Services/AppNotifications/Push/PushNotificationOutboxRecord.cs`: expiry.
- `src/services/Odin.Services/AppNotifications/Push/PushNotificationService.cs`: relay request and WebPush options.
- `src/apps/Odin.PushNotification/PushNotification.cs`: the FCM/APNs mapping.
- Tests: `tests/services/Odin.Services.Tests/AppNotifications/Push/`, `tests/apps/Odin.PushNotification.Tests/PushNotificationBuildMessageTests.cs`, `tests/apps/Odin.Hosting.Tests/_V2/Tests/LiveRelay/V2LiveRelayPushTests.cs`.
