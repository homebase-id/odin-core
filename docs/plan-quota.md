# Plan quotas: disk usage limits per hosting plan

Status: built on branch `plan-quota`.

## Today

- An identity's plan is `PlanId` on its registration. Since #1908 it comes only from the invitation code it
  signed up with; without one the registry stores `free`.
- `TenantQuotaGuard.AssertCanAddPayloadBytes()` is the one enforcement point. It is called only where payload
  or thumbnail bytes are added (the upload, update and attachment writers, the peer equivalents, the profile
  photo, contact images and the contacts' app blob) and refuses with 507 and a one-hour Retry-After. Peers keep
  the item in their outbox and retry. It fires only while an admin has set the identity's status to
  `OutOfQuota`.
- Usage is already counted: `driveMainIndex.GetTotalSizeAllDrivesAsync()` is `SUM(byteCount)` over the
  identity's index rows, cached and cleared on drive writes. The admin metrics report the same figure as
  `totalBytes`.
- Nothing maps a plan to a limit, and nothing compares usage to one.

## Decisions

| | |
|---|---|
| Where plans are defined | Config, per host |
| A plan that is not in the config | Gets the `free` plan's quota |
| What counts as usage | All indexed bytes, tombstones included (`totalBytes`) |
| Enforcement | A live check in the existing guard, no automatic status changes |
| Out of quota | File headers still upload and update; payloads and thumbnails do not |
| Owner and apps | Can read their usage on the V2 API |

## Design

### 1. Plans in config

```json
"Registry": {
  "Plans": [
    { "Id": "free", "QuotaBytes": 1073741824 },
    { "Id": "pro-100gb", "QuotaBytes": 107374182400 },
    { "Id": "unlimited" }
  ]
}
```

- `QuotaBytes` missing or null means unlimited.
- No `Plans` section at all means quotas are off, so self-hosted servers and existing hosts behave as today.
- If `Plans` is set it must contain `free`, and ids must be unique; otherwise the host fails at startup, like
  the overlapping invitation-code check in `RegistrySection`.
- Lookup is by exact id, case-insensitive. An id not in the list, or a null plan, gets `free`'s quota.

### 2. One resolver

`PlanQuotas.QuotaFor(string? planId) => long?` (null for unlimited) reads the config once. The guard, the V2
endpoint and the admin metrics all call it, so they never disagree about a limit.

### 3. Enforcement in the guard

`AssertCanAddPayloadBytes` becomes `AssertCanAddPayloadBytesAsync` and refuses when either:

- the status is `OutOfQuota` (still settable by hand, for an admin who wants to stop an identity regardless of
  plan), or
- the identity's quota is not unlimited and its cached usage is at or above it.

Same 507, same Retry-After, same message to peers, so nothing downstream changes. Because the guard is only
called where payload or thumbnail bytes are added, headers, metadata, reactions, read receipts and deletes keep
working, and deleting files brings an identity back under its quota at once.

It is a soft limit: an upload's size is not known until it has been streamed, so the check is "is this identity
already full", not "would this fit". One upload can go over. That avoids buffering or pre-declared sizes.

The guard needs the identity database for the usage figure, so it changes from a per-tenant singleton to a
per-lifetime-scope registration in `TenantServices`, and its seven callers await it.

### 4. V2 endpoint for the owner and apps

`GET /api/v2/storage/usage`, policy `OwnerOrApp`, no permission key:

```json
{ "planId": "free", "usedBytes": 734003200, "quotaBytes": 1073741824, "isOverQuota": false }
```

`quotaBytes` is null for unlimited. `isOverQuota` is exactly what the guard would decide, including a hand-set
`OutOfQuota`, so an app can explain a 507 before the user hits one.

### 5. Admin metrics

`/api/admin/v1/tenants/metrics` gains `quotaBytes` per registered identity, from the same resolver.

## Out of scope

- Changing an identity's plan after signup (an admin command). Today a plan is fixed at signup.
- Warning the owner as they near their quota.
- Email storage, which Stalwart limits on its own.
- Hard limits that refuse an upload that would cross the quota.

## Tests

- Config: no section means off; `free` required; duplicate ids refused; unknown and null plans resolve to
  `free`; null `QuotaBytes` is unlimited.
- Guard: refuses payloads and thumbnails at or over quota, allows them under it, allows headers either way,
  still honours a hand-set `OutOfQuota`, never refuses with quotas off or an unlimited plan.
- Integration (V2): an identity over a small quota can upload a header but gets 507 for a payload; after a
  delete it can upload again; the usage endpoint reports the same numbers for the owner and an app.
- Admin metrics include `quotaBytes`.
