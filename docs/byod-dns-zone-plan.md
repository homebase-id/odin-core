# BYOD: always create a PowerDNS zone per own-domain identity (apex included) + NS-delegation support

Status: design/plan — not yet implemented. Written 2026-08-15.

## Context

Today BYOD (bring-your-own-domain) users must create 3–4 DNS records manually; upcoming email support would multiply that. Target model: every BYOD identity domain (subdomain **or apex**) gets a zone in our PowerDNS, created unconditionally at signup — whether or not the user delegates. Users can then either keep the manual-records flow (zone sits dormant and harmless) or delegate via NS records (subdomain: 2 NS records at their DNS host; apex: nameserver change at their registrar) and never touch DNS again. Decisions baked into this plan: the zone is created at the same point in the flow where managed domains get their records written (explicit call when the user commits to the domain, before provisioning); delegation detection IS in scope; existing identities are backfilled via a CLI command.

## Verified starting points (odin-core, `main` as of 2026-08-15)

- `IDnsRestClient` / `PowerDnsRestClient` (`src/services/Odin.Services/Dns/`): `CreateZone(zoneName, nameServers[], adminEmail)` and `DeleteZone` **exist but are dead code — no callers anywhere**. `CreateZone` already passes `nameservers` (PowerDNS creates the in-zone NS rrset) and a hand-built SOA, `kind=Native`, `dnssec=true`, `api_rectify=true`. Never validated against a live server.
- `CreateARecords`/`CreateCnameRecords`/`Delete*` always write `{name}.{zoneId}` — **cannot write records at the zone apex** (empty name → `.zone.`). Managed domains never hit this (they write `prefix` labels into shared apex zones).
- Record source of truth: `DnsLookupService.GetDnsConfiguration` (`src/services/Odin.Services/Registry/Registration/DnsLookupService.cs`) — apex A (`Registry:DnsRecordValues:ApexARecords[0]`), apex ALIAS (`ApexAliasRecord`), `capi`/`file` CNAMEs.
- Verification: `GetAuthoritativeDomainDnsStatusAsync` resolves the domain's authority iteratively (`Odin.Core.Dns.AuthoritativeDnsLookup`) and queries records there; `GetExternalDomainDnsStatusAsync` asks public resolvers. **Once a domain is delegated to us, both checks pass with zero changes** — our PowerDNS is the authority and serves the expected records (the success rule "A or ALIAS, plus all CNAMEs" is satisfied by the zone's apex A + CNAMEs). Delegation detection is therefore additive UX, not a rewrite.
- Deletion paths: `IdentityRegistrationService.DeleteOwnDomain` (DEBUG endpoint) and `Odin.Services.Admin.Tenants.Jobs.DeleteTenantJob` (the real one, calls `identityRegistry.DeleteRegistration`).
- PowerDNS availability gate pattern: `GetManagedDomainApexes` returns empty when `Registry:PowerDnsApiKey` + `PowerDnsHostAddress` are both blank — reuse this to no-op all zone logic for self-hosters.
- CLI command pattern: `src/apps/Odin.Hosting/Cli/Commands/` (e.g. `CreateCdnCat.cs`, `DockerSetup.cs`).

## Does this need new API calls to our DNS service?

**PowerDNS server HTTP API: no new endpoints.** Everything uses the already-modelled operations: `POST /zones` (create zone incl. SOA + NS), `PATCH /zones/{id}` (rrsets), `GET /zones/{id}`, `DELETE /zones/{id}`.

**Our `IDnsRestClient` wrapper: yes, three changes:**
1. **Apex record support** — change `CreateARecords`/`DeleteARecords` (and for symmetry the CNAME pair) to treat `name == ""` as the zone apex (`name == "" ? zoneId : $"{name}.{zoneId}"`). Managed-domain callers always pass a prefix, so this is backward compatible.
2. **`ZoneExistsAsync(zoneId)`** helper (GetZone + catch Refit 404) so zone creation is idempotent.
3. **First real use of `CreateZone`/`DeleteZone`** — validate against a live PowerDNS: the interplay of the explicit SOA rrset with the `nameservers` field, and the hardcoded SOA serial `1111` (with `api_rectify` PowerDNS manages serials via `SOA-EDIT-API`; adjust content if the server rejects it).

No TXT/MX/SRV support yet — that's the future email phase.

## Changes (odin-core)

### 1. Config — `OdinConfiguration.RegistrySection` (`src/services/Odin.Services/Configuration/OdinConfiguration.cs`)
- `Registry:DnsRecordValues:NameServers` — `List<string>`, our authoritative NS hostnames (e.g. `ns1.…`, `ns2.…`). Required when PowerDNS is configured.
- `Registry:DnsRecordValues:SoaAdminEmail` — for the SOA record.
- Add to `appsettings.development.json` and test env baselines (`WebScaffold`, `OdinHost` set `Registry__DnsRecordValues__*` vars).

