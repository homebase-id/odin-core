# Invitation codes (#1908)

An invitation code is owned by an identity and can be used a set number of times to sign up. The codes and
their use counts live on the identity host, in a SQLite database of their own
(`{Host:SystemDataRootPath}/invitation-codes.db`), apart from the system database. Who signed up with a code
is kept with the new identity, not with the codes: signup puts it on the registration
(`IdentityRegistration.Onboarding`, in the registrations json column) and first run copies it into the
identity's own database. The owner console and the Chat app read it at `GET /api/v2/onboarding`; Chat marks
it handled once (`POST /api/v2/onboarding/handled`) after offering to connect with the inviter.

**Prototype:** each host has its own codes, so a code created on one cluster does not work on another. Good
for bleeding edge; production with more than one provisioning cluster needs the codes in one shared place.
Invitation codes are on by default only on bleeding edge (`provisioning.demo.dominion.id`), and creating
and listing codes uses a key hard-coded in `InvitationCodePrototype`, which odin-admin and chat-kmp carry
too. Replace both before this goes anywhere else.

The host-wide codes in `Registry:InvitationCodes` keep working as before; the two never overlap.

## Flow

- **Signup** (`IdentityRegistrationService`):
  - `IsValidInvitationCode` accepts a configured code, or one in the store with a use left.
  - `CreateIdentityOnDomainAsync` takes a use before registering, and gives it back if registering fails.
    It then creates the new identity's own code (`DefaultUses` uses), best-effort.
- **Owner** (`GET /api/v2/invitation-codes`, owner console or any of the owner's apps): the tenant's own
  codes, with uses left and the signup link to share. An identity with none gets one on first ask; an empty
  list when the host has invitation codes off.
- **Create and list** (provisioning domain, `Odin-Invitation-Code-Key` header; odin-admin defaults to
  bleeding edge and the prototype key, `-P` and `-K` override them):
  - `POST /api/registration/v1/invitation-codes {owner, maxUses}` — `odin-admin invitation-code create <owner> --uses N`
  - `GET /api/registration/v1/invitation-codes?owner=<odinId>` — `odin-admin invitation-code list <owner>`

## Identity host configuration

| Key | Value |
|---|---|
| `Registry__InvitationCodeStore__Enabled` | `true` or `false`; by default on for bleeding edge's provisioning domain only. When on, signup needs a code |
| `Registry__InvitationCodeStore__DefaultUses` | uses for each new identity's code (default 5) |

The database file is created on first use in `Host:SystemDataRootPath`, so it needs no volume of its own; back it up with the rest of that directory.

Codes are 12 Crockford base32 characters shown as `K7QX-M2PD-9FHW`; input is case-insensitive and ignores
dashes and spaces.

## Known gaps

- No rate limiting on validation.
- A host that crashes between taking a use and registering leaks that use.
- Codes stay valid after their owner is deleted.
- The CLI cannot change or disable a code.
