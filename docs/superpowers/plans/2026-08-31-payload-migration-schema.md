# Payload Migration Schema Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the schema the payload migration needs, one column and one table, on `main` in both repos, so the feature work that follows builds against a merged schema.

**Architecture:** `Odin-SQLite-Generator` gains an `Int32 lifecycleState` column on the existing `Registrations` table and a new non-exportable `PayloadMigration` table. `odin-core` receives the regenerated output, plus the `TenantLifecycleState` enum and the registry mapping that make the new column reachable. No feature behaviour is built here.

**Tech Stack:** .NET 9, NUnit, raw SQL (no ORM), SQLite + PostgreSQL.

**Spec:** `docs/superpowers/specs/2026-08-31-payload-migration-design.md`, sections "Schema changes" and "Sequencing".

**Repos:** two.
- `/workspace/Odin-SQLite-Generator`: Tasks 2 and 3
- `/workspace/odin-core`: Tasks 1, 4, 5, 6 and 7

## Global Constraints

- **No `Co-Authored-By` or `Generated with Claude Code` trailers** in any commit message.
- **Never use em dashes** in code comments, output strings, or documentation.
- **Generated files are never hand-edited.** Every `Table*CRUD.cs`, `*Database.Generated.cs`, `*Migrator.Generated.cs` and file under `Database/*/Migrations/` carries an auto-generated banner. All changes to them come from re-running the generator.
- **Dependencies flow downward only:** `Odin.Hosting` → `Odin.Services` → `Odin.Core.*`. Never reference upward.
- **Migration versions are `YYYYMMDDHHMM`** and must exceed every existing version in their namespace. The current System maximum is `202608201100` (`DkimKeys`).
- **This plan does not use `/` in git branch names.**

## Build and test commands

```bash
# odin-core
cd /workspace/odin-core
dotnet build ./odin-core.sln
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityJsonExport"

# generator
cd /workspace/Odin-SQLite-Generator
ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
```

## Prerequisite, not a task

`odin-core`'s `identity-json-export` branch is unmerged and 26 commits ahead of `main`. The `Registrations` version bump in Task 2 is free only while that is true, because no export file exists yet whose version map it invalidates.

**Merge `identity-json-export` to `odin-core` main before starting Task 1.** Doing the generator round trip first forces a rebase of those 26 commits onto new generated code including a version-map change, for no benefit.

If that merge has not happened, STOP and raise it. Do not start Task 1.

---

### Task 1: Establish the regenerate-produces-no-diff baseline

Without this, you cannot tell your changes apart from pre-existing drift between the committed generated files and what the generator currently produces.

**Files:**
- Modify: none. This task only verifies.

**Interfaces:**
- Consumes: nothing
- Produces: a verified-clean starting tree in both repos

- [ ] **Step 1: Confirm both trees are clean**

```bash
cd /workspace/odin-core && git status --short && git rev-parse --abbrev-ref HEAD
git status --short && git rev-parse --abbrev-ref HEAD
```

Expected: no output from either `git status --short`. If either tree is dirty, STOP and report.

- [ ] **Step 2: Run the generator against the unmodified definitions**

```bash
cd /workspace/Odin-SQLite-Generator
ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
```

Expected: runs to completion with no exception.

- [ ] **Step 3: Confirm it produced no diff**

```bash
cd /workspace/odin-core && git diff --stat
```

Expected: **empty**.

If the diff is not empty, STOP. The committed generated files do not match what the current generator produces. Report the diff and ask before continuing.

- [ ] **Step 4: Confirm odin-core builds and the storage tests pass**

```bash
cd /workspace/odin-core
dotnet build ./odin-core.sln
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj
```

Expected: build succeeded, tests pass. Record the passing test count; Task 4 compares against it.

---

### Task 2: Add `lifecycleState` to `Registrations`

**Files:**
- Modify: `/workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs:3357-3455` (the `Registrations()` function)

**Interfaces:**
- Consumes: nothing
- Produces: `RegistrationsRecord.lifecycleState` of type `Int32`, non-null, defaulting to `0`. Consumed by Task 5.

- [ ] **Step 1: Read the current definition**

```bash
sed -n '3357,3370p' /workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs
```

Confirm `migrationVersion = 202607101000` and that no `exportScopeColumn` is set, so it inherits the `"identityId"` default and stays exportable. Both facts matter: the version bump is what generates the migration, and the table staying exportable is deliberate.

