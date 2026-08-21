# Identity JSON Export / Import — Design

Date: 2026-08-19
Repos touched: `odin-core`, `Odin-SQLite-Generator`

## Problem

We need to lift a single identity off one system and land it on another. Nothing
today produces a portable document for that.

The existing "export tenant" (`ExportTenantJob` → `IIdentityRegistry.CopyRegistration`,
`FileSystemIdentityRegistry.cs:229`) is a filesystem copy: it disables the tenant,
`cp -r`s the registration directory, then walks the payload shards. It throws
outright when `S3Payload.Enabled` is set, and it produces a directory tree rather
than a document you can inspect, diff, or hand to a different system.

`DataImporter` (`src/core/Odin.Core.Storage/DatabaseImport/DataImporter.cs`) does
move an identity's rows, but database-to-live-database only, and it costs a
hand-maintained enumeration of all 25 tables. That enumeration is policed by
`DataImporterTests`, which greps `DataImporter.cs` **as text** for
`sourceIdentityDatabase.{Table}.PagingByRowIdAsync` per table. Its own header
comment concedes this is a stopgap and describes the real fix.

A second hand-written enumeration would mean a second grep-the-source test. So the
export is generated instead: `Odin-SQLite-Generator` already knows every table,
column, SQL type, and `DbType`, and already emits per-namespace aggregate files.
Adding a table there should extend the export with no edit in `odin-core` at all.

## Requirements

Decided up front, not open:

1. **Export everything, always. Filter on import.** The exporter writes every
   table in the Identity database, every column, every row for that `identityId`,
   with no curation and no per-table policy. `Inbox`, `Outbox`, and `Nonce`
   included. Whether a given table's rows are *replayed* is decided by the import
   side. See **Import filtering**.
2. **Same `identityId` and same domain.** A pure snapshot restore. No remapping.
3. **Identity database plus the identity's System rows** (`Registrations`,
   `Certificates`, `DkimKeys`) in the same file.
4. **Payloads out of scope.** Bytes and thumbnails move separately.
5. **One JSON file.**
6. **Zero per-table maintenance in `odin-core` for reading and writing rows.**
   Adding a table to the generator must extend both export and import with no edit
   in `odin-core`. The one deliberate exception is the import skip list, which
   defaults to "import" so a new table still flows through untouched.
7. **Every timestamp imports as-is.** No column may be rewritten to import-time
   wall-clock. This has bitten before; `DataImportPatcher.cs` exists solely to
   repair it after the fact. See **Timestamp fidelity**.
8. **Import fails if the `identityId` OR the domain already exists on the target.**
   Not one or the other, and not a merge. Either collision is a hard stop before
   any row is written. See **Import preconditions**.
9. **Export refuses unless the identity is frozen.** Disabling an identity only
   closes the HTTP front door; its background workers keep writing. Export requires
   them stopped, which needs a freeze mechanism that does not exist today. See
   **Freezing the identity**.
10. **Every table's schema version must match between source and target, and the
    rule is all-or-nothing.** Not the database as a whole: each table individually.
    No row from any table is imported unless every table matches, including tables
    the import would otherwise skip. A table present on one side and not the other
    is a mismatch too. Import refuses, listing every difference. The file also
    carries its own `formatVersion` for the envelope, which is a separate thing.
    See **Versioning**.

## Non-goals

- Payload and thumbnail bytes. A tables-only import yields file headers whose
  bytes are absent until payloads are copied by other means.
- Remapping `identityId` or domain. Both are hard for the same reason: the domain
  is embedded as text inside JSON blobs (`hdrFileMetaData`, `hdrAppData`,
  `senderId`), so a rename is a content rewrite, not a column update.
- Merging into a target that already holds this identity. Import refuses.
- Encrypting the file. See **Security**; this is the caller's responsibility and
  is documented, not implemented.

## Verified groundwork

Facts established by reading the code, not assumed:

- **No foreign keys** anywhere in the Identity schema, and no table stores another
  table's `rowId`. The only `rowId` occurrences outside cursor code are `RETURNING`
  clauses. Insert order is therefore unconstrained.
- **All 23 Identity tables carry an `identityId` column.** Scoping is uniform, so
  "everything for this identity" needs no per-table hint in that namespace.
- **Every record type already round-trips through `System.Text.Json`** with no new
  converters. `UnixTimeUtc` and `OdinId` carry `[JsonConverter]` on the type
  (`UnixTimeUtc.cs:53`, `OdinId.cs:11`), serializing to a number and a domain
  string. `OdinSystemSerializer.JsonSerializerOptions` supplies `ByteArrayConverter`
  (base64), `GuidConverter`, and `NullableGuidConverter`.
