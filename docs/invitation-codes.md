# Invitation codes (#1908)

An invitation code is owned by an identity and can be used a set number of times to sign up. The codes and
their use counts live in one place for every cluster: the invitation-code service on SetupHelper
(`setup.homebase.id`), in SQLite. Who signed up with a code is kept on the new identity's own registration
(`IdentityRegistration.InvitationCode`, in the registrations json column), not in the service.

The host-wide codes in `Registry:InvitationCodes` keep working as before; the two never overlap.

## Flow

- **Signup** (`IdentityRegistrationService`):
  - `IsValidInvitationCode` accepts a configured code, or asks the service.
  - `CreateIdentityOnDomainAsync` takes a use from the service before registering, and gives it back if
    registering fails. It then creates the new identity's own code (`DefaultUses` uses), best-effort.
- **Owner** (`GET /api/v2/invitation-codes`, owner console or any of the owner's apps): the tenant's own
  codes, with uses left and the signup link to share. An identity with none gets one on first ask.
- **Admin** (`odin-admin invitation-code create <owner> --uses N -S <service url> -K <create key>`).

## SetupHelper configuration

| Key | Value |
|---|---|
| `InvitationCodes__DatabasePath` | e.g. `/data/invitation-codes.db` — on a persistent volume |
| `InvitationCodes__SignupApiKey` | the signup key |
| `InvitationCodes__CreateApiKey` | the create key |

The service is off unless all three are set. SQLite means **one SetupHelper instance**, and the database
file should be backed up.

## Identity host configuration (Homebase-run hosts only)

| Key | Value |
|---|---|
| `Registry__InvitationCodeService__Url` | `https://setup.homebase.id` |
| `Registry__InvitationCodeService__SignupApiKey` | the signup key |
| `Registry__InvitationCodeService__CreateApiKey` | the create key |
| `Registry__InvitationCodeService__DefaultUses` | uses for each new identity's code (default 5) |

Off when `Url` is empty. **Never give self-hosted servers these keys**: the signup key lists any
identity's codes.

Keys are 32 random bytes, base64url: `openssl rand -base64 32 | tr '+/' '-_' | tr -d '='`. They travel in
the `Odin-Api-Key` header.

## API (SetupHelper, `/api/v1/invitation-codes`)

| Call | Key | Answer |
|---|---|---|
| `POST /validate {code}` | signup | `{valid}` — exists and has a use left |
| `POST /use {code}` | signup | `{used}` — takes a use, atomically |
| `POST /undo-use {code}` | signup | 204 — gives the latest use back |
| `GET /?owner=<odinId>` | signup | `[{code, owner, maxUses, usesLeft}]` |
| `POST / {owner, maxUses}` | create | `{code, owner, maxUses, usesLeft}` |

Codes are 12 Crockford base32 characters shown as `K7QX-M2PD-9FHW`; input is case-insensitive and ignores
dashes.

## Known gaps

- No rate limiting on validation.
- A host that crashes between taking a use and registering leaks that use.
- Codes stay valid after their owner is deleted.
- The CLI only creates codes; listing, changing and disabling are not built.
