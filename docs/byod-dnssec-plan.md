# BYOD DNSSEC: signing status, DS interchange with the parent, and a status API

Status: design/plan — not yet implemented. Written 2026-08-18. Builds on `docs/byod-dns-zone-plan.md` (shipped: PR #1638) and assumes the typed-domain refactor (PR #1640, `AsciiDomainName` through the registration/DNS services).

## Context

BYOD identities can now delegate their domain to our PowerDNS (`NS -> ns1.id.pub/ns2.id.pub`). `PowerDnsRestClient.CreateZone` already sends `dnssec: true, api_rectify: true`, so every zone we create is signed by PowerDNS from day one. **Verified live 2026-08-18** on the first real NS-delegation apex signup (`gabriel.ninja`, delegated at the registrar, zone created at Provision): the fresh zone serves a DNSKEY (flags 257, algorithm 13 = ECDSA P-256 — the auto-created CSK) and RRSIGs on its answers, and validating resolvers (1.1.1.1) resolve it fine in the no-DS "insecure" state. So the signing half needs zero additional work. What's missing is the other half of DNSSEC: the **DS record at the parent** (registrar for an apex, the user's DNS host for a subdomain). The possible states, which drive everything below:

- **No DS at the parent**: the zone is signed but the chain of trust stops at the parent — resolution works everywhere, just "insecure". Harmless default.
- **Wrong/stale DS at the parent** (e.g. left over from a previous DNS provider before delegating to us): validating resolvers SERVFAIL the domain — the identity goes dark for most of the internet, while our own authoritative-only validation checks still show green.
- **Parent zone itself unsigned** (registrar/DNS host without DNSSEC support): a DS cannot chain at all — nothing the user can do with us; the chain is broken upstream of our zone.
- **Matching DS at the parent**: fully validated chain.

Scope of this phase: **backend + API only.** The owner-console "DNS section" where users verify/add DNSSEC is a future phase — the API here is shaped for it. Deliverables: (1) make sure our zones really are signed and we can read their DS records, (2) a DNSSEC status service + anonymous read-only API that reports which state a domain is in and which DS record(s) to publish, (3) CDS/CDNSKEY publication so supporting registries install the DS automatically, (4) ops tooling to check the fleet.

## Verified starting points (odin-core, 2026-08-18)

- `PowerDnsRestClient.CreateZone` (`src/services/Odin.Services/Dns/PowerDns/PowerDnsRestClient.cs`) already passes `dnssec: true`, `api_rectify: true`. Zone deletion deletes keys with the zone — no orphan concern.
- `IPowerDnsApi` (Refit, `src/services/Odin.Services/Dns/PowerDns/IPowerDnsApi.cs`) models zones + rrsets only; **no cryptokeys or metadata endpoints yet**.
- DnsClient **1.8.0** (Odin.Core.csproj) supports `QueryType.DS` / `QueryType.DNSKEY` and parses `DsRecord` (KeyTag, Algorithm, DigestType, Digest) and `DnsKeyRecord` — verified in the package assembly.
- `DnsLookupService.GetParentDelegationNameServersAsync` (`src/services/Odin.Services/Registry/Registration/DnsLookupService.cs`) is the exact pattern to reuse: resolve the PARENT's authority via `IAuthoritativeDnsLookup`, query it directly with `Recursion = false, UseCache = false` (cache-safe, never touches public recursive resolvers). DS records live at the parent, same as delegation NS records — so DS state is checkable regardless of who hosts the child zone.
- PowerDNS cryptokeys API (`GET/POST /servers/localhost/zones/{zone_id}/cryptokeys`) returns per-key `{keytype, active, published, dnskey, ds: ["<keytag> <algo> <digesttype> <digest>"], ...}`; zone metadata via `/zones/{zone_id}/metadata`. **Modelled from PowerDNS API docs (outside this repo); shapes must be validated against the live dev server** — same caveat `CreateZone` had before PR #1638.

## DNSSEC status model (the heart of the feature)