- [ ] **Step 2: Bump the migration version**

Replace `migrationVersion = 202607101000,` with:

```csharp
            migrationVersion = 202608311200,
```

- [ ] **Step 3: Add the column**

Find `t.FinallyAddCreatedModified();` inside `Registrations()` (near line 3444) and insert immediately **before** it:

```csharp
        // Tenant lifecycle, distinct from `disabled`. 0 Active | 1 MigratedAway.
        // `disabled` means an admin suspended this tenant; this means the tenant is
        // mid-migration. One bit cannot carry both, which is why this is a separate
        // column and not a reuse of `disabled`.
        // The default is what lets the migration land on existing rows with no backfill.
        t.AddColumn(new Column()
        {
            name = "lifecycleState",
            type = Types.Int32,
            notNull = true,
            unique = false,
            defaultValue = "0"
        });
```

Column order matters only in that it must precede `FinallyAddCreatedModified()`, which appends `created` and `modified` and must stay last.

- [ ] **Step 4: Regenerate**

```bash
cd /workspace/Odin-SQLite-Generator
ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
```

Expected: runs to completion with no exception.

- [ ] **Step 5: Verify what it produced**

```bash
cd /workspace/odin-core && git status --short
```

Expected, and nothing else:
- Modified: `src/core/Odin.Core.Storage/Database/System/Table/TableRegistrationsCRUD.cs`
- Modified: `src/core/Odin.Core.Storage/Database/System/SystemMigrator.Generated.cs` (may be unchanged; the list entry already exists)
- Modified: `src/core/Odin.Core.Storage/Database/System/Migrations/TableRegistrationsMigrationList.cs`
- New: `src/core/Odin.Core.Storage/Database/System/Migrations/TableRegistrationsMigrationV202608311200.cs`

Then confirm the new migration carries the column with its default and copies the old data forward:

```bash
grep -n "lifecycleState" src/core/Odin.Core.Storage/Database/System/Migrations/TableRegistrationsMigrationV202608311200.cs
grep -n "CopyDataAsync" -A 12 src/core/Odin.Core.Storage/Database/System/Migrations/TableRegistrationsMigrationV202608311200.cs
```

Expected: `lifecycleState BIGINT NOT NULL DEFAULT 0` in the CREATE TABLE, and a `CopyDataAsync` whose INSERT column list contains the pre-existing columns but **not** `lifecycleState`, so existing rows take the default.

- [ ] **Step 6: Confirm the migration list chains correctly**

```bash
cat src/core/Odin.Core.Storage/Database/System/Migrations/TableRegistrationsMigrationList.cs
```

Expected: a new final entry `new TableRegistrationsMigrationV202608311200(202607101000),` whose constructor argument is the previous version.

- [ ] **Step 7: Build**

```bash
cd /workspace/odin-core && dotnet build ./odin-core.sln
```

Expected: build succeeded. `SaveRegistrationInternal` in `FileSystemIdentityRegistry.cs` does not set `lifecycleState`, which compiles because the record field is a plain property; Task 5 supplies it.

- [ ] **Step 8: Commit the generator change**

```bash
cd /workspace/Odin-SQLite-Generator
git add Odin-SQLite-Generator/Program.cs
git commit -m "Add Registrations.lifecycleState

Tenant lifecycle state, distinct from disabled. One bit cannot mean both
an admin suspended this tenant and this tenant is mid-migration.

Defaults to 0 (Active) so the migration lands on existing rows without a
backfill."
```

Leave the regenerated `odin-core` files uncommitted; Task 4 commits them in one piece.

---

### Task 3: Add the `PayloadMigration` table

**Files:**
- Modify: `/workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs` (new function beside `DkimKeys()` at 3156, and a registration line near 6300)

**Interfaces:**
- Consumes: nothing
- Produces: `TablePayloadMigration` with `PayloadMigrationRecord`, in namespace `Odin.Core.Storage.Database.System.Table`. Non-exportable, so it emits no `ExportRowsAsync` or `ImportRowAsync` and stays out of `SystemDatabase.ExportableTables`. Asserted by Task 6.

- [ ] **Step 1: Read the two patterns you are copying**

```bash
sed -n '3156,3172p' /workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs
sed -n '3455,3470p' /workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs
```

The first is `DkimKeys()`, a recent System table. The second is `Settings()`, the existing System table that sets `exportScopeColumn = null`. The new table takes its header shape from the first and its export exclusion from the second.

