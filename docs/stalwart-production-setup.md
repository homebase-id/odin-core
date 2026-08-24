# Stalwart on a Homebase host

Two phases. **Phase 1 is a plumbing test** and needs no MX, no port 25, no valid
TLS. Phase 2 adds real mail flow.

Dev equivalent: `docs/stalwart-dev-setup.md`. Wire contract:
`docs/stalwart-admin-api-notes.md`.

---

## Phase 1 — plumbing test

Proves: provisioning, key generation, WKD, autoconfig, the client app, and a
real Thunderbird login with at-rest decryption.

Not in scope: inbound internet mail, deliverability, valid TLS.

> MX records only matter for *other mail servers* delivering to you. A mail
> *client* needs an A record and an open port. That is why Phase 1 is cheap.

### Container

- Same host as Homebase, on a **shared docker network**.
- Volumes: `/etc/stalwart`, `/var/lib/stalwart` — include in backups.
- Admin credential: **not** `admin:devadminpass`.

### Ports

| Port | Exposure |
|---|---|
| 993 (IMAPS), 465 (SMTPS) | public |
| 8080 (admin/JMAP) | **internal network or loopback only — never public** |
| 25, 587 | not needed |

Homebase keeps 80/443; no collision.

### DNS

One A record: `<mail hostname>` → the VPS. Nothing else.

### Config

Dropped as a file; no ansible change needed.

```
Email:TenantMail:Enabled=true
Email:TenantMail:MxNodes:0=<mail hostname>
Email:TenantMail:SpfIncludeTarget=<spf host>
Email:TenantMail:DmarcReportEmail=<address>
Email:TenantMail:TlsReportEmail=<address>
Email:DkimStorageKey=<openssl rand -hex 32>
Email:Stalwart:BaseUrl=http://stalwart:8080
Email:Stalwart:AdminUsername=<user>
Email:Stalwart:AdminPassword=<strong>
```

- `BaseUrl` is the **container name**, not `localhost`.
- `DkimStorageKey` must be exactly 64 hex chars, else the host throws at boot.
- All four `TenantMail` values are required once `Enabled` — an incomplete file
  fails boot loudly rather than half-working.
- Omit `Email:Provider` unless you also want system mail.

### Two things to know before flipping it

- **`Enabled` is host-wide, not per-tenant.** It opens the mail API for every
  identity on the host. Nothing self-provisions — a mailbox exists only once a
  user runs setup — and the client app is dev-menu gated. Be deliberate, not
  surprised.
- **Thunderbird will warn about the certificate.** Expected in Phase 1: it is
  self-signed. Click through.

### Verify

```bash
curl -s http://stalwart:8080/healthz/live                      # 200
curl -s https://<identity>/.well-known/autoconfig/mail/config-v1.1.xml
```

Boot log should say: `Tenant mailbox provider: Stalwart at http://stalwart:8080`.

Then, in the client: enable the dev menu → Email setup → run it → import the
private key into Thunderbird → send yourself mail. It arrives encrypted.

---

## Phase 2 — real mail flow

Three problems, none solved by Phase 1. Decide before starting.

1. **TLS for the mail hostname.** Homebase owns :80 and :443, so Stalwart can
   run neither HTTP-01 nor TLS-ALPN-01. Options: DNS-01, or mount a cert
   Homebase obtains. No cheap answer — settle this first.
2. **Port 25 egress + rDNS.** Many VPS providers block outbound 25, and mail
   without a matching PTR is filed as spam regardless. If egress is blocked,
   outbound needs a smarthost.
3. **Per-domain DNS.** MX, SPF, DKIM, DMARC, MTA-STS, TLS-RPT. The records are
   already generated server-side; publishing them per tenant domain is the open
   question.

Also needed: inbound 25 open, and 587 if any client wants submission over
STARTTLS.