### 2. Zone lifecycle in `IdentityRegistrationService` (`src/services/Odin.Services/Registry/Registration/IdentityRegistrationService.cs`)
- `CreateOwnDomainZoneAsync(domain)`: no-op unless PowerDNS configured; `zoneId = domain + "."`; if `!ZoneExists` → `CreateZone(zoneId, NameServers, SoaAdminEmail)`, then populate from `GetDnsConfiguration(domain)` exactly like `CreateManagedDomain` does, except: `A` apex written via the new apex support (name `""`), `ALIAS` entry skipped (in our own zone the apex A is authoritative; ALIAS is only an instruction for third-party DNS hosts), CNAMEs written as `record.Value + "."`. Idempotent (rrsets use `REPLACE`).
- **Domain-control gate** (added after review): the zone is only created when control of the domain is proven — an identity is registered for it, OR the parent zone's delegation NS records point at our nameservers (`DnsLookupService.IsDomainDelegatedToUsAsync`, queryable *before* our zone exists because delegation records live at the parent), OR the manual DNS records validate. Without this, anyone could claim a zone for any domain.
- **Shadow guard** (defense in depth): refuse any domain that falls inside a zone already hosted in our PowerDNS — a child zone would shadow that part of the parent (e.g. a hostile `demo.id.pub` zone would hijack the name away from our `id.pub` zone).
- Timing: the frontend calls `create-own-domain-zone` before each DNS-status poll (it returns `created=false` until the control proof appears); additionally `CreateIdentityOnDomainAsync` ensures the zone best-effort after registration, covering the manual-records path.
- `DeleteOwnDomainZoneAsync(domain)`: best-effort `DeleteZone(domain + ".")` — log failures, never block account deletion. Skip domains under any `ManagedDomainApexes` apex (those live in shared apex zones and use the existing record-deletion path).
- Call `DeleteOwnDomainZoneAsync` from `DeleteOwnDomain` and from `DeleteTenantJob` (only for non-managed domains).

### 3. New registration endpoint (`src/apps/Odin.Hosting/Controllers/Registration/RegistrationController.cs`)
- `POST /api/registration/v1/registration/create-own-domain-zone/{domain}` — mirrors `create-managed-domain`'s position in the flow. Guards: valid domain (`AsciiDomainNameValidator`), `is-own-domain-available` check, invitation-code assertion when codes are configured (same as managed), PowerDNS configured (else 200 no-op so the frontend needn't branch). Idempotent.
- Orphan cleanup: extend the backfill CLI command (below) with a `--prune` mode that deletes zones having no matching registration — run periodically by ops. (No automatic reaper in this phase.)

### 4. Delegation detection (`DnsLookupService`)
- `GetDnsConfiguration`: append one `DnsConfig` entry per configured nameserver — `Type = "NS"`, `Name = ""`, `Value = ns host`. Today's frontend ignores unknown types safely (it filters by type/name), but mind the `"Frontend depends on this class layout"` note on `DnsConfig.cs` — additive entries only, no field changes.
- `VerifyDnsRecordAsync`: handle `QueryType.NS` — success when the domain's NS rrset (queried at the discovered authority, same as other records) contains that entry's `Value` (normalize case + trailing dot). The `MultipleRecordsNotSupported` rule must not apply to NS (two+ records are expected).
- `AreDnsLookupsSuccessful`: success = **all NS entries Success** (delegated mode) **OR** the existing rule (manual mode). This makes `own-domain-dns-status` naturally report "delegated" via per-record statuses.

### 5. CLI backfill (`src/apps/Odin.Hosting/Cli/Commands/`)
- New command (e.g. `create-own-domain-zones`): iterate `IIdentityRegistry` registrations, skip domains under `ManagedDomainApexes`, call `CreateOwnDomainZoneAsync` for each; report created/existing/failed. `--prune` flag as above. Re-runnable.

## Changes (odin-js, provisioning-app)

- `ProvisionOwnDomain.tsx`: on the `EnteringDetails → DnsRecords` transition, call the new `create-own-domain-zone/{domain}` endpoint (fire-and-await; non-2xx blocks advancing with an error).
- `DnsSettingsView.tsx`: render NS entries (`type === 'NS'`, currently silently ignored by the ALIAS/A/subrecord split) as the **preferred** setup block: subdomain case → "add these NS records for `<label>` at your `<zone apex>` DNS host" (zone apex already fetched via `lookup-zone-apex`); apex case → "change your domain's nameservers at your registrar to …" plus an explicit warning that ALL existing DNS for the domain (web, mail) moves to us. Manual records become the fallback accordion. Delegated status = all NS rows `success` (statuses arrive through the same polled `own-domain-dns-status` payload).

## Verification

1. Unit tests (`Odin.Services.Tests`): `GetDnsConfiguration` includes NS entries; success rule passes on NS-only success and on legacy rule; apex-name handling in `PowerDnsRestClient` rrset payload shapes.
2. **Live PowerDNS validation** (required — `CreateZone` has never run): against the dev PowerDNS instance, create a zone for a test domain, `dig @<ns> A/NS/SOA <domain>`, verify apex A + capi/file CNAMEs + NS; re-run for idempotency; delete. Validate the SOA/serial behavior with `api_rectify`. (The PowerDNS deployment itself is outside this repo — assumed reachable via `Registry:PowerDnsHostAddress`.)
3. Integration: `dotnet test --filter FullyQualifiedName~Registration` plus full suite.
4. End-to-end: provisioning app against a dev host — BYOD signup with a real delegatable test subdomain: (a) manual-records path still works unchanged; (b) NS path: add NS records at the parent, watch `own-domain-dns-status` flip to success without any manual A/CNAMEs, provision, cert issued (HTTP-01 unaffected — apex A points at the host either way).
5. CLI backfill in dev: run against existing registrations, confirm zones appear, re-run is a no-op.

## Out of scope (explicitly)

- Email DNS records (MX/SPF/DKIM/DMARC) — the payoff phase this enables; needs TXT/MX support in `IDnsRestClient` when it comes.
- ACME DNS-01 / wildcard certs.
- Automatic orphan-zone reaper (manual `--prune` only for now).
- Importing a user's pre-existing records when an apex delegates (UI warns instead).