- **`InsertAsync` hard-codes `created`/`modified` to `NOW()`** via `{sqlNowStr}` in
  the generated SQL (`TableDrivesCRUD.InsertAsync`). This is why
  `DataImporter.ImportTimestampedTableAsync` needs a follow-up `UPDATE`, and why
  `DataImportPatcher.cs` (336 lines) exists at all.
- **`pagingGet` is optional per table** in the generator (`Program.cs:287`). All 23
  Identity tables declare it today, but a new one need not. Export must not depend
  on it.
- **The generator emits for six namespaces, and only two are in scope.** Identity
  (23 tables, every one carrying `identityId`) and System (6 tables) are this feature's
  territory. KeyChain, Notary and Attestation are standalone databases with their own
  connection factories, and SocialSync is a separate database in a separate repo; all
  four are unrelated applications that happen to share the generator. They are excluded
  by namespace, via `IdentityHostNamespaces`, rather than by per-table annotation, so a
  table added to any of them stays out of the export even if it carries an `identityId`
  column. Only the 6 System tables need a per-table annotation.
- **In the System database three tables are identity-scoped**: `Registrations`
  (by `identityId`), `Certificates` (by `domain`), and `DkimKeys` (by `domain`).
  `Jobs`, `Settings`, and `LastSeen` are system-wide. `DkimKeys` holds the
  identity's DKIM signing keys, one row per selector, primary key
  `(domain, selector)`, so unlike `Certificates` it contributes several rows per
  identity rather than exactly one.

## Design

### 1. Generator: `Odin-SQLite-Generator/Program.cs`

**1a. One new field on `Table`.**

```csharp
// Column that scopes a row to a single identity. Default covers every Identity
// table. Set to "domain" for Certificates and DkimKeys. Set to null to exclude
// the table from identity export entirely (Jobs, Settings, LastSeen).
public string? exportScopeColumn = "identityId";
```

This is the only per-table knowledge the feature needs, it lives next to the
column definitions it refers to, and it defaults to the correct answer for the
namespace that matters. Five System table declarations set it explicitly (`Registrations` uses the
default, since it really does scope on `identityId`); nothing in the Identity
namespace does, and nothing outside the two exportable namespaces reads
it at all.

Defaulting to `"identityId"` rather than to `null` is what delivers requirement 6: a
table added to the Identity namespace is exported with no further edit. A table added
to the System namespace without a real `identityId` column fails at generation time
with a named error rather than silently exporting nothing, so the failure direction is
safe, and it is zero-maintenance where it counts.

**The namespace gate is separate from, and stronger than, the annotation.** Every
generator method this feature adds begins with `IsIdentityHostNamespace(table)`. That is
what guarantees no export or import code reaches an unrelated application, whatever columns its tables happen to have. The per-table
`exportScopeColumn` then decides scope *within* the two namespaces that are in
scope.

**1b. `GenerateExportRows(table)` — emitted into each `Table*CRUD.cs`.**

One query per table, streamed row by row off the reader. Emitted unconditionally,
so it never depends on whether the table happens to declare `pagingGet`:

```csharp
internal virtual async Task ExportRowsAsync(Guid identityId, Func<CircleRecord, Task> onRow)
{
    // SELECT <all columns> FROM Circle
    // WHERE identityId = @identityId
    // ORDER BY rowId ASC
    // while (await rdr.ReadAsync()) await onRow(ReadRecordFromReaderAll(rdr));
}
```

**Not paged.** Reading row by row off a single open reader is already O(1) memory
in the number of rows, so keyset paging buys nothing and costs three things: N
round trips instead of one, N re-executions of the query, and, worst, a loss of
consistency. Under `READ COMMITTED` each statement takes its own snapshot, so
paging across a live table can miss rows or return them twice. One statement
cannot.

The callback shape is deliberate. `IAsyncEnumerable` would read better but appears
**zero** times in `odin-core` today, and generated code that lands in 25 files is
the wrong place to introduce a new idiom. A `Func<TRecord, Task>` matches the
existing reader-loop style (see `TableLastSeen.GetAllAsync`) and keeps the
generated code format-agnostic: it knows nothing about JSON, so changing the file
format never requires a regenerate.

`ORDER BY rowId ASC` still matters, for insert ordering on the target rather than
for paging: see **Ordering and rowId**.

**1c. `GenerateImportRow(table)` — emitted into each `Table*CRUD.cs`.**

An INSERT naming **every** column except `rowId`, including `created` and
`modified` as real parameters:

```csharp
internal virtual async Task<int> ImportRowAsync(DrivesRecord item)
{
    // INSERT INTO Drives (identityId,DriveId,...,created,modified)
    // VALUES (@identityId,@DriveId,...,@created,@modified)
    // No Validate(). No RETURNING. No {sqlNowStr}.
}
```

Two deliberate omissions:

