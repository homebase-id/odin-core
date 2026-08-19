# Email phase: keys, Stalwart integration, and the activation flow

Status: design/plan — not yet implemented. Companion to `docs/email-dns-plan.md` (DNS/discovery surface). Scope: key lifecycle, the mail server integration, and how a tenant activates email.

## Custody principle

The private-key perimeter is the Homebase server plus the owner — never the mail server, never the relay. Custody strength follows compromise impact:

| Key | Custody class | Given to Stalwart? | Given to relay? | If compromised |
|---|---|---|---|---|
| E2E/at-rest encryption keypair (per identity) | **Owner-locked**: encrypted on the email drive, under Shamir recovery; server cannot use it | **Public key only** (encryption-at-rest) | never | all stored mail readable - catastrophic, unrecoverable. Hence the strongest custody |
| DKIM signing keypair (per identity) | **Server-operational**: AES-encrypted at rest with the server storage key, server-decryptable unattended - the exact custody class of the TLS certificate private key (`CertificateStore`) | never | never (relay forwards already-signed mail) | DMARC-passing spoofs until rotation (new key + TXT - minutes, no data exposed). Strictly less severe than a TLS-key compromise, which the same custody already guards |
| Zone DNSSEC keys | PowerDNS-managed (`docs/byod-dnssec-plan.md`) | — | — | — |

**Two signatures, two signers - permanently disambiguated:** DKIM says "this message is authorized by the *domain*" and is verified by receiving *servers* via the DNS TXT record - an inherently server-side function (no MUA can DKIM-sign, and a client-computed signature breaks on any header the submission path touches). The OpenPGP/E2E signature says "this content is from this *person*", is verified by receiving *clients*, and is produced by the **client**, which holds that key. Both travel on the same message. DKIM's public half lives in DNS because that is where its verifiers look (protocol-fixed); `.well-known`/WKD serves the other key to the other audience.

## Architecture

- **One Stalwart cluster per host group**: up to ~1000 users are served by a group of 2-3 servers for HA; the group runs Stalwart nodes sharing state, so any node accepts mail for any tenant of the group. Each tenant's MX lists the group's nodes (multiple MX records - SMTP's native failover; receivers walk the priority list).
- **Storage split (hard requirement)**: Stalwart metadata/headers in the existing **PostgreSQL**; message **blobs on disk or (preferred) S3** - Stalwart supports an S3-compatible blob store, which fits the platform's existing S3 payload setup. **No mail blobs in Postgres.** Blobs are ciphertext (encryption-at-rest happens before storage), so S3 holds only encrypted objects.
- **Encryption at rest**: Stalwart receives the tenant's public key and hybrid-encrypts every arriving plaintext message (random symmetric key, wrapped with the tenant's public key) before storage. Only ciphertext touches disk. This is server-assisted at-rest protection — the server sees plaintext briefly on arrival. True E2E for Homebase↔Homebase is sender-side encryption via DID/WKD discovery (DNS doc); both coexist.
- **Outbound pipeline**: client (chat-kmp / IMAP client) → Stalwart submission (auth) → smarthost = Homebase mail-out service, which **DKIM-signs with the tenant's key** → external relay (forwards pre-signed) or direct. Homebase-native sends (via our own API) sign in Homebase directly, skipping the Stalwart hop. Stalwart is deliberately NOT the DKIM signer.
- Stalwart provides IMAP/SMTP/JMAP for standard clients; webmail (e.g. Bulwark) is a separate deployment, out of scope here.

## The email drive: activation, key storage, recovery

Creating email is a client action (chat-kmp):

