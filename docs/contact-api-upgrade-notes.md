# Contact API — upgrade / migration notes (v8 → v9)

> Living checklist for the Contact API rollout. The Contact API makes the server the writer of the
> ContactDrive (writes funnel through `/api/v2/contacts`, gated by the new
> `PermissionKeys.ManageContacts`). Anything that grants write access to the ContactDrive, or assumes
> a particular grant shape, has to be reconciled for **existing** installs — not just new ones. As we
> build the rest of the feature, re-check every box here before shipping.

## What ships in v9 (done in this change)

1. **New permission key** `PermissionKeys.ManageContacts = 160` (added to `PermissionKeys.All` and
   `PermissionKeyAllowance.Apps`). Asserted by `ContactService` on every write.

2. **Default app permissions updated** (`SystemAppConstants`): the **Chat** and **Mail** apps —
   the two system apps that hold `ContactDrive` `ReadWrite` — now include `ManageContacts` in their
   default `PermissionSet`. ⚠️ This only affects **freshly provisioned** apps: `TenantConfigService`
   registers system apps with `if (null == existingApp)`, so an already-installed Chat/Mail app keeps
   its persisted grant and does **not** pick up the new key from the constant. Existing installs are
   handled by the migration below.

3. **`V8ToV9VersionMigrationService`** (`Configuration/VersionUpgrade/Version8tov9/`), wired into
   `VersionUpgradeService` at the `currentVersion == 8` step and registered in `TenantServices`.
   `Version.DataVersionNumber` bumped `8 → 9`.
   - **UpgradeAsync**: enumerates all registered apps; for each app holding **Write** on the
     `ContactDrive` (today: Chat + Mail, plus any 3rd-party app that requested contact write access),
     adds `ManageContacts` to its existing `PermissionSet` via `UpdateAppPermissionsAsync`. Existing
     drive grants are preserved verbatim; idempotent (skips apps that already have the key).
   - **ValidateUpgradeAsync**: asserts every contact-writing app now has `ManageContacts`.

## Decisions deliberately NOT made here (confirm before/while building the rest)

- [ ] **"Chat drive" vs "contact drive" wording.** The request said apps with the *chat drive* need
  `ManageContacts`; the feature is really about apps that **write the contact drive**. We granted it
  to every app with `ContactDrive` Write at v9 (Chat, Mail, and Feed). Mail has no chat-drive grant — confirm
  Mail is intended to be included (it should be; it manages contacts).
- [x] **Downgrade `ContactDrive` `ReadWrite` → `Read`?** Done in v21 → v22 (#1614): the `SystemAppConstants`
  defaults (Chat, Feed) and the migration (Chat, Feed, and Mail, which nothing registers any more) give them `Read` on the ContactDrive, so their tokens can no longer
  write it directly. Keys were not touched: Feed holds `ManageContacts` only where v9 gave it, and the Contacts
  app still holds `ReadWrite`.
- [ ] **Storage-key dependency for app writes.** `ContactService` encrypts content at rest with the
  drive storage key. `OdinContextUpgrades.UpgradeToByPassAclCheck` grants the *write permission* but
  **no storage key** — so an app can only write contacts if it already holds a grant on the
  ContactDrive that carries the storage key (a **Read** grant is enough). An app with `ManageContacts`
  but **no** ContactDrive grant at all will pass the permission check and then fail to get the storage
  key. If we downgrade to `Read` (above), keep the storage-key-bearing Read grant — do not remove the
  grant entirely. Covered by `ContactTests` (the test app is given `Read` on the ContactDrive).

## Double-check list as the feature grows

- [ ] **Other apps with contact write access.** Re-scan `SystemAppConstants` (and any new system app)
  before release: Owner app, Photo app, Feed app — do any gain `ContactDrive` write access? If so add
  `ManageContacts` to their defaults too. The migration already covers *any* app with contact write
  access, system or 3rd-party, so the migration is the safety net; the defaults are the per-app intent.
- [ ] **Migration re-run safety.** `UpgradeAsync` is idempotent (skips apps that already have the
  key). Confirm it still behaves if run after the defaults change (new installs already have the key →
  skipped).
- [ ] **`UpdateAppPermissionsAsync` side effects.** It rebuilds the app's exchange grant from the
  passed `PermissionSet` + `Drives`. We re-pass the app's existing drive grants, but confirm this does
  not drop the transient temp drive (it auto-re-adds when transit is requested) or reset anything we
  care about (CORS host, circle grants are separate and untouched).
- [ ] **Circles.** The ContactDrive is not granted to circles, and `ManageContacts` is an app-only
  key (`PermissionKeyAllowance.Apps`, not `.Circles`). Confirm no circle path needs it.
- [ ] **Peer writes.** ContactDrive is `OwnerOnly` and non-distributed, so peers can't write it — no
  migration needed there. Re-confirm if that drive config ever changes.
- [ ] **odin-js client migration (separate plan).** Clients must (a) write contacts via
  `/api/v2/contacts` instead of direct drive upload, and (b) read connection status from the live
  `Relationship`/`CircleNetworkService` rather than the dropped stored `source` field. Sequence this
  with any `ReadWrite → Read` downgrade so old and new clients don't break mid-rollout. As of v22 (#1614),
  odin-js and chat-kmp main write contacts (create, update, delete, image) only through `/api/v2/contacts`;
  both still *request* ContactDrive Write, so their missing-permissions prompts must stop asking for it.
- [x] **Validation breadth.** v9's `ValidateUpgradeAsync` only checks contact-writing apps got the key; the
  downgrade's grant shape is checked by `V21ToV22VersionMigrationService.ValidateUpgradeAsync` (#1614).
- [ ] **`VersionUpgradeTests`.** The existing integration test forces failure at v4 (TestMode) and
  asserts version 4 — unaffected by adding the v9 step. If we add a test that runs the full ladder,
  update the expected terminal version to 9.
