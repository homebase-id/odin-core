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
Email:TenantMail:SpfIncludeTarget=<hostname holding an SPF TXT record>
Email:TenantMail:DmarcReportEmail=<address>
Email:TenantMail:TlsReportEmail=<address>
Email:DkimStorageKey=<openssl rand -hex 32>
Email:Stalwart:BaseUrl=http://stalwart:8080
Email:Stalwart:AdminUsername=<user>
Email:Stalwart:AdminPassword=<strong>
```

| Value | What it is | Phase 1 |
|---|---|---|
| `MxNodes:0` | The mail hostname. Autoconfig advertises it to clients at 993/465, and it becomes the MX target in Phase 2. | Must resolve (the one A record above). This is the only one clients actually use. |
| `SpfIncludeTarget` | A hostname whose TXT record holds `v=spf1 ...`. Tenant SPF records are generated as `v=spf1 include:<this> -all`. | Not used — no records are published. Set the intended value if known, else a placeholder. |
| `DmarcReportEmail` | Goes into generated DMARC records as `rua=mailto:`. | Not used in Phase 1. |
| `TlsReportEmail` | Goes into generated TLS-RPT records as `rua=mailto:`. | Not used in Phase 1. |
| `DkimStorageKey` | **Encrypts tenants' DKIM private keys at rest** (AES-CBC). Exactly 32 bytes as 64 hex chars. | Generate **once**, back it up, never change it — see below. |
| `Stalwart:*` | Container address and admin credential. | `BaseUrl` is the container name on the shared docker network, not `localhost`. |

**`DkimStorageKey` is the one that bites.** It is not a throwaway: every DKIM private
key the server stores is encrypted under it. Change or lose it and existing keys
cannot be decrypted — the tenants involved need new DKIM keys and new DNS records.
Generate it once, store it wherever the other production secrets live, and treat it
as permanent. An incomplete or wrong-length value fails boot loudly, which is the
good case.

The other three DNS values are required once `Enabled` is true, but nothing reads
them in Phase 1: their only consumers are the generated DNS records and the
network checks, and both are Phase 2. Fill them in with the intended production
values if they are known — it costs nothing and means Phase 2 does not need a
config edit.

### Two things to know before flipping it

- **`Enabled` is host-wide, not per-tenant.** It opens the mail API for every
  identity on the host. Nothing self-provisions — a mailbox exists only once a
  user runs setup — and the client app is dev-menu gated. Be deliberate, not
  surprised.
- **Thunderbird will warn about the certificate.** Expected in Phase 1: it is
  self-signed. Click through.
- **Mailgun is a separate concern, and stays where it is.** `Mailgun:*` at the
  top level is how the host sends its OWN mail to users — password recovery,
  security health reports. It has nothing to do with tenant mail, and nothing
  here touches it. Leave it alone; enabling tenant mail does not affect it, and
  its absence does not hold tenant mail back.
- **Expect no email-related errors at boot.** Tenant mail on with Mailgun off
  logs two INF lines and nothing else:
  `Tenant mailbox provider: Stalwart at ...` and
  `Mailgun is not enabled; this host sends no mail of its own`. Anything at ERR
  is a real finding.

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