1. The app creates the **email drive** — its existence is the per-tenant "email activated" indicator the monthly check and RCPT-time policy key off (DNS doc).
2. The app (or server on its behalf) generates the **encryption keypair** and stores it **encrypted on the email drive**. Drive storage puts it under the existing master-key/**Shamir recovery** umbrella automatically — losing the key means losing all encrypted mail, so recoverability is non-negotiable and this design gets it for free.
3. **Rotation** = append a new keypair to the same drive; the current key is the newest by creation time (or an explicit unique-id pointer). **Old private keys are never deleted** — mail stays encrypted to the key that was current at receipt.
4. The app calls the server's **activate-email API**, which does everything server-side (below).

Key algorithm: decision point between ECC-384 (P-384 — aligns with Homebase's existing ECC infrastructure; supported by Stalwart and OpenPGP/RFC 6637) and Curve25519 (smaller, the OpenPGP ecosystem's favorite). Either works for all consumers here; pick once at implementation time. OpenPGP certificate packaging (for Stalwart + WKD) via an OpenPGP library (e.g. BouncyCastle) — keep the certificate minimal.

## Activation flow (server side, idempotent)

`POST /api/owner/v1/mail/activate` (owner-authenticated), called by the app after drive+key creation:

1. Generate the tenant's **DKIM keypair(s)** (two selectors), stored server-operational per the custody table (TLS-key pattern). Unattended availability is what keeps legacy IMAP/SMTP clients and future scheduled-send working without owner presence.
2. Write the **on-activation DNS records** (DKIM TXT) into the tenant's zone / apex prefix (emission paths per the DNS doc); manual-records tenants instead see them as instructions in the Email tab.
3. Publish the **encryption public key**: DID document `keyAgreement` entry + WKD.
4. Provision **Stalwart** via the wrapper (below): create the account + domain association, upload the public key, enable encryption-at-rest with it.
5. Mark active; the startup/monthly verification (DNS doc) now covers this tenant.

Rotation re-runs 2–4 with the new key (publish new, grace period, retire old from discovery — old keys remain on the drive). Tenant deletion rides `DeleteTenantJob` like DNS cleanup does: delete the Stalwart account + mail data, best-effort, never blocking deletion.

## The Stalwart wrapper

Abstracted behind an interface, following the `IDnsRestClient`/PowerDNS precedent — one implementation, no Stalwart types leaking upward:

```
IMailboxProvider
  CreateMailboxAsync(domain, address)          // account + domain association
  SetEncryptionKeyAsync(domain, openPgpCert)   // upload public key, enable encryption-at-rest
  DeleteMailboxAsync(domain)                   // tenant deletion ride-along
  ProvisionAppPasswordAsync(domain, ...)       // client auth (below)
```

Configuration joins the `Email` section (DNS doc): Stalwart base address + admin credentials per host group, plus its storage backends (PostgreSQL connection for metadata, S3 bucket for blobs - reusing the platform's existing S3 configuration pattern). **Caveat, same as PowerDNS had**: the exact Stalwart management surface (REST admin API vs JMAP objects like `PublicKey`/`AccountSettings.encryptionAtRest`) and the PostgreSQL+S3 cluster configuration are modelled from documentation and have never run against a live server - first implementation step is live verification against a dev Stalwart, mirroring how `CreateZone` was validated.

## Client access & auth

- **Autoconfig**: `https://<tenant>/.well-known/autoconfig/mail/config-v1.1.xml` — another anonymous controller beside WebFinger/DID/WKD. Pre-fills `%EMAILADDRESS%`; never contains secrets.
- **Auth, short term**: app passwords — generated in Homebase, provisioned into Stalwart via the wrapper, revocable from the owner console.
- **Auth, longer term**: OAuth2 against youAuth (the autoconfig file can point clients at the issuer). Separate work item.
- chat-kmp is the first-party client and talks to Homebase/JMAP directly; Thunderbird/FairEmail-class clients use IMAP/SMTP with the above.

## Verification hooks (extends the DNS doc's checks)

- Startup canary round-trip exercises this whole plan too: submit → sign in Homebase → relay → canary's MX → Stalwart stores encrypted → verify the stored object is ciphertext (in S3, not Postgres) and the DKIM signature verifies.
- Startup check verifies each MX node of the host group (A + TLSA + port 25 reachable) - HA only works if all listed nodes actually serve.
- Monthly per-tenant check adds: Stalwart account exists and its encryption-at-rest key matches the tenant's current published key; DKIM TXT matches the tenant's current signing key. ERR on mismatch.
- Email tab shows key state: current key created/rotated dates, "encrypted at rest: on", discovery (DID/WKD) serving the current key.

## Out of scope

- Webmail deployment; spam filtering; mailbox quotas.
- The addressing model (localparts) — defined with the mailbox implementation; everything here consumes it.
- OAuth2/youAuth bridging (own work item).
- Proton-interop key discovery (Proton doesn't publish via standard WKD; nothing to do on our side).
