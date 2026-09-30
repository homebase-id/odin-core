# Moving an identity to another host

The runbook for moving one identity (tenant) from a source host (or cluster) to a target host.
It is written for an operator, and for an agent working in this folder (see `CLAUDE.md`).

The database rows move in a file (export, then import). The payloads (files, photos, thumbnails)
follow on their own: the target pulls them from the source over HTTPS in the background, starting
the moment the import commits. See **Payloads** below.

## Email does not move (yet)

**We do not yet export an identity's email.** An identity moved with email activated arrives on the
target **without working email**:

- **Left behind:** the mailbox and every message in it (they live in the mail server, not in
  Homebase), the mailbox's account, aliases and app passwords, and the DKIM signing keys (the export
  leaves the `DkimKeys` rows out on purpose, and says so for each key).
- **Carried, but not working:** the email setup record and the email drive's files are ordinary
  identity data and move with the rest. The target can therefore look as if email is set up while
  it has no mailbox and no DKIM keys.
- **Not checked:** what happens to the identity's mail DNS records (MX, SPF, DMARC, DKIM TXT) when
  DNS is repointed.

Until email transfer is built, move only identities without email activated. If the export prints
`Leaving DKIM key ... behind`, the identity has email: stop and ask the operator.

## Before you start

You need:

- **Admin API access to both hosts.** `odin-admin` (in the host image at `/app/cli`) with
  `-I <Admin:Domain>:<Admin:ApiPort>` and `-K <Admin:ApiKey>`, or `ODIN_ADMIN_IDENTITY_HOST` /
  `ODIN_ADMIN_API_KEY`. The admin API answers only on that host name and port, so the name must
  resolve to the host (an `/etc/hosts` entry, or `curl --resolve`; see `docs/admin-tenant-metrics.md`).
- **A shell on a source host and a target host**, to run the host binary's CLI verbs inside the running
  container (checked by ops on every cluster, 2026-09-29):

  ```
  sudo docker exec -i <container> dotnet /app/web/Odin.Hosting.dll <verb> ...
  ```

  - `<container>` is `identity-host-identity-host-1` on OVH (NA and EU, either core) and
    `identity-host` on Hetzner.
  - Use `-i` or no flags. `-t` needs a terminal and fails in scripts and over ssh.
  - The verb runs with the container's environment, so the host's own configuration applies. `-e NAME=value`
    adds a setting for that one command only, never for the running host.
  - Write export files under **`/identity-host/tmp`**: a bind mount at the same path inside and outside
    the container, which survives redeploys. Anywhere else inside the container is wiped by the next
    deploy (the root filesystem is writable, so a write there seems to work). Never write into
    `/identity-host/data/...`.

  Below, `Odin.Hosting <verb>` is short for that whole command.
- **PowerDNS API access** (`Registry:PowerDnsHostAddress`, `Registry:PowerDnsApiKey`) for the TTL step.
- **The same odin-core version on both hosts.** Import refuses if any table version differs.
- **`PayloadMove:SourceEnabled=true` in the source host's configuration**: the running host serves the
  payloads, so passing it to the export command with `-e` is not enough. Export refuses while it is off.
  The target pulls from the source's provisioning domain (`Registry:ProvisioningDomain`) over public
  HTTPS; ops checked that `createme.na.ravenhosting.cloud`, `createme.eu.ravenhosting.cloud` and
  `createme.ravenhosting.cloud` all reach each other (2026-09-29).

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
the old answer that long, so lower the TTL at least one old TTL (an hour) before the cutover. On a
**source** host, while the identity is still active there:

```
Odin.Hosting repoint-identity-dns <domain> --ttl 60            # dry run: only the TTLs change
Odin.Hosting repoint-identity-dns <domain> --ttl 60 commit
```

