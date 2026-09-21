# Plan: mandatory app slugs, drive slugs, and AppId everywhere

Status: **backend done** (odin-core PR #1787), **clients done** (odin-js #935, chat-kmp #1564), 2026-09-17.

The "where things stand" table below described the code *before* those PRs and has been removed rather
than left to mislead; what remains is the rules, and the items still outstanding.

## The rules

1. **Every drive has an `AppId`.** Nothing is created without an owner.
2. **Every circle has an `AppId`.**
3. **Every drive has a `DriveSlug` and a `DriveTypeSlug`.** Always.
4. **Owner-console drives and circles are owned by the System app** (`SystemAppConstants.SystemAppId`).
   That covers the wallet drive, ad-hoc drives the owner creates, user circles, and the two system circles.
5. **An app registration must carry an app slug.** No deriving one from the name.

## Backend changes (odin-core)

1. **Drive create** — `DriveManager.CreateDriveAsync`, owner `drive/mgmt/create`, setup wizard:
   - `AppId` required, and **taken on trust — the app does not have to be registered yet**. It cannot be:
     a registration is granted its drives and `ExchangeGrantService` refuses a grant naming a drive that
     does not exist, so an app's drives are created before the app is registered (`BuiltinProvisioner`
     and the owner console both do this). Requiring a registration here made the two rules mutually
     exclusive and left a third-party app that asks for a new drive impossible to register. The check
     lives on `set-owner` / `reassign-owner` instead, where the drive already exists and the ordering
     cannot bite. An owner request without an `AppId` defaults to the System app rather than failing, so
     the console and wizard keep working.
   - `DriveSlug` required. (Runtime instance drives — channels, communities, profiles — may keep deriving
     server-side until clients name them; everything else must send one.)
   - `DriveTypeSlug` required, and `TypeSlugFor` must never return null.
2. **Circle create** — `CircleDefinitionService.CreateCircleInternalAsync`, owner create endpoint, setup
   wizard: `AppId` required, defaulting to the System app for owner callers; the app must be registered.
   Unlike drive create, which cannot check: nothing has to create a circle before its app exists, so the
   ordering that forces the exemption on drives does not arise here.
3. **App slug required at registration** — `AssignSlugAsync` refuses a missing slug instead of deriving;
   validated in `AssignSlugAsync` rather than `IsValid()`, which cannot see which apps are built in;
   YouAuth requires `as` only when the app still needs registering (so an already-registered app signing
   in is unaffected, and the parameter is deliberately not `[Required]`). `AppSlugGenerator` stays for
   migrations.
4. **Reserve built-in app slugs**: refuse a slug that `BuiltinApps` assigns to a different `AppId`.
5. **Migration (v18 → v19)**: stamp the System app id on every drive and circle with a null `AppId`,
   deriving drive slugs and type slugs where missing, including the two system circles. Assert afterwards
   that nothing is left null.
6. **Fix what "the owner's own" means.** A null `AppId` is the current test for it, and after the migration
   nothing is null. Each of these must compare against the System app id instead:
   - `CircleNetworkService.cs:786` (an app may not enrol anyone into an owner circle), `:515`, `:1484`,
     `:1582`, `:2156`, `:2478`;
   - `CircleMembershipService.cs:328` (`HideOwnerCirclesFromApps`);
   - `CircleDefinitionService.cs:190` (`SetOwningAppAsync`) and `DriveManager.cs:547`
     (`SetDriveOwningAppAsync`) — both exist to give an *unowned* drive or circle to an app and refuse one
     that already has an owner. With nothing unowned, they become reassign-only or go away.
   Miss one and an app gets treated as the owner of console drives and circles.
7. Update the stale comments in `CreateDriveRequest.cs:27-37`.

## Tests

Extend the existing fixtures:

- **App slug** — `Ported/Profile/AppSlugRegistrationTests.cs`: `AnOmittedSlugIsStillDerived` flips to "is
  refused"; add "a built-in slug is refused for another app". Fix helpers that register without a slug:
  `Api/OwnerAdmin.Apps.cs`, `V2Fixture.cs`, `AppAPI/Utils/AppApiTestUtils.cs`, `OwnerApi/Utils/OwnerApiTestUtils.cs`.
