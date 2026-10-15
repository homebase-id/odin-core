# Invitation codes (#1908)

An invitation code is owned by an identity and can be used a set number of times to sign up. The codes and
their use counts live on the identity host, in a SQLite database of their own
(`{Host:SystemDataRootPath}/invitation-codes.db`), apart from the system database. Who signed up with a code
is kept on the new identity's own registration (`IdentityRegistration.InvitationCode`, in the registrations
json column), not with the codes.

**Prototype:** each host has its own codes, so a code created on one cluster does not work on another. Good
for bleeding edge; production with more than one provisioning cluster needs the codes in one shared place.

The host-wide codes in `Registry:InvitationCodes` keep working as before; the two never overlap.

## Flow

- **Signup** (`IdentityRegistrationService`):
  - `IsValidInvitationCode` accepts a configured code, or one in the store with a use left.
  - `CreateIdentityOnDomainAsync` takes a use before registering, and gives it back if registering fails.
    It then creates the new identity's own code (`DefaultUses` uses), best-effort.
- **Owner** (`GET /api/v2/invitation-codes`, owner console or any of the owner's apps): the tenant's own
  codes, with uses left and the signup link to share. An identity with none gets one on first ask; an empty
  list when the host has invitation codes off.
- **Admin** (admin API, `Odin-Admin-Api-Key`):
  - `POST /api/admin/v1/invitation-codes {owner, maxUses}` — `odin-admin invitation-code create <owner> --uses N`
  - `GET /api/admin/v1/invitation-codes?owner=<odinId>` — `odin-admin invitation-code list <owner>`

## Identity host configuration

| Key | Value |
|---|---|
| `Registry__InvitationCodeStore__Enabled` | `true` to turn invitation codes on; off by default. When on, signup needs a code |
| `Registry__InvitationCodeStore__DefaultUses` | uses for each new identity's code (default 5) |

The database file is created on first use in `Host:SystemDataRootPath`, so it needs no volume of its own; back it up with the rest of that directory.

Codes are 12 Crockford base32 characters shown as `K7QX-M2PD-9FHW`; input is case-insensitive and ignores
dashes and spaces.

## Known gaps

- No rate limiting on validation.
- A host that crashes between taking a use and registering leaks that use.
- Codes stay valid after their owner is deleted.
- The CLI cannot change or disable a code.
