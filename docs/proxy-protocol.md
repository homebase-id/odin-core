# PROXY protocol: running odin-core behind an L4 load balancer

odin-core selects a certificate per tenant from SNI at handshake time, so a load balancer in
front of it cannot terminate TLS and must pass TCP through. Passthrough hides the client's
address behind the balancer's, which breaks the per-IP rate limiter (every client collapses
into one bucket) and request-log attribution. PROXY protocol is how the balancer hands the
client address across: a small header before the TLS ClientHello, which odin-core consumes at
the connection layer and applies to `HttpContext.Connection.RemoteIpAddress` before TLS or HTTP
see the connection.

## Configuration

Per listen entry. Both the HTTP and HTTPS ports of the entry get the same treatment.

```json
"Host": {
  "IPAddressListenList": [
    {
      "Ip": "*",
      "HttpsPort": 443,
      "HttpPort": 80,
      "ProxyProtocol": {
        "Enabled": true,
        "TrustedProxies": [ "10.1.0.0/24", "10.1.1.7" ]
      }
    }
  ]
}
```

- `Enabled`: every connection on this entry must start with a PROXY v1 or v2 header. A
  connection without one, or with a malformed one, is closed. This is deliberate: accepting
  headerless connections would let a client bypass the header by omitting it.
- `TrustedProxies`: CIDRs or single addresses. A header is honoured only when the transport
  peer is in this list; any other peer is closed without reading a byte. Required when
  `Enabled` is true (startup fails otherwise). Never put `0.0.0.0/0` here: it would let any
  client claim any source address, which is worse than the problem being solved.
- A v2 `LOCAL` header (or v1 `UNKNOWN`) is accepted and keeps the balancer's own address; that
  is what a balancer sends when talking on its own behalf.

Alternatively keep the public entry as-is and add a second, PROXY-enabled entry on a port only
the balancer can reach (cloud security group), which is the "dedicated listener" design.

## Health checks

Prefer a monitor that sends the PROXY header: on OpenStack Octavia the pool protocol (`PROXY` /
`PROXYV2`) applies to monitors as well. Confirm on the actual balancer before cutting over.

A plain TCP-connect monitor also works, because the connect succeeds before the listener gets to
the header: the monitor sees an open port and marks the member up, and the listener then closes
the headerless connection. Two consequences worth knowing:

- The listener holds each probe connection for up to 5 seconds (the header wait) before giving up.
- Each probe is recorded at `Verbose`, not `Warning`, so it stays out of the log stream at the
  default `Debug` minimum level. That is deliberate: a connection that sends *zero* bytes and goes
  away is a probe, not a malformed client, and warning about it once per probe buried everything
  else (issue #1731). A connection that sends something that is not a valid header still warns, as
  does a header from a peer outside `TrustedProxies`.

To see the probe records, override the level for this source:
`Serilog__MinimumLevel__Override__Odin.Hosting.Kestrel=Verbose`.

## Verifying on real infrastructure

`GET /api/v2/health/ip` echoes what the host believes the caller's address is. From two
different external addresses through the balancer it must return each caller's own address,
not the balancer's. `Host:IpRateLimitEnabled` forces the rate limiter on outside Production
if you want to watch it partition per client in a staging environment.

Tests: `tests/apps/Odin.Hosting.Tests/Kestrel/` (parser unit tests, listener tests on real
Kestrel, and the rate-limit partition test).
