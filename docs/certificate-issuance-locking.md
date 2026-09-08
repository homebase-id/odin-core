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

## The follow-up incident, 2026-09-08 (same day)

The fix above removed the exception but not the stall. On the NA cluster the host logged:

```
WRN Certificate lookup for delete.n1.id.pub took 60s on the TLS handshake path
WRN Lock odin:lock:CertificateServiceLock:delete.n1.id.pub was held for 60s
```

### What "never waits" actually meant

`CreateCertificateAsync` never waited **for the lock**. The connection that *won* the lock
still ran the entire ACME order inline, because `InternalCreateCertificateAsync` was always
on the handshake path and the first fix left it there. So the queue behind the holder was
fixed and the holder itself was not.

That path is bounded by Kestrel's 60-second handshake deadline (`Program.cs`,
`handshakeTimeoutTimeSpan`), and an ACME order legitimately takes minutes. Every attempt
therefore ran to 60s and was killed **mid-order** — after the CA had already been asked to
validate. An abandoned order costs the same rate-limit allowance as a completed one and
produces no certificate.

### Why the backoff never engaged

Worse, the cancellation was treated as a non-event:

```csharp
catch (OperationCanceledException) { throw; }   // before the generic catch
catch (Exception e) { … await NoteFailureAsync(…); }
```

The rethrow was there so a client disconnect would not poison a domain for five minutes.
The effect was the opposite of the intent: when the handshake deadline killed the order, **no
backoff was recorded**, so the next inbound connection started a fresh order immediately.

### What was NOT the cause

Recorded because it was the first theory and it was wrong: the in-call retry loop was **not**
re-burning the allowance. Production logs showed zero `(will retry)` lines over ninety
minutes and `AcmeRateLimitedException` being honoured exactly as designed — backoffs an hour
apart. The loop was removed anyway (see below), but on the arithmetic, not on this evidence.

### The other bug it exposed: one optional name sinks the whole certificate

The rate-limited identifier was `mta-sts.<domain>` — an **optional** SAN. A certificate order
is all-or-nothing: refuse one name and the CA refuses the order. So an optional, email-only
name denied the identity the certificate it needed for its apex, `capi` and `file` names.

The original gate tested whether the mta-sts record *resolves*. Resolving is a weaker
property than being able to serve the name's challenge, so it did not protect against this.

## The rules now

### Issuance never happens on a request path

The TLS handshake path looks the certificate up and, if it is missing, calls
`RequestIssuanceAsync` — which pulses the background issuer and returns. It never places an
order itself. `CreateCertificateAsync` is background-only, and its cancellation token must
have application lifetime, never a request's.

The trade, deliberately accepted: the first requests to a brand-new identity fail fast and
the client must retry, rather than one request blocking until the certificate exists. In
practice that blocking request never succeeded anyway — it stalled 60s and died.

### A cancelled order still counts as a failure

By the time an order is abandoned the CA has usually been asked to validate, so it has cost
the same allowance as a completed one. Cancellation records a backoff and then propagates.
If the cancellation is process shutdown this costs nothing, since the backoff map is
in-memory and dies with the process.

### One order per invocation

The retry loop is gone. Every iteration called `NewOrder` and created a fresh set of
authorizations; re-attempts belong to the backoff, which is spaced to respect the CA's limit.

### An optional name may not sink the order

If the CA's refusal names optional SANs and no required name, the optional names are dropped
and the order is placed once more. Ordering again immediately is safe precisely because the
names that remain are not the ones being refused. Working out which names a refusal
implicates uses `AcmeError.Subproblems` where the CA provides them, and otherwise the
hostname the CA writes into the detail text.

Having dropped an optional name, it must not be asked for again straight away. The
certificate is now missing a SAN that `NeedsRenewalAsync` thinks it ought to have, so the
next sweep would renew to re-add it, fail, drop it, and issue *another* certificate - a
duplicate every 12 hours, against a Let's Encrypt limit of five duplicates (identical name
set) per week. Optional names are therefore suppressed for seven days after a refusal, which
holds it to one. The suppression is node-local and lost on restart, so an operator who has
just fixed the record and bounced the service gets an immediate retry.