- **YouAuth** — unregistered app without `as` → bad request; registered app without `as` → still signs in.
- **Drives** — `Addressing/AppDriveAddressingTests.cs`, `Ported/DriveManagement/DriveManagementTests.cs`:
  owner create with no `AppId` → System app + slug; unknown `AppId` → 400; missing slug → 400; missing type
  slug → 400; two owner drives cannot claim one slug. Update `OwnerAdmin.CreateDrive` and
  `DriveManagementApiClient`, which create ownerless drives today.
- **Circles** — `Ported/Circles/CircleDefinitionTests.cs`, `CircleOwningAppTests.cs`,
  `Ported/Connections/CircleMembership/AppOwnerCircleRestrictionTests.cs`: owner create with no `AppId` →
  System app; unknown `AppId` → 400; **an app still cannot enrol anyone into a console circle**, and
  `HideOwnerCirclesFromApps` still hides them — these are the checks that change meaning.
- **Migration** — v18 → v19: drives and circles with null `AppId`, including the two system circles, come
  out owned by the System app with slugs; app-owned rows untouched.
- **Setup wizard** — `Ported/Configuration/SystemInitializeConfigTests.cs`: wizard drives and circles follow
  the same rules.

## odin-js changes

1. **js-lib**
   - `getRegistrationParams`: `appSlug` required (move it out of the trailing optional position);
     `AppAuthorizationParams.as` required.
   - `TargetDriveAccessRequest`: `driveSlug` and `driveTypeSlug` required, and `ds`/`ts` with them.
   - `ensureDrive(...)`: `appId`, `driveSlug`, `driveTypeSlug` required.
2. **owner-app**
   - `RegisterApp.tsx`: a missing `as` is a bad request, like a missing `appId`; `AppRegistrationRequest.appSlug`
     required.
   - `useApp.ts` (`registerNewApp`, `extendPermissions`): refuse grants missing `ds`/`ts` instead of the
     `'channel'` fallback or undefined; stop passing the registering app's id for shared drives it doesn't own
     (profile, contacts, chat-in-community) — only create drives the app actually owns.
   - Circle create (`CircleDialog` → `useCircle.createOrUpdate`) and the wizard (`useInit.ts`): rely on the
     server default (System app) rather than each client picking an id.
   - `CircleProvider.updateCircleDefinition`: strip `appId`/`isTreeDeclared` so an update never looks like an
     ownership change.
3. **Runtime drives** — `useManageChannel.ts` (feed) and `useCommunity.ts` (community) extend URLs: add `ts`
   (`channel` / `community`). Their drive slugs stay server-derived until we decide clients name them.
4. The four apps' fixed drive lists already comply.

## chat-kmp changes

1. Add `as` to `AppAuthorizationParams` plus an `AppConfig.APP_SLUG = "chat"`, passed from `LoginViewModel`
   and `main.wasm.kt`. Not needed while Chat is built-in and always registered, but it makes the request
   complete.
2. Add `driveSlug`/`driveTypeSlug` to `TargetDriveAccessRequest` (`ds`/`ts`) and set them on every drive in
   `AppConfig`, matching `BuiltinDrives`: chat `chat/chat`, stickers `stickers/sticker`, contacts
   `contacts/contact`, profile `profile/profile`, feed `feed/feed`, public channel `posts/channel`, email
   `email/email`, location `location/location`, moments `moments/list`, vault `vault/vault`, webdrop
   `webdrop/webdrop`. Same for the extend params.
3. Nothing to do for circles: chat-kmp creates none. Its membership calls (`circles/add`, `add-many`,
   `revoke`) touch Emergency Location Access and the relationship circles, so re-check them if app-ownership
   rules tighten on membership later.
4. Drop the stale "placeholder" comments on the Vault and Location drives.

## Still outstanding

1. Clients naming runtime drive slugs (feed channels, communities, profiles). Server-derived today,
   because only the server knows which slugs the owning app already holds.
2. Whether adoption (`set-owner`) and reassignment (`reassign-owner`) should collapse into one
   operation. Nothing is unowned any more, so adoption is "take from the owner console" and the two
   paths differ only in which source state they refuse.
3. Whether the owner console should get its own app id rather than sharing `SystemAppId`. Doing it later
   means a second data migration, since v19 writes the shared id into every row.
4. Vault and Contacts still share a drive *type* GUID, so clients send two different type slugs for one
   type.
