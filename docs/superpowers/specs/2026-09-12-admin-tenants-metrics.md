# Admin API: per-tenant storage and activity metrics

**For odin-core. Written 2026-09-12 from the DevOps side**, after measuring the live `na-metal`
cluster. Everything below marked *verified* was read from the running fleet, not from source.

## Why this is wanted

We are designing backup and DR for the fleet, and the decisive question turned out to be
**"would we notice if tenant data disappeared?"** Today the answer is no: nothing watches storage
at all, and the first signal of a mass deletion would be a user complaining.

The fix needs a daily per-tenant figure — bytes, files, last activity — so a drop between two days
is a finding. We would rather that come from **one implementation in odin-core, exposed on the
admin API, than from SQL we hand-maintain on each cluster.** The tenant→identity mapping, the
`Guid` byte order and the notion of what counts as live data are all odin-core's to own; a
DevOps-side query would duplicate all three and drift.

## What the endpoint returns today — verified

`GET /api/admin/v1/tenants` on i1-1 returns eight fields per tenant:

```
domain, id, enabled, enablePublicWebPresence,
registrationPath, registrationSize,     <- path is real, size is always 0
payloadPath, payloadSize                <- BOTH null on every tenant
```

**`payloadSize` is a promise the tool does not keep.** The field is declared and never populated.
Tried `?includePayload=true`, `?include=size`, and the per-tenant route: all `200`, all `null`.

The reason appears to be that these two fields are *local-filesystem* measurements:
`registrationPath` is a real directory on disk (`/identity-host/data/tenants/registrations/<id>`)
and `payloadPath` is null because **payloads live in S3, not on disk** - there is no local payloads
directory on the host at all. So the current implementation has nothing to stat.

**But the data is not missing, only unqueried.** See below.

## Requested fields, with the source we verified

All column names below were read from the live PostgreSQL 18 database on `db1-1`.

| # | field | source | verified |
|---|---|---|---|
| 1 | **every tenant** | `registrations` (3 rows) | yes - see the orphan note, this list is NOT complete |
| 2 | **total bytes** | `SUM(drivemainindex.bytecount)` per `identityid` | yes - returns real figures |
| 3 | **total files** | `COUNT(*)` on the same grouping | yes |
| 4 | **last activity** | `lastseen.timestamp` keyed by `lastseen.subject` = domain | yes - unix millis |
| 5 | **quota** | `registrations.planid` (all `free` today) | partly - see open question |
| 6 | **enabled / capabilities** | `registrations.disabled`, `.enablepublicwebpresence`, `.json` | yes |

The working query, which returns bytes *and* files and separates live from deleted:

```sql
SELECT encode(identityid,'hex') AS id_hex,   -- NOT the output format, see below
       COUNT(*)                                        AS files,
       SUM(bytecount)                                  AS bytes,
       SUM(bytecount) FILTER (WHERE filestate = 1)     AS active_bytes
FROM drivemainindex
GROUP BY identityid;
```

Live result, 2026-09-12: four identities, 13-18 files each, 69-97 KB each.

### REQUIRED: `id` is always a canonical UUID string, never the stored bytes

**Emit `id` in the same form the existing endpoint already uses** -
`41c8334b-feee-48f9-8475-a55903c2330b` - **for every row, including identities with no
registration.** Never the raw or hex byte form.

This is a requirement rather than a nicety because of trap 1 below: the stored bytes are
little-endian in the first three groups, so `9ed681aab6d4a249a6d4764745ca1efc` and the UUID it
represents look unrelated. A consumer given hex cannot join it to anything - not to the tenant
list, not to logs, not to a support ticket - without independently rediscovering the byte-order
rule. The `encode(...,'hex')` above is how we *read* the table by hand; it is explicitly **not**
the shape we want back.

**Orphaned identities have no domain**, since the `registrations` row is what carries it. Return
them anyway, with the canonical `id`, a null `domain`, and a flag distinguishing them - do not omit
them, and do not invent a name. Suggested shape:

```json
{ "id": "aa81d69e-d4b6-49a2-a6d4-764745ca1efc",
  "domain": null,
  "registered": false,
  "files": 18, "totalBytes": 96861, "activeBytes": 96861 }
```

A caller can then say "31 files belong to identities that are not registered" without doing any
byte manipulation at all, which is the whole point.

