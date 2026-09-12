# Admin API: per-tenant storage and activity metrics

`GET /api/admin/v1/tenants/metrics`

Built for fleet backup/DR monitoring: one figure per identity per day, so a drop between two days
is a finding. Spec: [`docs/superpowers/specs/2026-09-12-admin-tenants-metrics.md`](superpowers/specs/2026-09-12-admin-tenants-metrics.md).

Auth is the same as the rest of the admin API: the `Admin:ApiKeyHttpHeaderName` header with
`Admin:ApiKey`, on `Admin:ApiPort`, with `Host` matching `Admin:Domain`. Wrong domain or wrong port
returns 404 on purpose.

```bash
curl -s -H "Odin-Admin-Api-Key: $KEY" \
  --resolve i1.na.ravenhosting.cloud:4444:127.0.0.1 \
  https://i1.na.ravenhosting.cloud:4444/api/admin/v1/tenants/metrics
```

The `--resolve` matters: hitting `https://127.0.0.1:4444` directly gets the TCP connection dropped
in under 2ms because SNI does not match the certificate. That looks exactly like a dead service.

## Response

```jsonc
{
  "generatedAt": 1757664000000,
  "databaseType": "postgres",
  "indexOrphanScanSupported": true,
  "tenants": [
    { "id": "41c8334b-feee-48f9-8475-a55903c2330b",
      "domain": "frodo.dotyou.cloud", "registered": true, "orphanSource": null,
      "enabled": true, "enablePublicWebPresence": true,
      "email": "frodo@example.com", "planId": "free",
      "createdAt": 1750000000000, "markedForDeletionDate": null,
      "lastActivity": 1757663000000,
      "files": 18, "totalBytes": 96861, "activeBytes": 96861,
      "driveCount": 7, "registrationSize": 0 },

    { "id": "aa81d69e-d4b6-49a2-a6d4-764745ca1efc",
      "domain": null, "registered": false, "orphanSource": "index",
      "files": 18, "totalBytes": 96861, "activeBytes": 96861, "driveCount": 3 }
  ]
}
```

`id` is always a canonical UUID string, on every row, registered or not. Timestamps are unix
milliseconds. **Null means "not applicable or unknown", never zero.**

| field | meaning |
|---|---|
| `totalBytes` | Every `drivemainindex` row for the identity, including soft-deleted tombstones (a delete keeps the row with a header-sized `byteCount`). Same number the existing `/tenants/{domain}?include-payload=true` reports as `payloadSize`. |
| `activeBytes` | `fileState = Active` rows only — "how much would we restore". |
| `files` | Row count, tombstones included. |
| `driveCount` | Drives owned by this identity. |
| `registrationPath` / `payloadPath` | Where the registration directory and the payloads live (payloads being the S3 `service/bucket/id` prefix when S3 payloads are on). Populated for orphans too: for an identity with no registration, this is where to go looking for what it left behind. |
| `registrationSize` | Bytes on local disk under the registration directory. On Postgres + S3 this is legitimately near zero: the database is remote and payloads are in S3. Only meaningful on SQLite. |
| `lastActivity` | See the caveat below — this is **not** "when the tenant was last used". |
| `enabled` | Inverse of `registrations.disabled`, toggled by `odin-cli tenant enable` / `disable`. Boolean. |
| `enablePublicWebPresence` | The other CLI-togglable flag (`odin-cli tenant enable-public-web-presence` / `disable-public-web-presence`). Boolean; the V202607101000 migration backfilled every existing row to true. |
| `markedForDeletionDate` | Null, or when the **owner** requested deletion (`OwnerSecurityController`, not the CLI). A tenant pending deletion is neither enabled nor gone, and a report that cannot say so will mislabel it. |
| `planId` | Free text, `"free"` everywhere today. See caveat 5. |
| `registered` | False when the identity has no `Registrations` row. Such rows have `domain: null`; we do not invent a name. |
| `metricsError` | Null on a healthy row. Set when that tenant's figures could not be read, with the storage fields left **null rather than zero** — a zero there is indistinguishable from the data loss this report exists to detect. One unreadable tenant does not fail the whole report. |
| `orphanSource` | `"index"` (still owns rows) or `"directory"` (a registration directory with no registration). Null when registered. |

### Orphan detection

Two independent scans, because they catch different things:

- **`index`** (`IdentityStorageCensus`) — one `GROUP BY identityId` pass over the whole database.
  It earns its place twice over. First, it sees identities the registry cannot name: totalling one
  identity's storage is just `SUM(byteCount) WHERE identityId = @id`, but that only works for
  identities you can *enumerate*, and the enumeration comes from the registry — so an identity
  whose registration is gone can never be asked about. Measured on na-metal on 2026-09-12,
  `registrations` held 3 tenants while `drivemainindex` held 4 distinct `identityId`s, two of them
  carrying ~166 KB with no registration at all. Second, it is one query instead of N: the same pass
  supplies every *registered* tenant's figures too, so the endpoint issues two queries rather than
  two per tenant.
  **Postgres only**: there every tenant shares one database; on SQLite each tenant has its own file
  and an orphaned file is not reachable from any other. `indexOrphanScanSupported` says which you
  got. On SQLite an identity whose rows survived but whose directory was removed will not appear.
