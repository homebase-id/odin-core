# Email phase: DNS records, key discovery via .well-known, and reverse DNS

Status: design/plan — not yet implemented. Scope: the DNS/discovery surface only — the SMTP/inbound server, mailbox storage and key-management UX are separate plans. Outbound sending goes via SendGrid; the tables below also carry the self-sending values in case outbound ever moves in-house. Inbound MX is ours in both cases.

## Design principles

- **Tenant identity in email comes from the domain layer** (MAIL FROM + DKIM signature + SPF authorization), **never from the connecting IP**. Receivers fully expect one IP/provider to send for thousands of domains.
- **Everything per-tenant lives in the tenant's zone** (which we host and write via the existing PowerDNS path); **everything per-infrastructure lives once, in an infra zone** (e.g. `id.pub`) — a provider or IP change touches one record, not 1000 zones.

## Per-tenant DNS records (written into each identity's zone)

Example tenant `gabriel.ninja`; `<infra>` = the infra zone, e.g. `id.pub`.

| Record | Name | Value (SendGrid) | Value (self-sending) | Note |
|---|---|---|---|---|
| MX | apex | `10 mx1.<infra>.` | same | Inbound is ours either way. MX targets must be A records, not CNAMEs |
| TXT (SPF) | apex | `v=spf1 include:_spf.<infra> -all` | same | The `include:` indirection is the point: provider/IP authorization lives in ONE infra record |
| DKIM | `s1._domainkey`, `s2._domainkey` | CNAME → `s1.domainkey.u<id>.wl.sendgrid.net.` etc. (keys SendGrid-managed; values per tenant from their API) | TXT `v=DKIM1; k=ed25519; p=<key>` (keys Homebase-managed; ed25519 + rsa-2048 selectors) | Two selectors either way — legacy receivers and rotation |
| CNAME (return path) | `em<id>` | `u<id>.wl.sendgrid.net.` | — (not needed) | SendGrid bounce/return-path domain; gives DMARC its SPF alignment |
| TXT (DMARC) | `_dmarc` | `v=DMARC1; p=reject; rua=mailto:...` | same | |
| TXT (MTA-STS) | `_mta-sts` | `v=STSv1; id=<version>` | same | Plus the policy file at `https://mta-sts.<tenant>/.well-known/mta-sts.txt` (extra CNAME/A + cert on our web tier); policy lists the shared MX |
| TXT (TLS-RPT) | `_smtp._tls` | `v=TLSRPTv1; rua=mailto:...` | same | |

Deliberately absent:

- **No per-tenant TLSA**: DANE binds to the **MX target hostname**, which is shared — one TLSA record in the infra zone covers all tenants.
- **No OPENPGPKEY record** — see "Key discovery" below.
- **No per-tenant PTR** — see "Reverse DNS" below.

## Infra zone records (once, in `<infra>`)

| Record | Name | Value (SendGrid) | Value (self-sending) | Note |
|---|---|---|---|---|
| A | `mx1` (`mx2`, …) | inbound mail IP(s) | same | |
| TLSA | `_25._tcp.mx1` | `3 1 1 <SPKI SHA-256>` | same | DANE for every tenant at once; requires `<infra>` signed + anchored (true for `id.pub`). Cert rotation must keep the pinned key stable or update this record first |
| TXT (SPF target) | `_spf` | `v=spf1 include:sendgrid.net -all` | `v=spf1 ip4:<relay-ip> ip4:<relay-ip2> -all` | The single place outbound is authorized; swapping providers touches this one record |

## Key discovery for E2E: .well-known, not DNS

E2E public keys are generated and managed inside Homebase (`PublicPrivateKeyService` already backs the DID document's published key). Discovery goes over HTTPS:

- **Homebase ↔ Homebase**: the recipient's existing discovery surface — `.well-known/did.json` (`DidController`) / WebFinger (`WebFingerController`) — carries the encryption public key (a `keyAgreement` entry in the DID document alongside today's signing key). Senders fetch it over the recipient's HTTPS and encrypt **before** submission: true E2E. With SendGrid in the outbound path this is the privacy boundary — the relay only ever sees ciphertext for Homebase-to-Homebase mail.
- **External OpenPGP world**: the same key additionally served as **WKD** — `https://<tenant>/.well-known/openpgpkey/hu/<z-base32(sha1(localpart))>` — a new anonymous controller in the same family as the two above. GnuPG/Thunderbird/Proton-class clients actually fetch WKD.

Why not the OPENPGPKEY DNS record (RFC 7929): essentially zero client support; payload-size pressure on DNS; and its trust model is DNSSEC, which makes it unverifiable exactly for the identities that cannot anchor (managed domains inherit; some DNS hosts cannot hold child DS records). WKD/DID ride the per-identity WebPKI certificate every identity already has, so they work for all tenants uniformly. DNSSEC's irreplaceable email role is transport (DANE/TLSA), not content keys.

Nuance: giving the SMTP server the public key enables *encrypt-on-arrival* for mail from legacy senders (at-rest protection; the server briefly saw plaintext). True E2E is sender-side encryption via the discovery above. Both coexist.

## Reverse DNS (PTR)

PTR records belong to the IP, not to any tenant domain: reverse DNS is set by whoever owns the IP block, and receivers verify the connecting IP's PTR/forward/HELO agreement. The PTR count equals the number of outbound IPs, never the number of tenants.