**That example id is worth dwelling on.** The stored bytes are `9ed681aab6d4a249a6d4764745ca1efc`
and the canonical id is `aa81d69e-d4b6-49a2-a6d4-764745ca1efc` - **they do not share a single
leading character.** The first draft of this very section got it wrong by inserting dashes into the
hex rather than swapping the bytes, which is exactly the mistake being warned about, made by the
person writing the warning. Convert with `uuid.UUID(bytes_le=...)`, never by reformatting.

### Worth adding while the shape is open

- **`activeBytes` vs `totalBytes`** - `drivemainindex.filestate` distinguishes them. Billing and
  "how much would we restore" are different numbers and we will want both.
- **`markedForDeletionDate`** - `registrations.markedfordeletiondate` exists and is not exposed.
  A tenant pending deletion is neither enabled nor gone, and a report that cannot say so will
  mislabel it.
- **`createdAt`** - `registrations.created`. Tenant age makes a usage figure interpretable.
- **`email`** - `registrations.email`. Who to contact when their data is the anomaly.
- **`driveCount`** - from `drives`. A tenant with an unexpected number of drives is its own signal.

## Three traps for whoever implements this

**1. `identityid` is a .NET `Guid` and the first three groups are little-endian.** The API's
`id` field `41c8334b-feee-48f9-8475-a55903c2330b` is stored as
`4b33c841eefef9488475a55903c2330b`. Comparing the hex against the UUID string returns nothing and
looks exactly like "no such tenant". In python: `uuid.UUID(id).bytes_le.hex()`. This cost us a
wrong conclusion before we spotted it.

**2. `registrations` IS NOT the whole population, and this is a real defect** - see below.

**3. The endpoint is loopback-only and needs correct SNI.** `https://127.0.0.1:4444/...` **TCP
connects and is then dropped in under 2 ms**, which looks precisely like a dead service; it returns
`000` from curl while `ss` shows the socket `LISTEN`. It needs
`--resolve i1.na.ravenhosting.cloud:4444:127.0.0.1`. Already documented in
`ovhcloud/DELETING-AN-IDENTITY.md`; repeated here because it will otherwise be rediscovered.

## A defect this work surfaced: deleted tenants leave payload rows behind

**Verified 2026-09-12.** `registrations` holds **3** tenants. `drivemainindex` holds **4** distinct
`identityid`s. Two of them have no `registrations` row at all:

```
stored bytes                      canonical id                           files    bytes
9ed681aab6d4a249a6d4764745ca1efc  aa81d69e-d4b6-49a2-a6d4-764745ca1efc      18   96,861
df3c12c4215b6040a13026e7d9fb7f0a  c4123cdf-5b21-4060-a130-26e7d9fb7f0a      13   69,599
```

Both ids are given in canonical form above so they can be searched for directly - in logs, in the
S3 prefix listing, or in a support ticket - without anyone having to redo the byte swap.

`lastseen` likewise carries `jane.doe.id.pub` and `test.user9876.id.pub`, neither of which is a
registered tenant. Four identities were deleted on this cluster on 2026-09-10 via the documented
admin-API route, so this is almost certainly their residue.

**Two consequences, and the second is the serious one:**

- **Billing.** We are storing ~166 KB of data for tenants that no longer exist. Trivial today,
  linear in deleted tenants at 200 GB each later.
- **Erasure.** A tenant was told their identity was deleted, and their file rows are still in the
  index. Whether the payload *objects* were removed from S3 is a separate question we have not yet
  answered, and it should be answered before anyone relies on deletion being complete.

This is orthogonal to the metrics work but was found by it, and a metrics endpoint that reports
only `registrations` would **hide** it - which is why item 1 above asks for every identity with
data, not every registration.

## Open question we cannot answer from here

**Quota.** `registrations.planid` is `free` for all three tenants, so quota is presumably a
property of the plan rather than the tenant. There is no plans table in this database and no
plan→quota mapping we can see. Either expose the resolved quota on the tenant, or expose `planId`
and tell us where plans are defined.

## Non-goals

- We do not need this in real time. Once a day is enough; a cached figure refreshed hourly is fine.
- We do not need per-drive or per-file breakdowns.
- This does not replace bucket-level totals. `objectsCount`/`objectsSize` from the OVH API is
  out-of-band and a compromised odin-core cannot lie about it. **Divergence between what odin-core
  believes it stored and what S3 actually holds is itself the signal we want** - so both numbers
  are wanted, and neither substitutes for the other.
