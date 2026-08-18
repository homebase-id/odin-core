# Owner console: generic DNS health + DNSSEC panel (Security tab)

Status: design/plan — not yet implemented. Written 2026-08-18. Companion to `docs/byod-dnssec-plan.md` (the PowerDNS-side backend phase); the two are **independent** — neither blocks the other — but they compose (see "Relationship" below).

## Context

Decided earlier: DNSSEC has no place in the signup flow (the zone doesn't exist before Provision), so the user-facing surface is the owner console. This plan adds that surface: a panel under the **Security** tab where the owner can **verify** their domain's DNS health — the status of every required DNS record, an optional `www` check, and the DNSSEC chain of trust with the exact DS record to **add** at their registrar/DNS host. (Decided 2026-08-18: record status rides along — the machinery is already generic, and one "is my DNS healthy" surface beats two.)

Design principle (user-set): **generic DNS only — no PowerDNS coupling.** PowerDNS is merely our DNS server; the panel must work identically when the identity's DNS is hosted by us (BYOD delegated zone), by a third party (manual-records BYOD, e.g. Cloudflare), or self-hosted. This is achievable because everything the panel needs is public DNS data:

- The domain's **DNSKEY** is served by whatever nameservers host the zone (verified live: `gabriel.ninja` serves its auto-created CSK publicly).
- The **DS the user must publish is computable from that DNSKEY** (RFC 4034: key tag algorithm + SHA-256 digest over owner name + DNSKEY RDATA) — no privileged API needed. Where the zone publishes **CDS** records (ours will, once `byod-dnssec-plan` ships; some third-party hosts do too), those are preferred verbatim — CDS RDATA *is* the desired DS.
- The **published DS**, **parent's signing state**, and the **zone cut** are all plain authoritative queries.

Because it is fully generic, the panel also degrades gracefully for every identity type:

- **BYOD, delegated to us**: full flow — status + DS to add + where to add it.
- **BYOD, manual records / third-party DNS**: same flow; whether the zone is signed at all depends on their DNS host ("your DNS host does not sign this zone" state).
- **Managed domain** (`frodo.id.pub`): the identity domain is not a zone cut — DNSSEC is inherited from the apex zone we operate. Read-only informational state ("handled by Homebase"), detected generically via zone-apex lookup.
- **Self-hosted**: works out of the box; nothing assumes our infrastructure.

## Verified starting points

- `Odin.Core.Dns.AuthoritativeDnsLookup` resolves a domain's authority and zone apex; `DnsLookupService.LookupZoneApexAsync` already exposes apex lookup. The parent-authority + `Recursion=false, UseCache=false` query discipline from `GetParentDelegationNameServersAsync` (`src/services/Odin.Services/Registry/Registration/DnsLookupService.cs`) is the pattern for all queries here (cache-safe).
- DnsClient 1.8.0 parses `DnsKeyRecord` (flags/protocol/algorithm/public key) and `DsRecord` (KeyTag/Algorithm/DigestType/Digest); `QueryType.DS`/`QueryType.DNSKEY` exist. CDS (type 59) has no enum member — query it as a raw type cast or parse `UnknownRecord` RDATA (it shares the DS wire format); if that fights the library, skip CDS and always compute from DNSKEY — computation is the universal path anyway.
- The tenant/identity host already runs DNS lookup machinery (`CertificateService` uses `IDnsLookupService`), so an owner endpoint doing authoritative queries introduces no new infrastructure.
- Owner API pattern: `src/apps/Odin.Hosting/Controllers/OwnerToken/*` (e.g. `Configuration`); owner-console settings pages: `odin-js/packages/apps/owner-app/src/templates/Settings/*` + hooks-per-feature convention.

## Changes — odin-core

### 1. `Odin.Core.Dns.DnssecLookup` (new, beside `AuthoritativeDnsLookup`)
Generic, config-free primitives — deliberately in Odin.Core so the registration-host service from `byod-dnssec-plan.md` reuses them instead of implementing its own parent-DS/DNSKEY queries:
- `GetZoneDnsKeysAsync(zone)` / `GetParentDsRecordsAsync(domain)` / `IsZoneSignedAsync(zone)` — authoritative, non-recursive, no-cache.
- `ComputeDsFromDnsKey(ownerName, dnskey, digestType)` — RFC 4034 key tag (App. B) + digest over canonical owner name + RDATA. Pure function.
- `GetCdsRecordsAsync(zone)` — best-effort (see CDS caveat above).

### 2. Owner endpoint (`src/apps/Odin.Hosting/Controllers/OwnerToken/` — new `Dns/` controller)
`GET /api/owner/v1/dns/status` — owner-authenticated, no parameters (the tenant's own domain from `IOdinContext`). One response with three parts:

**a) `records`** — the status of every required DNS record, straight from the existing generic `IDnsLookupService.GetAuthoritativeDomainDnsStatusAsync` (authoritative-only, cache-safe; already registered on identity hosts — `CertificateService` uses it there today). Same `DnsConfig` payload the provisioning screens consume: apex A/ALIAS, capi/file CNAMEs, and the NS rows showing delegation state. Delegated-to-us zones read all-green automatically; manual-records users see exactly which record broke — useful long after signup (changed DNS host, deleted record, etc.).

**b) `optionalRecords`** — the optional `www.{domain}` check. Deliberately NOT added to `GetDnsConfiguration`: that list feeds the signup success rule (`AreDnsLookupsSuccessful`) and certificate checks, and a missing optional record must never fail either. Separate lookup instead: `www` is healthy when it is a CNAME to the domain (or the apex alias) or an A record matching the apex A. Three informational states, none of them errors: `success`, `notSet` ("not set — that's fine"), `pointsElsewhere` ("not pointing at your identity — fine if intentional", e.g. a deliberate separate www site).