- **No `{sqlNowStr}`.** See **Timestamp fidelity** below. Naming `created` and
  `modified` as parameters makes the clobbering bug structurally impossible, which
  removes the reason `DataImportPatcher` exists.
- **No `Validate()`.** A restore must be faithful, not re-validated. This is what
  eliminates per-table special cases: `DataImporter` has to skip expired `Nonce`
  rows only because `InsertAsync` calls `Validate()` and rejects them. More
  generally, a validation rule tightened after a row was written must not make
  that row unrestorable.

`internal` is the right visibility: `IdentityDatabase` and the consumer both live
in `Odin.Core.Storage`, and the hand-written `Table*.cs` wrappers inherit these
without needing an `internal new` re-declaration.

**Timestamp fidelity.** Requirement 7 reduces to a single argument. The generator's
`GetTuples(...)` takes a `sqlNowSubstitute` flag (`Program.cs:3937`); when true it
replaces the `created` and `modified` columns with the literal `{sqlNowStr}`
instead of a bind parameter. `GenerateInsert` passes it as **true**, which is the
entire cause of the historical bug. `GenerateImportRow` passes it as **false**, so
every column including `created` and `modified` becomes a real parameter carrying
the source value.

Three things were checked to be sure nothing else rewrites a time:

- The substitution keys purely off the `Column.created` / `Column.modified` flags
  set by `FinallyAddCreatedModified()`. No other column can trigger it.
- The generator emits **no** database triggers (zero `CREATE TRIGGER` in
  `Program.cs`), so nothing rewrites a timestamp at the SQL layer either.
- Only `created` and `modified` were ever at risk. Every other time-typed column
  is already an ordinary parameter and always was: `timestamp` (AppNotifications),
  `expiresAt` (ClientRegistrations), `ReviewedAt` (Connections), `userDate`
  (DriveMainIndex), `isReadByRecipient` (DriveTransferHistory), `timeStamp`
  (Inbox), `expiration` (Nonce), `nextRunTime` (Outbox). These are in scope for the
  fidelity test regardless.

Precision is exact end to end, not merely close. `UnixTimeUtcConverter` writes via
the `Utf8JsonWriter.WriteNumberValue(long)` overload and reads via
`reader.TryGetInt64` (`UnixTimeUtc.cs:12-27`), so millisecond values round-trip as
`Int64` with no `double` in the path.

**1d. `GenerateAllGlobalExportLists()` — a new per-namespace aggregate.**

Modelled directly on the existing `GenerateAllGlobalTableLists()`. Emits
`{Pretty}Database.Export.Generated.cs`:

```csharp
public partial class IdentityDatabase
{
    // Authoritative list, used by the coverage test.
    public static readonly ImmutableList<string> ExportableTables =
        ["Drives", "DriveMainIndex", ..., "Nonce"];

    public async Task ExportAsync(Utf8JsonWriter writer, Guid identityId) { /* unrolled */ }

    public async Task<int> ImportRowAsync(string table, JsonElement data) { /* switch */ }
}
```

Only tables with a non-null `exportScopeColumn` appear. `SystemDatabase` gets the
same pair, covering just `Registrations`, `Certificates`, and `DkimKeys`.

**Scope parameters are derived, not fixed.** The generator emits one parameter per
*distinct* `exportScopeColumn` used in the namespace, typed from that column's
declaration. Every Identity table scopes on `identityId`, so that namespace gets a
single `Guid identityId`. The System namespace scopes `Registrations` on
`identityId` and both `Certificates` and `DkimKeys` on `domain`, so it gets both
parameters. Note the parameter list is one per *distinct* scope column, not one
per table, so `DkimKeys` joining the namespace adds no parameter:

```csharp
public partial class SystemDatabase
{
    public async Task ExportAsync(Utf8JsonWriter writer, Guid identityId, OdinId domain);
    public async Task<int> ImportRowAsync(string table, JsonElement data);
}
```

Each generated call site passes whichever argument matches its own table's scope
column. A future table scoped on a third column extends the signature
automatically, and the consumer fails to compile rather than silently exporting
nothing.

### 2. `odin-core`: no per-table code anywhere

Three areas, in three layers. Only the first two are new files; none of them names
a table.

**`Odin.Core.Storage`**, in `src/core/Odin.Core.Storage/DatabaseImport/` alongside
the existing importer:

- **`IdentityJsonExporter.cs`** — opens the stream, writes the header, calls
  `systemDb.ExportAsync(...)` then `identityDb.ExportAsync(...)`, closes the array.
  Runs inside one `RepeatableRead` transaction. Takes a flag asserting the caller
  has frozen the identity; it cannot check that itself without referencing upward.
