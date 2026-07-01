# OpenObserve Log Aggregation — Deployment Plan

Centralized, self-hosted log aggregation for Homebase/ODIN identity hosts. Each identity host ships
structured Serilog logs directly to a central OpenObserve instance over OTLP gRPC + TLS. This folder
holds the reference stack (`compose.s3.yml`, `README.md`) and this plan; it is the source for the Ansible
roles described below. The C# sink lives in `src/apps/Odin.Hosting` and is **not** part of this folder
(only its configuration is referenced here).

---

## 1. Architecture & decisions

| Area | Decision | Why / rejected alternatives |
|------|----------|-----------------------------|
| Product | **OpenObserve OSS (AGPL-3.0)** | Open-source requirement. Rejected: OO **Enterprise free-tier** (proprietary, phone-home license key, 12-month expiry), **Seq** (closed, free tier is single-user), **Graylog** (Elasticsearch-backed, heavy), **Loki/SigNoz** (fine, but OO won on footprint + native OTLP + indexed field search for correlation IDs). |
| Shipping | **Serilog → `Serilog.Sinks.OpenTelemetry`, direct, no collector** | Fewest moving parts. Tradeoffs accepted: in-process batching (a hard `kill -9` drops the in-flight batch; graceful shutdown flushes via `Log.CloseAndFlush()`), app coupled to backend, credentials in app config. A shared OTel Collector becomes worth it only once several services justify amortizing it. |
| Protocol | **OTLP gRPC over TLS** | Cross-host ingestion. (HTTP/protobuf is equivalent functionally; gRPC chosen per requirement.) |
| Edge TLS | **The host's existing Caddy** (separate container, `--network host`) fronts ingestion | The CCX12 already runs Caddy for other sites, so this stack does **not** bundle Caddy (would collide on 80/443). That Caddy uses `--network host`, so it can't join a Docker bridge net or resolve `openobserve` by name; OO instead publishes gRPC on `127.0.0.1:5081` and Caddy (in the host namespace) proxies there. You add one site block to the existing Caddyfile (see `README.md`). Auto-ACME there still removes cert maintenance; the real cert means no client CA-trust config and no browser warnings. gRPC is HTTP/2 end-to-end, so the upstream is **h2c** to `127.0.0.1:5081`. |
| Org / stream | org `default`, stream **`homebase`** | The `default` org cannot be renamed and multi-org is enterprise-gated; the **stream** is the correct granularity for labeling, set via the `stream-name` header. |
| Storage | metadata = **SQLite** (local), data = **Parquet in Hetzner S3** (`ZO_LOCAL_MODE_STORAGE=s3`) | Single-node stays in **local mode** (`ZO_LOCAL_MODE` left unset); only the *data* backend is S3. **Cluster mode requires S3, but S3 does not require cluster mode.** Bulk Parquet lives in object storage (durable, cheap, and it bounds local disk); local disk now holds only WAL + the size-capped read cache (`ZO_DISK_CACHE_MAX_SIZE`). Metadata stays SQLite — and becomes a critical local SPOF (see §8). |
| Retention | `ZO_COMPACT_DATA_RETENTION_DAYS=30` (tunable) | Default is 3650 (~10y) which grows disk unbounded. Compactor prunes older partitions. Per-stream override available in the UI. |
| UI access | **Bound to `127.0.0.1:5080`, reached via SSH tunnel only** | The OO login has weak brute-force protection. Rejected: public UI + `basic_auth` (collides with OO's own `Authorization` header and would break the SPA; also not real rate limiting), public UI + IP allowlist (viable, but the tunnel has strictly lower exposure for an ops-only UI). |
| Ingestion auth | **Dedicated non-root `ingest@…` user**, secrets in vault. On **OSS this is an org `admin`** (see §5.3). | Goal was least privilege, but OSS blocks it: creating a user with any non-admin role returns `400 "Custom roles not allowed"` (RBAC is enterprise), and OSS service accounts have full access too. The real benefit is **credential hygiene**: root never lands on a shipper, and the ingest credential rotates/disables independently. Provisioned in-stack by `oo-init`, not root. |
| Network exposure | Only Caddy `:443` (+ `:80` for ACME) public; OO `:5081` and UI `:5080` published on `127.0.0.1` only | **Docker bypasses host firewalls (ufw):** published ports get iptables rules ahead of ufw. Binding to `127.0.0.1` keeps 5080/5081 off-box; for anything you *do* expose, restrict via cloud **security groups** or the **`DOCKER-USER`** iptables chain, not ufw. |

---

## 2. Topology

```
 identity host (frodo)        identity host (sam)            operator laptop
 ┌───────────────────┐        ┌───────────────────┐         ┌──────────────┐
 │ Odin.Hosting      │        │ Odin.Hosting      │         │ ssh -L 5080  │
 │ Serilog OTLP sink │        │ Serilog OTLP sink │         └──────┬───────┘
 └─────────┬─────────┘        └─────────┬─────────┘                │ tunnel
           │  gRPC/TLS :443             │  gRPC/TLS :443           │
           └──────────────┬────────────┘                          │
                          ▼                                        ▼
        ┌──────── log host (CCX12, hel1) ─────────────────────────────────┐
        │  existing Caddy (network=host; also serves other sites) :443 :80 │
        │     │ h2c to 127.0.0.1:5081 (loopback-published)                 │
        │     ▼                                                            │
        │  openobserve  :5081 gRPC  (127.0.0.1 only)                       │
        │               :5080 UI  (127.0.0.1 only) ◄──────── SSH tunnel ───┤
        │  oo-data vol: SQLite meta + WAL + read cache   (BACK THIS UP)    │
        └───────────────────────────────┬─────────────────────────────────┘
                                         │ batched Parquet PUT/GET (cached)
                                         ▼
                              ┌────────────────────────┐
                              │  Hetzner S3 (hel1)       │
                              │  Parquet log data        │
                              └────────────────────────┘
```

---

## 3. Files in this folder

- **`compose.s3.yml`** — the OpenObserve stack (no Caddy; the host already runs one). Params via `${VAR}` from `.env`.
- **`compose.local.yml`** — local-storage, ephemeral alternative (no S3, no persistent volume; logs die with the container).
- **`README.md`** — the Caddy ingest site block to paste into the host's existing Caddyfile, plus setup notes.
- **`.env.example`** — the full env-var surface the compose interpolates (Ansible renders the real `.env`
  from the vault). No secrets; placeholders only.
- **`PLAN.md`** — this document.

---

## 4. Host prerequisites

- **Host:** the CCX12 (hel1). Fine to share with the light static sites already there, but NOT with a
  production DB or other heavy/latency-sensitive service (see §1 placement reasoning).
- **Existing Caddy (`--network host`):** the host already runs an independent Caddy container for those
  sites, started with `--network host`. Because a host-mode container shares the host's network namespace
  and **cannot** be attached to a Docker bridge network, there is no shared network to put `openobserve`
  on and Caddy can't resolve it by container name. Instead, `compose.s3.yml` publishes OO's gRPC on
  `127.0.0.1:5081` and Caddy (in the host namespace) reaches it on loopback. Paste the ingest block from
  `README.md` into the existing Caddyfile. Do **not** run a second Caddy.
- **DNS:** `ingest.logs.<domain>` (A/AAAA) → the host IP. The existing Caddy owns :80/:443 and provisions
  the cert via its current ACME setup (or DNS-01 if the host isn't reachable on 80/443).
- **Docker Engine + compose v2 plugin.**
- **CPU / RAM:** start at **~4 vCPU / 8–16 GB** (single-node + S3). Memory dominates and is cache-driven:
  OpenObserve's in-memory caches each default to ~50% of RAM, so the compose **pins** them
  (`ZO_MEMORY_CACHE_MAX_SIZE`, `ZO_MEMORY_CACHE_DATAFUSION_MAX_SIZE`) and sets a `mem_limit`. With S3,
  cache RAM trades directly against query latency and S3 reads, so prefer more RAM. CPU is light for
  ingest, spikes on heavy queries + compaction; on a 2-vCPU box compaction parallelism is capped via
  `ZO_FILE_MOVE_THREAD_NUM=1` (default is cores×2). On Hetzner, prefer **dedicated** vCPU (CCX) over
  shared if query latency matters. Scale **CPU before RAM**; size up from actual GB/day after go-live.
- **Hetzner S3 bucket** for log data + credentials (access/secret key). **Co-locate** the log host in the
  same Hetzner location as the bucket: in-region traffic is low-latency and not egress-billed, and
  compaction's write amplification makes a remote bucket expensive.
- **Local disk** is now small: it holds only the SQLite metadata, the WAL, and the read cache (capped by
  `ZO_DISK_CACHE_MAX_SIZE`). Size it for `cache + WAL + metadata`, not for the full data set.
- **Firewall / security group:** 80 open for ACME; 443 open to the identity-host IPs. Remember the
  Docker/ufw caveat above.

---

## 5. Ansible implementation

### 5.1 Layout

```
roles/
  open_observe/                 # the log host
    defaults/main.yml           # non-secret defaults (versions, retention, paths)
    tasks/main.yml
    templates/compose.s3.yml.j2 # = compose.s3.yml in this folder; rendered to compose.yml on the host
    templates/caddy-ingest.j2   # = the README.md ingest block; dropped into the EXISTING Caddy's config + reload
    templates/oo.env.j2         # renders the 0600 .env from vault vars
    handlers/main.yml
  log_shipper/                  # each identity host
    tasks/main.yml
    templates/...               # app logging config (endpoint, creds, stream)
group_vars/
  log_host/vault.yml            # ansible-vault encrypted (see §6)
```

`compose.s3.yml.j2` is byte-for-byte `compose.s3.yml` (all variability is in `.env`). `caddy-ingest.j2` is the
ingest site block (from `README.md`) templated with the ingestion hostname, dropped into the existing Caddy's config
dir (then reload Caddy). Keeping them `.j2` lets you parameterize further without touching task code.

### 5.2 Log-host role tasks (idempotent)

1. **Docker + compose** — `geerlingguy.docker` role, or `package` + `service`. Idempotent.
2. **Deploy dir** — `ansible.builtin.file: path=/opt/open-observe state=directory mode=0750`.
3. **Render `.env`** — `ansible.builtin.template: src=oo.env.j2 dest=/opt/open-observe/.env mode=0600
   owner=root`. Contains `OPENOBSERVE_VERSION`, `OO_ROOT_USER_EMAIL`, `OO_ROOT_USER_PASSWORD`,
   `OO_RETENTION_DAYS`, the memory bounds, the **ingest-user vars** (`OO_INGEST_EMAIL`, `OO_INGEST_PASSWORD`,
   `OO_INGEST_ROLE`, consumed by `oo-init`), and the S3 vars (`ZO_S3_SERVER_URL`, `ZO_S3_REGION_NAME`,
   `ZO_S3_BUCKET_NAME`, `ZO_S3_ACCESS_KEY`, `ZO_S3_SECRET_KEY`). See `.env.example`. Notify *recreate stack*.
4. **Render the compose file** — `template` module: `compose.s3.yml.j2` → host `compose.yml` (no Caddy
   service). Notify *recreate stack*.
5. **(No shared network needed)** — the host's Caddy runs with `--network host`, so it can't join a Docker
   bridge net; OO is reached via the loopback-published `127.0.0.1:5081` (see `compose.s3.yml` ports). There
   is no `docker_network` task. Just ensure nothing else on the host already binds `127.0.0.1:5081`.
6. **Bring up** — `community.docker.docker_compose_v2: project_src=/opt/open-observe state=present`.
   Idempotent: converges, recreates only changed services.
7. **Install the Caddy ingestion site** — template `caddy-ingest.j2` into the existing Caddy's config
   (its `conf.d` or an `import`ed file), then **reload that Caddy** (`docker exec <caddy> caddy reload
   --config /etc/caddy/Caddyfile`). Idempotent on the templated file + a reload handler.
8. **Ingestion user + health-wait: handled in-stack by `oo-init`** (started by task 6). It polls `/healthz`,
   then creates the ingest user idempotently. There is no separate Ansible task. Verify with
   `docker compose logs oo-init` (`created` / `already exists`). See §5.3.

**Handler** *recreate stack*: `community.docker.docker_compose_v2: project_src=/opt/open-observe
state=present` (compose detects the changed files and recreates affected containers).

### 5.3 Ingestion-user provisioning (in-stack, via `oo-init`)

**What runs it:** a one-shot `oo-init` service in `compose.s3.yml` / `compose.local.yml`, not an Ansible API
task, by design (provisioning lives in the deployable stack, so `docker compose up` self-bootstraps). It
waits for `/healthz`, then, authenticated as root, lists users and creates the ingest user via
`POST /api/default/users` only if missing. Idempotent; runs on every `up`/recreate, then exits.

**OSS reality on roles (verified on v0.91.0):** OpenObserve OSS accepts **only `role: admin`** when creating
a user. `editor` / `viewer` / `user` are rejected with `400 {"message":"Custom roles not allowed"}`, because
non-admin roles are enterprise RBAC (OpenFGA). Service accounts exist in OSS but have **full access by
default**, so they are not scoped either. **There is no least-privilege ingestion identity on OSS:** the
ingest user is an org **admin**. The value of a dedicated user is credential hygiene (root stays off every
shipper; the ingest credential rotates/disables independently), not privilege reduction. True scoping needs
Enterprise (OpenFGA + scoped service accounts), which conflicts with decision #1 (open-source).

**Auth:** the sink authenticates with `Basic base64(ingest_email:ingest_password)`. Password-based basic auth
works for user creation on v0.91.0 (`POST` returns `200`); the per-user passcode at `GET /api/default/passcode`
is an alternative token, not needed here.

**API reference** (org = `default`, authenticated as root, over the loopback UI port):
- List:   `GET  /api/default/users`
- Create: `POST /api/default/users` body `{email, first_name, last_name, password, role}`. `role` **must be
  `admin`** on OSS.
- Update: `PUT  /api/default/users/{email}`, to converge password/role. Resetting the password rotates the
  live ingestion credential, so coordinate with a shipper redeploy or you will 401 live shippers.

The exact provisioning script (health-wait, list, check-then-create) lives in the `oo-init` service in
`compose.s3.yml` / `compose.local.yml`. Its `POST` prints OpenObserve's status and body on failure, so a
rejected create (bad role, password policy) is visible in `docker compose logs oo-init` rather than a bare
curl exit. Because the check-then-create guard skips an existing email, changing `OO_INGEST_PASSWORD` later
does **not** rotate an existing user; that needs a manual `PUT` and a coordinated shipper redeploy.

### 5.4 Log-shipper role (each identity host)

- **Code (done):** the sink now reads from the `OpenObserve` section of `OdinConfiguration`, gated on
  `OpenObserve:Enabled`. The app builds the `Basic` header itself from `Username`/`Password`, and the org
  is hardcoded to `default`, so the shipper role only templates these keys (env or appsettings):
  - `OpenObserve__Enabled: true`
  - `OpenObserve__Endpoint: https://{{ oo_ingest_hostname }}`   (gRPC base URL, no path)
  - `OpenObserve__Protocol: Grpc`
  - `OpenObserve__Username: {{ oo_ingest_email }}`
  - `OpenObserve__Password: {{ oo_ingest_password }}`           (secret, from vault)
  - `OpenObserve__Stream: homebase`
  - `OpenObserve__ServiceName: <this identity's domain>`        (so logs are attributable per host)
- No collector. Notify a handler to restart the app service on config change.

---

## 6. Secrets (Ansible vault)

Encrypt the secret values in `group_vars/log_host/vault.yml` with `ansible-vault`:

| Variable | Secret? | Purpose |
|----------|---------|---------|
| `oo_root_email` / `oo_root_password` | **yes** | UI admin + provisioning API auth |
| `oo_ingest_email` / `oo_ingest_password` | **yes** | dedicated ingestion credential (an org **admin** on OSS; see §5.3) used by every shipper |
| `oo_ingest_hostname` | no | ingestion DNS name (used by the Caddy snippet + the shippers' endpoint) |
| `openobserve_version` | no | pinned image tag |
| `oo_retention_days` | no | retention window |
| `oo_s3_access_key` / `oo_s3_secret_key` | **yes** | Hetzner S3 credentials for the data backend |
| `oo_s3_server_url` / `oo_s3_region` / `oo_s3_bucket` | no | Hetzner endpoint, region, bucket name |

Non-secret values can live in `defaults/main.yml` or plain `group_vars`. Never commit the secret ones in
clear; `compose.s3.yml` uses `${...}` indirection so no secret ever lands in git.

---

## 7. Security model (recap)

- **In transit:** the host's existing Caddy terminates a real ACME cert at `:443`; only Caddy is public;
  OO gRPC is published on `127.0.0.1:5081` (loopback only), reachable by the host-mode Caddy but not off-box.
- **Auth:** dedicated non-root **admin** user for ingestion (OSS has no scoped role, see §5.3); root kept
  off the shippers; all creds from vault.
- **UI:** loopback + SSH tunnel; never publicly exposed.
- **Network:** security-group/`DOCKER-USER` allowlist (ufw is bypassed by Docker); 80 for ACME, 443 from
  identity hosts.
- **At rest:** secrets in `ansible-vault`; none in git.

---

## 8. Operations

- **Cert persistence:** handled by the host's existing Caddy (its own data volume), already in place for
  the other sites. This stack no longer owns a Caddy volume.
- **SQLite metadata is a critical local SPOF — back it up.** With data in S3, the `oo-data` volume holds
  only the SQLite metadata (including the file-list index that maps queries to S3 objects), the WAL, and
  the read cache. The log **data** is durable in S3, but **lose the SQLite metadata and OpenObserve cannot
  locate or read that S3 data** ("losing the SQLite data will make OpenObserve inoperable"). Snapshot or
  copy the metadata on a schedule; this is now mandatory, not optional.
- **Retention & disk:** retention stays time-based (`OO_RETENTION_DAYS`, global or per-stream); there is
  **no size-based retention**. Local disk is bounded by the read cache (`ZO_DISK_CACHE_MAX_SIZE`); S3 holds
  the bulk. Monitor S3 bucket growth and tune the retention window.
- **S3 I/O shape:** writes are batched Parquet PUTs (every `ZO_FILE_PUSH_INTERVAL`); the compactor adds
  read-merge-write-delete churn (write amplification); reads are cached range-GETs, so recent-window
  queries rarely touch S3. Keep host and bucket in the same Hetzner region.
- **Upgrades:** pin `OPENOBSERVE_VERSION`; bump in vault/defaults; the compose handler pulls + recreates.
  (Caddy is the host's existing container — upgrade it on its own cadence.) Validate in staging first.
- **Self-monitoring:** probe `/healthz` and `/readyz`; optionally scrape OO's own Prometheus metrics.

---

## 9. Open items / prerequisites before go-live

- [x] **Code:** sink config externalized into the `OpenObserve` section of `OdinConfiguration` (done).
- [x] **Storage:** resolved — data in Hetzner S3 via `ZO_LOCAL_MODE_STORAGE=s3`, `ZO_LOCAL_MODE` unset.
- [x] **Pin** `OPENOBSERVE_VERSION=v0.91.0` (OSS GA, 2026-06-22). (Caddy version is managed by the host's
      existing Caddy, not this stack.)
- [ ] **DNS** for `ingest.logs.<domain>`; confirm inbound 80/443 (else DNS-01 + custom Caddy image).
- [x] **Ingestion user:** provisioned in-stack by `oo-init`; OSS role is **`admin`-only** (verified on
      v0.91.0, non-admin roles 400 "Custom roles not allowed"). Not least-privilege; see §5.3.
- [ ] **Verify end-to-end ingestion:** point one real Serilog shipper at the endpoint and confirm logs land
      in the `homebase` stream using the ingest user's `Basic` auth.
- [ ] **Verify** single-node + S3 on the pinned version. (Hetzner settings resolved: region `hel1`,
      endpoint `https://hel1.your-objectstorage.com`, path-style `false` / virtual-hosted.)
- [ ] **Back up** the SQLite metadata volume (now critical with S3 data; §8).
- [ ] **Firewall/security-group** rules (Docker/ufw caveat).
- [ ] **Sizing:** estimate GB/day → S3 growth + retention window; size local disk for cache + WAL.
