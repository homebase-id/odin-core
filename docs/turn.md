# TURN: relaying calls that cannot connect directly

When two devices cannot reach each other directly (both behind symmetric NAT, or a network that
blocks UDP), a WebRTC call needs a relay: a TURN server (RFC 8656) both ends send their media
through. The relay only ever sees DTLS-SRTP-encrypted media.

odin-core does not relay. A **coturn** runs next to it on each host and does the relaying.
odin-core's only job is to hand a device a short-lived login for that coturn, right before a call.
Each device uses **its own identity's** host's TURN server. The other end of the call never needs a
login there: coturn relays to any address the login's holder has opened a permission for.

## The shared secret

odin-core and its coturn share one secret, and nobody else holds it: not devices, apps, tenants, or
other hosts and clusters. Each cluster generates its own (`openssl rand -hex 32`). odin-core signs
logins with it; coturn checks them with it. There is no user database and no call between the two.

```
username   = "<unix-expiry-seconds>:<identity-domain>"
credential = base64(HMAC-SHA1(sharedSecret, username))
```

This is the TURN REST scheme coturn checks with `use-auth-secret`. coturn strips the expiry and
counts `user-quota` per identity, and its log names the identity behind every relay.

What a leaked login can do: open relays until it expires (minutes), sharing that identity's quota,
traceable to it. coturn checks the expiry only when a session starts, so a call outlives its login;
a device asks for a new one before an ICE restart.

## Configuration

Top-level `Turn` section. **Off by default.**

```json
"Turn": {
  "Enabled": false,
  "Urls": [],
  "SharedSecret": "",
  "CredentialLifetimeSeconds": 300
}
```

- `Enabled`: whether devices get a TURN login. Off, they get STUN only, through the same endpoint.
- `Urls`: the `turn:` / `turns:` URLs devices get, each optionally ending `?transport=udp` or
  `?transport=tcp`. A `turns:` URL only works once coturn has TLS on (not in the config below). Empty means this host under the identity's own name:
  `turn:<identity-domain>:3479?transport=udp` and `…?transport=tcp`. A cluster behind a load
  balancer lists its cores' own names, as `Stun:PublicUrls` does (docs/stun.md). Environment form:
  `Turn__Urls__0=turn:turn1-1.eu.example:3479?transport=udp`.
- `SharedSecret`: the secret above, at least 32 characters, or startup fails. The same value is
  coturn's `static-auth-secret`. Environment form: `Turn__SharedSecret=…`.
- `CredentialLifetimeSeconds`: how long a login can open a relay, 60 to 86400. Default 300.

Only checked when `Enabled` is true.

## How a device gets the servers

Right before it creates a call's `RTCPeerConnection`, and again before an ICE restart, the app asks
its own host:

```
POST /api/v2/calling/ice-servers   { "peer": "sam.example" }

200 { "iceServers": [ { "urls": ["stun:stun1-1.eu.example:3478"] },
                      { "urls": ["turn:frodo.example:3479?transport=udp",
                                 "turn:frodo.example:3479?transport=tcp"],
                        "username": "1760000300:frodo.example",
                        "credential": "8L3k…" } ],
      "expiresAt": 1760000300000 }
```

The client passes `iceServers` to WebRTC as given. Without TURN the list holds only the STUN entry
and `expiresAt` is null. The STUN URLs are the same ones the socket handshake sends (docs/stun.md).

| Caller | Answer |
|---|---|
| App holding `UseTransitWrite` (the LiveRelay gate), `peer` connected | 200 |
| `peer` not connected | 403, `notAConnectedIdentity` |
| App without `UseTransitWrite`, guest | 403 |
| Owner with no app (LiveRelay needs an app too) | 400 |
| An app past 20 logins a minute (per identity, per app, per node) | 200, STUN only, `expiresAt` null |

The response is `Cache-Control: no-store`. odin-core logs identity, peer, app and expiry for every
login, never the credential.

The peer check is accountability, not enforcement: call setup is an opaque LiveRelay blob, so
neither odin-core nor coturn knows the peer's addresses. The real limits are LiveRelay's own
Connected check, the short lifetime, coturn's quotas, the egress filter and the logs.

## Ports

- **3479 udp and tcp** for TURN. 3478/udp is the STUN responder's, which binds every interface, so
  coturn cannot share it. The STUN responder stays as it is, so a host without TURN still has STUN.