New enum `DnssecStatus` + result DTO in `Odin.Services.Registry.Registration`:

| Verdict | Meaning | User action |
|---|---|---|
| `NotConfigured` | No PowerDNS on this deployment | — |
| `ZoneNotHosted` | No zone for the domain in our PowerDNS (not provisioned yet, or managed domain) | — |
| `ZoneNotSigned` | Zone exists but has no active published cryptokey (should not happen given `dnssec:true`; surfaced so ops can fix/backfill) | contact us / ops |
| `ParentUnsigned` | Our zone is signed, but the parent zone publishes no DNSKEY — a DS cannot extend the chain | none possible (informational; resolution works insecurely) |
| `DsMissing` | Parent is signed, no DS for the domain at the parent | add the DS we provide (registrar form for apex; DS record at the DNS host for a subdomain) |
| `DsMismatch` | DS records exist at the parent but **none** matches any of our keys — validating resolvers will SERVFAIL once delegation is live | replace/remove the DS |
| `Secure` | At least one published DS matches our keys | none — done |

Notes:
- Extra stale DS records *alongside* one matching one are fine — validation needs only one match — so `DsMismatch` fires only on zero matches.
- The DTO carries, alongside the verdict: our DS records (parsed keyTag/algorithm/digestType/digest — exactly what the user must enter), the parent's published DS records, `parentZoneSigned`, and best-effort `parentChainValidates` (AD flag from one recursive DNSKEY query for the parent apex against `Registry:DnsResolvers` — a positive query on an existing name, so no negative-caching risk; distinguishes "parent signed but its own chain is broken further upstream" from a truly working chain). If DnsClient can't surface the AD bit cleanly, the field is dropped — the verdict does not depend on it.
- Verdict computation is a pure static function (inputs: our DS set, parent DS set, parent-signed flag, zone/config flags) — unit-testable at the data level exactly like `AreDnsLookupsSuccessful`.

## Changes — odin-core

### 1. `IPowerDnsApi` + `PowerDnsRestClient` + `IDnsRestClient` (`src/services/Odin.Services/Dns/`)
- `[Get("/servers/localhost/zones/{zone_id}/cryptokeys")]` → `IList<Cryptokey>` + `Cryptokey` model (new file next to `Zone.cs`).
- Zone metadata endpoint (`PUT/POST /servers/localhost/zones/{zone_id}/metadata/{kind}`) for CDS publication.
- `IDnsRestClient.GetZoneDsRecords(string zoneId)` → parsed DS list from active+published keys (empty when zone unsigned). Prefer digest type 2 (SHA-256) first in ordering.
- `IDnsRestClient.SetZoneMetadata(zoneId, kind, values)` (or a dedicated `PublishCds(zoneId)`).
- Only if live verification shows `dnssec:true` does NOT auto-create keys: add `POST .../cryptokeys` (`{keytype: "csk", active: true}`) called from `CreateOwnDomainZone`. Not expected to be needed.

### 2. `DnsLookupService` (`src/services/Odin.Services/Registry/Registration/DnsLookupService.cs`)
- `GetParentDsRecordsAsync(AsciiDomainName domain, ...)` — clone of `GetParentDelegationNameServersAsync` with `QueryType.DS` (Answers + Authorities, referral edge cases included). Same parent-authority, non-recursive, no-cache discipline.
- `IsParentZoneSignedAsync(...)` — `QueryType.DNSKEY` for the parent apex at the parent's own authority.
- `GetParentChainValidatesAsync(...)` — the best-effort recursive AD check described above (nullable result).
- These stay lookup-only; PowerDNS access stays in `IdentityRegistrationService` (same split as today).

