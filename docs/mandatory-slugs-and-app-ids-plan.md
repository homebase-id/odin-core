# Plan: mandatory app slugs, drive slugs, and AppId everywhere

Status: **plan**, 2026-09-17.

## The rules

1. **Every drive has an `AppId`.** Nothing is created without an owner.
2. **Every circle has an `AppId`.**
3. **Every drive has a `DriveSlug` and a `DriveTypeSlug`.** Always.
4. **Owner-console drives and circles are owned by the System app** (`SystemAppConstants.SystemAppId`).
   That covers the wallet drive, ad-hoc drives the owner creates, user circles, and the two system circles.
5. **An app registration must carry an app slug.** No deriving one from the name.

## Where things stand (read in the code)

| Area | Today |
|---|---|
| App slug | Optional. `AppRegistrationService.AssignSlugAsync:608-612` derives one from `Name` when missing. The column is already `NOT NULL`, so every stored app has one. Built-in slugs are not reserved: a caller can take `chat` first-come. |
| Drive create | Owner only (master key); apps cannot create drives. `AppId`, `DriveSlug`, `DriveTypeSlug` all optional (`CreateDriveRequest.cs:24-42`). Slugs are derived only when `AppId` is set; with no `AppId` a supplied slug is stored as-is. `TypeSlugFor` can return null. Callers: owner `POST drive/mgmt/create`, the setup wizard, and `BuiltinProvisioner` (which already requires all three). |
| Circle create | Owner only (master key); **no app creates circles** anywhere — server, odin-js or chat-kmp. `AppId` is copied from the request body, with no check that the app exists (`CircleDefinitionService.cs:552`). |
| Null `AppId` today | Wizard/console drives, `WalletDrive`, anything `StampRemainingDrivesAsync` left, user circles, and the two system circles. |
| odin-js | chat/mail/feed/community send an app slug and slugs for their fixed drives. Runtime drives (feed channels, communities, profiles) send `appId` + type slug, no drive slug. Two extend URLs send no slugs. The owner console never sends `appId` when creating a circle, and has no create-drive UI. |
| chat-kmp | Sends no app slug and no drive slugs. Creates nothing itself; every drive it asks for already exists server-side (GUIDs match `BuiltinDrives`; its "placeholder" comments are stale). |

## Backend changes (odin-core)

1. **Drive create** — `DriveManager.CreateDriveAsync`, owner `drive/mgmt/create`, setup wizard:
   - `AppId` required; the app must be registered. An owner request without one defaults to the System app
     rather than failing, so the console and wizard keep working.
   - `DriveSlug` required. (Runtime instance drives — channels, communities, profiles — may keep deriving
     server-side until clients name them; everything else must send one.)
   - `DriveTypeSlug` required, and `TypeSlugFor` must never return null.
2. **Circle create** — `CircleDefinitionService.CreateCircleInternalAsync`, owner create endpoint, setup
   wizard: `AppId` required, defaulting to the System app for owner callers; the app must be registered.
3. **App slug required at registration** — `AssignSlugAsync` refuses a missing slug instead of deriving;
   `AppRegistrationRequest.IsValid()` includes it; YouAuth requires `as` only when the app still needs
   registering (so an already-registered app signing in is unaffected). `AppSlugGenerator` stays for
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

## Order

1. Clients send slugs (odin-js, chat-kmp) — backward compatible, no server change yet.
2. Migration stamps the System app id on every null `AppId`; switch the "owner's own" checks to compare
   against it.
3. Require `AppId` and both slugs on create, defaulting to the System app for owner callers.
4. Require the app slug at registration; reserve built-in slugs.
5. Later: clients name runtime drive slugs; decide what adoption (`set-owner`) means now that nothing is
   unowned.
