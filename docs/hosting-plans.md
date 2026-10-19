# Hosting plans: a disk quota and a public web presence per plan

Status: built on branch `plan-quota` (#1974).

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
| Public web presence | The plan decides it when it says, live; otherwise the identity's own setting |

## Design

### 1. Plans in config

```json
"Registry": {
  "Plans": [
    { "Id": "free", "QuotaBytes": 1073741824 },
    { "Id": "pro-100gb", "QuotaBytes": 107374182400, "PublicWebPresence": true },
    { "Id": "unlimited" }
  ]
}
```

- `QuotaBytes` missing or null means unlimited.
- `PublicWebPresence` missing or null leaves it to the identity (section 7).
- No `Plans` section at all means quotas are off, so self-hosted servers and existing hosts behave as today.
- If `Plans` is set it must contain `free`, and ids must be unique; otherwise the host fails at startup, like
  the overlapping invitation-code check in `RegistrySection`.
- Lookup is by exact id, case-insensitive. An id not in the list, or a null plan, gets `free`'s quota.

### 2. One resolver

`HostingPlans` reads the config once and is the one place a plan id becomes what it gives:
`QuotaFor(string? planId) => long?` (null for unlimited) and `PublicWebPresenceFor(planId, identitySetting)`.
The guard, the V2 endpoint, the public pages and the admin metrics all call it, so they never disagree.

### 3. Enforcement in the guard

`AssertCanAddPayloadBytes` becomes `AssertCanAddPayloadBytesAsync` and refuses when either:

- the status is `OutOfQuota` (still settable by hand, for an admin who wants to stop an identity regardless of
  plan), or
- the identity's quota is not unlimited and its cached usage is at or above it.

Same 507, same Retry-After, same message to peers, so nothing downstream changes. Because the guard is only
called where payload or thumbnail bytes are added, headers, metadata, reactions, read receipts and deletes keep
working, and deleting files brings an identity back under its quota within a minute.

It is a soft limit: an upload's size is not known until it has been streamed, so the check is "is this identity
already full", not "would this fit". That avoids buffering or pre-declared sizes. The usage it reads is the
same sum, cached under its own key for up to a minute rather than dropped on every write, so a busy identity
does not pay for a full sum on each upload: an upload or two can go over, and freed space can take a minute to
count.

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

### 6. Changing a plan

`odin-cli tenant set-plan <tenant> <plan>`, or `PATCH /api/admin/v1/tenants/{domain}/plan` with
`{ "planId": "pro-100gb" }`, returns `{ "previousPlanId": ... }`.

- While quotas are on, the plan must be in `Registry:Plans` (400 otherwise), so a typo cannot quietly mean
  `free`'s quota. While they are off any id is accepted, so plans can be assigned before quotas are turned on.
- The registry saves and announces it like a status change. The guard reads the plan from the in-memory
  registration, so every node enforces the new quota once it has applied the announcement.

### 7. Public web presence

Whether an identity has a public home page and sitemap (`TenantContext.EnablePublicWebPresence`, checked by
`RequirePublicWebPresenceAttribute`, the sitemap and the SSR page service).

- A plan with `PublicWebPresence: true` or `false` decides it, over the identity's own setting.
- A plan without it, or with plans off, leaves it to the identity's own setting: on, unless it signed up with a
  configured code in `Registry:InvitationCodesWithoutPublicWebPresence` or an admin changed it. A code from the
  invitation-code service no longer sets it (docs/invitation-codes.md), so every plan a service code names
  should say `PublicWebPresence`.
- It is resolved live, when the identity's `TenantContext` is built, so changing an identity's plan (section 6)
  changes it at once, on every node once the announcement is applied. The identity's own setting is kept, and
  applies again if it moves to a plan that does not say.
- Every existing identity is on `free` with its own setting. Leave `free` without `PublicWebPresence` to keep
  them as they are; `false` there takes every such identity's public page down.

## Out of scope

- Warning the owner as they near their quota.
- Email storage, which Stalwart limits on its own.
- Hard limits that refuse an upload that would cross the quota.

## Tests

- Config: no section means off; `free` required; duplicate ids refused; unknown and null plans resolve to
  `free`; null `QuotaBytes` is unlimited.
- Public web presence: a plan's `true` or `false` wins over the identity's setting; a plan without it, an
  unknown plan whose `free` has none, and plans off all keep the identity's setting.
- Guard: refuses payloads and thumbnails at or over quota, allows them under it, allows headers either way,
  still honours a hand-set `OutOfQuota`, never refuses with quotas off or an unlimited plan.
- Integration (V2): an identity over a small quota can upload a header but gets 507 for a payload; after a
  delete it can upload again; the usage endpoint reports the same numbers for the owner and an app.
- Admin metrics include `quotaBytes`.
- Set plan: a configured plan brings its quota (an over-quota identity can upload payloads again on an
  unlimited plan), an unconfigured plan is a 400, an unknown tenant a 404.
