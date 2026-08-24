# Local Stalwart dev environment — setup runbook

Production/bleeding-edge setup: `docs/stalwart-production-setup.md`.

Companion to `docs/stalwart-admin-api-notes.md` (the live-verified
management-API wire contract). The README deliberately says nothing about
Stalwart yet - this doc is the dev-facing runbook.

Verified working 2026-08-21 on this machine against **Stalwart 0.16.18**.

## 0. Docker

Ubuntu's distro packages are enough (avoid the snap — its confinement makes
bind mounts painful):

```bash
sudo apt install -y docker.io docker-compose-v2
sudo usermod -aG docker $USER
# group membership needs a re-login to apply to new shells; until then prefix
# docker commands with:  sg docker -c "docker ..."
```

## 1. Start the container

The compose file lives at `docker/stalwart-dev/compose.yml`.
Key choices baked into it:

- **Image pinned to `stalwartlabs/stalwart:v0.16`** — build the provider
  against a known tag, not `latest`.
- **Ports are remapped around everything else on a dev box**:

  | Host | Container | What |
  |---|---|---|
  | 9080 | 8080 | admin UI + management/JMAP API (NOT 8080: WebScaffold binds it) |
  | 9443 | 443  | same over TLS (NOT 8443: WebScaffold binds it) |
  | 2525 | 25   | SMTP inbound (25 is below this box's unprivileged-port floor) |
  | 1587/1465 | 587/465 | submission / submissions |
  | 1143/1993 | 143/993 | IMAP / IMAPS |

  The odin dev host owns 80/443/4444; the vite dev servers own 3000-3006.
- **`STALWART_RECOVERY_ADMIN=admin:devadminpass`** — deterministic dev admin
  credentials instead of fishing a generated password out of `docker logs`.
- **`STALWART_RECOVERY_MODE=1`** — mail services suspended, management API
  only. That's all the provider work needs; remove the variable and
  `docker compose up -d` again for normal mail-serving operation.
- **Storage: container-default RocksDB.** The management API is
  backend-agnostic, so this is the fastest path. (Production targets
  PostgreSQL metadata + S3 blobs; the WebUI can repoint backends at
  `docker/start-dev-servers.sh`'s postgres later without reinstalling.)

```bash
cd docker/stalwart-dev
sg docker -c "docker compose up -d"
```

## 2. First boot is special: bootstrap vs recovery

A fresh volume has no `config.json`, so Stalwart starts in **bootstrap
mode**: only the setup wizard works, the whole API 404s. Skip the wizard
headlessly by writing the one-line config and restarting:

```bash
sg docker -c "docker exec stalwart-dev sh -c \
  'echo {\"@type\":\"RocksDb\",\"path\":\"/var/lib/stalwart/\"} > /etc/stalwart/config.json'"
sg docker -c "docker compose restart"   # from docker/stalwart-dev
```

With config.json present + the recovery env vars, it boots into **recovery
mode**: management API live at `http://localhost:9080`, basic auth
`admin:devadminpass`. (v0.16 keeps all real configuration in the database as
a registry; config.json only says where the database is.)

## 3. Verify

```bash
curl -s http://localhost:9080/healthz/live                      # 200
curl -s -u admin:devadminpass http://localhost:9080/jmap/session | head -c 200
# capabilities include urn:stalwart:jmap = the management registry
```

Admin WebUI (same creds): http://localhost:9080

## 4. Talking to the management API

Everything provisioning-related is JMAP against `POST /jmap` — object types
like `x:Domain`, `x:Account`, `x:DkimSignature` with per-type `/get`/`/set`
methods. The full verified wire contract (payload shapes, variant `@type`s,
the numeric-keyed List quirk, serverSet fields, error cheatsheet) is in
**`docs/stalwart-admin-api-notes.md`** — read that before writing any call.

The registry is self-describing: when a payload is rejected, the answer is in

```bash
curl -sL -u admin:devadminpass http://localhost:9080/api/schema | gunzip > schema.json
```

(150 object types, fields with mutable/serverSet flags. It's gzipped
regardless of Accept-Encoding.)

## 5. Current state of THIS machine's instance

Provisioned during the 2026-08-21 probing session (recovery mode):
domain `frodo.dotyou.cloud` (id `b`), user account `frodo` (id `b`) with
two aliases (mail@, hello@), encryption-at-rest ON (Aes256 + a gpg-generated
test cert — NOT the odin-published one), an active ed25519 DKIM key
(selector `s1`), and one app password ("Thunderbird"). Wipe it any time:

```bash
sg docker -c "docker compose down -v"   # -v removes the volumes = full reset
```

## 6. Gotchas learned the hard way

- Ports 8080/8443 on the host break `Odin.Hosting.Tests` (WebScaffold) —
  hence the 9080/9443 remap. If tests suddenly fail with "address already
  in use", check what's squatting.
- User (non-admin) auth returns 403 in recovery mode — expected; test
  app-password logins only after switching to normal mode.
- DeepWiki/older docs mention `Registry/set` methods and a REST admin API —
  both stale for v0.16. Per-type JMAP methods are the real interface.