- **`IdentityJsonImporter.cs`** — streams envelopes, dispatches each to
  `ImportRowAsync(table, data)`, all inside one stacked transaction with a
  `commit` flag.

**`Odin.Services`**, in `Registry/`:

- **`IIdentityRegistry` / `FileSystemIdentityRegistry`** gain
  `FreezeIdentityAsync` / `UnfreezeIdentityAsync`. Thin, since `ToggleDisabled` and
  the private `Start`/`StopBackgroundServices` already exist. See **Freezing the
  identity**.

**`Odin.Hosting`**, CLI verbs in `Cli/CommandLine.cs` following the existing
`sqlite2pg-*` pattern. This is the only layer that can both freeze and export,
so it owns the sequencing:

```
dotnet run -- identity-export <domain> <file.json>   # freeze, export, unfreeze
dotnet run -- identity-import <file.json> [commit]
```

The export verb unfreezes in a `finally`, so a failed export does not leave the
identity frozen. It logs loudly if the unfreeze itself fails, since that state
needs an operator.

### 3. File format

A top-level JSON array whose first element is the header, then one element per row:

```json
[
  {"kind":"header","formatVersion":1,"exportedAt":1755600000000,
   "identityId":"...","domain":"frodo.dotyou.cloud",
   "identitySchemaVersion":<IdentityMigrator.GetCurrentVersionAsync>,
   "systemSchemaVersion":<SystemMigrator.GetCurrentVersionAsync>,
   "tableVersions":{"identity":{"Drives":202608040942,"Circle":202608040942},
                    "system":{"Registrations":...,"Certificates":...,
                              "DkimKeys":...}}},
  {"kind":"row","db":"system","table":"Registrations","data":{ }},
  {"kind":"row","db":"identity","table":"Drives","data":{ }}
]
```

One valid JSON document, so `jq` works on it. It streams in both directions with
stock APIs: `Utf8JsonWriter` out, `JsonSerializer.DeserializeAsyncEnumerable` in.

The `db` discriminator is what the consumer switches on to pick which database
object to dispatch to: `"system"` calls `systemDb.ImportRowAsync(table, data)`,
`"identity"` calls `identityDb.ImportRowAsync(table, data)`. Each generated
aggregate only knows its own tables, so the discriminator lives in the file rather
than being inferred from the table name.

System rows are written before identity rows, so a truncated file fails on the
registration rather than leaving orphaned identity data.

The prettier alternative, `{"tables":{"Drives":[...]}}`, was rejected: it forces
either a whole-document `JsonDocument.Parse` on import or manual `Utf8JsonReader`
refill plumbing. `DriveMainIndex` alone carries `hdrFileMetaData`, `hdrAppData`,
and `hdrTransferHistory` for every file the identity owns, so whole-document
parsing is not safe to assume.

`rowId` **is** present in each `data` object. It is informational, useful for
diffing an export against its source, and ignored on import.

### 4. Ordering and rowId

`rowId` is not carried across. It is an autoincrement surrogate, it is referenced
by no other table, and it never reaches a client DTO on its own; the only place it
travels is `TimeRowCursor`, where it is the tiebreaker in a composite
`(time, rowId)` sort, not the primary ordering key. Nothing persists a
`QueryBatchCursor` server-side, and follower paging uses domain-string cursors.

Carrying it would also require a `setval` fixup per table on Postgres, since an
explicit insert into a `BIGSERIAL` leaves the sequence behind. Getting that wrong
means a primary-key collision on the target's next insert.

The mitigation is ordering, not copying. Export reads `ORDER BY rowId ASC` and
import inserts in stream order, so the target assigns new `rowId`s in the same
relative sequence as the source. Ordering is preserved; only the absolute values
differ.

Residual risk, accepted and worth stating in release notes: a client holding a
cursor from before the migration could mis-page, and only across rows sharing an
identical millisecond timestamp. The answer is for clients to resync after a
migration, not to pin surrogate keys.

### 5. Import filtering

The export file is a complete snapshot. Some of what it contains is transient
state that describes the *source system's* in-flight work, not the identity, and
replaying it on the target ranges from useless to actively broken. The exporter
does not decide this. The importer does.

**Default skip list**, a single named constant in `IdentityJsonImporter` with a
rationale comment per entry:

| Table | Default | Why |
|---|---|---|
| `Inbox` | skip | Rows reference **staged files in the inbox folder**, which are temp state and out of scope. Importing them guarantees the `File does not exist <inbox key>` failure documented in `DataImporter.cs` and reproduced in `2026-06-03-drive-storage-backend-unification-design.md`. No payload copy fixes this. |
| `Nonce` | skip | Short-lived auth nonces. None will still be valid by import time. This is the one table `DataImporter` already special-cases. |
| `Outbox` | skip | Rows reference long-term files that *are* exported, so replay is structurally sound once payloads land. Skipped by default because the tool cannot verify payloads are present and cannot know whether the source is still live. |

