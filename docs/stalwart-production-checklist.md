# Stalwart in production — what we learned the hard way

Everything here cost us time on `bleeding` on 2026-08-26. None of it is guesswork:
each item is something that actually went wrong, with the symptom that gave it away.

Read alongside `docs/stalwart-production-setup.md` (how to set it up) and
`docs/stalwart-admin-api-notes.md` (the JMAP wire contract). This file is the
"things that will bite you" list.

---

## 1. Settings do not take effect until Stalwart restarts

**The single biggest time sink.** We configured the outbound relay route and the routing
strategy, verified by reading them back over the API that they were exactly right, and mail
kept being delivered direct to the recipient's MX for over an hour.

There is no reload endpoint (`/api/reload` and friends all 404). Settings written through
the admin API or the web UI land in the store but the running server keeps using what it
loaded at startup.

- **Always restart after changing configuration**, and verify the *behaviour*, not the
  stored value. Reading back what you wrote proves the write, not the effect.
- Any automation that configures Stalwart must restart it afterwards, or its changes are
  invisible. This applies to the certificate reconciler as much as to routing.
- The give-away: the built-in defaults produce plausible-looking behaviour. Route selection
  falls back to `mx` (direct delivery) with **no error and no log line**, so a strategy that
  is never read looks like a strategy that chose `mx`.

## 2. Outbound routing needs a strategy, not just a route

Creating a relay route does nothing on its own. Route selection happens in the
`MtaOutboundStrategy` **singleton**, whose `route` expression names a route per recipient:

```json
{ "match": { "0": { "if": "is_local_domain(rcpt_domain)", "then": "'local'" } },
  "else": "'SMTP2GO'" }
```

- Values are **expressions**, so the route name needs inner quotes: `'SMTP2GO'`, not `SMTP2GO`.
- `route` and `schedule` select from **different namespaces**. `route` names an `MtaRoute`
  (`mx`, `local`, your relay); `schedule` names an `MtaDeliverySchedule` (`local`, `remote`,
  `dsn`, `report`). `queueName = "remote"` in the logs is the *schedule*, and says nothing
  about which route delivered.
- Keep the `local` and `mx` routes. `local` is how inbound reaches mailboxes; `mx` is the
  fallback Stalwart uses when a strategy names a route it cannot resolve.

**Wire format gotcha:** `match` is an object keyed by `"0"`, `"1"`, … even though the schema
renders the defaults as an array. Sending an array returns `invalidPatch`.

**Reading it back:** `MtaOutboundStrategy/get` with `ids: null` returns an EMPTY list. You
must pass `ids: ["singleton"]`. A UI or script that queries the first way will tell you
nothing is configured when it is.

## 3. Turn off automatic DKIM management per domain

`x:Domain.dkimManagement` defaults to `Automatic`, which mints Stalwart's own keypair the
moment a domain is created — selectors from a `v{version}-{algorithm}-{date}` template — and
**rotates it every 90 days**.

We publish DNS only for our own `s1`/`s2`, so those keys sign mail nothing can verify. On
real mail to Gmail:

```
dkim=permerror (no key for signature) header.s=v1-ed25519-20260826
dkim=pass                             header.s=s2
```

- Create domains with `dkimManagement: {"@type": "Manual"}`. odin-core's provisioning does
  this now.
- There is **no server-wide setting** — it is per-domain only.
- Deleting the keys is not enough on its own: rotation mints replacements later.
- Applies to the **host's own domain** too, not just tenants. Its bounce notices and
  DMARC/TLS reports were signed with unverifiable keys.

## 4. Ports: three of them, and inbound 25 is separate from outbound 25

Only 993 was open initially. Every SMTP port was shut, which presents as Thunderbird hanging
on "sending message" with no error until TCP times out.

| Port | Direction | Needed for |
|---|---|---|
| 25 | **inbound** | delivery from the world to your MX. Without it the MX record is decorative |
| 465 | inbound | client submission (implicit TLS, which is what our autoconfig advertises) |
| 993 | inbound | IMAP |
| 25 | outbound | **not needed** — the relay handles sending, and Hetzner blocks it by default |

Hetzner blocking *outbound* 25 is why a relay exists at all. Do not let "we got 25 opened"
be read as "we no longer need the relay": the relay is for deliverability and IP reputation,
not only for the port.

## 5. Stalwart ships with no TLS certificate

It generates a self-signed `CN=rcgen self signed cert` (valid 1975→4096) and logs
`No TLS certificates available` every 30 seconds. Consequences:

- Every mail client needs a manual security exception — untenable for real users, and it
  trains people to click through certificate warnings on a mail app.
- Fine only while MTA-STS is `mode: testing`. **Before switching to `enforce`, this must be
  fixed**, or enforcing senders stop delivering.

Certificates can be **pushed** over the API — `x:Certificate` has `certificate` and
`privateKey` as mutable PEM fields, with `notValidAfter` server-set (so a reconciler can
compare and push only on change). Stalwart also has its own ACME, but HTTP-01/TLS-ALPN-01
are unavailable when odin-core owns 80/443, and DNS-01 needs a supported provider —
PowerDNS is not documented as one.

## 6. The admin UI's Logs and Traces are Enterprise-only

`x:Log` and `x:Trace` return
`"This feature is only available in the Enterprise edition"`. A blank Log Entries screen is
expected, not a sign that nothing happened.

Use the container logs instead — `docker logs` — which contain everything you need:
route selection, relay connection, delivery result. `~/odin-bleeding-stalwart-logs.sh` wraps
this. **Filter for failure words as well as success ones**; a filter matching only the happy
path goes silent exactly when something breaks.

## 7. Things that look broken but are not

- **`DKIM verification failed ... result = []`** on submission is Stalwart *verifying* an
  inbound message that carries no signatures (clients do not sign). Not about your keys.
- **`TLSA record not DNSSEC signed`** — informational, DANE simply is not in use.
- **`Error fetching MTA-STS policy ... Server Failure`** is usually the *recipient's* DNS,
  not yours. Check the same lookup from a public resolver before investigating your own.
- **A record that "did not publish"** may be negative-cached. Probing a name *before*
  creating it caches NXDOMAIN for the TTL. Always verify against the authoritative
  nameserver (`dig … @ns1.<zone>`), not a public resolver.

## 8. Verify with a real message, not with DNS

DNS being 10/10 says nothing about whether mail flows. Dickus showed a fully green Email tab
for an afternoon while nothing could reach it.

The only test that proves the pipeline is a message sent to an address whose **raw headers**
you can read (Gmail is ideal — it adds `Authentication-Results`). Check:

- `Received:` — shows the relay hop, or its absence
- `Return-Path:` — should be the relay's bounce subdomain if relaying
- `spf=pass` — and note *which* domain it was evaluated against
- one `dkim=pass` per selector you expect, and **no `permerror`**
- `dmarc=pass`

**Beware self-addressed tests.** Mail to your own domain takes the `local` route and never
touches the relay, so it proves nothing about outbound.

## 9. Keep both DKIM algorithms

Gmail reports our ed25519 signature as `dkim=neutral (no key)` while RSA passes — it does not
appear to validate ed25519 (RFC 8463). RSA is doing all the work at the largest mailbox
provider. Do not "simplify" to ed25519-only.

That redundancy has already earned its keep: one message passed DMARC under `p=reject` on
`s2` alone, while SPF hard-failed.