**c) `dnssec`** — the chain-of-trust verdict: zone-apex lookup → managed/inherited detection (apex != domain) → zone DNSKEY → parent DS. Mirrors the DTO from `byod-dnssec-plan.md` (same verdict names where they overlap) plus:
- `Inherited` (not a zone cut; DNSSEC governed by the enclosing zone — terminal, informational),
- `ZoneUnsigned` replaces the PowerDNS-specific `ZoneNotSigned` ("whoever hosts your DNS does not sign it"),
- `dsToPublish`: CDS verbatim when present, else computed from the active KSK/CSK DNSKEYs (digest type 2).
No PowerDNS states (`NotConfigured`/`ZoneNotHosted`) — they don't exist generically.

### 3. Shared-code note for `byod-dnssec-plan.md`
When that phase is implemented, its `DnsLookupService` additions (`GetParentDsRecordsAsync`, `IsParentZoneSignedAsync`) should be thin wrappers over `DnssecLookup` from §1. Whichever plan lands first builds `DnssecLookup`; the other consumes it.

### 3b. Security health email (decided 2026-08-18)
The existing monthly security health email (`SecurityHealthCheckJob` → `OwnerSecurityHealthService.GetSecurityNeedsAttentionStatus` → `RecoveryNotifier`, `src/services/Odin.Services/Security/`) is only sent when something needs attention — DNSSEC joins it:

- The owner-side DNSSEC status service from §2 (generic lookups — the job runs in tenant scope, so per the architectural boundary the PowerDNS API is off-limits here anyway) is consulted during the health check.
- **Counts toward needs-attention (and appears in the email):**
  - `DsMismatch` — always: the domain is (or will be, once delegation is live) SERVFAIL for validating resolvers; the email names the stale DS records to remove/replace.
  - `DsMissing` — when the parent is signed: the chain is one user-actionable record away; the email carries the DS tuple and points at the Security-tab panel.
