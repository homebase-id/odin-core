# Go-live checklist: Circle, AppRegistrations and Drive addressing

Derived working checklist for the table moves (#1688), chat circle ownership (#1691), and the drive
addressing columns. `docs/drive-addressing.md` and `docs/connection-defaults.md` remain the specs;
where this file and those disagree, those win.

Status as of 2026-08-27.

## Done

| # | Item | Where |
|---|---|---|
| 0.1 | Circle definitions moved out of the key-three-value blob into the `Circle` table | #1688 |
| 0.2 | App registrations moved into the `AppRegistrations` table, slugs coined | #1688 |
| 0.3 | `GetByGrantOnAsync` + cache invalidation; `CreateCircleRequest` carries the promoted fields; deposit-only guard | #1688 |
| 0.4 | v12 -> v13 migration, both moves in one step, idempotent, blob rows kept | #1688 |
| 0.5 | `Circle.circleId` global UNIQUE dropped (`v202608261644`) | #1688 |
| 0.6 | `AppRegistrations` create reachable on databases that predate the table (`v202608271000`) | #1688 |
| 0.7 | Legacy blob read with a frozen DTO -- the `[JsonIgnore]` bug that migrated zero apps | #1688 |
| 0.8 | Chat app owns Friends/Family/Work/Acquaintances; v13 -> v14 rebinds ownership only | #1691 |
| 0.9 | `AppId` / `DriveSlug` / `DriveTypeSlug` plumbed through `StorageDrive`, `DriveManager`, `CreateDriveRequest`, `OwnerClientDriveData`; `OdinSlug` format validator | #1688 |

## Blocked on decisions

| # | Item | Note |
|---|---|---|
| 1.1 | Owning app per system drive | `scratchpad/drives.csv`. Not inferable: no drive is granted to exactly one app, and Contact/Profile/Sticker are granted to all three |
| 1.2 | `DriveSlug` per system drive | Same file. Defaults in it are proposals, not from the codebase |
| 1.3 | `DriveTypeSlug` values, and whether it follows the type guid or the drive | Profile/Wallet/HomePageConfig share one type guid; Moments/Lists share another. Per-type means HomePageConfig's slug reads as `profile` |
| 1.4 | Do ownerless drives get slugs at all? | `drive-addressing.md` OQ3. The doc's invariant says `DriveSlug` is null when `AppId` is null |
| 1.5 | Owning app for Confirmed / Auto-connected / Emergency Location Access | `scratchpad/circles.csv`. Emergency Location should agree with whoever owns LocationDrive |
| 1.6 | Do Lists, Location and ShardRecovery become their own apps before or after this ships? | If before, defaulting them to chat means a second ownership migration |
| 1.7 | Is `(AppSlug, DriveSlug)` the successor to `TargetDrive` or a parallel name? | `drive-addressing.md` OQ1. Decides whether slugs are an address or a convenience |

## Derivation and backfill

| # | Item |
|---|---|
| 2.1 | `DriveSlugGenerator`, mirroring `AppSlugGenerator`: fixed slugs for known drives, derived-from-name otherwise, whole set resolved with dedupe before anything is written |
| 2.2 | Derive on drive create, so drives made after the backfill are not immediately null again |
| 2.3 | Backfill migration for existing drives: ownership, slug, type slug |
| 2.4 | Backfill `Circle.AppId` for the system and built-in circles (the wizard four are already covered by #1691) |
| 2.5 | Decide whether `GrantOn` / `Designation` get set during backfill or stay at their defaults |

## Enforcement

Everything below is currently accepted-but-unenforced. Each line is a separate decision about *when*
to tighten, because each one can reject a call that works today.

| # | Item | Today |
|---|---|---|
| 3.1 | **App slug supplied by the caller** at registration, rather than server-coined | `AppRegistrationRequest` has no slug field; `AppSlugGenerator` derives one. Adding the field means validating it and rejecting duplicates |
| 3.2 | **App slug immutability** across updates | Enforced: updates carry the stored slug forward |
| 3.3 | **App slug uniqueness** | Enforced by `UNIQUE(identityId, AppSlug)` |
| 3.4 | **Drive slug required when `AppId` is set** | Not enforced. An app-owned drive can be created with no slug |
| 3.5 | **Drive slug forbidden when `AppId` is null** | Not enforced. The doc's invariant; without it, ownerless drives can carry slugs the unique index cannot constrain (NULL `AppId` rows do not collide in either dialect) |
| 3.6 | **Drive slug uniqueness for ownerless drives** | Impossible at the constraint level while `AppId` is null; needs a code check, or 3.5 |
| 3.7 | **Drive slug immutability** | No update path writes it yet. Once slugs are addresses, renaming breaks other identities' links |
| 3.8 | **Reserved-segment denylist** | `OdinSlug.Reserved` is empty and must grow whenever a literal route segment is added at `{appSlug}` or `{driveSlug}` position |
| 3.9 | **System app slugs protected** | `AppSlugGenerator` orders known apps first so a user app cannot take `chat`, but nothing stops a caller-supplied slug from doing so once 3.1 lands |
| 3.10 | **`Type` becomes app-scoped** | Four call sites still read it as a global vocabulary: `FollowerService`, `FollowerPerimeterService`, `FeedDriveDistributionRouter`, `FeedNotificationMapper` |

## Routes

| # | Item |
|---|---|
| 4.1 | `/api/v2/apps/{appSlug}/drives/{driveSlug}/...` -- not built |
| 4.2 | `/api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/...` -- not built. This is the payoff: neither side shares a Guid |
| 4.3 | Recipient-side slug resolution, and what happens when a slug does not resolve |
| 4.4 | Keep `type` in responses until `TargetDrive` retires end to end -- two independent steps, in that order |

## Deploy safety

| # | Item |
|---|---|
| 5.1 | **The app list reads the table exclusively.** The v12 -> v13 upgrade runs only when the owner logs in (`OwnerAuthenticationHandler` -> `EnsureScheduledAsync`), so between deploy and that login an identity has no apps and app clients break. Decide: blob fallback until migrated, a non-owner-triggered upgrade, or a two-release sequence |
| 5.2 | **Never renumber a migration that has run anywhere.** A demo box was stranded at v13 when v13 changed meaning between builds; the version number is the only record of what actually happened |
| 5.3 | Confirm no environment other than the demo box ran the intermediate two-step build |
| 5.4 | Blob cleanup job for the migrated circle and app rows -- deliberately deferred, still owed |
| 5.5 | Generator: stamp a new table's create migration at the current version, never 0, and branch `UpAsync` on table-exists. Both are hand-edited in the repo today and the next regen reverts them |
| 5.6 | Audit other tables whose migration list holds only a `V0` entry but that were added after first release -- same latent 42P01 |

## Tests owed

| # | Item |
|---|---|
| 6.1 | `TableAppRegistrationsTests` -- no CRUD test exists at all; every sibling table has one |
| 6.2 | Duplicate-slug rejection: `UNIQUE(identityId, AppSlug)` is the reason for the whole table move and nothing asserts it |
| 6.3 | **Migration from an existing database**, not an empty one. Every migration test starts empty, which is exactly the case that works -- this is the gap that let the missing-table bug reach demo |
| 6.4 | Circle table tests for the four promoted columns, `GetByGrantOnAsync`, and its cache invalidation |
| 6.5 | API-level assertion that the promoted circle fields survive a definition round-trip, and that a trimmed update body does not silently clear them |

## Client coordination

| # | Item |
|---|---|
| 7.1 | Does any client filter circles by `appId`? Stamping the relationship circles could make them vanish from a circle manager that shows only owner circles |
| 7.2 | What does the setup wizard actually post for Friends/Family/Work/Acquaintances? The server now wins the create race, so anything extra the wizard sends is silently dropped for new identities |
| 7.3 | Clients must round-trip the full circle definition on update, or `grantOn` / `designation` / `emoji` are cleared |
| 7.4 | Owner console: surface `appId` / `driveSlug` / `driveTypeSlug` where useful |
