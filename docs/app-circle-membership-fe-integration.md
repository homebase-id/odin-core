# FE integration: app-driven circle membership

Spec for a client (chat app or owner-console) integrating with the
app-driven circle-membership feature described in
[`app-circle-membership-status.md`](./app-circle-membership-status.md). Everything
below is via the V2 unified API, base path `/api/v2/connections`, which accepts
both the app's own auth token and the owner token — same endpoints, same shapes,
no branching needed in client code.

## Prerequisite

The calling app must have been registered with the `ManageCircleMembership`
permission key (51). If it wasn't, every mutating call below returns
`403 Forbidden`. This is an install-time/owner-consent concern, not something
client code decides — just handle the 403 if it ever comes back (see error table).

## 1. Add a contact to a circle

```
POST /api/v2/connections/circles/add
{ "circleId": "<guid>", "odinId": "<contact's identity>" }
```

- `200 OK` — the grant was accepted. **This does not mean the contact is a member
  yet** — see "Pending vs. real" below.
- `403 Forbidden` — the call itself was invalid. Most causes (missing
  `ManageCircleMembership`, contact not connected at all) are bugs in how the
  client is calling the API — **not user-facing, don't build recovery UI for
  them.** **One 403 cause is different and *is* worth checking:** the circle
  includes a drive the app doesn't itself have access to. This one carries a
  specific `errorCode` (`CannotSourceDriveStorageKeyForGrant`, 4173) in the
  `problem+json` body — **403s are not always codeless**, check `errorCode` on
  every 403 the same as you would on a 400 before writing it off as a bug (see
  error table).
- `400 Bad Request` — a real, user-facing outcome:
  - the contact is connected but only via auto-connect and hasn't been confirmed
    by the owner yet (check `vetted` before attempting, see below)
  - the contact is already a member of this circle (or already has a pending
    deposit for it)
  - (rarer) the connection predates this feature and hasn't been backfilled yet —
    surfaces as a generic 400 with no dedicated code; treat as "try again shortly"

## 2. Check where a contact stands

```
GET /api/v2/connections/status?odinId=<contact>
```

Returns a `RedactedIdentityConnectionRegistration`:

```jsonc
{
  "odinId": "sam.example.com",
  "status": "Connected",       // "None" | "Connected" | "Blocked"
  "vetted": true,               // false = auto-connected but not yet confirmed by
                                 // the owner — circles/add will 400 against this contact
  "accessGrant": {
    "circleGrants": [ { "circleId": "...", ... } ],   // REAL memberships, usable now
    "pendingCircleIds": [ "..." ],                     // deposited, NOT usable yet
    "appGrants": { ... }
  }
}
```

**Pending vs. real, and why it matters for the UI:** a circle-add doesn't take
effect the instant it's called. It's deposited, sealed, and converts into a real
grant the next time the owner touches that connection's grants or the contact's
own server talks to this one for any reason — could be seconds, could be longer.
Until then, the circle shows up in `pendingCircleIds`, not `circleGrants`. **Show
these as two different states** ("Pending" vs. "Member") — don't collapse them,
and don't assume a successful `circles/add` call means the contact can use
anything yet.

Practical polling guidance: after a successful add, no need to poll aggressively —
if the contact's app is active at all (sending messages, etc.), their side
naturally triggers conversion the next time they interact with this identity.
Re-check `status` next time the UI is naturally re-rendered (e.g. on next page
load / next time the user opens that contact's profile) rather than setting up a
tight poll loop.

## 3. List everyone in a circle

```
GET /api/v2/connections/circles?circleId=<guid>
→ ["sam.example.com", "frodo.example.com", ...]
```

Only **real** members — pending deposits are intentionally not included here
(there's no per-circle "who's pending" list; check per-contact via `status` above).

## 4. List all circles with their members at once (owner/app only, not guest)

```
GET /api/v2/connections/circles/with-members?includeSystemCircle=true
→ [ { "circle": { "id": "...", "name": "...", "permissions": {...} }, "members": ["..."] } ]
```

Useful for a circle-management screen. Same caveat: `members` is real members only.

## 5. Revoke a contact from a circle

```
POST /api/v2/connections/circles/revoke
{ "circleId": "<guid>", "odinId": "<contact>" }
```

`200 OK` on success. This also silently drops any of the calling app's still-pending
deposits for that circle/contact pair — no separate cleanup call needed.

## Error-handling checklist

Every error response is a `problem+json` body with `status` and, where the server
provides one, an `errorCode` integer extension field.

| HTTP | `errorCode` | Meaning | What to do |
|---|---|---|---|
| 403 | **4173** (`CannotSourceDriveStorageKeyForGrant`) | the circle grants access to a drive the calling app cannot itself read — the app has no storage key to mint that portion of the grant | **user-facing**: tell the user this app can't add this circle because it includes a drive outside the app's own access (e.g. "the Chat app can't grant the Location circle — it includes a drive Chat can't read") |
| 403 | *(0 / absent — no code)* | anything else: missing `ManageCircleMembership`, contact not connected at all, etc. | **not a user-facing state** — log it as a bug, don't build recovery UI. These remain indistinguishable from each other, and that's fine — a well-behaved client should never hit them. |
| 400 | **3010** (`CannotGrantAutoConnectedMoreCircles`) | contact is auto-connected but not yet confirmed by the owner | tell the user to confirm the connection in the owner console first — the client can't do this for them |
| 400 | **3005** (`IdentityAlreadyMemberOfCircle`) | contact is already a member, or already has a pending deposit, for this circle | no-op from the user's perspective — refresh `status` and show current state |
| 400 | *(0 / `UnhandledScenario`)* | connection predates this feature, not yet backfilled | rare, self-resolving; show a generic "try again" rather than a specific message |

**Always check `errorCode` before branching on HTTP status alone** — as of this
code, a 403 can carry a meaningful `errorCode` just like a 400 does. Don't
special-case the *other* 403s further — the server doesn't emit distinct codes
for those, and they're not meant to be reachable in normal use.

## What NOT to build

Don't build a "waiting for confirmation" push/poll mechanism expecting near-instant
conversion — there isn't one, and there's no reliable way to force it faster (it
depends on the owner or the contact's server acting, neither of which the client
controls). Show "Pending" honestly and let it resolve on its own.