- **Does not trigger or appear:** `Secure`, `Inherited` (managed domains — our responsibility), `ParentUnsigned` and third-party `ZoneUnsigned` (not actionable through us; nagging monthly about a registrar's missing DNSSEC support helps no one).
- Best-effort, same rule as the provisioning email: a DNSSEC lookup failure must never block or delay the health report, and must not by itself count as needs-attention.

## Changes — odin-js (owner-app)

### 4. Security tab → DNS health panel (`packages/apps/owner-app/src/templates/Settings/`)
The panel lives under the owner console's **Security** tab (decided 2026-08-18) — DNS health and chain-of-trust verification are security concerns, and the tab already exists. New `DnsSecuritySettings.tsx` (pattern: existing `*Settings.tsx` files) rendered in the Security section; if a broader "DNS" section materializes later (record overview editing, email DNS), the panel can move there then. Three stacked blocks fed by the one endpoint:
- **Records**: the required records with green/orange status rows (rendering modeled on the provisioning `DnsSettingsView`, simplified — read-only, no setup instructions), NS rows shown as delegation state.
- **Optional www**: one quiet row; `notSet`/`pointsElsewhere` are neutral text, never warning-colored.
- **DNSSEC status display** per verdict: Secure (green, show matched DS), DsMissing (amber: "signed but not anchored" + the add-DS instructions), DsMismatch (red: stale DS warning + which records to remove/replace), ZoneUnsigned / ParentUnsigned (informational, with "what would need to change" text), Inherited (quiet informational).
- **DS table**, copyable per-field (Key tag / Algorithm / Digest type / Digest) — matching registrar forms (e.g. Squarespace) column-for-column.
- **Where-to-add guidance**: apex → "at your registrar"; subdomain → "as a DS record next to your NS records at your DNS host". Apex/subdomain detection reuses the name-based `registrableDomain` helper from the provisioning app — move it to `common-app` (no new npm deps; it is dependency-free).
- **Verify button** → refetch (react-query), same spinner discipline as the provisioning screens (local state, not background-fetch state).
- New hook `useDnsStatus` following the owner-app hook conventions (one query for the whole panel).

## Relationship to `byod-dnssec-plan.md`

Independent by design. The backend phase still matters for: CDS **publication** (so supporting registries auto-install the DS — the panel then shows Secure without the user doing anything), the provisioning-email DS instructions, the registration-host `dnssec-status` endpoint (pre-console consumers, ops), and the fleet CLI. This panel is the *read/verify* side and works today against any DNS host. Implementation order is flexible; §1/§3 define the shared core either way.

## Tests

1. **`ComputeDsFromDnsKey` data-level tests**: RFC 4034 test vectors, plus a fixture captured from a real signed domain (DNSKEY + its published DS must match the computation) — this function is the correctness heart of the generic approach.
2. **Verdict tests**: pure-function coverage incl. `Inherited` (apex != domain) and `ZoneUnsigned`.
2b. **Optional-www tests**: healthy via CNAME-to-domain, CNAME-to-alias and matching-A; `notSet` and `pointsElsewhere` are informational; the signup success rule and certificate checks are provably unaffected (www never enters `GetDnsConfiguration`).
3. **Mocked-lookup tests** (pattern: `AuthoritativeDnsLookupTest` mocked server tree) for the query orchestration.
4. **`[Explicit]` live test**: computed DS for a known signed domain equals its published DS (e.g. `internetsociety.org`; later `gabriel.ninja` once its DS is at the registrar).
5. **Security email tests**: needs-attention flips on `DsMismatch` and on signed-parent `DsMissing`; stays quiet on `Secure`/`Inherited`/`ParentUnsigned`; a throwing DNSSEC lookup neither blocks the report nor counts as attention.
6. **Manual E2E**: owner console on a delegated BYOD identity shows all records green + DsMissing with values matching `dig DNSKEY` + `dnssec-dsfromkey`; add the DS at the registrar; Verify flips to Secure; a managed-domain identity shows records green + Inherited; break a manual-records domain's CNAME and see the records block flag exactly that row.

## Out of scope

- A dedicated DNS section (record *editing*, propagation checks, email DNS) — if/when it materializes, this panel can migrate there from Security.
- Creating the optional `www` record in delegated zones (a write path; belongs to future record management).
- Any write path (we cannot write to registrars; CDS publication is the backend plan's).
- Key rollover UX.
