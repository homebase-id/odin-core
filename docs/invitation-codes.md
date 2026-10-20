# Invitation codes (#1908)

Invitation codes live in their own service, [homebase-id/homebase-invites](https://github.com/homebase-id/homebase-invites):
it issues the codes and counts their uses, and its README has the API. odin-core only asks it about a code
at signup. Who signed up with a code is kept on the new identity's registration, not in the service.

The host-wide codes in `Registry:InvitationCodes` keep working as before; the two never overlap.

## In odin-core

`InvitationCodeServiceClient` is the one plug point, used by `IdentityRegistrationService`:

- `IsValidInvitationCode` accepts a configured code, or asks the service (`validate`). Input that cannot be a
  code (not 12 Crockford base32 characters once dashes and spaces are dropped) is refused without a call.
- `create-identity-on-domain` checks only a service code's shape up front (`IsInvitationCodeWorthUsing`):
  using it is the real check, so it is not validated first as well.
- `CreateIdentityOnDomainAsync` takes a use (`use`) before registering, and gives it back (`undo-use`) if
  registering fails. The registration stores `Invitation` (code, the code's owner as `InvitedBy`, source)
  in its json column. A code's `enablePublicWebPresence`, which older services still send, is ignored: the
  identity's plan decides its public home page (docs/hosting-plans.md), and without a say from the plan the
  identity has one.
- An identity's hosting plan comes only from its invitation code (`planId`), never from the signup request:
  `create-identity-on-domain` no longer takes a `planId`, and one the client still sends is ignored. A code
  without a plan, a configured code, a service from before plans, or a host with no codes leaves it unset, which
  is the default plan. What a plan gives is odin-core's (docs/hosting-plans.md).

## Configuration (Homebase-run identity hosts only)

| Key | Value |
|---|---|
| `Registry__InvitationCodeService__Url` | the service's base URL; off when empty. Must be `https`, or `http` on loopback |
| `Registry__InvitationCodeService__ApiKey` | the service's signup key. **Never give it to self-hosted servers.** |

A URL without a key, or a plain-http URL that is not loopback, leaves invitation codes off and logs an error the
first time signup needs the client, rather than failing to start or sending the key in clear.
