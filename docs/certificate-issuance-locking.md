# Certificate issuance: locking, backoff and the TLS handshake path

Written after the 2026-09-08 incident on the NA cluster, where a rate-limited Let's
Encrypt order made every inbound HTTPS request for one domain hang 30 seconds and
throw. This is the reference for how certificate issuance is allowed to behave, and
why.

## The incident

The first identity provisioned on the new NA cluster (`delete.n1.id.pub`) could not
get a certificate. Let's Encrypt was refusing the order:

```
urn:ietf:params:acme:error:rateLimited: too many failed authorizations (5)
```

That part was expected and self-correcting: the first attempts fired before PowerDNS
had published all three records (`capi`, `file`, `mta-sts`), and LE allows 5 failed
authorizations per hostname per hour.

The rate limit was not the bug. While the certificate service sat in that failing
loop, *every* inbound HTTPS request for the domain produced:

```
ERR Unhandled exception while processing 0HNODMEJMQ3D2.
  (Could not acquire lock 'odin:lock:CertificateServiceLock:delete.n1.id.pub'.
   Timeout after 30s.)
Odin.Core.Storage.Concurrency.RedisLockException
   at Odin.Services.Certificate.CertificateService.CreateCertificateAsync(...)
   at Odin.Hosting.Program.ServerCertificateSelector(...)
```

So the failure mode for an ordinary, recoverable condition was a 30-second stall on
the TLS handshake path followed by an unhandled exception, repeated for every request,
for as long as the rate limit lasted.

DNS, the challenge path, the SAN set and Redis itself were all checked and ruled out
on the infrastructure side.

## What was actually wrong

Three defects, each harmless on its own, compounding:

### 1. The TLS handshake path took a blocking distributed lock

`ServerCertificateSelector` runs once per inbound connection. It called
`CreateCertificateAsync`, which wrapped the **entire ACME order** in
`INodeLock.LockAsync` with the 30-second default timeout. `ServerCertificateSelector`
was the only caller of that method, so the handshake path was doing inline issuance
and every other connection to the domain queued behind it.

Worse, the `RedisLockException` escaped the Kestrel `UseHttps` callback, which is why
it was logged as an unhandled connection fault rather than as contention.

Waiting there was never useful: if another thread or node holds the lock, an order is
already in flight and this connection still has no certificate to serve.

### 2. The retry loop was spending the CA's allowance itself

`InternalCreateCertificateAsync` retried **any** exception up to 10 times, 2 seconds
apart. Each retry calls `ICertesAcme.CreateCertificateAsync`, which places a fresh
`NewOrder` with fresh authorizations. One un-issuable domain therefore produced up to
ten orders in about twenty seconds — against an allowance of five failed
authorizations per hostname per hour.

In other words the retry loop was not recovering from the rate limit, it was causing
it. (That LE counts each of those as a failed authorization is LE's documented
behaviour; it is not something this repository can verify.)

### 3. Nothing backed off

Every inbound connection restarted the whole sequence from the top, so the lock was
continuously hot. During the incident the Redis key was observed with TTL 583s — not
leaked, genuinely held, over and over, by a loop that kept failing.

Separately: `NodeLock`, the single-node implementation used when Redis is not
configured, **ignored** the `timeout` argument entirely. The same condition on a
single-node deployment was an unbounded hang rather than a 30-second one.

## The rules now

### Issuance never blocks a request

`ICertificateService.CreateCertificateAsync` is non-blocking **by contract**. It
returns null promptly rather than waiting, in three cases:

- the domain is in failure backoff,
- another thread or node holds the certificate lock,
- the order itself failed.

`ServerCertificateSelector` additionally catches everything, so nothing can escape to
Kestrel as an unhandled connection fault, and logs a warning if the whole selector
takes more than 5 seconds.

`RenewIfAboutToExpireAsync` is also non-blocking, for a different reason: whoever
holds the lock is ordering for the same domain, so waiting only risks a lock timeout
and an alarming-looking error. The background loop comes around again.

**Trade-off, deliberately accepted.** Concurrent connections to a domain that has no
certificate yet are now dropped promptly instead of queued. The first connection still
issues inline and succeeds; a browser opening six parallel connections to a
brand-new identity will have five dropped and retried. That is strictly better than
five 30-second stalls, but it is a real behaviour change on first contact.

### `INodeLock.TryLockAsync`

Added for exactly this shape of problem: acquire if free right now, otherwise return
null. Use it on any latency-sensitive path where waiting out a contended lock buys
nothing.