- [ ] **Step 2: Add the table definition**

Insert immediately before `private static Table Certificates()` (near line 3230):

```csharp
    // Payload migration bookkeeping, one row per identity being migrated. A source host
    // populates the handoff columns and a target host populates the drain columns; each
    // leaves the other half null.
    //
    // Deliberately non-exportable. A cursor, a failure list and a bearer credential are
    // job state, not registration data, and exporting them would carry one host's
    // migration bookkeeping into every future export of the identity.
    //
    // No column defaults: the table is new, so there are no pre-existing rows for a
    // default to serve, and every row is written by code that sets what it needs.
    private static Table PayloadMigration()
    {
        var t = new Table()
        {
            migrationVersion = 202608311201,
            prettyName = SystemPrettyName,
            tableName = "PayloadMigration",
            connectionFactory = "ScopedSystemConnectionFactory",
            folderName = OdinSystemFolder,
            nameSpace = OdinCoreSystemNamespace,
            exportScopeColumn = null,
        };

        t.AddRowIdColumn();

        t.AddColumn(new Column()
        {
            name = "identityId",
            type = Types.Guid,
            notNull = true,
            unique = true
        });

        // Source side: what the migration endpoint checks an incoming bearer against.
        t.AddColumn(new Column()
        {
            name = "handoffTokenHash",
            type = Types.String,
            minLength = 0,
            maxLength = 128,
            notNull = false,
            unique = false
        });

        t.AddColumn(new Column()
        {
            name = "handoffTokenExpiry",
            type = Types.UnixTimeUtc,
            notNull = false,
            unique = false
        });

        t.AddColumn(new Column()
        {
            name = "handoffConsumed",
            type = Types.Boolean,
            notNull = false,
            unique = false
        });

        t.AddColumn(new Column()
        {
            name = "drainCredentialHash",
            type = Types.String,
            minLength = 0,
            maxLength = 128,
            notNull = false,
            unique = false
        });

        // Target side: what the drain worker resumes from.
        t.AddColumn(new Column()
        {
            name = "sourceBaseUrl",
            type = Types.String,
            minLength = 0,
            maxLength = 512,
            notNull = false,
            unique = false
        });

        t.AddColumn(new Column()
        {
            name = "drainCredential",
            type = Types.String,
            minLength = 0,
            maxLength = 512,
            notNull = false,
            unique = false
        });

        // The highest DriveMainIndex.rowId present at import. The cursor walks down from
        // here, so rows created after cutover are excluded without a date comparison.
        t.AddColumn(new Column()
        {
            name = "startRowId",
            type = Types.Int64,
            notNull = false,
            unique = false
        });

        t.AddColumn(new Column()
        {
            name = "cursorRowId",
            type = Types.Int64,
            notNull = false,
            unique = false
        });

        // 0 Pending | 1 Draining | 2 Throttled | 3 Complete | 4 Failed
        t.AddColumn(new Column()
        {
            name = "status",
            type = Types.Int32,
            notNull = false,
            unique = false
        });

        // JSON array of objects that failed to transfer. A throttled object is not a
        // failure and never appears here.
        t.AddColumn(new Column()
        {
            name = "failures",
            type = Types.String,
            minLength = 0,
            maxLength = 65535,
            notNull = false,
            unique = false
        });

        t.FinallyAddCreatedModified();

        t.primaryKey = [t.ColumnIndex("identityId")];

        t.pagingGet = new myPaging[] { new myPaging() { cursor = t.ColumnIndex("rowId"), fixedKeys = new int[] { } } };

        t.AddDefaults();

        return t;
    }
```

- [ ] **Step 3: Register the table**

Confirm the surrounding block first:

```bash
sed -n '6296,6316p' /workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs
```

Expected: a run of two-line pairs ending with `table = Settings(); GenerateCode(table);` and then a closing brace. Insert immediately after the `Settings()` pair and before that closing brace:

```csharp
        table = PayloadMigration();
        GenerateCode(table);
```

Nothing else registers a table. There is no separate list to add to; `GenerateCode` is what puts it into the namespace group that the aggregate and migrator files are built from.

- [ ] **Step 4: Regenerate**

```bash
cd /workspace/Odin-SQLite-Generator
ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
```

Expected: runs to completion with no exception. If it throws about `exportScopeColumn`, the validation at `Program.cs:6130` is telling you the scope column is not a real column on the table; `null` should bypass it, so re-check Step 2.

