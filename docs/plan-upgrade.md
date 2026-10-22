# Plans on the identity, a hosting provider, and upgrading

Status: odin-core part built on #1994 (`upgrade-url`); homebase-invites, DevOps, odin-js and chat-kmp to follow. Decisions are Todd's, 2026-10-22.

## Goal

An owner who wants more space taps "How to upgrade", is told they are going to their hosting provider, picks a
bigger plan there and has it straight away. The plan catalog (what each plan gives and what it costs) belongs to
the hosting provider, not to odin-core.

## Decisions

- odin-core has no plan catalog any more: `Registry:Plans` is removed. Each identity's registration stores its
  own `PlanId` (a label), `QuotaBytes` and `EnablePublicWebPresence`.
- A new `HostingProvider` config section: `DisplayName`, `UpgradeUrl`, `AutoConsentDomains`, `DefaultQuotaBytes`.
- An identity with no quota stored gets `HostingProvider:DefaultQuotaBytes`, which defaults to 60 GB
  (64424509440 bytes). A self-hosted server can change it.
- There is no unlimited: every identity has a number of bytes. For "lots", give a big quota (1 TB).
- The catalog lives in homebase-invites (the hosting provider's account service). It sends the plan's quota and
  public web presence with the plan, whenever it gives one: a code at signup, a grant, a purchase.
- Apps never build the upgrade address: `GET /api/v2/plan` returns it ready to open, with the hosting
  provider's name.
- The hosting provider's own YouAuth domains (`AutoConsentDomains`) sign an owner in without the consent screen.
- chat-kmp: "How to upgrade" opens a dialog naming the hosting provider, then goes straight to its upgrade page.
  With no hosting provider configured, it says to contact whoever runs the server. The Account category moves
  to right below General.

## odin-core

### Config: `HostingProvider`

| Key | | Default |
|---|---|---|
| `HostingProvider:DisplayName` | shown to owners ("We'll send you to Raven Hosting") | `""`: no hosting provider |
| `HostingProvider:UpgradeUrl` | the provider's upgrade page; absolute `https`, no `#fragment`, or the host refuses to start. Replaces `Registry:UpgradeUrl` | `""` |
| `HostingProvider:AutoConsentDomains` | domains whose YouAuth domain sign-in skips the consent screen; exact names, no wildcards | `[]` |
| `HostingProvider:DefaultQuotaBytes` | the quota of an identity with none stored | 64424509440 (60 GB) |

`Registry:AccountService:ApiKey` (the account service API key, #1994) stays where it is.

### The registration carries the plan

- `IdentityRegistration` gains `QuotaBytes` (`long?`); `PlanId` and `EnablePublicWebPresence` are already there.
  A registration file without `QuotaBytes` reads as null: the identity gets `DefaultQuotaBytes`.
- The quota guard, the public web presence (`TenantContext`), `GET /api/v2/plan` and the admin metrics read
  them from the registration. There is no default plan and no "is this plan configured" check: a plan Id is
  whatever the setter says.
- `HostingPlans` and `Registry:Plans` (and its validation) are removed.

### Setting a plan

One registry method sets them together (saved and announced, like today's `SetPlanAsync`). `planId` is
required; `quotaBytes` and `publicWebPresence` left out keep the identity's current values.

- Account service API: `PUT /api/plans/v1/identities/{domain}/plan`
  `{ planId, quotaBytes, publicWebPresence, reference }`. `GET` returns the plan Id, the quota in effect and the
  public web presence.
- Admin: `PATCH /api/admin/v1/tenants/{domain}/plan` with the same body, and
  `odin-admin tenant set-plan <tenant> <planId> [--quota-bytes N] [--public-web-presence true|false]`.

### Signup with an invitation code

homebase-invites' `use` answer carries the code's `planId`, `quotaBytes` and `publicWebPresence`; the host
stores them on the new registration. A code without them (an older service) stores the plan Id alone, so the
identity gets the default quota and the public web presence it would have had.

### `GET /api/v2/plan`

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

`hostingProvider` is null when `DisplayName` is empty; `upgradeUrl` is null when it is empty. `isDefault`
goes (there is no default plan).

### YouAuth auto-consent

`YouAuthUnifiedService.NeedConsent`: a `domain` client whose domain is in `AutoConsentDomains` needs no
consent. Apps are never auto-consented. The owner still has to be signed in to their identity in that browser.

### Existing identities

They have no `QuotaBytes` stored, so they get `DefaultQuotaBytes` (60 GB, the size `free` has today) and keep
their own public web presence. Nothing changes for them.

## homebase-invites

- The plan catalog: every plan's `Id`, `Name`, `Description`, `QuotaBytes`, `PublicWebPresence`, and whether
  it is for sale.
- `use` returns the code's plan values; grants and checkout send all three to the host.

## DevOps

- Remove `hosting-plans.yml` and its `Registry:Plans` wiring (Hetzner `json_patch`, OVH `managed.env`).
- Add `HostingProvider`: `DisplayName`, `UpgradeUrl` (`https://invites.ravenhosting.cloud/upgrade`),
  `AutoConsentDomains` (`invites.ravenhosting.cloud`), `DefaultQuotaBytes`.
- The plan catalog goes into homebase-invites' config.

## odin-js

Plan & storage reads `hostingProvider` from `/api/v2/plan`: the "See plans and upgrade" button opens its
`upgradeUrl`, and names the provider; with none, the "contact your hosting provider" text.

## chat-kmp

- "How to upgrade" opens a dialog: "We'll send you to <displayName> to upgrade your plan", Continue (opens
  `upgradeUrl` in the browser) and Cancel. No hosting provider: "Contact whoever runs your server."
- The Account category sits right below General.

## Order

odin-core (this branch), then homebase-invites, then DevOps, then odin-js and chat-kmp.