- **SendGrid outbound**: nothing to do at OVH. SendGrid owns the sending IPs and their reverse DNS; our servers submit over 587/443, so no port-25 egress unblocking, no IP warm-up.
- **Self-sending (if ever)**: per relay IP at OVH — reverse entry → canonical hostname (OVH manager/API `/ip/{ip}/reverse`; OVH validates the forward A first, so create it first), HELO set to the same hostname, port-25 unblock request, dedicated stable IPs. (OVH platform details are outside this repo — verify when relevant.)
- **Inbound MX on our OVH machines**: receiving requires no PTR; a truthful reverse entry on those IPs is good hygiene, nothing more.

## Config file changes (`OdinConfiguration` + appsettings)

One consolidated `Email` section replaces today's top-level `Mailgun` section and covers **both kinds of sending** - the server's own system mail (provisioning/security emails, today's Mailgun traffic, sent via `IEmailSender`) and tenant mail - through **one provider entry**. Provider implementations sit behind the existing `IEmailSender` seam (`MailgunSender` today; a `SendGridSender` joins it), so switching provider is config, not code.

```jsonc
{
  "Email": {
    "Enabled": false,                       // master switch for all sending (today: Mailgun:Enabled)
    "Provider": "SendGrid",                 // "SendGrid" | "Mailgun" | "Smtp" - used by BOTH system and tenant mail

    // provider credentials (only the selected provider's section is required)
    "SendGrid": { "ApiKey": "..." },        // also used for per-tenant domain-authentication onboarding
    "Mailgun": { "ApiKey": "...", "EmailDomain": "..." },
    "Smtp": {                               // self-sending
      "RelayHost": "mx1.id.pub",            // canonical hostname: HELO, PTR target, TLSA owner
      "RelayIps": [ "x.x.x.x" ]             // published in the infra _spf record
    },

    "SystemFrom": {                         // system/transactional mail sender (today: Mailgun:DefaultFrom*)
      "Name": "Team Homebase",
      "Email": "no-reply@id.pub"
    },

    "TenantMail": {
      "Enabled": false,                     // gates tenant mailboxes AND emission of the per-tenant email DNS records
      "MailExchangers": [ "mx1.id.pub" ],   // per-tenant MX targets
      "SpfIncludeTarget": "_spf.id.pub",    // what tenant SPF records include:
      "DmarcReportEmail": "dmarc-reports@id.pub",
      "TlsReportEmail": "tls-reports@id.pub"
    }
  }
}
```

Notes:

- **Everything email lives under `Email`** - provider, credentials, system-mail sender, tenant-mail DNS values. `DnsConfigurationSet` gains the `TenantMail` values at construction (it already takes plain config keys; the section they come from is immaterial).
- **Migration**: existing deployments carry `Mailgun:*` keys - coordinate the rename to `Email:*` with the ansible templating update (same literal-passthrough caveat as the nameserver values), or read the old keys as a fallback for one release.
- **Self-hosters**: `Email:Enabled=false` (default) keeps today's behavior exactly; enabling it with their own values assumes nothing about our infrastructure.
- **Per-tenant SendGrid CNAME values are not config** - they are API-issued per tenant at onboarding and stored per tenant, keeping the config finite at 1000 tenants.
- **Infra zone records** (mx A, TLSA, `_spf` content) derive from this config but are written to the infra zone as an ops/CLI action, not per-tenant.

## What the codebase needs

- **`IDnsRestClient`/`PowerDnsRestClient`** (`src/services/Odin.Services/Dns/`): today writes A + CNAME rrsets only (plus DNSSEC cryptokeys/CDS). Needs **MX and TXT** rrset support for tenant zones; TLSA only if the infra zone is also managed through this API.
- **`DnsLookupService.GetDnsConfiguration`** (`src/services/Odin.Services/Registry/Registration/`): gains the per-tenant email entries so zone populate and the owner-console records view pick them up. Additive entries only (`DnsConfig` layout is a frontend contract), and the email records must **not** join the identity-validation success rule (`AreDnsLookupsSuccessful`) or certificate checks — same isolation discipline as the optional `www` record.
- **`OdinConfiguration`**: the consolidated `Email` section above (replacing `MailgunSection`); `DnsConfigurationSet` gains the `TenantMail` DNS values. `IEmailSender` gets a `SendGridSender` sibling; registration in `SystemServices` becomes provider-switched.
- **SendGrid onboarding automation**: per tenant, create the authenticated domain via SendGrid's API, fetch the issued CNAMEs, write them into the tenant zone (existing CNAME rrset support suffices), trigger SendGrid's validation. Check SendGrid plan limits on authenticated-domain count (~1000 needed) — outside-repo assumption to verify.
- **WKD controller**: sibling of `WebFingerController`/`DidController` (`src/apps/Odin.Hosting/Controllers/Anonymous/`), serving the Homebase-managed OpenPGP key; DID document gains a `keyAgreement` entry (`DidService`, `src/services/Odin.Services/Fingering/`). OpenPGP certificate packaging needs an OpenPGP library (e.g. BouncyCastle, already referenced by the crypto layer) — use Curve25519 keys, keep the certificate minimal.
- **CLI/backfill**: the existing `create-own-domain-zones commit` re-populate path applies new static records to existing zones once `GetDnsConfiguration` includes them; the SendGrid CNAMEs are per-tenant values and flow through the onboarding automation instead.
- **Owner console**: the Security→DNS panel renders whatever the status endpoint returns, so MX/TXT rows appear once emitted; DNSSEC red-dot semantics already cover the DANE era.

## Out of scope

- The SMTP/inbound server itself, outbound submission integration, mailbox storage, client UX.
- Key management/rotation UX (couple its design with the deferred DNSSEC key-rollover work).
- MTA-STS policy hosting implementation details.
- Third-party DNS (manual-records) tenants: they get the same record list as instructions instead of zone writes — the provisioning `dns-config` endpoint already carries unknown record types to the UI safely.
