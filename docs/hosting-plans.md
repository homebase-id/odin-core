# Hosting plans: a disk quota and a public web presence per plan

Status: built on branch `plan-quota` (#1974).

## Before plans

- An identity's plan is `PlanId` on its registration. Since #1908 it comes only from the invitation code it
  signed up with; without one it has none (the registry wrote `free` for that until this change).
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
| A plan that is not in the config, or none | Gets the `Default` plan's quota and web presence |
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
    { "Id": "legacy", "Default": true },
    { "Id": "free-1gb", "QuotaBytes": 1073741824, "PublicWebPresence": false },
    { "Id": "pro-100gb", "QuotaBytes": 107374182400, "PublicWebPresence": true }
  ]
}
```

- `QuotaBytes` missing or null means unlimited.
- `PublicWebPresence` missing or null leaves it to the identity (section 7).
- No `Plans` section at all means quotas are off, so self-hosted servers and existing hosts behave as today.
- If `Plans` is set exactly one plan is `Default: true`, and ids must be unique; otherwise the host fails at
  startup, like the overlapping invitation-code check in `RegistrySection`.
- The default plan is every identity's whose plan is not in the list: no plan, an unknown id, or `free`, which
  the registry stored for no plan before this change. So every identity that existed before plans is on the
  default plan until an admin gives it another, with no migration. Do not name a plan `free` unless it is meant
  for all of them.
- Lookup is by exact id, case-insensitive. An id not in the list, or a null plan, gets the default plan.

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

`GET /api/v2/plan`, policy `OwnerOrApp`, no permission key: the identity's plan, what it gives, and how much
of it is used.

```json
{
  "id": "free-1gb",
  "isDefault": false,
  "publicWebPresence": false,
  "storage": { "usedBytes": 734003200, "quotaBytes": 1073741824, "isOverQuota": false },
  "upgradeUrl": "https://billing.example.com/upgrade?id=frodo.dotyou.cloud"
}
```

- `id` is the plan the identity gets, as configured: the default's for no plan or an unknown one, so an app
  never needs the fallback rule. Null, with `isDefault` false, while plans are off.
- `publicWebPresence` is the identity's effective setting: the plan's when it says, otherwise the identity's own.
- `storage.quotaBytes` is null for unlimited. `storage.isOverQuota` is exactly what the guard would decide,
  including a hand-set `OutOfQuota`, so an app can explain a 507 before the user hits one.
- `upgradeUrl` is the hosting provider's page for a bigger plan (section 8), or null.
- What a plan gives will grow; each new thing is one more field here.

### 5. Admin metrics

`/api/admin/v1/tenants/metrics` gains `quotaBytes` per registered identity, from the same resolver.

### 6. Changing a plan

`odin-cli tenant set-plan <tenant> <plan>`, or `PATCH /api/admin/v1/tenants/{domain}/plan` with
`{ "planId": "pro-100gb" }`, returns `{ "previousPlanId": ... }`.

- While quotas are on, the plan must be in `Registry:Plans` (400 otherwise), so a typo cannot quietly mean
  the default plan. While they are off any id is accepted, so plans can be assigned before quotas are turned on.
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
- Every identity from before plans is on the default plan with its own setting. Leave the default plan without
  `PublicWebPresence` to keep them as they are; `false` there takes every such identity's public page down.

### 8. Getting a bigger plan

`Registry:UpgradeUrl` (optional) is the hosting provider's page for a bigger plan, e.g.
`https://billing.example.com/upgrade`. It must be an absolute `https` address without a `#fragment`, or the
host refuses to start.

- `/api/v2/plan` returns it as `upgradeUrl`, with `?id=<identity>` added (or `&id=` when it has a query).
- The apps link to `/owner/settings/plan?upgrade=1` for "how to upgrade". With an upgrade URL, the owner console
  sends the owner on to it; without one (self-hosted, or a provider with no such page) it tells them to contact
  their hosting provider. The console only ever goes to this configured address, never to one from the link.
- The page logs the owner in with YouAuth as a domain client (docs/youauth-unified-authorization.md). Nothing
  is installed on the identity. The owner is already signed in to their console, so there is no password;
  there is a consent screen the first time, unless they approved the domain before or the identity is
  connected to it.
- `id` only says who to log in. Anyone can put any id into a link, so the page must act on the identity YouAuth
  returns, never on `id`.
- The page changes the plan with the admin API (section 6) once the owner has paid. That needs the host's admin
  key, which can do far more than change a plan.

## Out of scope

- Warning the owner as they near their quota.
- Email storage, which Stalwart limits on its own.
- Hard limits that refuse an upload that would cross the quota.

## Tests

- Config: no section means off; exactly one `Default` required; duplicate ids refused; unknown, null and
  `free` plans resolve to the default; null `QuotaBytes` is unlimited.
- Public web presence: a plan's `true` or `false` wins over the identity's setting; a plan without it, an
  unknown plan whose default has none, and plans off all keep the identity's setting.
- Guard: refuses payloads and thumbnails at or over quota, allows them under it, allows headers either way,
  still honours a hand-set `OutOfQuota`, never refuses with quotas off or an unlimited plan.
- Integration (V2): an identity over a small quota can upload a header but gets 507 for a payload; after a
  delete it can upload again; the plan endpoint reports the same plan and numbers for the owner and an app.
- Admin metrics include `quotaBytes`.
- Set plan: a configured plan brings its quota (an over-quota identity can upload payloads again on an
  unlimited plan), an unconfigured plan is a 400, an unknown tenant a 404.