- **The relay range**, 61000-65535/udp (coturn's `min-port` / `max-port`), open in the firewall. It
  starts above Linux's ephemeral range (32768-60999), so relays and the host's own outgoing
  connections never compete for a port.
- **TLS on 443** (`turns:`) gets through networks that allow only 443, but Kestrel holds 443. It
  needs its own IP or an SNI router in front (to build).

## coturn

Upstream image, version 4.18 or later (distributions ship much older versions), host networking,
only the features we use. The config below runs on coturn 4.18.0; check its options against
`turnserver --help` of the version deployed, since coturn removes options between releases, and
with Enable Security's `coturn-secure-config`.

The image's `/usr/bin/turnserver` carries a file capability, so a container with `cap_drop: ALL`
cannot start it ("Operation not permitted"). Keep `NET_BIND_SERVICE` in the bounding set; with
`no-new-privileges` the process still runs with no effective capabilities.

```
listening-port=3479
min-port=61000
max-port=65535
fingerprint
realm=<cluster name>

use-auth-secret
static-auth-secret=<the same value as Turn:SharedSecret>

# Limits (starting values, not tuned): concurrent relays per identity, all relays, bytes/s per relay
user-quota=12
total-quota=1200
max-bps=375000

# Features we do not use. The admin console and DTLS are off unless --cli / --dtls switch them on
no-tls
no-tcp-relay
no-multicast-peers

# Never relay to anything but the public internet.
# No entry may start at "::": coturn then refuses every IPv4 peer ("A peer IP 8.8.8.8 denied in the
# range: ::-::ffff:ffff"), and a bare :: or ::1 does the same. IPv6 loopback stays denied without
# one: coturn refuses loopback peers unless allow-loopback-peers is set, and the egress filter
# below drops ::/127 as well. An IPv4-mapped peer (::ffff:10.0.0.1) leaves the host as IPv4, so
# the egress filter's IPv4 rules catch it.
denied-peer-ip=0.0.0.0-0.255.255.255
denied-peer-ip=10.0.0.0-10.255.255.255
denied-peer-ip=100.64.0.0-100.127.255.255
denied-peer-ip=127.0.0.0-127.255.255.255
denied-peer-ip=169.254.0.0-169.254.255.255
denied-peer-ip=172.16.0.0-172.31.255.255
denied-peer-ip=192.0.0.0-192.0.0.255
denied-peer-ip=192.0.2.0-192.0.2.255
denied-peer-ip=192.88.99.0-192.88.99.255
denied-peer-ip=192.168.0.0-192.168.255.255
denied-peer-ip=198.18.0.0-198.19.255.255
denied-peer-ip=198.51.100.0-198.51.100.255
denied-peer-ip=203.0.113.0-203.0.113.255
denied-peer-ip=224.0.0.0-255.255.255.255
denied-peer-ip=64:ff9b::-64:ff9b::ffff:ffff
denied-peer-ip=100::-100::ffff:ffff:ffff:ffff
denied-peer-ip=2001::-2001:0:ffff:ffff:ffff:ffff:ffff:ffff
denied-peer-ip=2002::-2002:ffff:ffff:ffff:ffff:ffff:ffff:ffff
denied-peer-ip=2001:db8::-2001:db8:ffff:ffff:ffff:ffff:ffff:ffff
denied-peer-ip=fc00::-fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff
denied-peer-ip=fe80::-febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff
# and our own public subnets
```

**Plus an egress filter at the network level**, dropping the same ranges, IPv6 loopback (`::/127`)
and our own subnets for coturn's traffic only. With host networking coturn shares the host's network namespace, and a
host-wide rule would cut odin-core off from its own private services, so match coturn's process
instead: run it as its own user and filter on that (`iptables -A OUTPUT -m owner --uid-owner turnserver
-d 10.0.0.0/8 -j DROP`, and so on), or give coturn a network namespace of its own. coturn's `denied-peer-ip` has been bypassed several
times; a second layer below it catches that whole class of bug.

coturn has a steady stream of security advisories, so tracking its releases is ongoing work.

## Checking a host

Make a login by hand on the host:

```bash
secret='<Turn:SharedSecret>'
user="$(( $(date +%s) + 300 )):check.example"
pass=$(printf '%s' "$user" | openssl dgst -sha1 -hmac "$secret" -binary | base64)
echo "$user" "$pass"
```

Then, from a browser on another network, open WebRTC's Trickle ICE sample page, add
`turn:<core-name>:3479?transport=udp` with that username and credential, and gather candidates. A
candidate of type `relay` means coturn accepted the login and can allocate. None means the secret
differs, the clocks disagree, or the ports are closed.

## Changing the secret

odin-core signs with one secret. Change it in both odin-core and coturn and restart both. New relays
fail until both run the new value, and logins issued before the change fail at allocate; asking
the endpoint again gives a working one. If the deployed coturn accepts more than one
`static-auth-secret` line (not checked), add the new secret to coturn first, then switch odin-core,
then remove the old one, and no login fails. Restarting coturn cuts relayed calls in
progress either way, so change it when needed rather than on a schedule.
