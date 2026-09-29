# Moving an identity to another host

The runbook for moving one identity (tenant) from a source host (or cluster) to a target host.
It is written for an operator, and for an agent working in this folder (see `CLAUDE.md`).

> **Not ready for a real identity yet.** Export and import carry the identity's database rows
> only. Its payloads (files, photos, thumbnails) do not move: the payload transfer is still to be
> built. Until it is, run this only as a rehearsal, on a test identity whose payloads you can
> lose. See **Payloads** below.

## Before you start

You need:

- **Admin API access to both hosts.** `odin-admin` (in the host image at `/app/cli`) with
  `-I <Admin:Domain>:<Admin:ApiPort>` and `-K <Admin:ApiKey>`, or `ODIN_ADMIN_IDENTITY_HOST` /
  `ODIN_ADMIN_API_KEY`. The admin API answers only on that host name and port, so the name must
  resolve to the host (an `/etc/hosts` entry, or `curl --resolve`; see `docs/admin-tenant-metrics.md`).
- **A shell on a source host and a target host**, to run the host binary's CLI verbs. They read the
  host's own configuration. *Unverified:* in the Docker deployment that is
  `docker exec -it <container> dotnet /app/web/Odin.Hosting.dll <verb> ...`, with files written under
  the mounted `/homebase` so they survive the container. Confirm with ops before the first move.
- **PowerDNS API access** (`Registry:PowerDnsHostAddress`, `Registry:PowerDnsApiKey`) for the TTL step.
- **The same odin-core version on both hosts.** Import refuses if any table version differs.

Throughout, `<domain>` is the identity's domain, for example `frodo.id.pub`.

## Steps

### 1. Check that we control the identity's DNS

We can only repoint DNS that lives in our PowerDNS. Classify the domain:

| Kind | How to tell | Movable? |
|---|---|---|
| **Managed domain**, a name under one of our apexes (`Registry:ManagedDomainApexes`, e.g. `name.id.pub`) | The domain ends in a managed apex. Its records sit in the apex zone: an `A` record at the name, `CNAME`s `capi.<name>` and `file.<name>`. | Yes |
| **Own domain, delegated to us** | `dig NS <domain>` answers only our nameservers (`Registry:DnsRecordValues:NameServers`). The zone is in our PowerDNS. | Yes |
| **Own domain with the user's own records** | Its NS are the user's DNS provider; the apex `A`/`ALIAS` and the `CNAME`s are set there by the user. | **No.** Stop: the user has to change those records. Moving it needs their cooperation. |

The registration API reports the per-record status as the source host sees it:
`GET https://<Registry:ProvisioningDomain>/api/registration/v1/registration/own-domain-dns-status/<domain>`
(200 = valid, 202 = not; works for any domain; needs `Registry:ProvisioningEnabled`).

If the domain has any `AAAA` record on the apex, `capi` or `file`, stop and fix that first: the
records must be exactly what the host expects (`DnsLookupService.VerifyDnsValue`).

### 2. Check the queues (optional, recommended)

Messages still waiting in the identity's inbox or outbox **do not move** (import skips them and
warns). Losing a few is tolerable, but check how many there are:

- **SQLite:** each identity has its own database file,
  `<Host:TenantDataRootPath>/registrations/<identityId>/headers/identity.db`:
  `sqlite3 <that file> "select 'inbox', count(*) from inbox union all select 'outbox', count(*) from outbox"`.
- **Postgres:** the tables are shared; filter by `identityId`. It is stored as .NET
  `Guid.ToByteArray()`, which byte-swaps the first three groups of the UUID, so a plain hex decode
  of the id does **not** match. Read the bytes off one of the identity's rows instead.
- **Or, after step 5:** the dry-run import on the target prints
  `skipped Inbox: N queued item(s), which do not move with the identity` (and the same for Outbox).

If there is a backlog, let it drain (resume, wait, pause again) before exporting.

### 3. Lower the DNS TTL, well ahead of the move