### 3. `IdentityRegistrationService` (`src/services/Odin.Services/Registry/Registration/IdentityRegistrationService.cs`)
- `GetDnssecStatusAsync(AsciiDomainName domain, CancellationToken)` orchestrates: config gate (`CanHostOwnDomainZones`) → managed-domain check → `ZoneExists` → `GetZoneDsRecords` → parent DS + parent-signed lookups → pure verdict function → DTO. Added to `IIdentityRegistrationService`.
- **CDS/CDNSKEY auto-publication (decided: in scope):** `CreateOwnDomainZone` sets zone metadata `PUBLISH-CDS` / `PUBLISH-CDNSKEY` after creation. Registries/DNS hosts that scan CDS per RFC 8078 (e.g. .dk, .ch, Cloudflare) then install/update the DS automatically with zero user action; everywhere else the records are harmless. Idempotent; the existing `create-own-domain-zones` CLI backfill applies it to existing zones on re-run (populate is already re-applied there).

### 4. API endpoint (`src/apps/Odin.Hosting/Controllers/Registration/RegistrationController.cs`)
- `GET /api/registration/v1/registration/dnssec-status/{domain}` — `ParseDomain` boundary, anonymous read-only (all returned material is public DNS data; it writes nothing, so no invitation-code gate). Returns the DTO with a camelCase verdict, same style as `create-own-domain-zone`. The future owner-console DNS section (and the provisioning app, if we later want it) consume it as-is.

### 5. CLI (`src/apps/Odin.Hosting/Cli/`)
- `own-domain-dnssec-status`: iterate registrations (skip managed domains), print domain + verdict + our DS records — the fleet check. Same config-before-service-resolution guard as `create-own-domain-zones`.

### 6. Docs
- Extend `docs/byod-dns-zone-plan.md` with a pointer here; this doc gets a "what remains manual per registrar" note once verified.

## Decisions already made

- **CDS/CDNSKEY auto-publication: in scope** (see §3).
- **Stale DS detection stays out of the signup verdict for now**: `own-domain-dns-status` (200/202) is unchanged this phase; `DsMismatch` is reported only by the new `dnssec-status` endpoint, for the future DNS section to surface. Revisit blocking/warning at signup when that UI lands.

## Tests

1. **Pure verdict tests** (`Odin.Services.Tests`): every `DnssecStatus` outcome from data-level inputs, incl. "extra stale DS + one matching = Secure", "no overlap = DsMismatch", "parent unsigned wins over missing DS".
2. **DS parsing tests**: cryptokey `ds` strings → typed records; unsigned zone (no keys / inactive keys) → empty.
3. **Mocked-lookup tests** (pattern from `AuthoritativeDnsLookupTest`): parent authority returns DS answers → parsed KeyTag/Digest; no DS → empty. (Verify `DsRecord` is mock-constructible like `SoaRecord`/`NsRecord` were; else raw-wire mock or `[Explicit]` live case.)
4. **`[Explicit]` live tests**: DS lookup for a known signed domain; cryptokeys GET against the dev PowerDNS.
5. **Live verification checklist** (manual, dev PowerDNS — required because the cryptokeys/metadata API surface has never run against a real server):
   - ~~`dig +dnssec @ns1.id.pub <zone> SOA` → RRSIG present~~ **DONE 2026-08-18** (`gabriel.ninja`: DNSKEY 257/alg-13 + RRSIGs served — `dnssec:true` auto-creates keys and signs).
   - `GET .../zones/<existing-byod-zone>/cryptokeys` → confirm the response shape our `Cryptokey` model expects (the signing itself is proven; the API model is not).
   - Metadata PUT + `dig CDS <zone> @ns1.id.pub` shows the CDS record.
   - End-to-end on a real delegated test subdomain whose parent is signed: publish the DS at the parent (or let CDS scanning do it), then `delv <domain>` / `dig +ad` validates.

## Explicitly out of scope

- Owner-console DNS section UI (future — the API is shaped for it).
- Provisioning-app UI changes.
- DNSSEC for managed domains (`id.pub`, `demo.rocks`, … apex zones): our own ops task — sign the apex zones + DS at OUR registrar; unrelated to per-user zones.
- Key rollover management/scheduling (PowerDNS defaults; revisit when the first rollover is due).
- ACME DNS-01 / wildcard certificates.