`Outbox` is the entry most likely to be flipped and the reasoning is the weakest,
so it gets a flag rather than a hard rule. Two mitigations were verified and make
replay less alarming than it first looks: stale checkouts self-heal
(`TableOutbox.RecoverCheckedOutDeadItemsAsync`, driven from `PeerOutbox.cs:70`;
`TableInbox.PopRecoverDeadAsync` is the equivalent), and the receive path resolves
by `GlobalTransitId` before writing (`PeerFileWriter.cs:145`), so re-delivering a
file updates the recipient's existing copy rather than duplicating it. That lookup
is verified; whether every downstream side effect is idempotent is **not** —
notifications and read receipts may still double up. On a decommissioning move,
replaying the outbox is arguably the correct behaviour, since those messages were
never delivered. On a clone it means two systems sending as the same identity.

`--skip <tables>` and `--include <tables>` override the default. Every skipped
table is logged with its row count, so the operator sees exactly what was dropped
rather than inferring it.

**Not skipped, but call it out in the CLI warning:** `ClientRegistrations` is
preserved, so existing client sessions keep working. That is correct for a move
and wrong for a clone, where two systems would honour the same client tokens.
Preserving it is the right default because a move is the stated use case, but the
operator should be told.

**This is the one place per-table knowledge re-enters `odin-core`, and that is
acceptable** because the default is *import*, not skip. A table added to the
generator flows through untouched; forgetting to consider it means we imported
something we perhaps should not have, which is visible in the log and reversible,
rather than silently dropping a table the way `DataImporter`'s enumeration can.
The failure direction is the safe one, and the list is three named constants
rather than 25 call sites.

Filtering is whole-table. Row-level predicates (skip only *expired* nonces, skip
only *checked-out* outbox rows) are deliberately not built; if a case ever needs
one, the skip list becomes a table-to-predicate map.

### 6. Freezing the identity, and export consistency

These are **two separate problems** and conflating them leads to the wrong fix.

**Problem one: is the snapshot internally consistent?** The export issues one
statement per table, so without care it can capture a half-applied change: a
`DriveMainIndex` row read before a write and its `DriveAclIndex` rows read after.

Solved by a **single read transaction at `RepeatableRead`** on the source, so all
23 tables come from one pinned snapshot. Concurrent writes are then invisible to
the export and are *not* a consistency problem. The isolation level must be passed
explicitly: `BeginStackedTransactionAsync` defaults to `IsolationLevel.Unspecified`
(`ScopedConnectionFactory.cs:135`), and on Postgres that resolves to the server
default of `READ COMMITTED`, which takes a **fresh snapshot per statement**. That
`Unspecified` resolves to `READ COMMITTED` is standard Npgsql behaviour rather than
something verified in this repo; what is verified is that the parameter defaults to
`Unspecified` and that the API accepts an override. The cost is a longer-lived
snapshot holding up vacuum for the duration, acceptable for a one-off operator
action on a single identity.

**Problem two: are writes lost between the export and the cutover?** This is the
one that actually requires stopping work, and the existing machinery does not do
it.

Verified:

- `ToggleDisabled` (`FileSystemIdentityRegistry.cs:384`) only sets `reg.Disabled`
  and saves the registration. It never stops anything.
- `Disabled` is enforced in exactly one place, `MultiTenantContainerMiddleware.cs:30`.
  It is an inbound-HTTP gate and nothing more.
- `StopBackgroundServices` / `StartBackgroundServices` exist but are **private**
  (`FileSystemIdentityRegistry.cs:663,671`), reachable only through
  `UnloadRegistration` from `DeleteRegistration`.
- Six background workers run per identity, started by
  `StartTenantBackgroundServices` (`BackgroundServiceExtensions.cs:90`):
  `PeerOutboxProcessorBackgroundService`, `PeerInboxProcessorBackgroundService`,
  `InboxOutboxReconciliationBackgroundService`, `TempFolderCleanUpBackgroundService`,
  `InboxOrphanScanBackgroundService`, `SecurityHealthCheckBackgroundScheduler`.
  **None of them references `Disabled`** — zero occurrences in each file. They keep
  running against a disabled identity.

So a disabled identity is not a still identity. The outbox keeps sending and
updating `DriveTransferHistory`; the inbox keeps draining already-staged items.
Anything they write after the snapshot is lost at cutover.

**Freeze / unfreeze.** A new pair on `IIdentityRegistry`, alongside the existing
`ToggleDisabled`:

```csharp
Task FreezeIdentityAsync(string domain);    // disable + stop the tenant's workers
Task UnfreezeIdentityAsync(string domain);  // restart workers + restore prior disabled state
```

