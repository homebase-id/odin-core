# Drive deletion and app uninstall

Notes on odin-core #1879 (issues #1869, #1870) and its owner-console counterpart, odin-js #962.
**They ship together:** #1879 removes `appmanagement/deleteApp`, which the old console's **Remove app** called.

## What the owner can do

| Operation | Runs | Requires |
|---|---|---|
| Hard-delete selected files | In the request, file by file | Master key |
| Empty a drive (keep the drive) | Background job | Archived, non-system drive |
| Delete a drive | Request removes the drive and its grants; files go in a background job | Archived, non-system drive |
| Revoke an app | In the request; reversible | Master key |
| Uninstall an app | In the request, except the files of its drives (background) | Third-party app; owned circles and drives deleted with it |

All of these are **owner only** (master key) and **local only**: nothing is sent to peers, and copies they already received stay with them.

## Endpoints

| Endpoint | Answer | Does |
|---|---|---|
| `POST drive/files/harddeletefileidbatch` | 200 | Hard-deletes each listed file, as `harddelete` does |
| `POST drive/mgmt/empty` | 202 | Queues removal of the files present now; files added later survive |
| `POST drive/mgmt/delete` | 202 | Removes the drive and every grant naming it now; files go in the background |
| `GET drive/mgmt/purges` | 200 | Empties and deletes still in progress: name, type, owning app, files remaining (capped at 10,001), last error, stopped |
| `POST drive/mgmt/purges/retry` | 202 | Restarts a purge whose job stopped |
| `POST appmanagement/uninstall` | 200 | `{ AppId, DeleteOwnedCirclesAndDrives }` |
| ~~`POST appmanagement/deleteApp`~~ | — | **Removed.** It deleted only the registration row and left the app's clients behind |

App registrations now carry `IsBuiltIn` (the owner console and platform apps), so the console can tell before asking that an app cannot be uninstalled.

## How a drive delete works

1. **Guards:** master key; not a system drive (`BuiltinDrives.Protected`); archived.
2. **One transaction:**
   - removes the drive from every grant that names it: circle definitions, the `CircleMember` and `AppGrants` rows (where connections and YouAuth domains hold their grants), deposits on connected and blocked identities, and each app's own and circle-member grants;
   - drops pending outbox and inbox items, followers, and the drive record;
   - records the purge in `DrivePurgeRegistry`.
3. **`DrivePurgeJob`** then works through the files, 500 per run:
   - payloads first, then rows, so a failed run leaves files the next run finds again;
   - when no files are left, it sweeps the remaining rows and directories and clears the registry record.

**Why grants are removed rather than left inert:** apps re-create drives with fixed aliases. A leftover grant, write included, would carry over to the new drive.

**A circle that granted only the deleted drive** is kept, granting nothing, so its members stay until the owner edits or deletes it.

## How empty works

Same guards. It drops pending transfers, then records the purge with the time of the request. The job deletes only files created by then, so a drive restored and used straight away keeps its new files. The owner can still write to an archived drive; apps and peers can't.

## Safety rules in the purge

- **Alias lock:** a deleted drive's alias cannot be created again until its purge finishes. The job deletes by drive id, and a drive's id is its alias.
- **One job per drive** (by hash). A second empty or delete updates the registry record; the job re-reads it before finishing and repeats if it changed.
- **Finished and failed jobs are deleted at once**, so a later empty or a retry is never swallowed by a job kept under the same hash. A failure stays visible on the registry record (`LastError`).
- **Directory deletes are guarded:** `TenantPathManager.AssertIsDriveDirectory` refuses any path that is not one drive's own.
- **Counts are capped** at 10,001: the status endpoint is polled, and an uncapped count scans every row of the drive.

## How an app uninstall works

1. **Guards:** master key; not built-in (`AppUninstallService.IsBuiltIn`); not while the v13 registration move is pending (its blob copy would bring the app back).
2. **Refused if the app owns circles or drives,** unless `DeleteOwnedCirclesAndDrives` is set.
3. **Then, in an order that can be re-run** (the registration goes last):
   - its drives are archived and deleted, as above;
   - it is removed from every connection: its grants, the grants it deposited, the enrollments it owns or requested;
   - its circles are removed from other apps, then their members, then the circles themselves;
   - its clients (live and expired) and their push subscriptions are deleted;
   - its registration is deleted.

## Owner console (odin-js #962)

- **Drive page dropdown:**
  - **Archive drive** (with a confirmation), or **Restore drive** once archived.
  - **Empty drive** and **Delete drive** are always listed, disabled with "Archive the drive first" until archived.
  - None of these appear on system drives or the transient temp drive.
- **App page:** the header keeps **Restore app** (when revoked). A **Danger zone** at the bottom holds:
  - **Revoke app:** reversible; disabled once revoked.
  - **Uninstall app:** disabled with "Revoke the app first", or "Built-in apps can't be uninstalled".
- **Confirmations:** destructive actions ask you to type the action and the name (`empty Photos`, `delete Photos`, `uninstall Chess`). Uninstall names every drive and circle it deletes. Drive dialogs say to export first.
- **Purge status:**
  - "Emptying/Deleting <drive>… N files left" on the drive page, the owning app's page and the apps dashboard.
  - The console polls every 3 seconds while a purge runs, and refreshes the drive and its files when it finishes.
  - A stopped purge shows its error and a **Retry** button.

## Tests

- **odin-core:**
  - `DriveDeletionTests`: batch hard delete, empty, delete, guards, alias lock, empty cutoff, batching, grant removal, purge status, retry.
  - `AppUninstallTests`: built-in refused, clients removed, owned drives and circles, connection grants.
  - Storage tests for per-file cleanup, batched select and delete, and a coverage guard that fails if a drive-keyed table is not cleared.
- **Each safeguard is shown to matter:** removing the alias lock, the cutoff, the per-file payload delete or the grant removal fails its test.
- **Jobs are cleared before each test:** they live in the system database, which the per-test reset does not restore.

## Not covered

- PostgreSQL and S3 at real scale. Batching is tested with a batch size of 2.
- The job's error-recording path. Tests simulate a stopped job by removing it.
- The console in a browser.

## Follow-ups worth filing

1. **Job failures on the job record:** let `JobExecutionResult.Fail` carry a message, and let a hashed retry revive a failed job, instead of copying errors onto the registry.
2. **A drive state instead of a registry:** "being deleted" on the drive itself would make the alias lock and status natural. The cost is that every drive read must skip such drives.
3. **An app-wide purge notice** in the layout, next to `CriticalOwnerAlerts`, instead of three pages.
4. **Faster emptying of very large drives:** group payload deletes by directory, or one S3 bulk delete per batch.
5. **Uninstall leftovers:** app notifications, scheduled notification jobs and contacts' per-app data.
6. **A console UI for bulk hard delete** (the file browser would need multi-select).
7. **The purge notice on the Owner console page** matches a drive's app id exactly, so a legacy drive with no app id wouldn't show there.
