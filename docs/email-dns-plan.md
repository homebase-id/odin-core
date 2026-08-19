# Email phase: the DNS records, key discovery via .well-known, and reverse DNS at scale

Status: design/plan — not yet implemented. Written 2026-08-19; revised same day for the SendGrid decision. This is the payoff phase the BYOD zone work (`docs/byod-dns-zone-plan.md`) and DNSSEC work (`docs/byod-dnssec-plan.md`) were built for. Scope here: the DNS/discovery surface only — the SMTP/inbound server, mailbox storage and key-management UX are separate plans.

**Outbound goes via SendGrid (decided 2026-08-19).** Consequences threaded through below: the outbound-IP story (PTR/FCrDNS/HELO/port 25/warm-up) becomes SendGrid's problem and disappears from our ops; per-tenant DKIM becomes SendGrid-issued CNAMEs (keys SendGrid-managed behind the delegation) plus per-tenant onboarding automation against SendGrid's API; the SPF indirection resolves to `include:sendgrid.net`. Inbound-side records (MX, DANE/TLSA, MTA-STS) are unaffected, assuming inbound MX stays ours. SendGrid also strengthens the E2E rationale: outbound transits a third party, so Homebase-to-Homebase traffic encrypted sender-side means SendGrid relays ciphertext only.

## The scale-defining decision: shared mail infrastructure, per-tenant identity

With ~1000 tenants on shared hosts, per-tenant mail *servers* don't exist — there are a handful of shared SMTP endpoints and outbound IPs. The design follows from that:

- **Tenant identity in email comes from the domain layer** (MAIL FROM + DKIM signature + SPF authorization), **never from the connecting IP**.
- **Everything per-tenant lives in the tenant's zone** (which we host and can write via the existing PowerDNS path); **everything per-infrastructure lives once, in an infra zone** (e.g. `id.pub`), so an IP or host change touches one zone, not 1000.

## Per-tenant DNS records (written into each identity's zone)

Example tenant `gabriel.ninja`; `<infra>` = the infra zone, e.g. `id.pub`.

| Record | Name | Value | Note |
|---|---|---|---|
| MX | apex | `10 mx1.<infra>.` | Shared MX target. MX targets must be A records, not CNAMEs (RFC 2181/5321) |
| TXT (SPF) | apex | `v=spf1 include:_spf.<infra> -all` | The `include:` indirection is the point: the provider authorization lives in ONE infra record |
| CNAME (DKIM) | `s1._domainkey`, `s2._domainkey` | `s1.domainkey.u<id>.wl.sendgrid.net.` etc. | SendGrid domain authentication: keys are SendGrid-managed behind CNAME delegation; values come per tenant from their API |
| CNAME (return path) | `em<id>` | `u<id>.wl.sendgrid.net.` | SendGrid bounce/return-path domain - this is what gives DMARC its SPF alignment |
| TXT (DMARC) | `_dmarc` | `v=DMARC1; p=reject; rua=mailto:...` | |
| TXT (MTA-STS) | `_mta-sts` | `v=STSv1; id=<version>` | Plus the policy file served at `https://mta-sts.<tenant>/.well-known/mta-sts.txt` — an extra CNAME/A record + cert on our web tier; the policy lists the shared MX |
| TXT (TLS-RPT) | `_smtp._tls` | `v=TLSRPTv1; rua=mailto:...` | |

Deliberately absent:

- **No per-tenant TLSA**: DANE binds to the **MX target hostname**, and that's shared — see infra zone below. One TLSA record covers all 1000 tenants.
- **No OPENPGPKEY record** — see "Key discovery" below.
- **No per-tenant PTR** — see "Reverse DNS" below.

## Infra zone records (once, in `<infra>`)

| Record | Name | Value | Note |
|---|---|---|---|
| A | `mx1` (`mx2`, …) | outbound/inbound mail IP(s) | |
| TLSA | `_25._tcp.mx1` | `3 1 1 <SPKI SHA-256>` | DANE for every tenant at once. Requires `<infra>` signed + anchored — verified already true for `id.pub`. Certificate rotation must keep the pinned key stable or update this record first |
| TXT (SPF target) | `_spf` | `v=spf1 include:sendgrid.net -all` | The single place the outbound provider is authorized; swapping providers touches this one record |

## Key discovery for E2E: .well-known, not DNS (decided 2026-08-19)

The E2E public keys are generated and managed inside Homebase (the machinery exists: `PublicPrivateKeyService` already backs the DID document's published key). Discovery goes over HTTPS, not DNS records:

- **Homebase ↔ Homebase**: the recipient's existing discovery surface — `.well-known/did.json` (`DidController`) / WebFinger (`WebFingerController`) — carries the encryption public key (a `keyAgreement` entry in the DID document alongside today's signing key). Senders fetch it over the recipient's authenticated HTTPS and encrypt **before** submission: true E2E.
- **External OpenPGP world**: the same key additionally served as **WKD** — `https://<tenant>/.well-known/openpgpkey/hu/<z-base32(sha1(localpart))>` — a new anonymous controller in the same family as the two above. GnuPG/Thunderbird/Proton-class clients actually fetch WKD.

