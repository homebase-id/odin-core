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

The balancer's monitor must send the PROXY header too, or a listener that correctly rejects
headerless connections will fail every health check. On OpenStack Octavia the pool protocol
(`PROXY` / `PROXYV2`) applies to monitors as well; confirm on the actual balancer before
cutting over.

## Verifying on real infrastructure

`GET /api/v2/health/ip` echoes what the host believes the caller's address is. From two
different external addresses through the balancer it must return each caller's own address,
not the balancer's. `Host:IpRateLimitEnabled` forces the rate limiter on outside Production
if you want to watch it partition per client in a staging environment.

Tests: `tests/apps/Odin.Hosting.Tests/Kestrel/` (parser unit tests, listener tests on real
Kestrel, and the rate-limit partition test).
