# Email phase: the DNS records, key discovery via .well-known, and reverse DNS at scale

Status: design/plan — not yet implemented. Written 2026-08-19. This is the payoff phase the BYOD zone work (`docs/byod-dns-zone-plan.md`) and DNSSEC work (`docs/byod-dnssec-plan.md`) were built for. Scope here: the DNS/discovery surface only — the SMTP server itself, mailbox storage and key-management UX are separate plans.

## The scale-defining decision: shared mail infrastructure, per-tenant identity

With ~1000 tenants on shared hosts, per-tenant mail *servers* don't exist — there are a handful of shared SMTP endpoints and outbound IPs. The design follows from that:

- **Tenant identity in email comes from the domain layer** (MAIL FROM + DKIM signature + SPF authorization), **never from the connecting IP**.
- **Everything per-tenant lives in the tenant's zone** (which we host and can write via the existing PowerDNS path); **everything per-infrastructure lives once, in an infra zone** (e.g. `id.pub`), so an IP or host change touches one zone, not 1000.

## Per-tenant DNS records (written into each identity's zone)

Example tenant `gabriel.ninja`; `<infra>` = the infra zone, e.g. `id.pub`.

| Record | Name | Value | Note |
|---|---|---|---|
| MX | apex | `10 mx1.<infra>.` | Shared MX target. MX targets must be A records, not CNAMEs (RFC 2181/5321) |
| TXT (SPF) | apex | `v=spf1 include:_spf.<infra> -all` | The `include:` indirection is the point: relay IPs are maintained in ONE infra record |
| TXT (DKIM) | `<selector>._domainkey` | `v=DKIM1; k=ed25519; p=<key>` | Per-tenant signing key, Homebase-managed. Two selectors (ed25519 + rsa-2048) for legacy receivers and for rotation |
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
| TXT (SPF target) | `_spf` | `v=spf1 ip4:<relay-ip> ip4:<relay-ip2> -all` | The single place relay IPs are listed |

## Key discovery for E2E: .well-known, not DNS (decided 2026-08-19)

The E2E public keys are generated and managed inside Homebase (the machinery exists: `PublicPrivateKeyService` already backs the DID document's published key). Discovery goes over HTTPS, not DNS records:

- **Homebase ↔ Homebase**: the recipient's existing discovery surface — `.well-known/did.json` (`DidController`) / WebFinger (`WebFingerController`) — carries the encryption public key (a `keyAgreement` entry in the DID document alongside today's signing key). Senders fetch it over the recipient's authenticated HTTPS and encrypt **before** submission: true E2E.
- **External OpenPGP world**: the same key additionally served as **WKD** — `https://<tenant>/.well-known/openpgpkey/hu/<z-base32(sha1(localpart))>` — a new anonymous controller in the same family as the two above. GnuPG/Thunderbird/Proton-class clients actually fetch WKD.

Why not the OPENPGPKEY DNS record (RFC 7929): essentially zero client support; payload-size pressure on DNS; and its trust model is DNSSEC — which makes it *unverifiable exactly for the identities that cannot anchor* (managed domains inherit; e.g. Namecheap-hosted parents can't hold child DS records, verified 2026-08-18). WKD/DID ride the per-identity WebPKI certificate every identity already has, so they work for **all** tenants uniformly. DNSSEC's irreplaceable role in the email phase is transport (DANE/TLSA), not content keys.

Nuance to keep straight in later plans: giving the SMTP server the public key enables *encrypt-on-arrival* for mail from legacy senders (at-rest protection; the server briefly saw plaintext). True E2E is sender-side encryption via the discovery above. Both coexist.

## Reverse DNS (PTR) at OVH — per IP, never per tenant

PTR records live in the reverse zone of whoever owns the IP block — for OVH-hosted machines that is **OVH's control panel/API**, not our PowerDNS and not any registrar. The critical scale fact: **the PTR count equals the number of outbound mail IPs, not tenants.** A thousand tenant domains share the same few PTRs; nothing is ever added at OVH per tenant.

What must be right at OVH (per outbound mail IP):

1. **Reverse entry** on the IP → the canonical relay hostname (e.g. `mx1.<infra>`), set in the OVH manager or via API (`/ip/{ip}/reverse`).
2. **Forward-confirmed reverse DNS**: `mx1.<infra>` must have an A record resolving back to that same IP — and OVH validates that the forward name resolves **before** it accepts the reverse entry, so create the A record first.
3. **HELO/EHLO** of the outbound relay = the same canonical hostname. PTR ↔ A ↔ HELO all agreeing is what receivers score.
4. **Port 25 egress**: OVH blocks outbound SMTP by default on cloud instances until you request the block removed (anti-spam policy). Do this before any deliverability testing.
5. **Dedicated, stable mail IPs**, separate from web-ingress IPs if possible: IP reputation is earned slowly (warm-up) and lost quickly; you don't want web-tier IP churn or a compromised web host burning the mail reputation.

(Items 2's OVH-validation detail and 4 describe OVH platform behavior — outside this repo, verify against their current docs when setting up.)

## What the codebase needs (gaps found by reading, not guessing)

- **`IDnsRestClient`/`PowerDnsRestClient`** (`src/services/Odin.Services/Dns/`): today writes A + CNAME rrsets only (plus DNSSEC cryptokeys/CDS). Needs **MX and TXT** rrset support for tenant zones; TLSA only if the infra zone is also managed through this API.
- **`DnsLookupService.GetDnsConfiguration`** (`src/services/Odin.Services/Registry/Registration/`): gains the per-tenant email entries so zone populate and the owner-console records view pick them up. Additive entries only (`DnsConfig` layout is a frontend contract), and the email records must **not** join the identity-validation success rule (`AreDnsLookupsSuccessful`) or certificate checks — same isolation discipline as the optional `www` record.
- **`DnsConfigurationSet`** (+`OdinConfiguration`): new values — MX target host(s), SPF include target, DMARC/TLS-RPT report addresses.
- **WKD controller**: sibling of `WebFingerController`/`DidController` (`src/apps/Odin.Hosting/Controllers/Anonymous/`), serving the Homebase-managed OpenPGP key; DID document gains a `keyAgreement` entry (`DidService`, `src/services/Odin.Services/Fingering/`). OpenPGP certificate packaging needs an OpenPGP library (e.g. BouncyCastle, already referenced by the crypto layer) — use Curve25519 keys, keep the certificate minimal.
- **CLI/backfill**: the existing `create-own-domain-zones commit` re-populate path applies new records to existing zones once `GetDnsConfiguration` includes them — no new tooling needed.
- **Owner console**: the Security→DNS panel's records block renders whatever the status endpoint returns, so MX/TXT rows appear once emitted; DNSSEC red-dot semantics already upgraded for the DANE era.

## Out of scope

- The SMTP server itself (inbound MX, outbound relay, submission), mailbox storage, client UX.
- Key management/rotation UX (couple its design with the deferred DNSSEC key-rollover work).
- MTA-STS policy hosting implementation details.
- Third-party DNS (manual-records) tenants: they get the same record list as instructions instead of zone writes — the provisioning `dns-config` endpoint already carries unknown record types to the UI safely.
