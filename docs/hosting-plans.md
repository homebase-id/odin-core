# Hosting plans: a quota and a public web presence on each identity

Status: built on branch `upgrade-url` (#1994), replacing the `Registry:Plans` catalog of #1974. The plan behind it
is [plan-upgrade.md](plan-upgrade.md).

## The model

| | |
|---|---|
| Where plans are defined | By the hosting provider, outside odin-core (raven-hosting holds the catalog) |
| What an identity has | Its own `PlanId` (a label), `QuotaBytes` and `EnablePublicWebPresence`, on its registration |
| An identity with no quota stored | `HostingProvider:DefaultQuotaBytes`, 60 GB unless configured |
| Unlimited | There is none: every identity has a number of bytes |
| What counts as usage | All indexed bytes, tombstones included (`totalBytes`) |
| Enforcement | A live check in the existing guard, no automatic status changes |
| Out of quota | File headers still upload and update; payloads and thumbnails do not |
| Owner and apps | Read their plan, usage and hosting provider on the V2 API |

odin-core has no plan catalog: it does not know what a plan Id means. Whoever gives an identity a plan gives
what it comes with at the same time: the invitation code at signup, the account service after a purchase, or an
admin.

## 1. Config: `HostingProvider`

Who runs this host for its owners.

| Key | | Default |
|---|---|---|
| `HostingProvider:DisplayName` | shown to owners ("We'll send you to Raven Hosting to upgrade your plan") | `""`: no hosting provider |
| `HostingProvider:UpgradeUrl` | the provider's page for a bigger plan; absolute `https` without a `#fragment`, or the host refuses to start | `""`: none |
| `HostingProvider:AutoConsentDomains` | domains whose YouAuth domain sign-in skips the consent screen (section 7); exact names, no wildcards | `[]` |
| `HostingProvider:DefaultQuotaBytes` | the quota of an identity with none stored; more than 0 | `64424509440` (60 GB) |

A self-hosted server can leave all of it out: no hosting provider, and 60 GB per identity, or what it sets.

## 2. The registration carries the plan

`IdentityRegistration` has `PlanId`, `QuotaBytes` (`long?`) and `EnablePublicWebPresence`. `PlanId` and
`EnablePublicWebPresence` are columns of the registrations table; `QuotaBytes` is in its `json` column (no schema
change), so a row written before it reads as null and the identity gets the default quota.

- **Existing identities** have no quota stored: they get `DefaultQuotaBytes` (60 GB, what `free` gave before)
  and keep their own public web presence. Nothing changes for them.
- **The quota in effect** is `QuotaBytes ?? HostingProvider:DefaultQuotaBytes`, read by the quota guard, the plan
  endpoint, the account service API and the admin metrics.
- **Public web presence** is the registration's own `EnablePublicWebPresence` (`TenantContext`), set with the plan.
  Changing it re-creates the identity's tenant context, so the public page follows at once, on every node once the
  change is announced.

## 3. Enforcement in the guard

`TenantQuotaGuard.AssertCanAddPayloadBytesAsync` refuses when either:

- the status is `OutOfQuota` (settable by hand, for an admin who wants to stop an identity regardless of quota),
  or
- the identity's cached usage is at or above its quota in effect.

It refuses with 507 and a one-hour Retry-After; peers keep the item in their outbox and retry (a recipient out of
storage is recorded as `RecipientIdentityOutOfStorage`, waiting, not failed). It is called only where payload or
thumbnail bytes are added (the upload, update and attachment writers, the peer equivalents, the profile photo,
contact images and the contacts' app blob), so headers, metadata, reactions, read receipts and deletes keep
working, and deleting files brings an identity back under its quota within a minute.

It is a soft limit: an upload's size is not known until it has been streamed, so the check is "is this identity
already full", not "would this fit". The usage it reads is `SUM(byteCount)` over the identity's index rows,
cached for up to a minute: an upload or two can go over, and freed space can take a minute to count.

## 4. `GET /api/v2/plan`, for the owner and apps

Policy `OwnerOrApp`, no permission key: the identity's plan, what it gives, how much of it is used, and who to
ask for more.

```json
{
  "id": "veterans",
  "publicWebPresence": true,
  "storage": { "usedBytes": 734003200, "quotaBytes": 1099511627776, "isOverQuota": false },
  "hostingProvider": {
    "displayName": "Raven Hosting",
    "upgradeUrl": "https://invites.ravenhosting.cloud/upgrade?id=frodo.dotyou.cloud&host=provisioning.dotyou.cloud"
  }
}
```

- `id` is the plan on the registration, null for none.
- `storage.quotaBytes` is the quota in effect; `storage.isOverQuota` is exactly what the guard would decide,
  including a hand-set `OutOfQuota`, so an app can explain a 507 before the user hits one.
- `hostingProvider` is null when `HostingProvider:DisplayName` is empty. Its `upgradeUrl` is null when
  `HostingProvider:UpgradeUrl` is empty; otherwise it is ready to open: the configured address with
  `?id=<identity>&host=<this host's provisioning domain>` added (`&` when it already has a query). Apps never
  build it.

## 5. Changing a plan

One registry change sets the plan Id and, when given, the quota and public web presence, saved and announced
together. `planId` is required (trimmed, not empty); `quotaBytes` (more than 0) and `publicWebPresence` left out
keep the identity's current values. Any plan Id is accepted: it is the provider's label.

- **Admin:** `PATCH /api/admin/v1/tenants/{domain}/plan` `{ "planId", "quotaBytes", "publicWebPresence" }`,
  returns `{ "previousPlanId", "previousQuotaBytes", "previousPublicWebPresence" }` (`previousQuotaBytes` null for
  the default). 400 for an empty plan Id or a quota that is not more than 0, 404 for an unknown tenant.
- **CLI:** `odin-admin tenant set-plan <tenant> <planId> [--quota-bytes N] [--public-web-presence true|false]`.
- **The account service:** section 6.

## 6. The account service API

The hosting provider's account service (raven-hosting, which already gives plans out through invitation codes)
manages identities on its hosts with one key, `Registry:AccountService:ApiKey`, on each host's provisioning
domain:

| Call | Answer |
|---|---|
| `GET /api/plans/v1/identities/{domain}` | `{ "domain", "planId", "quotaBytes", "publicWebPresence" }`: the plan Id, the quota in effect and the public web presence; 404 if the identity is not on this host |
| `PUT /api/plans/v1/identities/{domain}/plan` `{ "planId", "quotaBytes", "publicWebPresence", "reference" }` | the same, after setting it as in section 5; 400 for an empty plan Id or a quota that is not more than 0, 404 if the identity is not on this host |

- The key is sent in the `Odin-Api-Key` header; a wrong or missing key is a 401. One key shared by every host, for
  now.
- Without a key the API is not there (404), and it is only on the provisioning domain, whether or not this host
  provisions (like payload move). On an identity's own domain it is a 404.
- It can only read and set plans, through the same code as `odin-admin tenant set-plan`; the admin key, which can
  delete or move identities, never leaves the host.
- Setting what an identity already has changes nothing, so the service retries until it hears back. `reference`
  (the service's payment or grant id) is logged with the change.
- The 404 for an identity not on this host is how the service finds where an identity lives: it tries the `host`
  from the upgrade link first, if it is a host it knows, and otherwise asks each host it knows.

## 7. Signing in to the hosting provider

The upgrade page signs the owner in with YouAuth as a domain client (docs/youauth-unified-authorization.md).
Nothing is installed on the identity. A domain in `HostingProvider:AutoConsentDomains` needs no consent: the
provider already runs the host, so knowing who the owner is gives it nothing new. Every other domain asks as
before, and apps are never auto-consented. The owner still has to be signed in to their identity in that browser.

`id` and `host` in the upgrade link are hints anyone can edit. The page must act on the identity YouAuth returns,
never on `id`, and use `host` only to pick one of the hosts it already knows (with the account service key), which
then confirms the identity is its own. An unknown `host` is ignored, so an edited link cannot send a key anywhere.

## 8. Signup with an invitation code

raven-hosting's `use` answer carries the code's `planId` and what the plan gives, `quotaBytes` and
`publicWebPresence`; the host stores all three on the new registration. A service that sends no `quotaBytes` leaves
the default quota, and no `publicWebPresence` leaves what signup gives without a plan (on, unless a configured code
says otherwise). The code's older `enablePublicWebPresence` is ignored.

## 9. Admin metrics

`/api/admin/v1/tenants/metrics` reports each registered identity's `planId` and `quotaBytes` (the quota in effect)
next to `totalBytes`.

## Out of scope

- Warning the owner as they near their quota.
- Email storage, which Stalwart limits on its own.
- Hard limits that refuse an upload that would cross the quota.

## Tests

- Config (`HostingProviderSectionTests`): defaults (no provider, 60 GB); a stored quota wins over the default; the
  default can be set but not to 0; the upgrade URL gets `id` and `host`, must be https without a fragment;
  auto-consent domains are lower-cased and must be domains.
- Registration json (`RegistrationJsonMapperTests`): the quota round-trips and survives a status change; a row from
  before quotas has none.
- Signup (`IdentityRegistrationServiceTest`): the code's plan, quota and public web presence are stored; without
  them, the defaults.
- Guard and endpoint (`PlanQuotaTests`): over a small default quota a header uploads, a payload gets 507, and the
  plan endpoint reports the same plan, numbers and hosting provider to the owner and an app; with no provider,
  60 GB and no `hostingProvider`.
- Set plan (`SetTenantPlanTests`): a bigger quota lets a payload in; a plan without a public web presence takes
  the page down and back; values left out stay; a quota of 0 or less and an empty plan are 400; an unknown tenant
  is 404.
- Every path that adds payload bytes (`PlanQuotaPathTests`), including peers waiting in the outbox, refuses once
  the identity is over its quota and lets the bytes in once set-plan gives it room.
- The account service API (`PlanServiceApiTests`) and auto-consent (`YouAuthAutoConsentTests`).