Our records are written with a TTL of 3600 s (`PowerDnsRestClient.DefaultTtl`). Resolvers cache
the old answer that long, so lower the TTL at least one old TTL (an hour) before the cutover.
Nothing in odin-core does this; use the PowerDNS API on the zone that holds the records (the apex
zone for a managed domain, the domain's own zone otherwise). For each of the `A` and both `CNAME`s:

```
PATCH https://<PowerDnsHostAddress>/api/v1/servers/localhost/zones/<zone>.
X-API-Key: <PowerDnsApiKey>
{"rrsets":[{"name":"<record>.","type":"A","ttl":60,"changetype":"REPLACE",
            "records":[{"content":"<unchanged value>","disabled":false}]}]}
```

`REPLACE` needs the full record set, so copy the current values (`GET` the zone first). Then wait
for the old TTL to pass.

### 4. Pause the identity on the source, and wait

```
odin-admin tenant pause <domain>            # against the source's admin API
```

Callers now get 503 with `Retry-After` and peers queue their messages for later (they wait up to
7 days). Every source node stops the identity's workers and jobs within about 30 s. Requests
already in flight are allowed to finish.

Wait **at least 2 minutes**: the export refuses until the identity has been paused for
`2 × Registry:CatchUpIntervalSeconds + 60` s, and says how long is left.

### 5. Export on the source

```
Odin.Hosting identity-export <domain> <file.json>
```

- Refuses while the identity is not paused (or disabled) long enough, and says why.
- Writes `<file.json>.partial` first and renames it when complete. If it fails, nothing is left
  behind; if a `.partial` from a crash is in the way, it refuses: look at it and delete it.
- **The file is the identity**: it holds password data, private keys and the TLS certificate key.
  Mode 0600. Move it only over an encrypted channel, and delete every copy when the move is done.

### 6. Import on the target, dry run first

```
Odin.Hosting identity-import <file.json>          # dry run: checks everything, writes nothing
Odin.Hosting identity-import <file.json> commit
```

- The target hosts keep running. The identity lands **paused**, and every target node loads it
  within `Registry:CatchUpIntervalSeconds` (30 s) without a restart.
- Refuses cleanly (exit 1, reasons listed) if the target already has the identity, a certificate
  or DKIM rows for the domain, a table version differs, or the file is not a complete export.
- A failed import can simply be run again: it clears what the failed one left.
- Check it arrived: `odin-admin tenant show <domain>` against the target shows `Paused`.

### 7. Repoint DNS to the target

Run on a **target** host (it writes the target's own IP and alias host from its configuration):

```
Odin.Hosting populate-managed-domain-records commit     # managed domains
Odin.Hosting create-own-domain-zones commit             # own domains delegated to us
```

Both walk every identity registered on that host and write its records idempotently. The moved
identity's records now point at the target, **with the TTL back at 3600**. Run without `commit`
first to see what it would do.

Verify from outside: `dig +short <domain>` (and `capi.<domain>`, `file.<domain>`) answers the
target's values, and the registration API on the target reports the domain valid (step 1).

### 8. Resume on the target

```
odin-admin tenant resume <domain>            # against the target's admin API
```

Peers that queued messages retry within their `Retry-After` and now reach the target.

### 9. Retire the source copy

```
odin-admin tenant set-status <domain> disabled --reason moved    # against the source's admin API
```

A moved identity can never be enabled again on the source.

> **Never run `odin-admin tenant delete` on the source after a move.** Deleting a tenant also
> deletes its DNS: its records in the apex zone (managed domain) or its whole zone (own domain).
> Our PowerDNS is shared, so that deletes the **target's** live DNS. It would also delete the
> source's payloads, which the payload transfer still needs.

## Payloads

Payload bytes live in the host's payload store (local disk or S3) under the identity's id, and the
two hosts do not share a store. The design for moving them over HTTP between the hosts, in the
background after the cutover, resumable and several at a time, is
`docs/superpowers/specs/2026-08-31-payload-migration-design.md` (being revived). Until it ships,
`identity-export` and `identity-import` refuse unless payloads are on S3, and an imported identity
has file headers whose payloads are missing.

## What is not covered yet

- Payload transfer (above).
- Carrying the inbox/outbox queues (`--carry-queues`), and scheduled jobs (file expiry,
  scheduled notifications): they stay behind on the source.
- A DNS command that lowers and restores the TTL, and one that repoints a single identity rather
  than walking every identity on the host.
