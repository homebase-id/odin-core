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

### Why mta-sts could never validate — the actual root cause

Found on the third review pass, after the fallback below had already been built around it.
`MtaStsMiddleware` is registered in `Startup` **before** `CertesAcmeMiddleware`, and when
`Email.TenantMail.Enabled` is true it answers every request on an `mta-sts.*` host: the
policy file for `/.well-known/mta-sts.txt`, and **404 for everything else** — including
`/.well-known/acme-challenge/<token>`. `CertificateService` only adds the mta-sts SAN to the
order when that same flag is on. So in the only configuration that requests the name, the
challenge for it was refused by our own middleware. Deterministically, on every host, for
every tenant.

That is why DevOps saw Let's Encrypt *fetch* the challenge and still fail the authorization:
the request arrived and got a 404. It was never DNS timing. (`RedirectIfNotApexMiddleware`
avoids the same trap only because it passes plain HTTP through, and HTTP-01 arrives on port
80.)

The middleware now lets the ACME challenge path through. The optional-SAN fallback that #1715
had built around this — dropping refused optional names and re-ordering, with a persisted
seven-day suppression to stop the renewal loop that created — was **removed** once the real
cause was found. It was ~200 lines, it was where three review rounds concentrated their
findings, and its most fragile part scanned Let's Encrypt's human-readable error text for
hostnames. With the challenge answerable, the DNS-resolves gate is the protection that remains:
an optional name joins the order only when its record points here.

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
failures are logged at warning level rather than swallowed quietly.

**There is no backstop.** The background issuer is the only thing that orders certificates
now, so a host that terminates TLS with `SystemBackgroundServicesEnabled` false will never
obtain one. Startup logs a warning saying so; it does not refuse to start, because hosts
serving pre-provisioned certificates legitimately run with background services off.

The pulse is **floored at one every ten seconds for the whole host**. It wakes a
whole-registry sweep — a registry read, a certificate-store read per tenant, and an
authoritative DNS lookup for every tenant still missing its mta-sts SAN — and `SleepAsync`
returns immediately when the wake event is already set, so unthrottled pulses run sweeps back
to back. A per-domain throttle was tried alongside this and removed: it bounds one domain
while still admitting one pulse per domain per minute, so the global floor is the bound that
actually holds.

This is a limit on ordinary traffic, not a defence. `ServerCertificateSelector` only reaches
`RequestIssuanceAsync` after the host has recognised the domain as one of its own, so the
reachable set is domains this host already serves that currently have no certificate.

`CreateCertificateAsync` is the opposite: it blocks for the whole order and is
**background-only**. Its cancellation token must have application lifetime.

`RenewIfAboutToExpireAsync` **respects the failure backoff**, and must. It used to ignore it
because "the loop's own interval is its rate limiter" — true while the sweep only ran on its
12h timer, false the moment the sweep became pulsable from the request path. Otherwise a host
with a few certificate-less domains cycling out of their backoffs pulses the sweep several
times an hour, and each sweep re-places a full order for every *other* domain whose renewal
is failing: the same allowance burn, arriving on a different domain than the one pulsed.
The cost is bounded, since the backoff caps at an hour and renewal starts 7 days before
expiry. `RenewIfAboutToExpireAsync` does not wait for the lock either — whoever holds it is
ordering for the same domain, so waiting only risks a timeout and an alarming log line.

**Trade-off, deliberately accepted.** The first requests to a brand-new identity fail
fast and the client must retry, rather than one request blocking until the certificate
exists. In practice that blocking request never succeeded — it stalled 60s and died.

### The order lock is an ordinary blocking lock

`TryLockAsync` was added in the first fix and **reverted**. Its justification was the
handshake stall, and once issuance moved off the handshake path the only callers left were
background work, where waiting costs nothing. What it cost was a new primitive resting on
undocumented Nito `AsyncLock` behaviour: if a package bump ever changed the fast path, it
would silently never acquire, `CreateCertificateAsync` and `RenewIfAboutToExpireAsync` would
both return null forever, and **no certificate would ever be ordered again** — visible only
as a debug line. A latent total outage in exchange for tidier logs.

Both callers now use `LockAsync` and treat a timeout as "somebody else is already ordering
for this domain", which is what it means.

### Failed orders back off

`CertificateService` keeps a per-domain backoff window and refuses to start a new
order inside it:

- **exponentially**: 5 minutes, then 10, 20, 40, capped at an hour, for a failure the CA
  handed down — that is the schedule sized for the allowance it spent,
- **on a short schedule**, 30 seconds doubling to a 5-minute cap, for a *transient* failure:
  `badNonce`, `serverInternal`, a network error or timeout reaching the CA, a validation we
  gave up waiting for, or the pre-order DNS gate. None of these spent allowance and RFC 8555
  expects them retried. They still escalate, so a CA outage is not asked sixty times an hour,
- **the CA's own hour** when Let's Encrypt says `rateLimited`.

One counter drives both schedules: a domain that keeps failing, whatever the cause, earns
more caution.

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

## Testing against Let's Encrypt staging

Set `CertificateRenewal:UseCertificateAuthorityProductionServers` to `false`
(`CertificateRenewal__UseCertificateAuthorityProductionServers=false` as an environment
variable). `CertesAcme` then orders from `WellKnownServers.LetsEncryptStagingV2`, whose rate
limits are far higher than production's. *(That the limits are higher is Let's Encrypt's
documented behaviour, not something this repository establishes.)*

What the code guarantees, and what it does not:

- **ACME accounts are separated.** `CertificateService` stores the account key under
  `acme-account-staging-pem` or `acme-account-prod-pem`, so flipping the switch never touches
  the production account.
- **The switch is host-wide.** Every certificate the host orders while it is false is a staging
  certificate. Setting it false also turns on `AllowUntrustedServerCertificate` in the HTTP
  client factories and the peer CAPI handler, and turns off HSTS. Use a host serving only
  throwaway identities.
- **A staging certificate overwrites the production one.** `CertificateStore.PutCertificateAsync`
  upserts by domain. Any domain issued during the test loses its trusted certificate row.
- **A domain that already holds a valid certificate is not re-ordered.** Renewal fires only
  within seven days of expiry, or when a tenant certificate lacks the mta-sts SAN while tenant
  mail is on. A system domain carries no SANs, so a provisioning host's own certificate is
  safe unless it is about to expire — check `NotAfter` before assuming.
- **Switching back does not re-issue.** For the same reason: the staging certificate is valid
  for 90 days, so nothing triggers renewal. Delete the certificate row for each domain issued
  during the test to force a production order.
- `SystemBackgroundServicesEnabled` must be true on the host — the background issuer is the
  only thing that orders — and port 80 must be reachable for HTTP-01.

Restore checklist: set the switch back to `true` → delete the certificate rows for every
domain issued while it was `false` → deploy. There is no other persisted state to clear.

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
- `src/core/Odin.Core.Storage/Concurrency/RedisLock.cs` — owner token, hold-time
  diagnostics
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

Provision an identity on a host with tenant mail enabled and watch the order. Before the
middleware fix the `mta-sts` authorization failed every time — Let's Encrypt fetched the
challenge and received a 404 from `MtaStsMiddleware` — and the whole certificate failed with
it. After it, the order completes with all four names.

For the handshake stall: make HTTPS requests to an identity that has no certificate. The
handshake must return promptly with no certificate (the client sees a dropped connection and
retries), never wait, and never log `took … on the TLS handshake path`.