The implementation is thin because the parts exist: `ToggleDisabled` plus the two
private `Start`/`StopBackgroundServices` methods promoted to be reachable from a
public entry point. `UnfreezeIdentityAsync` restores the identity's **previous**
disabled state rather than blindly enabling, since an identity may have been
disabled for unrelated reasons before the export.

**The export refuses to run against an identity that is not frozen.** This is a
precondition check, not something the exporter does implicitly, so the operator's
cutover window is explicit rather than accidental.

Layering note: freezing needs `IIdentityRegistry` from `Odin.Services`, which
`Odin.Core.Storage` must not reference. So the freeze/unfreeze calls live in the
`Odin.Hosting` CLI command; `IdentityJsonExporter` only receives a flag asserting
the caller has done it.

**Accepted residual risk: server-wide workers.** `StartSystemBackgroundServices`
runs six more workers that are not tenant-scoped, so a tenant freeze does not stop
them. Two touch data we export:

- `UpdateCertificatesBackgroundService` writes the `Certificates` table for all
  domains. A renewal during the migration window is lost, and self-heals when the
  target renews on its own.
- `JobRunnerBackgroundService` runs jobs from the system `Jobs` table, which can
  touch any tenant's data.

Stopping these per-identity would mean a job-runner exclusion and a cert-renewal
skip, which is a materially bigger change than the export itself. Deliberately not
built. Recorded here so it is a known gap rather than an oversight.

### 7. Versioning

Two independent version concepts, deliberately not merged:

**`formatVersion`** describes the envelope: the header fields, the `kind`/`db`/
`table`/`data` row shape. It changes only when the file layout changes. A binary
that reads `formatVersion` newer than it understands refuses.

**`tableVersions`** describes the schema each table was exported from. This is the
one requirement 10 is about.

Per-table rather than per-database, and it needs no new machinery on either side.
The generator already stamps a version into the table itself: the emitted
`CREATE TABLE` carries `-- { "Version": 202608040942 }` on SQLite and a matching
`COMMENT ON TABLE` on Postgres. `SqlHelper.GetTableVersionAsync(cn, tableName)`
(`SqlExtensions.cs:200`) reads it back, and `MigrationBase.CheckSqlTableVersion`
already uses exactly this to gate migrations.

So:

- **Export** calls `GetTableVersionAsync` per table and records the result in the
  header's `tableVersions` map.
- **Import** calls it per table on the target and compares.

**The rule is all-or-nothing, and it is total.** Not one row from any table is
imported unless *every* table version matches. There is no partial import, no
per-table fallback, and no "the tables I care about are fine" escape hatch.

Total means the table **sets** must be identical too, not merely the versions of
whatever the file happens to contain. Three distinct failures, all treated the
same way:

| Condition | Result |
|---|---|
| A table in the header is absent from the target | refuse |
| A table on the target is absent from the header | refuse |
| A table present in both has a different version | refuse |

The check runs before a single row is written, and the error lists **every**
difference in all three categories rather than the first, so one run tells the
operator the full extent of the drift instead of making them re-run to discover the
next problem.

This is deliberately strict, and it matches existing precedent:
`Sqlite2Pg.ImportIdentityAsync` already refuses outright with "Both databases must
be at the same schema version before importing data." Requirement 10 is the same
rule at finer granularity. The consequence is that you cannot import an older
export into a newer system, which is correct for this tool: reshaping data across
schema generations is a migration, and migrations are `AbstractMigrator`'s job, not
this one's. The operator's path is to bring both systems to the same version first.

**The skip list does not exempt a table from the version check.** Skipping `Outbox`
on import is a decision about replaying rows, not about schema compatibility. The
file still contains those rows and a later re-import with `--include Outbox` must
be valid, so `Outbox`'s version must match like any other.

`-1` means the table carries no embedded version, which `MigrationBase` treats as
legacy version 0. It compares like any other value: `-1` on both sides matches,
`-1` against a real version does not.

Why per-table and not just the migrator's database-level version: `GetCurrentVersionAsync`
returns a single number for the whole database, so two databases can report the
same number while an individual table differs, for instance after a partially
applied or hand-repaired migration. That is precisely the case where a
column-by-column import writes into the wrong shape. The database-level numbers are
still recorded in the header for a cheap fast-fail and for human inspection, but
`tableVersions` is the authoritative check.

### 8. Error handling

- **Table version mismatch** between the header and the target: refuse, listing
  every table that differs. Import runs `IdentityMigrator.MigrateAsync()` on the
  fresh target first so it is at the latest schema, then compares, mirroring
  `Sqlite2Pg.ImportIdentityAsync`. See **Versioning**.
- **Identity already present** on the target: refuse. See **Import preconditions**
  immediately below; the existing check in `Sqlite2Pg` is not sufficient on
  Postgres.