`RedisLock` implements it as a single `SET NX`. `NodeLock` implements it via
`KeyedAsyncLock.TryLockAsync`, which leans on an implementation detail of Nito's
`AsyncLock` (it takes a free lock synchronously and only consults the cancellation
token when it would have to queue). `KeyedAsyncLockTests.TryLockAsync_*` exist
specifically to fail loudly if that ever stops being true — without them, the handshake
path would silently stop issuing certificates.

### Failed orders back off

`CertificateService` keeps a per-domain backoff window and refuses to start a new
order inside it:

- **5 minutes** after a generic failure (bad DNS, transport, anything unclassified),
- **the CA's own hour** when Let's Encrypt says `rateLimited`.

The window is node-local on purpose. The node lock already stops two nodes ordering
at once, so the worst case is one wasted attempt per node per window, and losing the
state on restart is the right behaviour for an operator who has just fixed DNS and
bounced the service.

The failure is also still recorded in `Certificates.lastAttempt` / `lastError` for
operator visibility, exactly as before.

The **background** renewal loop (`UpdateCertificatesBackgroundService`) deliberately
ignores the backoff. Its own interval is its rate limiter, and it is the thing that
eventually heals a domain whose DNS has since been fixed. Nothing is waiting on it.

### Terminal CA verdicts are not retried

`CertesAcme` now classifies what the CA says:

| Condition | Exception | Behaviour |
| --- | --- | --- |
| `urn:ietf:params:acme:error:rateLimited` | `AcmeRateLimitedException` | terminal; backoff for `RetryAfter` (1 hour) |
| `badNonce`, `serverInternal` | original `AcmeRequestException` | transient; the retry loop handles it, per RFC 8555 §6.5/§6.6 |
| any other CA rejection | `AcmeOrderException` | terminal; no retry, 5-minute backoff |
| authorization or order reaches a failed/timed-out state | `AcmeOrderException` | terminal; no retry, 5-minute backoff |

The rule: **an order the CA has already rejected on its merits must not be placed
again in a tight loop.** Retrying it is not merely useless, it is what converts a few
minutes of DNS lag into an hour of rate-limited failure.

## Diagnosing a stuck certificate lock

The Redis lock value used to be an opaque GUID, which told an operator nothing. It is
now self-describing:

```
$ redis-cli GET odin:lock:CertificateServiceLock:example.com
"host-i1-na|pid:1234|2026-09-08T14:22:07.1234567+00:00|3f2b...-...."

$ redis-cli TTL odin:lock:CertificateServiceLock:example.com
(integer) 583
```

The value still doubles as the ownership token — the release script only deletes the
key when the value matches — so it stays unique; it is simply no longer opaque.

Where else to look:

- **Lock acquire timeouts** now name the holder and the remaining TTL in the exception
  message, e.g. `Could not acquire lock '...'. Timeout after 30s. Held by
  host-i1-na|pid:1234|..., expires in 583s.`
- **`RedisLock` warns** when a lock was held for more than a minute, and when a lock
  turns out to have been force-released while the holder was still working (which
  means two workers may have overlapped).
- **`Certificates.lastError` / `lastAttempt`** in the system database hold the last
  failure for the domain, with the correlation id of the request that hit it.
- **A slow handshake** logs `Certificate lookup for {hostName} took {elapsed}s on the
  TLS handshake path` above 5 seconds. On a healthy host this line should never appear.

## Relevant code

- `src/services/Odin.Services/Certificate/CertificateService.cs` — the lock and
  backoff policy
- `src/services/Odin.Services/Certificate/CertesAcme.cs` — CA error classification
- `src/services/Odin.Services/Certificate/AcmeExceptions.cs` — `AcmeOrderException`,
  `AcmeRateLimitedException`
- `src/core/Odin.Core.Storage/Concurrency/RedisLock.cs` — try-lock, owner token,
  hold-time diagnostics
- `src/core/Odin.Core/Threading/KeyedAsyncLock.cs` — `TryLockAsync`
- `src/apps/Odin.Hosting/Program.cs` — `ServerCertificateSelector`

## Reproducing

Provision an identity whose DNS records are not yet all live — or exhaust the LE
failed-authorization allowance for a hostname — then make HTTPS requests to it.
Before the fix each request hung ~30s and logged a `RedisLockException` from
`ServerCertificateSelector`. After it, the first request attempts issuance once,
records the failure, and every request for the next 5 minutes (or the hour LE asked
for) returns without locking, without a DNS lookup, and without a new order.
