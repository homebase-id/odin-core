# Prompt: client support for the V2 app-circle endpoints

Hand this to whoever adds client support (chat-kmp, odin-js, another app). Fill in `<REPO / MODULE>` and
`<SCREEN / FEATURE>` first. For chat-kmp, point it at `homebase-api/.../connections/ConnectionNetworkProvider.kt`
and `ProviderTypes.kt`, which already hold the `add`, `add-many` and `revoke` calls to copy from.

---

# Task: client support for the Homebase V2 app-circle endpoints

You are adding client support, in <REPO / MODULE>, for circle endpoints on the Homebase identity server
(odin-core) that let an **app** manage circles it owns. Calls are authenticated with the app's own client token,
exactly like the existing V2 `connections` calls. Base path: `/api/v2/connections`.

Before writing code, read how this client already calls `/api/v2/connections/circles/add`, `/circles/add-many`,
`/circles/revoke`, `/circles/disable` and `/circles/enable`. Reuse the same HTTP client, error handling and the
existing types for `DriveGrantRequest`, `PermissionedDrive`, `TargetDrive` and `PermissionSet`. Do not invent
new wire shapes. Drive permissions in particular must use whatever form this client already sends for circle
definitions.

## 1. Create a circle the app owns — NEW
`POST /api/v2/connections/circles/create`

Body (`CreateAppCircleRequest`):
```json
{
  "name": "string, required",
  "description": "string, optional",
  "emoji": "string, optional",
  "driveGrants": [ /* existing DriveGrantRequest shape */ ],
  "permissions": { "keys": [ /* int permission keys */ ] }
}
```
- Response: `200` with the new circle's id as a bare JSON GUID string. The **server chooses the id**; the request
  has no id field.
- The circle is owned by the calling app. Its enrolment tier (`grantOn`) is always `None`: members are added only
  explicitly, never automatically on connect.
- Rules enforced by the server. A violation returns `403` unless stated otherwise.
  - The app's own registration must **hold at least the permission it grants** on every drive in
    `driveGrants` (Write to grant Write, Read to grant Read). Each member the app adds receives the circle's
    access out of the app's own, so a circle the app couldn't fill is refused up front.
  - **On a drive the app owns:** any permission it holds.
  - **On a drive the app doesn't own:** **Read only**, and only if the app holds that drive's storage key.
    Example: Chat making a profile circle that grants Read on the Contacts app's ProfileDrive, the way
    Family, Friends and Work do. Write or React on another app's drive is refused.
  - `permissions.keys` may include **only keys the app itself holds**.
  - The circle must grant at least one drive or one key. Otherwise `400`,
    `atLeastOneDriveOrPermissionRequiredForCircle`.
  - A missing or empty name returns `400`.
  - The owner console's token is refused. The owner creates circles through the existing circle-definitions API.
- No permission key is needed to call this endpoint.

## 2. Delete a circle — NEW on V2
`POST /api/v2/connections/circles/delete?removeMembers=<true|false>`

- Body: the circle id as a bare JSON GUID string, the same as `circles/disable`.
- `removeMembers` is optional and defaults to `false`.
  - `false`: a circle that still has members is refused with `400`, `cannotDeleteCircleWithMembers`.
  - `true`: the server removes every member first, then deletes the circle.
- Response: `200` with an empty body.
- Rules:
  - An app may delete only circles it owns. Any other circle returns `403`. No permission key is needed.
  - Built-in platform circles (Friends, Chat, Feed and so on) can never be deleted, by the owner either. They
    return `400`, `cannotDeleteBuiltInCircle`.
  - An unknown circle returns `400`.
- The same operation exists on V1 (`…/circles/definitions/delete?removeMembers=true`). Use V2 here.

## 3. Members of the app's own circles — same endpoints, changed rules
`circles/add`, `circles/add-many` and `circles/revoke` keep their existing request and response shapes.
- **Owning the circle is enough.** An app adds and removes members of its own circles without
  `ManageCircleMembership`.
- Circles the app doesn't own need `ManageCircleMembership`. Once PR #1839 ships, new third-party registrations
  aren't offered it; Chat keeps it by default, because it adds people to Friends and Emergency Location Access.
- Membership changes apply on the member's very next call, not when their cached permissions expire.
- `circles/add-many` only offers circles whose tier is `Review` or `Connect`. App-created circles are tier `None`,
  so add members to them one at a time with `circles/add`.
- An add can be recorded as a full grant or as a pending deposit, depending on what the circle grants. The bulk
  result reports `enrolled` and `deposited` separately; treat both as success.
- Who can be added:
  - The person must be a **connected** identity.
  - Once PR #1839 ships, a `None` or `Review` circle also requires the contact to have been **reviewed** by the
    owner. Otherwise the add fails with `400`, `contactNotReviewed`. Show it as "the owner needs to review this
    contact first", and don't retry automatically.
  - Until then, an identity that is still only auto-connected is refused with
    `cannotGrantAutoConnectedMoreCircles`. Handle both codes.

## 4. Errors
Errors come back in the existing problem-details format, with the camelCase `errorCode` this client already parses.
Add handling or friendly messages for at least:
- `cannotDeleteCircleWithMembers`: offer to retry with `removeMembers=true`, after an explicit confirmation.
- `cannotDeleteBuiltInCircle`
- `atLeastOneDriveOrPermissionRequiredForCircle`
- `contactNotReviewed` and `cannotGrantAutoConnectedMoreCircles` (adding a member; see section 3)
- `403`: "this circle doesn't belong to this app, the app doesn't have that access itself, or it asked for more
  than Read on a drive it doesn't own".

## 5. Deliverables
- API-layer functions: `createCircle(request) -> circleId` and `deleteCircle(circleId, removeMembers)`.
- Wire both into <SCREEN / FEATURE> if one is in scope. Otherwise stop at the API layer.
- When building a create request, only offer drive access the app holds: anything it holds on its own drives,
  and Read on drives it doesn't own (when it can read them).
- Tests that follow this repo's existing patterns for the connections API: request serialization, including the
  `removeMembers` query flag, and parsing the GUID response from `create`.
- Run the repo's own build, lint and tests, and report exactly what ran and what passed.
- Do not change the server, and do not call V1 endpoints for these operations.

## Server status (for context)
- `circles/create`, `circles/delete` and owning-the-circle-is-enough for membership are on odin-core `main` (via
  PR #1879).
- Requiring the app to hold the access it grants at create: odin-core `main` (via PR #1894).
- Read on a drive the app doesn't own, and membership changes applying immediately: branch
  `app-circle-read-on-held-drives`.
- PR #1839 adds `contactNotReviewed` (3021). It keeps the `ManageCircleMembership` route for circles an app
  doesn't own, and Chat keeps that key by default.