Why not the OPENPGPKEY DNS record (RFC 7929): essentially zero client support; payload-size pressure on DNS; and its trust model is DNSSEC — which makes it *unverifiable exactly for the identities that cannot anchor* (managed domains inherit; e.g. Namecheap-hosted parents can't hold child DS records, verified 2026-08-18). WKD/DID ride the per-identity WebPKI certificate every identity already has, so they work for **all** tenants uniformly. DNSSEC's irreplaceable role in the email phase is transport (DANE/TLSA), not content keys.

Nuance to keep straight in later plans: giving the SMTP server the public key enables *encrypt-on-arrival* for mail from legacy senders (at-rest protection; the server briefly saw plaintext). True E2E is sender-side encryption via the discovery above. Both coexist.

## Reverse DNS (PTR): SendGrid's problem now — background kept for reference

PTR records belong to the IP, not to any tenant domain: reverse DNS is set by whoever owns the IP block, and receivers verify the CONNECTING IP's PTR/forward/HELO agreement (forward-confirmed reverse DNS). Tenant identity in email never comes from the IP - it comes from DKIM/SPF - so even self-hosted outbound would have needed only one PTR per relay IP, never one per tenant.

**With SendGrid outbound, none of this touches OVH**: SendGrid owns the sending IPs and their reverse DNS (including for dedicated-IP plans, where their UI manages it). Our servers submit to SendGrid over 587/443, so no port-25 egress unblocking, no IP warm-up, no reverse entries at OVH. Should outbound ever move in-house, the checklist is: one reverse entry per relay IP -> canonical hostname (OVH manager/API `/ip/{ip}/reverse`; OVH validates the forward A first), matching HELO, port-25 unblock request, dedicated stable IPs. (OVH platform details are outside this repo - verify when relevant.)

If inbound MX stays on our OVH machines: receiving requires no PTR; setting a truthful reverse entry on those IPs is good hygiene, nothing more.

## What the codebase needs (gaps found by reading, not guessing)

- **`IDnsRestClient`/`PowerDnsRestClient`** (`src/services/Odin.Services/Dns/`): today writes A + CNAME rrsets only (plus DNSSEC cryptokeys/CDS). Needs **MX and TXT** rrset support for tenant zones; TLSA only if the infra zone is also managed through this API.
- **`DnsLookupService.GetDnsConfiguration`** (`src/services/Odin.Services/Registry/Registration/`): gains the per-tenant email entries so zone populate and the owner-console records view pick them up. Additive entries only (`DnsConfig` layout is a frontend contract), and the email records must **not** join the identity-validation success rule (`AreDnsLookupsSuccessful`) or certificate checks — same isolation discipline as the optional `www` record.
- **`DnsConfigurationSet`** (+`OdinConfiguration`): new values — MX target host(s), SPF include target, DMARC/TLS-RPT report addresses.
- **WKD controller**: sibling of `WebFingerController`/`DidController` (`src/apps/Odin.Hosting/Controllers/Anonymous/`), serving the Homebase-managed OpenPGP key; DID document gains a `keyAgreement` entry (`DidService`, `src/services/Odin.Services/Fingering/`). OpenPGP certificate packaging needs an OpenPGP library (e.g. BouncyCastle, already referenced by the crypto layer) — use Curve25519 keys, keep the certificate minimal.
- **SendGrid onboarding automation**: per tenant, create the authenticated domain via SendGrid's API, fetch the issued CNAMEs, write them into the tenant zone (existing CNAME rrset support suffices), trigger SendGrid's validation. Check SendGrid plan limits on authenticated-domain count (~1000 needed) — outside-repo assumption to verify.
- **CLI/backfill**: the existing `create-own-domain-zones commit` re-populate path applies new records to existing zones once `GetDnsConfiguration` includes them — the SendGrid CNAMEs are per-tenant values, so they flow through the onboarding automation rather than static config.
- **Owner console**: the Security→DNS panel's records block renders whatever the status endpoint returns, so MX/TXT rows appear once emitted; DNSSEC red-dot semantics already upgraded for the DANE era.

## Out of scope

- The SMTP server itself (inbound MX, outbound relay, submission), mailbox storage, client UX.
- Key management/rotation UX (couple its design with the deferred DNSSEC key-rollover work).
- MTA-STS policy hosting implementation details.
- Third-party DNS (manual-records) tenants: they get the same record list as instructions instead of zone writes — the provisioning `dns-config` endpoint already carries unknown record types to the UI safely.