- **Unknown table name** in the file: refuse. An older binary must not silently
  drop rows from a newer export.
- **`formatVersion` newer than the binary understands**: refuse.
- **Dry run by default.** Everything runs inside one stacked transaction on the
  target and rolls back unless `commit` is passed, matching `DataImporter`.

One transaction for the whole import is the correct choice for atomicity. For a
very large identity on Postgres this is a long-lived transaction; that is accepted
for now and called out here so it is a known property rather than a surprise.

**Import preconditions.** All of these run before a single row is written, and any
one of them failing aborts with a message naming what collided:

1. `Registrations` contains no row where `identityId` equals the header's
   `identityId` **or** `primaryDomainName` equals the header's domain
   (case-insensitive). Either collision is a hard stop. This mirrors the existing
   check in `Sqlite2Pg.ImportIdentityAsync`.
2. `Certificates` contains no row for the header's domain. `Certificates` is keyed
   by domain rather than by `identityId`, so it can survive independently. This is
   why `DataImporter.DeleteIdentityFromSystemDataAsync` has to delete both rows to
   restore a retryable state.
3. `DkimKeys` contains no rows for the header's domain. Keyed by
   `(domain, selector)`, so it survives the registration for the same reason
   `Certificates` does, and `DeleteIdentityFromSystemDataAsync` deletes it by
   domain alongside the other two. Several rows per domain rather than one, so the
   check counts rather than testing for a single row, and the message reports the
   count.
4. **The identity tables hold no rows for this `identityId`.** Checks 1 to 3 are
   not sufficient on Postgres, for a reason that is easy to miss.

**Why check 4 exists.** The two backends differ in a way that matters here:
`AddSqliteIdentityDatabaseServices` takes a per-identity `databasePath`, so each
SQLite identity is its own file. `AddPgsqlIdentityDatabaseServices` takes one
shared `connectionString` and passes `identityId` only as a constructor parameter
to scope queries (`IdentityExtensions.cs:46`). **On Postgres every identity lives
in the same physical tables**, partitioned by the `identityId` column.

That matters because `FileSystemIdentityRegistry.DeleteRegistration` deletes the
`Registrations` row, the `Certificates` row, the tenant directory, and the
payloads, but never touches `IdentityDatabase` (`FileSystemIdentityRegistry.cs:206-227`).
On SQLite that is harmless: deleting the tenant directory takes `identity.db` with
it. On Postgres the identity's rows stay in the shared tables after its
registration is gone.

So on a Postgres target, an identity that was previously deleted leaves no trace in
`Registrations` and passes checks 1 to 3, while its old rows are still sitting in
`Drives`, `Circle`, and the rest. That the subsequent import would then collide on
unique constraints such as `Drives(identityId, DriveId)` is an **inference** from
the schema, not something observed; what is verified is that `DeleteRegistration`
leaves those rows behind. Either way the precondition should catch it cleanly
rather than letting the import discover it mid-write.

Check 4 is served by a generated helper, so it stays zero-maintenance and cannot
miss a newly added table:

```csharp
// in IdentityDatabase.Export.Generated.cs
public async Task<long> CountRowsForIdentityAsync(Guid identityId);  // SUM over ExportableTables
```

Out of scope for the importer but worth an operator warning: a leftover
registration directory at `<RegistrationRoot>/<identityId>` also indicates a live
or half-deleted identity. That is a filesystem concern, and payloads are out of
scope, so the CLI warns rather than the importer refusing.

### 9. Testing

- **Round-trip test.** Seed an identity database using the existing
  `DataImporterSeedHelper.cs`, export to JSON, import into a fresh database,
  compare row sets table by table with `rowId` excluded. Run against both SQLite
  and Postgres, as the rest of the storage tests do.
- **Coverage test that replaces the grep hack.** Assert
  `IdentityDatabase.ExportableTables` matches the table names derived from
  `IdentityDatabase.TableTypes`. This is a real runtime assertion against a
  generated data structure, so `DataImporterTests`' source-scanning approach is
  not repeated. Whether to retire the existing source-scanning tests for
  `DataImporter` itself is left to a follow-up.
- **Timestamp fidelity test.** Assert *every* time-typed column survives a round
  trip unchanged, not just `created` and `modified`. Seed with distinctive past
  values well clear of import-time wall-clock so a clobbered column fails loudly
  rather than landing within a plausible range. This is the regression
  `DataImportPatcher` exists to repair, so it gets an explicit test rather than
  relying on the general round-trip comparison to catch it.
- **Empty-identity test.** An identity with no drives and no files exports and
  imports cleanly.