- [ ] **Step 5: Verify the table is non-exportable**

```bash
cd /workspace/odin-core
grep -n "PayloadMigration" src/core/Odin.Core.Storage/Database/System/SystemDatabase.Export.Generated.cs
grep -c "ExportRowsAsync\|ImportRowAsync" src/core/Odin.Core.Storage/Database/System/Table/TablePayloadMigrationCRUD.cs
```

Expected: the first command prints **nothing**, and the second prints **0**. Both are the point of the task. If either fails, `exportScopeColumn = null` did not take effect.

- [ ] **Step 6: Verify the migration was created**

```bash
ls src/core/Odin.Core.Storage/Database/System/Migrations/ | grep PayloadMigration
grep -n "PayloadMigration" src/core/Odin.Core.Storage/Database/System/SystemMigrator.Generated.cs
```

Expected: a `TablePayloadMigrationMigrationList.cs` and a `TablePayloadMigrationMigrationV202608311201.cs`, and a `new TablePayloadMigrationMigrationList(),` entry in the migrator.

- [ ] **Step 7: Build**

```bash
cd /workspace/odin-core && dotnet build ./odin-core.sln
```

Expected: build succeeded.

- [ ] **Step 8: Commit the generator change**

```bash
cd /workspace/Odin-SQLite-Generator
git add Odin-SQLite-Generator/Program.cs
git commit -m "Add the PayloadMigration system table

One row per identity being migrated. A source host fills the handoff
columns and a target host fills the drain columns.

Non-exportable on purpose: a cursor, a failure list and a bearer
credential are job state, not registration data, and exporting them would
carry one host's bookkeeping into every future export of the identity."
```

---

### Task 4: Land the regenerated files in odin-core

**Files:**
- Modify: everything the generator wrote under `src/core/Odin.Core.Storage/Database/System/`

**Interfaces:**
- Consumes: the generated output of Tasks 2 and 3
- Produces: a building, passing `odin-core` tree with both schema changes

- [ ] **Step 1: Review the full generated diff before committing it**

```bash
cd /workspace/odin-core && git status --short && git diff --stat
```

Expected: changes confined to `src/core/Odin.Core.Storage/Database/System/`. Anything outside that path is unexpected. If you see changes under `Database/Identity/`, STOP and report; neither task touches identity tables.

- [ ] **Step 2: Confirm the migrations run on SQLite**

```bash
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj
```

Expected: passes, with a count equal to Task 1 Step 4's baseline. A failure here most likely means the migration chain is broken, so read the failing test's message before changing anything.

- [ ] **Step 3: Commit**

```bash
cd /workspace/odin-core
git add src/core/Odin.Core.Storage/Database/System/
git commit -m "Regenerate: Registrations.lifecycleState and the PayloadMigration table

Generated output for the two generator changes. No hand edits."
```

---

### Task 5: The `TenantLifecycleState` enum and the registry mapping

Without this the new column is unreachable from `odin-core` and `SaveRegistrationInternal` silently writes the default on every upsert.

**Files:**
- Create: `src/services/Odin.Services/Registry/TenantLifecycleState.cs`
- Modify: `src/services/Odin.Services/Registry/IdentityRegistration.cs`
- Modify: `src/services/Odin.Services/Registry/FileSystemIdentityRegistry.cs` (the `RegistrationsRecord` upsert near line 357, and the record-to-model mapping near line 482)
- Test: `tests/services/Odin.Services.Tests/Registry/TenantLifecycleStateTests.cs`

**Interfaces:**
- Consumes: `RegistrationsRecord.lifecycleState` from Task 2
- Produces: `TenantLifecycleState` enum with members `Active = 0`, `MigratedAway = 1`, `Freezing = 2`, `Frozen = 3`; `IdentityRegistration.LifecycleState` of that type

- [ ] **Step 1: Write the failing test**

Create `tests/services/Odin.Services.Tests/Registry/TenantLifecycleStateTests.cs`:

```csharp
using NUnit.Framework;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Registry;

public class TenantLifecycleStateTests
{
    // The column default is 0, set in the generator. If Active stops being 0, every
    // existing row silently means something else.
    [Test]
    public void Active_IsZero_MatchingTheColumnDefault()
    {
        Assert.That((int)TenantLifecycleState.Active, Is.EqualTo(0));
    }

    // Freezing and Frozen are declared but unused. Live export adds behaviour for them
    // later; naming them now means that work adds values rather than changing signatures.
    [Test]
    public void FreezeStates_AreDeclared()
    {
        Assert.That((int)TenantLifecycleState.MigratedAway, Is.EqualTo(1));
        Assert.That((int)TenantLifecycleState.Freezing, Is.EqualTo(2));
        Assert.That((int)TenantLifecycleState.Frozen, Is.EqualTo(3));
    }

    // IdentityRegistration is a plain class with an implicit parameterless constructor
    // (src/services/Odin.Services/Registry/IdentityRegistration.cs:34), so this compiles
    // without any test scaffolding.
    [Test]
    public void NewRegistration_DefaultsToActive()
    {
        var registration = new IdentityRegistration();
        Assert.That(registration.LifecycleState, Is.EqualTo(TenantLifecycleState.Active));
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```bash
cd /workspace/odin-core
dotnet test ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj --filter "FullyQualifiedName~TenantLifecycleState"
```

Expected: compile failure, `TenantLifecycleState` does not exist.

- [ ] **Step 3: Add the enum**

Create `src/services/Odin.Services/Registry/TenantLifecycleState.cs`:

```csharp
namespace Odin.Services.Registry;

/// <summary>
/// Where a tenant sits in its lifecycle on THIS host. Distinct from
/// <see cref="IdentityRegistration.Disabled"/>, which means an admin suspended the
/// tenant. One bit cannot carry both meanings.
/// </summary>
public enum TenantLifecycleState
{
    /// Normal. The host serves this tenant.
    Active = 0,

    /// Exported away. The host does not serve this tenant and must refuse to delete
    /// its registration, because deleting purges the payload prefix that has not
    /// finished draining to the target.
    MigratedAway = 1,

    /// Declared, not yet implemented. Live export sets these once workers observe the
    /// state at write boundaries and a freeze can be acknowledged across hosts. See
    /// section 13 of the payload migration design.
    Freezing = 2,
    Frozen = 3,
}
```

- [ ] **Step 4: Add the property to the model**

In `src/services/Odin.Services/Registry/IdentityRegistration.cs`, beside the existing `Disabled` property (near line 67):

```csharp
        /// <summary>
        /// Lifecycle state on this host. Independent of <see cref="Disabled"/>: a tenant
        /// can be both suspended and mid-migration.
        /// </summary>
        public TenantLifecycleState LifecycleState { get; set; } = TenantLifecycleState.Active;
```

- [ ] **Step 5: Run the test again**

```bash
dotnet test ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj --filter "FullyQualifiedName~TenantLifecycleState"
```

Expected: PASS.

- [ ] **Step 6: Map it on write**

In `FileSystemIdentityRegistry.SaveRegistrationInternal` (near line 357), add to the `new RegistrationsRecord { ... }` initialiser, after `enablePublicWebPresence`:

```csharp
            lifecycleState = (int)registration.LifecycleState
```

Remember to add a comma to the line above it.

- [ ] **Step 7: Map it on read**

In `FileSystemIdentityRegistry`, find the record-to-model mapping containing `Disabled = registrationRecord.disabled,` (near line 482) and add beneath it:

```csharp
                    LifecycleState = (TenantLifecycleState)registrationRecord.lifecycleState,
```

- [ ] **Step 8: Write the round-trip test**

Append to `tests/services/Odin.Services.Tests/Registry/TenantLifecycleStateTests.cs`, inside the class:

```csharp
    // Guards the two mapping points in FileSystemIdentityRegistry. A cast that silently
    // drops the value would leave every loaded registration Active regardless of the row.
    [Test]
    public void EnumRoundTripsThroughTheColumnRepresentation()
    {
        foreach (var state in System.Enum.GetValues<TenantLifecycleState>())
        {
            var stored = (int)state;
            Assert.That((TenantLifecycleState)stored, Is.EqualTo(state));
        }
    }
```

- [ ] **Step 9: Build and run the full storage and services suites**

```bash
cd /workspace/odin-core
dotnet build ./odin-core.sln
dotnet test ./tests/services/Odin.Services.Tests/Odin.Services.Tests.csproj --filter "FullyQualifiedName~TenantLifecycleState"
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj
```

Expected: build succeeded, all pass.

- [ ] **Step 10: Commit**

```bash
git add src/services/Odin.Services/Registry/ tests/services/Odin.Services.Tests/Registry/
git commit -m "Map Registrations.lifecycleState onto IdentityRegistration

