# STUN: how a device learns its own public UDP address

Peer-to-peer calling (WebRTC) needs each device to know the public IP:port its media socket
appears from. The address a Homebase server sees on a TLS connection is a different NAT binding:
media leaves from a UDP socket with its own mapping, and the only way to learn that one is to
send from that socket and be told what it looked like. That is STUN (RFC 8489). ICE speaks it
natively: the client is handed a `stun:` URL and gathers a server-reflexive candidate on its own.

odin-core runs a minimal STUN Binding responder in-process, next to Kestrel, so that call setup
(who is calling whom, from where, and when) never touches a third-party STUN server. Each device
asks **its own identity's** server; neither party's address ever reaches the other's
infrastructure. Issue #1838 has the full reasoning.

## What it does

- Listens on UDP 3478 (RFC-conventional; configurable).
- Answers a STUN **Binding request** with a Binding success response whose only attribute is
  `XOR-MAPPED-ADDRESS`: the source IP:port the request arrived from. 32 bytes for IPv4, 44 for
  IPv6. The reply carries the address family the request arrived on.
- Drops everything else silently: indications, responses, TURN methods, non-STUN bytes,
  datagrams whose header length disagrees with the datagram, datagrams over 1280 bytes. No error
  responses are sent, so the responder never emits a packet larger than 44 bytes.
- Stateless. No authentication (STUN is unauthenticated by design; a browser cannot attach a
  credential to a STUN packet), no storage, no tenant context.

Deliberate omissions: no TURN (relaying), no `MESSAGE-INTEGRITY`, no `FINGERPRINT`, no
`SOFTWARE`, no 420 error for unknown comprehension-required attributes (the request's attributes
are not inspected), and no rate limiting. The amplification factor is 20 bytes in, 32 or 44 bytes
out.

## Configuration

Top-level `Stun` section. All keys are optional.

```json
"Stun": {
  "Enabled": true,
  "Port": 3478,
  "BindAddress": "*"
}
```

- `Enabled`: **on by default**. This is the kill switch for an operator who does not want an
  open UDP responder on the host. Environment variable form: `Stun__Enabled=false`.
- `Port`: UDP port, default 3478. `0` asks the OS for an ephemeral port (tests only).
- `BindAddress`: `"*"` (default) binds one dual-stack socket on every interface (`[::]`, with
  IPv4 peers mapped in), or `0.0.0.0` on a host without IPv6. Unlike `"*"` in
  `Host:IPAddressListenList`, this is not IPv4-only, because ICE gathers both families. A literal
  address binds that address; `::` is dual-stack, any other literal is single-family.

The responder is a system background service, so it also needs
`BackgroundServices:SystemBackgroundServicesEnabled` (the default). The CLI turns system
background services off and never starts it.

When enabled, a bind failure (port in use, address not on this host) fails host startup, the same
as a Kestrel port clash. Startup logs `STUN responder listening on [::]:3478`, or
`STUN responder not started: Stun:Enabled is false`; a stats line
(`STUN responder stats: received=… answered=…`) is logged every ten minutes when there was
traffic.

## Firewall and ports

| port | protocol | direction | purpose |
|---|---|---|---|
| 3478 | UDP | inbound (replies go back out on the same flow) | STUN Binding responder |

The Docker image declares `EXPOSE 3478/udp` and the `--docker-setup` script publishes
`3478:3478/udp`. Production hosts run with host networking; the firewall rule for 3478/udp is
an ops-playbook change outside this repo.

## No proxy, no source-rewriting balancer

The whole point of the reply is the client's own source address, so the responder must receive
the datagram with that address intact. There is no PROXY protocol for UDP (contrast
`docs/proxy-protocol.md` for the TCP listeners). Anything that SNATs the client (an L4 balancer in
NAT mode, a reverse proxy) makes every reply report the balancer's address and the client will
offer a candidate nobody can reach. Bind directly on the public interface, or forward 3478/udp with
DNAT only (no source rewrite). Nothing in code can detect the wrong setup; a quick check is
below.

## Checking it

From a machine outside the host's network, with coturn's client:

```bash
turnutils_stunclient <host-ip-or-name>
```

or with stuntman:

```bash
stunclient <host-ip-or-name> 3478
```

Both print the mapped address; it must be the address of the machine you ran them on, as seen
from the internet. In a browser, a page running

```js
const pc = new RTCPeerConnection({ iceServers: [{ urls: 'stun:<host>:3478' }] });
pc.onicecandidate = e => e.candidate && console.log(e.candidate.candidate);
pc.createDataChannel('x'); pc.createOffer().then(o => pc.setLocalDescription(o));
```

logs a `typ srflx` candidate when the responder is reachable.

## Where the code is

- `src/services/Odin.Services/Stun/StunBindingCodec.cs`: the wire format, pure byte work.
- `src/services/Odin.Services/Stun/StunResponderBackgroundService.cs`: the socket and receive loop.
- `OdinConfiguration.StunSection`: the config keys.
- Tests: `tests/services/Odin.Services.Tests/Stun/` (codec and socket round trips) and
  `tests/apps/Odin.Hosting.Tests/Stun/` (the real host). `WebScaffold` binds the responder to an
  ephemeral loopback port so every hosting fixture runs its start/stop path without touching
  3478; the V2 TestServer harness disables it along with the other system background services.