- **`directory`** — a scan of the registration root for directories named with a GUID that has no
  registration. Works on both backends. Counts are `null`: we deliberately do not open a stray
  database file to measure it.

An identity found by both is reported once, as `index`, since that row carries real counts.

## One call, not two

This endpoint is a **superset** of `GET /tenants` — there is no need to call both. Every field the
older endpoint carries has an equivalent here:

| `/tenants?include-payload=true` | `/tenants/metrics` |
|---|---|
| `domain`, `id`, `enabled`, `enablePublicWebPresence` | same names |
| `registrationPath`, `registrationSize`, `payloadPath` | same names |
| `payloadSize` | `totalBytes` (identical figure — both sum `byteCount` over every file state) |

`AdminControllerTest.ItShouldSupersedeTheTenantEndpoint` asserts this field by field, so the two
cannot drift apart silently.

The older endpoint is unchanged and still serves `Odin.Cli` (`odin-cli tenants list`), which
deserializes `TenantModel`.

## Caveats, and corrections to the original spec

These were checked against this repository; the notes say where a claim is inference rather than
something read from code.

1. **`payloadSize` on the existing endpoint was never broken.** The query parameter is
   `include-payload`, not `includePayload`. `?includePayload=true` binds to nothing and silently
   leaves the flag false, which is why the field measured `null` on the live fleet. *Inference: the
   nulls observed on `na-metal` are this mismatch; that was not reproduced against the fleet.*

2. **`registrationSize: 0` on the live cluster is correct.** It is a real recursive directory walk;
   on Postgres + S3 the directory is genuinely near-empty. The repo's own test only asserts it is
   nonzero under SQLite.

3. **`lastseen` is not a per-tenant table, and its unregistered subjects are not deletion
   residue.** `LastSeenMiddleware` records `odinContext.Caller.OdinId` — the **caller**. For peer
   traffic that is a *remote* identity, so the table legitimately holds domains that are not tenants
   here; there is no tenant column. `lastActivity` therefore means "last time this identity called
   this node", which for owner and app sessions is the tenant itself. The unregistered subjects the
   spec found in `lastseen` are explained by this. The orphaned `drivemainindex` rows are a
   separate, real finding.

4. **`byteCount` does include S3 payloads.** It excludes payloads only when
   `FileMetadata.PayloadsAreRemote`, which means *the payload is hosted by another identity*, not
   "stored in S3".

5. **Quota: `planId` is the intended seam, but carries no quota today.** It is free text supplied
   by the caller at registration, never validated against any list, defaulted to `"free"`, and never
   branched on; there is no plans table, no config section, and nothing enforces a limit on
   `byteCount`. The plan on a registration (`"free"` on every tenant today) is what quotas are
   expected to hang off once plans exist, so `planId` is exposed here and a consumer should key
   quota lookups on it. Until then, resolve plan-to-quota outside odin-core.

6. **Byte order is only a concern outside .NET.** Guids are stored as `Guid.ToByteArray()` (the
   mixed-endian .NET layout) in an opaque `BYTEA`/BLOB, and round-trip unchanged. The API has always
   emitted canonical UUIDs. Reading the table by hand from psql is where you must swap the first
   three groups.

### Freshness

The figures come through the per-tenant table caches. This endpoint asks for a 1 minute TTL
(`TenantAdmin.MetricsCacheTtl`) rather than the 2 hour table default, since a report whose job is to
notice missing data should not be hours stale.

**Known pre-existing defect, not introduced by this endpoint:** cache invalidation does not appear
to reach these aggregates — an upload does not refresh `payloadSize` on the existing
`/tenants/{domain}?include-payload=true` route within its TTL either. Staleness is bounded by the
TTL, so with the 1 minute TTL above this endpoint is fine for daily collection, but the invalidation
path is worth a separate look.

### Cost

On **Postgres** the whole node costs two grouped queries: the census supplies both the registered
tenants' figures and the orphans. It is a full table scan — no index covers `byteCount` — so it is
fine at the spec's "once a day" cadence and should not be put on a per-minute poll.

On **SQLite** there is no census, so registered tenants are measured one at a time through a
sequential loop over tenant scopes, each in its own child lifetime scope because
`ScopedConnectionFactory` is not safe for concurrent use. That is N round trips, which is
acceptable for a dev or single-tenant box and is the reason the bulk path exists for production.

The per-tenant query (`TableDriveMainIndexCached.GetIdentityStorageStatsAsync`) remains public and
is the right call for anything that wants one tenant's figures on demand, such as a dashboard.

## Not answered here

Whether tenant deletion removes the payload **objects** from S3 is a separate question that cannot
be answered from this repository. This endpoint reports orphaned index rows only. It should be
answered before anyone relies on deletion being complete.