Declares the full state machine, including the Freezing and Frozen values
that live export will set, so that work adds values rather than changing
signatures.

Active is pinned to 0 by test, because that is the column default and a
change would silently reinterpret every existing row."
```

---

### Task 6: Pin the export surface

`PayloadMigration` being non-exportable is now load bearing rather than incidental. The existing `ExportCoverageTests` only covers `IdentityDatabase`, so nothing currently asserts anything about the System export surface.

**Files:**
- Modify: `tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/ExportCoverageTests.cs`

**Interfaces:**
- Consumes: `SystemDatabase.ExportableTables` from Task 3's generated output
- Produces: nothing later tasks depend on

- [ ] **Step 1: Write the failing test**

Add to `ExportCoverageTests`, and add `using Odin.Core.Storage.Database.System;` to the file's usings:

```csharp
    // The System export surface is curated, not "every table". PayloadMigration holds one
    // host's migration bookkeeping, and exporting it would carry a cursor, a failure list
    // and a bearer credential into every future export of the identity. This test fails if
    // someone gives that table an exportScopeColumn.
    [Test]
    public void SystemExportableTables_IsExactlyTheCuratedSet()
    {
        var expected = new[] { "Certificates", "DkimKeys", "Registrations" };

        Assert.That(SystemDatabase.ExportableTables.OrderBy(n => n).ToList(),
            Is.EqualTo(expected.OrderBy(n => n).ToList()),
            "The System export surface changed. If a table was added deliberately, the "
            + "export file format and its version map changed with it.");
    }
```

- [ ] **Step 2: Run it**

```bash
cd /workspace/odin-core
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~ExportCoverageTests"
```

Expected: PASS immediately. This test documents an invariant that Task 3 already satisfies rather than driving new code, so a failure here means Task 3's `exportScopeColumn = null` did not take effect. If it fails, go back to Task 3 Step 5.

- [ ] **Step 3: Prove the test can fail**

Temporarily edit the expected array to add `"PayloadMigration"`, re-run, confirm it FAILS, then revert the edit and re-run to confirm it passes again. A test that cannot fail is not a test.

- [ ] **Step 4: Confirm the export format is genuinely unchanged**

```bash
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityJsonExport"
```

Expected: all pass, including the round-trip tests. The version map in the export header must be unaffected by this plan, and these are the tests that would notice.

- [ ] **Step 5: Commit**

```bash
git add tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/ExportCoverageTests.cs
git commit -m "Pin the System export surface to its curated set

PayloadMigration being non-exportable is load bearing: exporting it would
carry one host's migration bookkeeping into every future export of the
identity. Nothing asserted anything about the System surface before."
```

---

### Task 7: Land both repos on main

**Files:**
- Modify: none

**Interfaces:**
- Consumes: Tasks 2 through 6
- Produces: the merged schema that the payload migration feature plan builds against

- [ ] **Step 1: Confirm both trees are clean and everything passes**

```bash
cd /workspace/odin-core && git status --short && dotnet build ./odin-core.sln && dotnet test ./odin-core.sln
cd /workspace/Odin-SQLite-Generator && git status --short && dotnet build ./Odin-SQLite-Generator.sln
```

Expected: no uncommitted changes, build succeeded, tests pass. Report any failing tests with their output rather than proceeding.

- [ ] **Step 2: Check `docs/flakytests.md` before blaming yourself for a red test**

```bash
cd /workspace/odin-core && cat docs/flakytests.md
```

If a failing test is listed there, confirm it is pre-existing by running it on a clean tree (`git stash`) and record that evidence. If you find a new flaky test, add an entry.

- [ ] **Step 3: Ask before merging**

Both merges are outward-facing and are the user's call. Report what is ready, on which branches, and ask which merge route they want (direct merge, or a PR per repo). Do not merge or push without an explicit answer.

---

## Out of scope

Everything in the design document's Design sections. This plan lands schema only. The feature plan that follows covers the source lifecycle enforcement, the migration endpoint, tokens, the drain worker, throttling, the 404 behaviour, and completion.

The four items in the design's "To verify before implementing" are also out of scope here. They gate the feature plan, not the schema, and the first of them (that no code path overwrites a payload object in place) can invalidate the cursor design, so it should be the feature plan's first task.