**Beware the suffix trap.** Every SAN has the apex as a suffix, so
`mta-sts.example.com` *contains* `example.com`. A substring test reads a complaint about the
optional name as a complaint about the apex as well, concludes a required name was refused,
and switches the fallback off in exactly the case it exists for. `MentionsName` matches on
label boundaries for this reason, and is tested for it.

### Nothing on a request path waits, for anything

`ServerCertificateSelector` looks the certificate up, calls `RequestIssuanceAsync`, and
returns. It catches everything, so nothing escapes to Kestrel as an unhandled connection
fault, and it logs a warning if the selector takes more than 5 seconds — a line that
should never appear on a healthy host.

`RequestIssuanceAsync` itself does not await the pulse it sends.
`BackgroundServiceManager.NotifyWorkAvailableAsync` waits up to **30 seconds** for the
background service to appear and then throws if it never does, which is exactly what
happens when `SystemBackgroundServicesEnabled` is false. Awaiting it would put a
30-second stall straight back onto the handshake path. The pulse is dispatched, its
failures are logged and swallowed, and the 12-hour sweep is the backstop.

The pulse is also **throttled to one per domain per minute**. It wakes a whole-registry
sweep, and the domain comes from attacker-chosen SNI: unthrottled, anyone could drive
back-to-back sweeps, each doing a registry read, a Redis lock attempt per domain, and an
authoritative DNS lookup for every tenant still missing its mta-sts SAN.

`CreateCertificateAsync` is the opposite: it blocks for the whole order and is
**background-only**. Its cancellation token must have application lifetime.
`RenewIfAboutToExpireAsync` does not wait for the lock either — whoever holds it is
ordering for the same domain, so waiting only risks a timeout and an alarming log line.

**Trade-off, deliberately accepted.** The first requests to a brand-new identity fail
fast and the client must retry, rather than one request blocking until the certificate
exists. In practice that blocking request never succeeded — it stalled 60s and died.

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

- **exponentially**: 5 minutes, then 10, 20, 40, capped at an hour,
- **30 seconds** for a transient CA error (`badNonce`, `serverInternal`), which RFC 8555
  expects to be retried and which says nothing about the domain, so it does not escalate
  the schedule,
- **the CA's own hour** when Let's Encrypt says `rateLimited`.

A flat five minutes was twelve attempts an hour, against an allowance of five failed
authorizations per hostname per hour — it breached the very limit it existed to protect.

The window is node-local on purpose. The node lock already stops two nodes ordering
at once, so the worst case is one wasted attempt per node per window, and losing the
state on restart is the right behaviour for an operator who has just fixed DNS and
bounced the service.

The failure is also still recorded in `Certificates.lastAttempt` / `lastError` for
operator visibility — **except** when the order was cancelled. A cancelled order backs
off in memory only: persisting it would stamp "cancelled before it completed" over the
real last error on every restart during an in-flight order, and would do a scoped
database write while the host is tearing down.

A backoff window that ended more than six hours ago is forgotten entirely, so a domain
that fails once every few months is not escalated to the hour cap forever.

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

**Gotcha when changing `RedisLock`:** it is a whitelisted singleton in
`src/apps/Odin.Hosting/_dev/AutofacDiagnostics.cs`, keyed by a hash of its constructor
signature. Change the constructor and that check logs
`MANUAL CHECK AND WHITE LISTING REQUIRED: ... = <newhash>` at Error level on every
startup, which fails ~54 `WebScaffold` tests via `DefaultAssertLogEvents`. Re-verify
that the new dependencies really are singletons, then update the hash to the one the
log reports. It only reproduces where Redis is configured (`RUN_REDIS_TESTS`); with
`NodeLock` the type is never registered, so a local run without that define stays
green.

## Reproducing

Provision an identity whose DNS records are not yet all live — or exhaust the LE
failed-authorization allowance for a hostname — then make HTTPS requests to it.
Before the fix each request hung ~30s and logged a `RedisLockException` from
`ServerCertificateSelector`. After it, the first request attempts issuance once,
records the failure, and every request for the next 5 minutes (or the hour LE asked
for) returns without locking, without a DNS lookup, and without a new order.