- **Version tests.** That the header records a version for every exported table.
  That import refuses, writing **zero** rows, in each of the three mismatch cases:
  a table in the header missing from the target, a table on the target missing from
  the header, and a shared table whose version differs. That a single differing
  table blocks the whole import even when every other table matches and the
  database-level version still agrees, which is the case the coarse check misses.
  That a version mismatch on a table the skip list would drop still blocks the
  import. That the error lists **all** differences rather than the first. That `-1`
  matches `-1`. And that a `formatVersion` newer than the binary is refused.
- **Freeze tests.** That `FreezeIdentityAsync` stops the six tenant background
  workers and `UnfreezeIdentityAsync` restarts them; that unfreeze restores a
  previously-disabled identity to disabled rather than enabling it; and that export
  refuses against an identity that is not frozen.
- **Precondition tests.** Five cases, each asserting the import aborts before
  writing anything: a target whose `Registrations` already holds the `identityId`;
  one that holds the domain under different casing; one that holds a leftover
  `Certificates` row for the domain but no registration; one that holds leftover
  `DkimKeys` rows for the domain but no registration; and, on Postgres only, one
  whose identity tables still hold rows for the `identityId` after its registration
  was deleted. The last is the case checks 1 to 3 miss.
- **Filter tests.** Assert the export file contains `Inbox`, `Outbox`, and `Nonce`
  rows even though the default import drops them; that the default skip list is
  honoured; that `--include Outbox` overrides it; and that every skipped table is
  logged with a row count. The first of these is the one that matters, because it
  pins the invariant that filtering is an import concern and never an export one.

### 10. Security

The export contains everything needed to be this identity. `KeyValue` holds
`PasswordData` (`OwnerSecretService.cs`), the ECC key lists and notification keys
(`PublicPrivateKeyService.cs`), and `Drives` holds
`MasterKeyEncryptedStorageKeyJson`. `Certificates` holds the TLS private key, and
`DkimKeys` holds the DKIM signing private keys.

Therefore:

- The exporter creates the file with `0600` and refuses to overwrite an existing
  file.
- The CLI prints an explicit warning naming what the file contains.
- Encryption at rest is the operator's responsibility for now, and this is stated
  in the CLI output rather than left implicit. If we later want it built in, the
  natural seam is an `age`-style recipient passed to the export command.

**One exported secret is not portable, and it is the only such case.**
`DkimKeys.privateKey` is AES-CBC ciphertext under `Email:DkimStorageKey`
(`DkimStore.cs`), which is a **server-wide** config value rather than anything
derived from the identity. Every other secret in the file is either plaintext or
encrypted under a key that travels with the identity, so it survives the move
untouched. DKIM does not: import replays the ciphertext faithfully, but a target
configured with a different `Email:DkimStorageKey` gets rows that are intact and
undecryptable, and `DkimStore` throws the next time the identity signs mail.

The importer cannot detect this. It never holds the storage key, and the key lives
in `Odin.Services` configuration, which `Odin.Core.Storage` must not reference. So
this is documented rather than checked. The operator has two ways out: configure
the target with the same `Email:DkimStorageKey`, or rotate the identity's DKIM keys
on the target and republish the DNS TXT records, which `MailActivationService`
already does. Deliberately not solved here, because both remedies are operational
and the alternative, re-encrypting the column during import, would drag the storage
key down into the storage layer.

## Open follow-ups

Deliberately out of this spec:

- Payload transfer, and an import-side check that expected payload files exist.
- **A portability check for `Email:DkimStorageKey`.** Some `Odin.Hosting`-level
  probe that warns when the target's storage key cannot decrypt the imported
  `DkimKeys` rows, which is the layer that can see both. See **Security**.
- Retiring `DataImportPatcher` once the generated import path proves the
  timestamp handling.
- Migrating `DataImporter` itself onto the generated `ExportRowsAsync` /
  `ImportRowAsync` pair, which would delete its 25-table enumeration, both
  source-scanning tests, and its own `PageSize = 100` paging.
- **Stopping server-wide workers per identity.** A job-runner exclusion and a
  cert-renewal skip for a single domain. See the residual risk in **Freezing the
  identity**.
- **`CopyRegistration` has the same gap.** It calls `ToggleDisabled(domain, true)`
  and copies while the tenant's background workers keep writing
  (`FileSystemIdentityRegistry.cs:229`). Switching it to `FreezeIdentityAsync` is a
  small follow-up once freeze exists, and is not done here to keep this change from
  altering the behaviour of the existing export path.
- **Identity deletion.** `DeleteRegistration` leaving identity rows behind on
  Postgres is one of several known shortcomings in how identities are deleted.
  Fixing that is acknowledged and deliberately deferred. Import precondition 3 is
  a **guard** against the current behaviour, not a fix for it, and it should stay
  in place even after deletion is cleaned up: an import must verify its target is
  empty regardless of how it got that way.