It writes only this identity's record sets, with the host's own values, which on the source are the
current ones. The dry run lists each record set as `now:` and `new:`; if any value other than the TTL
would change, stop: the source's configuration and the zone disagree. Then wait for the old TTL to
pass.

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
Odin.Hosting identity-export <domain> /identity-host/tmp/<domain>.json
```

- Refuses while the identity is not paused (or disabled) long enough, and says why.
- Writes `<domain>.json.partial` first and renames it when complete. If it fails, nothing is left
  behind; if a `.partial` from a crash is in the way, it refuses: look at it and delete it.
- Mints a single-use handoff token for the payloads and puts it in the file, with the source's address.
  Exporting again replaces it (the older file can then no longer fetch payloads).
- **The file is the identity**: it holds password data, private keys and the TLS certificate key, the
  last in the clear so the target can re-encrypt it under its own storage key.
  Mode 0600. Move it only over an encrypted channel (`scp` to the target's `/identity-host/tmp`), and
  delete every copy when the move is done.

### 6. Import on the target, dry run first

```
Odin.Hosting identity-import /identity-host/tmp/<domain>.json          # dry run: checks everything, writes nothing
Odin.Hosting identity-import /identity-host/tmp/<domain>.json commit
```

- The target hosts keep running. The identity lands **paused**, and every target node loads it
  within `Registry:CatchUpIntervalSeconds` (30 s) without a restart.
- Refuses cleanly (exit 1, reasons listed) if the target already has the identity, a certificate
  or DKIM rows for the domain, a table version differs, or the file is not a complete export.
- A failed import can simply be run again: it clears what the failed one left.
- Check it arrived: `odin-admin tenant show <domain>` against the target shows `Paused`.
- The payload transfer starts at once, while the identity is still paused. Follow it against the target:
  `odin-admin tenant payload-move <domain>`. It runs newest files first, 5 at a time
  (`PayloadMove:Parallelism`), survives restarts of either host, and waits out a throttling source.

### 7. Repoint DNS to the target

Run on a **target** host. It writes the target's own values from its configuration, for this
identity only:

```
Odin.Hosting repoint-identity-dns <domain> --ttl 60            # dry run: now -> new, per record set
Odin.Hosting repoint-identity-dns <domain> --ttl 60 commit
```

The record sets are the `A` at the name, the `capi` and `file` CNAMEs and, while tenant mail is on
(every cluster today, whether or not the identity has email), the mail set:

- the MX;
- the `mta-sts` CNAME;
- the `_mta-sts` TXT, whose policy id is derived from the target's own mail servers;
- the SPF, `_dmarc` and `_smtp._tls` TXTs.

Across clusters, expect six to change (A, capi, file, MX, mta-sts, `_mta-sts`) and the SPF, DMARC
and TLS-RPT values to stay the same. The MX, the `mta-sts` host and the `_mta-sts` id must name the
same cluster. The command writes them together, in one change. It never touches DKIM records,
DNSSEC or another identity's records, and it refuses unless the identity is registered on this
host and not disabled here, so after the move it cannot run on the source.

Verify from outside: `dig +short <domain>` (and `capi.<domain>`, `file.<domain>`, `MX <domain>`)
answers the target's values, and the registration API on the target reports the domain valid
(step 1).

Once the move has settled (after step 8), put the TTL back on the target:
`Odin.Hosting repoint-identity-dns <domain> commit` (3600 is the default).

**Never run `populate-managed-domain-records` or `create-own-domain-zones` on a source host.**
They rewrite the DNS of every identity the host has registered, and the source still has the moved
identity (paused, then disabled). Run there, even months later as a routine backfill, they point
it back at the source. Nor are they needed for a move: `repoint-identity-dns` does it for one
identity.

### 8. Resume on the target

```
odin-admin tenant resume <domain>            # against the target's admin API
```

Peers that queued messages retry within their `Retry-After` and now reach the target.

You do not have to wait for the payload transfer to finish: until a payload arrives, reading it answers
404 with `Cache-Control: no-store` and a `Retry-After`, so nothing caches its absence. Keep following
`odin-admin tenant payload-move <domain>` until it reads `Complete`. If it ends `CompleteWithFailures`,
read the failures it lists; `--retry` runs it again from the newest file, skipping everything that already
arrived.

### 9. Retire the source copy

```
odin-admin tenant set-status <domain> disabled --reason moved    # against the source's admin API
```

A moved identity can never be enabled again on the source. The source keeps the identity's payloads, and
refuses to delete it, until the target has reported the transfer `Complete` (see
`odin-admin tenant payload-move <domain>` against the source).

> **Never run `odin-admin tenant delete` on the source after a move.** Deleting a tenant also
> deletes its DNS: its records in the apex zone (managed domain) or its whole zone (own domain).
> Our PowerDNS is shared, so that deletes the **target's** live DNS. Purging the source's copy is a
> separate command still to be written.

## Payloads

Payload bytes live in each host's payload store (local disk or S3, chosen independently on each host) under the
identity's id, and the two hosts do not share one. The target pulls them from the source's
`https://<provisioning domain>/api/payload-move/...` with the credential it got for the handoff token,
object by object, into its own store. Design: `docs/superpowers/specs/2026-08-31-payload-migration-design.md`.

## What is not covered yet

- **Email:** the mailbox and its messages, the mailbox account and settings, and the DKIM keys. See
  **Email does not move (yet)** above.
- Purging the source's copy (payloads and registration, never DNS) once the transfer is complete.
- Carrying the inbox/outbox queues (`--carry-queues`), and scheduled jobs (file expiry,
  scheduled notifications): they stay behind on the source.
- The host-wide DNS commands skip nothing yet: a moved (disabled) identity is rewritten like any
  other if they run on the source.
