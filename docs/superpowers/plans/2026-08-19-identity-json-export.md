# Identity JSON Export / Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Export one identity's database tables to a single JSON file and import that file into another system, with the per-table read/write code generated so `odin-core` needs no per-table maintenance.

**Architecture:** `Odin-SQLite-Generator` emits an `ExportRowsAsync` and an `ImportRowAsync` into every `Table*CRUD.cs`, plus a per-namespace aggregate (`IdentityDatabase.Export.Generated.cs`) that unrolls calls across all tables and exposes a table-name-to-record-type map. `odin-core` adds exactly two files that know the file format, a freeze/unfreeze pair on `IIdentityRegistry`, and two CLI verbs. Generated code deals in `object` and `Type` only; all JSON lives in `odin-core`.

**Tech Stack:** .NET 9, NUnit, Autofac, raw SQL (no ORM), `System.Text.Json`, SQLite + PostgreSQL.

**Spec:** `docs/superpowers/specs/2026-08-19-identity-json-export-design.md`

**Repos:** two, both on branch `identity-json-export`:
- `/workspace/Odin-SQLite-Generator` — Tasks 1-6
- `/workspace/odin-core` — Tasks 7-12

## Global Constraints

- **Branch:** `identity-json-export` on both repos. Never use `/` in a git branch name.
- **No `Co-Authored-By` or `Generated with Claude Code` trailers** in any commit message.
- **Never use em dashes** in code comments, output strings, or documentation.
- **Dependencies flow downward only:** `Odin.Hosting` → `Odin.Services` → `Odin.Core.*`. Never reference upward.
- **Generated files are never hand-edited.** Every `Table*CRUD.cs`, `*Database.Generated.cs`, and `*Migrator.Generated.cs` carries `// THIS FILE IS AUTO GENERATED - DO NOT EDIT`. All changes to them come from re-running the generator.
- **Generated code must contain no JSON types.** No `Utf8JsonWriter`, `JsonElement`, `JsonSerializer`, or `System.Text.Json` using-directive in any generated file. The aggregate deals in `object` and `Type`.
- **Import writes timestamps as-is.** No generated import path may use `{sqlNowStr}`.
- **Import is all-or-nothing on schema version.** Zero rows written unless every table version matches.
- **Test framework is NUnit.** Postgres cases go behind `#if RUN_POSTGRES_TESTS`, matching `DataImporterEndToEndTests`.
- **`rowId` is never carried across.** Export selects it (for ordering and for the file's informational copy); import never inserts it.

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

---

# Phase A: Generator (`/workspace/Odin-SQLite-Generator`)

The generator has no test suite. Its verification loop is **regenerate, then diff**: a change that should not alter output must produce an empty `git diff` in `odin-core`, and a change that should alter output must produce exactly the expected diff. Task 1 establishes that loop before anything else changes, so every later task has a trustworthy baseline.

---

### Task 1: Make the generator runnable outside the four hardcoded machines

`Program.MyRootDirectory` switches on `$USER` and throws `Exception("User not found")` for anyone else. `$USER` is unset in containers and CI, so the generator cannot run at all there. Nothing downstream in this plan works until this does.

**Files:**
- Modify: `/workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs:18-35`

**Interfaces:**
- Consumes: nothing
- Produces: `ODIN_ROOT` environment variable overrides the developer-name switch. `MyRootDirectory` returns a path whose children are `odin-core/` and `homebase-social-sync/`.

- [ ] **Step 1: Read the current property**

Run: `sed -n '18,36p' /workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs`

Confirm it matches the `switch` on `Environment.GetEnvironmentVariable("USER")` shown below before editing.

- [ ] **Step 2: Add the environment variable override**

Replace the body of `MyRootDirectory` so the env var is checked first and the existing switch stays as the fallback:

```csharp
    // Point this to the root of your ODIN project and it'll generate the CRUD.cs files for you
    public static string MyRootDirectory
    {
        get
        {
            // ODIN_ROOT wins when set. Needed for containers and CI, where USER is
            // unset and the developer-name switch below throws.
            var odinRoot = Environment.GetEnvironmentVariable("ODIN_ROOT");
            if (!string.IsNullOrWhiteSpace(odinRoot))
            {
                return odinRoot;
            }

            var result = Environment.GetEnvironmentVariable("USER") switch
            {
                "seb" => Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "code/odin/"),
                "taud" => Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "src/odin/"),
                "todd" => Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "src/odin/"),
                "seifert" => Environment.GetEnvironmentVariable("HOME")?.StartsWith("/home/") == true
                    ? Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "odin")
                    : Path.Combine(@"C:\temp\Git\", ""),
                _ => throw new Exception("User not found. Set ODIN_ROOT to the directory containing odin-core.")
            };

            return result;
        }
    }
```

- [ ] **Step 3: Establish the regenerate-produces-no-diff baseline**

This is the critical verification step for all of Phase A. Run:

```bash
cd /workspace/odin-core && git status --short
cd /workspace/Odin-SQLite-Generator && ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
cd /workspace/odin-core && git diff --stat
```

Expected: the generator runs to completion with no exception, and `git diff --stat` is **empty**.

If the diff is not empty, STOP. Do not proceed to Task 2. A non-empty diff means the committed generated files do not match what the current generator produces, so you cannot tell your changes apart from pre-existing drift. Report the diff and ask before continuing.

- [ ] **Step 4: Confirm odin-core still builds**

Run: `cd /workspace/odin-core && dotnet build ./odin-core.sln`
Expected: build succeeded.

- [ ] **Step 5: Commit (generator repo only)**

```bash
cd /workspace/Odin-SQLite-Generator
git add Odin-SQLite-Generator/Program.cs
git commit -m "Generator: allow ODIN_ROOT to override the developer-name path switch

USER is unset in containers and CI, where the switch threw outright."
```

---

### Task 2: Add `exportScopeColumn` to `Table` and annotate the System tables

A field only. No emitted output changes, so the verification is again an empty diff. Keeping this separate from Task 3 means that if Task 3's diff looks wrong, you know the field itself is not the cause.

**Files:**
- Modify: `/workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs` — `Table` class (around line 291, beside `internalsVisibleTo`), and every table definition outside the Identity namespace: `Certificates()`, `DkimKeys()`, `Jobs()`, `Settings()`, `LastSeen()`, `KeyChain()`, `Notarius()`, `AttestationRequest()`, `AttestationStatus()`, `SocialSyncSubscriber()`, `SubscriberClientTokens()`, `SocialSyncSource()`

**The generator emits for six namespaces, not two.** Only `OdinCoreIdentityNamespace` (23 tables) has an `identityId` column on every table, so only there is the default correct. The other five are `OdinCoreSystemNamespace` (6 tables), `OdinCoreKeyChainNamespace`, `OdinCoreNotaryNamespace`, `OdinCoreAttestationNamespace` (2 tables), and `SocialSyncNamespace` (3 tables, and note these generate into the separate `/workspace/homebase-social-sync` repo). Every table in those five needs an explicit annotation or Step 3's validation throws.

**Interfaces:**
- Consumes: Task 1's working generator
- Produces: `Table.exportScopeColumn` (`string?`, default `"identityId"`). `null` means the table is excluded from identity export. Tasks 3, 4, and 5 all read it.

- [ ] **Step 1: Add the field to the `Table` class**

Add immediately after the `internalsVisibleTo` field:

```csharp
        // Column that scopes a row to a single identity. The default is correct for every
        // table in the Identity namespace. Set to "domain" for Certificates and DkimKeys.
        // Set to null to exclude the table from identity export entirely (Jobs, Settings,
        // LastSeen).
        public string? exportScopeColumn = "identityId";
```

- [ ] **Step 2: Annotate every table outside the Identity namespace**

In each table definition method, add the field to the object initializer. Watch for
initializers whose last entry has no trailing comma (`SocialSyncSource()` is one); add
the comma when inserting after it.

In `Certificates()` and `DkimKeys()`:

```csharp
            exportScopeColumn = "domain",
```

`DkimKeys` holds this identity's DKIM signing keys, one row per selector, primary
key `(domain, selector)`. It is identity-scoped by domain for the same reason
`Certificates` is, so it travels with the identity. Two differences from
`Certificates` matter downstream: its `domain` column is **not** declared `unique`,
so one identity owns several `DkimKeys` rows rather than exactly one, and its
`privateKey` is AES-CBC encrypted under the server-wide `Email:DkimStorageKey`
config value rather than under anything identity-derived. See the operator note in
Task 11.

In `Jobs()`, `Settings()`, and `LastSeen()`, which are system-wide rather than
identity-scoped:

```csharp
            exportScopeColumn = null,
```

Same `null` in `KeyChain()`, `Notarius()`, `AttestationRequest()` and
`AttestationStatus()`. Each of these is a standalone database with its own connection
factory (`ScopedKeyChainConnectionFactory`, `ScopedNotaryConnectionFactory`,
`ScopedAttestationConnectionFactory`), none is per-identity, and none carries an
`identityId` column.

Same `null` again in `SocialSyncSubscriber()`, `SubscriberClientTokens()` and
`SocialSyncSource()`. These generate into `/workspace/homebase-social-sync`, a separate
repo and a separate database, and are not part of an identity at all.

- [ ] **Step 3: Add a fail-fast validation in `GenerateCode`**

A typo in `exportScopeColumn` would otherwise surface as a confusing SQL error at runtime. Add at the top of `GenerateCode(Table table)`, right after the existing `nameSpace` argument check:

```csharp
        if (table.exportScopeColumn != null && !table.columns.Exists(c => c.name == table.exportScopeColumn))
            throw new ArgumentException(
                $"Table {table.tableName}: exportScopeColumn '{table.exportScopeColumn}' is not a column on this table.");
```

- [ ] **Step 4: Verify no output change**

```bash
cd /workspace/Odin-SQLite-Generator && ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
cd /workspace/odin-core && git diff --stat
```

Expected: generator completes without throwing (which proves every `exportScopeColumn` names a real column) and the diff is **empty**.

`/workspace/homebase-social-sync` carries pre-existing drift between its committed
generated files and what the current generator produces, unrelated to this work and
present before Task 1. Any regenerate step in this plan will dirty that repo. Revert it
with `git checkout -- .` after each regenerate, or
resolve the drift separately first.

- [ ] **Step 5: Commit**

```bash
cd /workspace/Odin-SQLite-Generator
git add Odin-SQLite-Generator/Program.cs
git commit -m "Generator: add Table.exportScopeColumn, annotate System tables

Defaults to identityId, which is correct for every Identity table and
only there. Certificates and DkimKeys scope on domain. Jobs, Settings
and LastSeen are system-wide; KeyChain, NotaryChain, Attestation and the
SocialSync tables are separate databases. All excluded. Validated
against the column list at generation time."
```

---

### Task 3: Emit `ExportRowsAsync` into every `Table*CRUD.cs`

**Files:**
- Modify: `/workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs` — new `GenerateExportRows` method, called from `GenerateCode`

**Interfaces:**
- Consumes: `Table.exportScopeColumn` from Task 2
- Produces: on each CRUD class, `internal virtual async Task ExportRowsAsync(<scopeType> <scopeName>, Func<XRecord, Task> onRow)`. Task 5's aggregate calls it. For Identity tables the signature is `ExportRowsAsync(Guid identityId, Func<XRecord, Task> onRow)`; for `Certificates` and `DkimKeys` it is `ExportRowsAsync(OdinId domain, Func<XRecord, Task> onRow)`.

- [ ] **Step 1: Write the generator method**

Add near `GeneratePagingGet`:

```csharp
    // Streams every row for one identity, in rowId order, off a single reader.
    // Deliberately not paged: one statement is O(1) memory per row already, and
    // multiple statements cannot give a consistent read under READ COMMITTED.
    public static void GenerateExportRows(Table table)
    {
        if (table.exportScopeColumn == null)
            return;

        var scope = table.columns[table.ColumnIndex(table.exportScopeColumn)];
        var scopeType = scope.GetTypeStringC();

        Output($"        internal virtual async Task ExportRowsAsync({scopeType} {scope.name}, Func<{table.RecordName()}, Task> onRow)");
        Output("        {");
        Output("            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();");
        Output("            await using var exportCommand = cn.CreateCommand();");
        Output("            {");
        Output($"                exportCommand.CommandText = \"SELECT {string.Join(",", GetTuples(table, "", table.UsesRowId()))} FROM {table.SqlTableName()} \" +");
        Output($"                                            \"WHERE {scope.name} = @{scope.name} ORDER BY rowId ASC;\";");
        Output($"                exportCommand.AddParameter(\"@{scope.name}\", {scope.GetTypeStringDbType()}, {scope.BuildValueAssignment()});");
        Output("                await using var rdr = await exportCommand.ExecuteReaderAsync(CommandBehavior.Default);");
        Output("                while (await rdr.ReadAsync())");
        Output("                {");
        Output("                    await onRow(ReadRecordFromReaderAll(rdr));");
        Output("                }");
        Output("            }");
        Output("        }");
        Output("");
    }
```

- [ ] **Step 2: Call it from `GenerateCode`**

In `GenerateCode(Table table)`, immediately after the existing `GenerateReadRecordAll` block:

```csharp
        GenerateExportRows(table);
```

It must come after `GenerateReadRecordAll`, because the emitted body calls `ReadRecordFromReaderAll`.

- [ ] **Step 3: Regenerate and inspect the diff**

```bash
cd /workspace/Odin-SQLite-Generator && ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
cd /workspace/odin-core && git diff --stat
```

Expected: every `Table*CRUD.cs` in Identity, plus `TableCertificatesCRUD.cs`, `TableDkimKeysCRUD.cs` and `TableRegistrationsCRUD.cs`, gains lines. `TableJobsCRUD.cs`, `TableSettingsCRUD.cs`, and `TableLastSeenCRUD.cs` are **unchanged**, because their `exportScopeColumn` is null.

- [ ] **Step 4: Read one generated method and check it by eye**

Run: `cd /workspace/odin-core && sed -n '/ExportRowsAsync/,/^        }$/p' src/core/Odin.Core.Storage/Database/Identity/Table/TableCircleCRUD.cs`

Expected output, exactly:

```csharp
        internal virtual async Task ExportRowsAsync(Guid identityId, Func<CircleRecord, Task> onRow)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var exportCommand = cn.CreateCommand();
            {
                exportCommand.CommandText = "SELECT rowId,identityId,circleId,circleName,data,AppId,GrantOn,Designation,Emoji FROM Circle " +
                                            "WHERE identityId = @identityId ORDER BY rowId ASC;";
                exportCommand.AddParameter("@identityId", DbType.Binary, identityId);
                await using var rdr = await exportCommand.ExecuteReaderAsync(CommandBehavior.Default);
                while (await rdr.ReadAsync())
                {
                    await onRow(ReadRecordFromReaderAll(rdr));
                }
            }
        }
```

Check specifically: `ORDER BY rowId ASC` is present, and the `SELECT` column list starts with `rowId`.

- [ ] **Step 5: Confirm odin-core builds**

Run: `cd /workspace/odin-core && dotnet build ./odin-core.sln`
Expected: build succeeded. A failure here most likely means a missing `using System;` for `Func<>` in the generated file header; check `GenerateFileHeader`.

- [ ] **Step 6: Commit both repos**

```bash
cd /workspace/Odin-SQLite-Generator
git add Odin-SQLite-Generator/Program.cs
git commit -m "Generator: emit ExportRowsAsync per table

One statement per table, streamed row by row in rowId order. Not paged:
a single reader is already O(1) memory per row, and multiple statements
cannot give a consistent read under READ COMMITTED."

cd /workspace/odin-core
git add src/core/Odin.Core.Storage/Database
git commit -m "Regenerate: add ExportRowsAsync to CRUD tables"
```

---

### Task 4: Emit `ImportRowAsync` into every `Table*CRUD.cs`

The one method where the timestamp bug is prevented. `GenerateInsert` calls `GetTuples(..., sqlNowSubstitute: true)`, which swaps `created` and `modified` for the `{sqlNowStr}` literal. This method passes `false`, so both become real parameters.

**Files:**
- Modify: `/workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs` — new `GenerateImportRow` method, called from `GenerateCode`

**Interfaces:**
- Consumes: `Table.exportScopeColumn` from Task 2
- Produces: on each CRUD class, `internal virtual async Task<int> ImportRowAsync(XRecord item)`, returning rows affected. Task 5's aggregate calls it.

- [ ] **Step 1: Write the generator method**

Add immediately after `GenerateExportRows`:

```csharp
    // Faithful restore of one exported row.
    //
    // Three deliberate differences from GenerateInsert:
    //   - sqlNowSubstitute: false, so created/modified are real parameters carrying
    //     the source values instead of the {sqlNowStr} literal. This is the entire
    //     reason DataImportPatcher had to exist.
    //   - No Validate(). A restore must be faithful, not re-validated: a rule
    //     tightened after a row was written must not make that row unrestorable.
    //   - No rowId. It is a surrogate, referenced by no other table, so the target
    //     assigns its own. Insert order preserves the relative sequence.
    public static void GenerateImportRow(Table table)
    {
        if (table.exportScopeColumn == null)
            return;

        Output($"        internal virtual async Task<int> ImportRowAsync({table.RecordName()} item)");
        Output("        {");
        Output("            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();");
        Output("            await using var importCommand = cn.CreateCommand();");
        Output("            {");
        Output($"                importCommand.CommandText = \"INSERT INTO {table.SqlTableName()} ({string.Join(",", GetTuples(table, "", false))}) \" +");
        Output($"                                            \"VALUES ({string.Join(",", GetTuples(table, "@", false))});\";");

        for (int i = table.BeginAtColumn(); i < table.columns.Count; i++)
        {
            if (table.columns[i].rowid)
                continue;
            Output($"                importCommand.AddParameter(\"@{table.columns[i].name}\", {table.columns[i].GetTypeStringDbType()}, item.{table.columns[i].BuildValueAssignment()});");
        }

        Output("                return await importCommand.ExecuteNonQueryAsync();");
        Output("            }");
        Output("        }");
        Output("");
    }
```

Note both `GetTuples` calls pass `sqlNowSubstitute` as its default `false`, and `includeRowId: false`.

- [ ] **Step 2: Call it from `GenerateCode`**

Immediately after the `GenerateExportRows(table);` line added in Task 3:

```csharp
        GenerateImportRow(table);
```

- [ ] **Step 3: Regenerate**

```bash
cd /workspace/Odin-SQLite-Generator && ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
```

- [ ] **Step 4: Verify no generated import path uses `{sqlNowStr}`**

This is the guard for requirement 7. Run:

```bash
cd /workspace/odin-core
sed -n '/async Task<int> ImportRowAsync/,/^        }$/p' src/core/Odin.Core.Storage/Database/Identity/Table/TableDrivesCRUD.cs
```

Expected: the `INSERT` names `created` and `modified` in the column list, the `VALUES` clause has `@created,@modified`, and **`sqlNowStr` does not appear**. Confirm mechanically:

```bash
sed -n '/async Task<int> ImportRowAsync/,/^        }$/p' src/core/Odin.Core.Storage/Database/Identity/Table/TableDrivesCRUD.cs | grep -c sqlNowStr
```

Expected: `0`

- [ ] **Step 5: Confirm odin-core builds**

Run: `cd /workspace/odin-core && dotnet build ./odin-core.sln`
Expected: build succeeded.

- [ ] **Step 6: Commit both repos**

```bash
cd /workspace/Odin-SQLite-Generator
git add Odin-SQLite-Generator/Program.cs
git commit -m "Generator: emit ImportRowAsync per table

Names every column including created and modified as real parameters, so
imported timestamps survive as-is. No Validate(), no rowId."

cd /workspace/odin-core
git add src/core/Odin.Core.Storage/Database
git commit -m "Regenerate: add ImportRowAsync to CRUD tables"
```

---

### Task 5: Emit the per-namespace export aggregate

**Files:**
- Modify: `/workspace/Odin-SQLite-Generator/Odin-SQLite-Generator/Program.cs` — new `GenerateAllGlobalExportLists` method, called from `Main`
- Creates (generated): `odin-core/src/core/Odin.Core.Storage/Database/Identity/IdentityDatabase.Export.Generated.cs`
- Creates (generated): `odin-core/src/core/Odin.Core.Storage/Database/System/SystemDatabase.Export.Generated.cs`

**Interfaces:**
- Consumes: `ExportRowsAsync` (Task 3), `ImportRowAsync` (Task 4)
- Produces, on `IdentityDatabase`:
  - `public static readonly ImmutableList<string> ExportableTables`
  - `public static readonly ImmutableDictionary<string, Type> ExportableRecordTypes`
  - `public async Task ExportAsync(Guid identityId, Func<string, object, Task> onRow)`
  - `public async Task<int> ImportRowAsync(string tableName, object record)`
  - `public async Task<long> CountRowsForIdentityAsync(Guid identityId)`
  - `public async Task<Dictionary<string, long>> GetTableVersionsAsync()`
- Produces, on `SystemDatabase`: the same, except `ExportAsync(Guid identityId, OdinId domain, Func<string, object, Task> onRow)` and no `CountRowsForIdentityAsync`.

- [ ] **Step 1: Write the generator method**

Model it on the existing `GenerateAllGlobalTableLists`. Add immediately after that method:

```csharp
    private static void GenerateAllGlobalExportLists()
    {
        foreach (var tableGroup in GroupedTablesByNamespace)
        {
            var exportable = tableGroup.Value.FindAll(t => t.exportScopeColumn != null);
            if (exportable.Count == 0)
                continue;

            var t0 = tableGroup.Value[0];
            var pretty = t0.prettyName;
            var (file, path) = GetGeneratedFileAndFolder(t0, $"{pretty}Database.Export.Generated", "");

            _file = new StreamWriter(file, false);

            Output("// THIS FILE IS AUTO GENERATED - DO NOT EDIT");
            Output("");
            Output("using System;");
            Output("using System.Collections.Generic;");
            Output("using System.Collections.Immutable;");
            Output("using System.Threading.Tasks;");
            Output("using Odin.Core.Identity;");
            Output("using Odin.Core.Storage;");        // SqlHelper.GetTableVersionAsync
            Output("using Odin.Core.Storage.Factory;"); // AddParameter
            Output($"using {t0.nameSpace}.Table;");
            Output("");
            Output("#nullable disable");
            Output("");
            Output($"namespace {t0.nameSpace};");
            Output("");
            Output($"public partial class {pretty}Database");
            Output("{");

            // Authoritative table-name list.
            Output("    public static readonly ImmutableList<string> ExportableTables = [");
            foreach (var table in exportable)
                Output($"        \"{table.SqlTableName()}\",");
            Output("    ];");
            Output("");

            // Table name -> record type, so callers deserialize without their own switch.
            Output("    public static readonly ImmutableDictionary<string, Type> ExportableRecordTypes =");
            Output("        new Dictionary<string, Type>");
            Output("        {");
            foreach (var table in exportable)
                Output($"            [\"{table.SqlTableName()}\"] = typeof({table.RecordName()}),");
            Output("        }.ToImmutableDictionary();");
            Output("");

            // Distinct scope columns become the parameter list, in a stable order.
            var scopes = new List<Column>();
            foreach (var table in exportable)
            {
                var c = table.columns[table.ColumnIndex(table.exportScopeColumn!)];
                if (!scopes.Exists(s => s.name == c.name))
                    scopes.Add(c);
            }
            scopes.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var scopeParams = new List<string>();
            foreach (var s in scopes)
                scopeParams.Add($"{s.GetTypeStringC()} {s.name}");

            Output($"    public async Task ExportAsync({string.Join(", ", scopeParams)}, Func<string, object, Task> onRow)");
            Output("    {");
            foreach (var table in exportable)
            {
                var prop = CleanCRUD(table.SqlTableName());
                var scopeName = table.exportScopeColumn;
                Output($"        await {prop}.ExportRowsAsync({scopeName}, async r => await onRow(\"{table.SqlTableName()}\", r));");
            }
            Output("    }");
            Output("");

            Output("    public async Task<int> ImportRowAsync(string tableName, object record)");
            Output("    {");
            Output("        switch (tableName)");
            Output("        {");
            foreach (var table in exportable)
            {
                var prop = CleanCRUD(table.SqlTableName());
                Output($"            case \"{table.SqlTableName()}\":");
                Output($"                return await {prop}.ImportRowAsync(({table.RecordName()})record);");
            }
            Output("            default:");
            Output("                throw new ArgumentException($\"Unknown exportable table '{tableName}'\", nameof(tableName));");
            Output("        }");
            Output("    }");
            Output("");

            Output("    public async Task<Dictionary<string, long>> GetTableVersionsAsync()");
            Output("    {");
            Output("        await using var cn = await CreateScopedConnectionAsync();");
            Output("        var result = new Dictionary<string, long>();");
            Output("        foreach (var name in ExportableTables)");
            Output("        {");
            Output("            result[name] = await SqlHelper.GetTableVersionAsync(cn, name);");
            Output("        }");
            Output("        return result;");
            Output("    }");
            Output("");

            // Only meaningful where every table shares one scope column, i.e. Identity.
            if (scopes.Count == 1)
            {
                var s = scopes[0];
                Output($"    public async Task<long> CountRowsForIdentityAsync({s.GetTypeStringC()} {s.name})");
                Output("    {");
                Output("        await using var cn = await CreateScopedConnectionAsync();");
                Output("        long total = 0;");
                Output("        foreach (var name in ExportableTables)");
                Output("        {");
                Output("            await using var cmd = cn.CreateCommand();");
                Output($"            cmd.CommandText = $\"SELECT COUNT(*) FROM {{name}} WHERE {s.name} = @{s.name};\";");
                Output($"            cmd.AddParameter(\"@{s.name}\", {s.GetTypeStringDbType()}, {s.BuildValueAssignment()});");
                Output("            total += (long)(await cmd.ExecuteScalarAsync() ?? 0L);");
                Output("        }");
                Output("        return total;");
                Output("    }");
                Output("");
            }

            Output("}");
            _file.Close();
            _file.Dispose();
        }
    }
```

- [ ] **Step 2: Call it from `Main`**

```csharp
    private static void Main(string[] args)
    {
        GenerateAllTables();
        GenerateAllGlobalMigrationLists();
        GenerateAllGlobalTableLists();
        GenerateAllGlobalExportLists();
    }
```

- [ ] **Step 3: Regenerate and read the Identity aggregate**

```bash
cd /workspace/Odin-SQLite-Generator && ODIN_ROOT=/workspace/ dotnet run --project Odin-SQLite-Generator/Odin-SQLite-Generator.csproj
cd /workspace/odin-core && cat src/core/Odin.Core.Storage/Database/Identity/IdentityDatabase.Export.Generated.cs
```

Expected: `ExportableTables` lists 23 names. `ExportAsync` takes `(Guid identityId, Func<string, object, Task> onRow)` with a single scope parameter. `CountRowsForIdentityAsync` is present.

- [ ] **Step 4: Read the System aggregate and check the two-scope case**

Run: `cd /workspace/odin-core && cat src/core/Odin.Core.Storage/Database/System/SystemDatabase.Export.Generated.cs`

Expected: `ExportableTables` lists exactly `Certificates`, `DkimKeys` and `Registrations`, in the order the tables are declared in the generator. `ExportAsync` takes **two** scope parameters, `OdinId domain` and `Guid identityId`, sorted ordinally so `domain` comes first. `Certificates` and `DkimKeys` both pass `domain` at their call sites; only `Registrations` passes `identityId`. `CountRowsForIdentityAsync` is **absent**, because the namespace has more than one scope column.

- [ ] **Step 5: Verify no JSON leaked into generated code**

Global constraint check. Run:

```bash
cd /workspace/odin-core
grep -rl "System.Text.Json\|Utf8JsonWriter\|JsonElement" src/core/Odin.Core.Storage/Database/Identity/ src/core/Odin.Core.Storage/Database/System/ | grep -v Migrations
```

Expected: no output.

- [ ] **Step 6: Confirm odin-core builds**

Run: `cd /workspace/odin-core && dotnet build ./odin-core.sln`
Expected: build succeeded.

- [ ] **Step 7: Commit both repos**

```bash
cd /workspace/Odin-SQLite-Generator
git add Odin-SQLite-Generator/Program.cs
git commit -m "Generator: emit per-namespace export aggregate

ExportableTables, ExportableRecordTypes, ExportAsync, ImportRowAsync,
GetTableVersionsAsync, and CountRowsForIdentityAsync where the namespace
has a single scope column. No JSON types: the aggregate deals in object
and Type so the file format stays entirely in odin-core."

cd /workspace/odin-core
git add src/core/Odin.Core.Storage/Database
git commit -m "Regenerate: add IdentityDatabase and SystemDatabase export aggregates"
```

---

### Task 6: Coverage test proving the aggregate can never miss a table

This replaces the source-scanning approach `DataImporterTests` had to use. It is a real runtime assertion against a generated data structure.

**Files:**
- Create: `odin-core/tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/ExportCoverageTests.cs`

**Interfaces:**
- Consumes: `IdentityDatabase.ExportableTables`, `IdentityDatabase.ExportableRecordTypes`, `IdentityDatabase.TableTypes`
- Produces: nothing consumed by later tasks

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Tests.IdentityJsonExport;

// DataImporter needs a hand-maintained list of tables, policed by a test that greps
// its source as text. The generated aggregate cannot drift the same way, and this
// test is the runtime proof: ExportableTables is generated from the same table
// definitions as TableTypes, so any table added to the generator appears in both.
public class ExportCoverageTests
{
    [Test]
    public void ExportableTables_CoversEveryIdentityTable()
    {
        var fromTableTypes = IdentityDatabase.TableTypes
            .Select(t => t.Name.StartsWith("Table") ? t.Name.Substring("Table".Length) : t.Name)
            .OrderBy(n => n)
            .ToList();

        var fromExport = IdentityDatabase.ExportableTables.OrderBy(n => n).ToList();

        Assert.That(fromExport, Is.EqualTo(fromTableTypes),
            "IdentityDatabase.ExportableTables does not match TableTypes. Re-run the generator.");
    }

    [Test]
    public void ExportableRecordTypes_HasAnEntryForEveryExportableTable()
    {
        var missing = IdentityDatabase.ExportableTables
            .Where(name => !IdentityDatabase.ExportableRecordTypes.ContainsKey(name))
            .ToList();

        Assert.That(missing, Is.Empty,
            "ExportableRecordTypes is missing: " + string.Join(", ", missing));
    }

    [Test]
    public void ExportableRecordTypes_MapsToRealRecordTypes()
    {
        foreach (var (name, type) in IdentityDatabase.ExportableRecordTypes)
        {
            Assert.That(type.Name, Is.EqualTo(name + "Record"),
                $"Table {name} maps to unexpected record type {type.Name}");
        }
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~ExportCoverageTests"`
Expected: 3 tests PASS. They pass immediately because Task 5 already produced correct output; the test exists to catch future drift, not to drive new code.

If `ExportableTables_CoversEveryIdentityTable` fails, the name-derivation assumption is wrong for at least one table. Print both lists and reconcile before continuing, since every later task depends on the aggregate being complete.

- [ ] **Step 3: Commit**

```bash
cd /workspace/odin-core
git add tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/ExportCoverageTests.cs
git commit -m "Test: prove the export aggregate covers every identity table

Runtime assertion against generated data, replacing the source-scanning
approach DataImporterTests had to use."
```

---

# Phase B: odin-core (`/workspace/odin-core`)

---

### Task 7: The file format types and the exporter

**Files:**
- Create: `src/core/Odin.Core.Storage/DatabaseImport/IdentityExportFile.cs`
- Create: `src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonExporter.cs`
- Create: `tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityJsonExporterTests.cs`

**Interfaces:**
- Consumes: `IdentityDatabase.ExportAsync`, `IdentityDatabase.GetTableVersionsAsync`, `SystemDatabase.ExportAsync`, `SystemDatabase.GetTableVersionsAsync`
- Produces:
  - `IdentityExportFile.CurrentFormatVersion` (`const int`, value `1`)
  - `ExportHeader` with `Kind`, `FormatVersion`, `ExportedAt`, `IdentityId`, `Domain`, `IdentitySchemaVersion`, `SystemSchemaVersion`, `TableVersions`
  - `ExportRow` with `Kind`, `Db`, `Table`, `Data`
  - `IdentityJsonExporter.ExportAsync(ILogger, Stream, Guid, string, SystemDatabase, IdentityDatabase, long, long, bool)` returning `Task<long>` (rows written)

- [ ] **Step 1: Write the format types**

Create `src/core/Odin.Core.Storage/DatabaseImport/IdentityExportFile.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// Shape of the identity export file. A single JSON array whose first element is the
// header and whose remaining elements are one row each.
//
// An array rather than {"tables":{...}} so the file streams in both directions with
// stock APIs: Utf8JsonWriter out, JsonSerializer.DeserializeAsyncEnumerable in.
// DriveMainIndex carries hdrFileMetaData and hdrAppData for every file the identity
// owns, so whole-document parsing is not safe to assume.
public static class IdentityExportFile
{
    // Describes the envelope only: header fields and row shape. Independent of the
    // per-table schema versions, which live in the header's TableVersions.
    public const int CurrentFormatVersion = 1;

    public const string KindHeader = "header";
    public const string KindRow = "row";

    public const string DbIdentity = "identity";
    public const string DbSystem = "system";
}

public class ExportHeader
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = IdentityExportFile.KindHeader;
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("exportedAt")] public long ExportedAt { get; set; }
    [JsonPropertyName("identityId")] public Guid IdentityId { get; set; }
    [JsonPropertyName("domain")] public string Domain { get; set; } = "";
    [JsonPropertyName("identitySchemaVersion")] public long IdentitySchemaVersion { get; set; }
    [JsonPropertyName("systemSchemaVersion")] public long SystemSchemaVersion { get; set; }

    // db name -> table name -> per-table schema version. Authoritative for the
    // all-or-nothing compatibility check on import.
    [JsonPropertyName("tableVersions")]
    public Dictionary<string, Dictionary<string, long>> TableVersions { get; set; } = new();
}

public class ExportRow
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = IdentityExportFile.KindRow;
    [JsonPropertyName("db")] public string Db { get; set; } = "";
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("data")] public JsonElement Data { get; set; }
}
```

- [ ] **Step 2: Write the failing exporter test**

Create `tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityJsonExporterTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Tests.DatabaseImport;
using Odin.Test.Helpers;

namespace Odin.Core.Storage.Tests.IdentityJsonExport;

public class IdentityJsonExporterTests
{
    private const string IdentityDomain = "frodo.dotyou.cloud";

    private Guid _identityId;
    private string _tempFolder = "";
    private TestServices _services = null!;
    private ILifetimeScope _scope = null!;

    [SetUp]
    public void Setup()
    {
        _identityId = Guid.NewGuid();
        _tempFolder = TempDirectory.Create();
        _services = new TestServices();
    }

    [TearDown]
    public void TearDown()
    {
        _services?.Dispose();
        _services = null!;
        _scope = null!;
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private async Task<MemoryStream> SeedAndExportAsync()
    {
        _scope = await _services.RegisterServicesAsync(DatabaseType.Sqlite, _tempFolder, _identityId);
        var sys = _scope.Resolve<SystemDatabase>();
        var id = _scope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(sys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(id);

        var logger = _scope.Resolve<ILogger<IdentityJsonExporterTests>>();
        var stream = new MemoryStream();
        await IdentityJsonExporter.ExportAsync(
            logger, stream, _identityId, IdentityDomain, sys, id,
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasFrozenIdentity: true);
        stream.Position = 0;
        return stream;
    }

    private static List<JsonElement> ReadElements(MemoryStream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Test]
    public async Task ExportAsync_WritesAHeaderAsTheFirstElement()
    {
        var elements = ReadElements(await SeedAndExportAsync());

        Assert.That(elements, Is.Not.Empty);
        var header = elements[0];
        Assert.That(header.GetProperty("kind").GetString(), Is.EqualTo("header"));
        Assert.That(header.GetProperty("formatVersion").GetInt32(),
            Is.EqualTo(IdentityExportFile.CurrentFormatVersion));
        Assert.That(header.GetProperty("identityId").GetGuid(), Is.EqualTo(_identityId));
        Assert.That(header.GetProperty("domain").GetString(), Is.EqualTo(IdentityDomain));
    }

    [Test]
    public async Task ExportAsync_RecordsAVersionForEveryExportableTable()
    {
        var elements = ReadElements(await SeedAndExportAsync());
        var versions = elements[0].GetProperty("tableVersions").GetProperty("identity");

        foreach (var table in IdentityDatabase.ExportableTables)
        {
            Assert.That(versions.TryGetProperty(table, out _), Is.True,
                $"header.tableVersions.identity is missing {table}");
        }
    }

    [Test]
    public async Task ExportAsync_EmitsRowsForEveryIdentityTable()
    {
        var elements = ReadElements(await SeedAndExportAsync());

        var tablesSeen = elements
            .Skip(1)
            .Where(e => e.GetProperty("db").GetString() == "identity")
            .Select(e => e.GetProperty("table").GetString()!)
            .ToHashSet();

        var missing = IdentityDatabase.ExportableTables.Where(t => !tablesSeen.Contains(t)).ToList();
        Assert.That(missing, Is.Empty,
            "No rows exported for: " + string.Join(", ", missing));
    }

    // Requirement 1: the exporter never filters. Inbox, Outbox and Nonce are dropped
    // on import, not on export, so they must be present in the file.
    [Test]
    public async Task ExportAsync_IncludesTablesTheImportWillSkip()
    {
        var elements = ReadElements(await SeedAndExportAsync());
        var tablesSeen = elements
            .Skip(1)
            .Select(e => e.GetProperty("table").GetString()!)
            .ToHashSet();

        Assert.That(tablesSeen, Does.Contain("Inbox"));
        Assert.That(tablesSeen, Does.Contain("Outbox"));
        Assert.That(tablesSeen, Does.Contain("Nonce"));
    }

    // Requirement 3: the identity-scoped System rows travel in the same file. Three
    // tables now, not two: DkimKeys joined Registrations and Certificates.
    // DataImporterSeedHelper.SeedAllSystemTablesAsync already seeds all three.
    [Test]
    public async Task ExportAsync_IncludesTheIdentitysSystemRows()
    {
        var elements = ReadElements(await SeedAndExportAsync());
        var systemTables = elements
            .Skip(1)
            .Where(e => e.GetProperty("db").GetString() == "system")
            .Select(e => e.GetProperty("table").GetString()!)
            .ToHashSet();

        Assert.That(systemTables, Does.Contain("Registrations"));
        Assert.That(systemTables, Does.Contain("Certificates"));
        Assert.That(systemTables, Does.Contain("DkimKeys"));
    }

    // Requirement 9: the exporter cannot check the freeze itself without referencing
    // upward, so it takes the caller's assertion and refuses a false one.
    [Test]
    public void ExportAsync_ThrowsWhenCallerHasNotFrozenTheIdentity()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            _scope = await _services.RegisterServicesAsync(DatabaseType.Sqlite, _tempFolder, _identityId);
            var logger = _scope.Resolve<ILogger<IdentityJsonExporterTests>>();
            await IdentityJsonExporter.ExportAsync(
                logger, new MemoryStream(), _identityId, IdentityDomain,
                _scope.Resolve<SystemDatabase>(), _scope.Resolve<IdentityDatabase>(),
                identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasFrozenIdentity: false);
        });
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityJsonExporterTests"`
Expected: compile error, `IdentityJsonExporter` does not exist.

- [ ] **Step 4: Write the exporter**

Create `src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// Writes one identity's tables to a single JSON file.
//
// Streams throughout: rows go straight from the reader to the Utf8JsonWriter, so
// memory is flat in the number of rows.
//
// The export runs inside one RepeatableRead transaction per database so all tables
// come from a single snapshot. Without the explicit isolation level the default is
// IsolationLevel.Unspecified, which on Postgres means READ COMMITTED and a fresh
// snapshot per statement.
//
// This class cannot verify the identity is frozen: that needs IIdentityRegistry from
// Odin.Services, which this layer must not reference. The caller asserts it.
public static class IdentityJsonExporter
{
    public static async Task<long> ExportAsync(
        ILogger logger,
        Stream output,
        Guid identityId,
        string domain,
        SystemDatabase systemDatabase,
        IdentityDatabase identityDatabase,
        long identitySchemaVersion,
        long systemSchemaVersion,
        bool callerHasFrozenIdentity)
    {
        if (!callerHasFrozenIdentity)
        {
            throw new InvalidOperationException(
                "Refusing to export: the identity must be frozen first. Disabling an identity "
                + "only closes the HTTP front door; its background workers keep writing.");
        }

        await using var systemTx = await systemDatabase.BeginStackedTransactionAsync(IsolationLevel.RepeatableRead);
        await using var identityTx = await identityDatabase.BeginStackedTransactionAsync(IsolationLevel.RepeatableRead);

        var header = new ExportHeader
        {
            FormatVersion = IdentityExportFile.CurrentFormatVersion,
            ExportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IdentityId = identityId,
            Domain = domain,
            IdentitySchemaVersion = identitySchemaVersion,
            SystemSchemaVersion = systemSchemaVersion,
            TableVersions = new Dictionary<string, Dictionary<string, long>>
            {
                [IdentityExportFile.DbSystem] = await systemDatabase.GetTableVersionsAsync(),
                [IdentityExportFile.DbIdentity] = await identityDatabase.GetTableVersionsAsync(),
            },
        };

        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartArray();
        JsonSerializer.Serialize(writer, header, OdinSystemSerializer.JsonSerializerOptions);

        var rowCount = 0L;

        Task WriteRow(string db, string table, object record)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", IdentityExportFile.KindRow);
            writer.WriteString("db", db);
            writer.WriteString("table", table);
            writer.WritePropertyName("data");
            JsonSerializer.Serialize(writer, record, record.GetType(), OdinSystemSerializer.JsonSerializerOptions);
            writer.WriteEndObject();
            rowCount++;
            return Task.CompletedTask;
        }

        // System rows first, so a truncated file fails on the registration rather than
        // leaving orphaned identity data.
        await systemDatabase.ExportAsync(new OdinId(domain), identityId,
            (table, record) => WriteRow(IdentityExportFile.DbSystem, table, record));

        await identityDatabase.ExportAsync(identityId,
            (table, record) => WriteRow(IdentityExportFile.DbIdentity, table, record));

        writer.WriteEndArray();
        await writer.FlushAsync();

        logger.LogInformation("Exported {count} rows for {domain}", rowCount, domain);
        return rowCount;
    }
}
```

Note the `SystemDatabase.ExportAsync` argument order is `(OdinId domain, Guid identityId, ...)`, because Task 5 sorts scope parameters ordinally and `domain` precedes `identityId`. If the build disagrees, read the generated signature and match it rather than editing generated code.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityJsonExporterTests"`
Expected: 6 tests PASS.

- [ ] **Step 6: Commit**

```bash
cd /workspace/odin-core
git add src/core/Odin.Core.Storage/DatabaseImport/IdentityExportFile.cs \
        src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonExporter.cs \
        tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityJsonExporterTests.cs
git commit -m "Add identity JSON exporter

Streams one identity's tables to a single JSON array. Exports everything
including Inbox, Outbox and Nonce; filtering is an import concern. Runs
in a RepeatableRead transaction so all tables come from one snapshot."
```

---

### Task 8: Import preconditions

Split from the importer itself because these are the checks that must hold before a single row is written, and they are worth a reviewer's gate on their own.

**Files:**
- Create: `src/core/Odin.Core.Storage/DatabaseImport/IdentityImportPreconditions.cs`
- Create: `tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityImportPreconditionTests.cs`

**Interfaces:**
- Consumes: `ExportHeader` (Task 7), `IdentityDatabase.GetTableVersionsAsync`, `IdentityDatabase.CountRowsForIdentityAsync`, `SystemDatabase.GetTableVersionsAsync`
- Produces: `IdentityImportPreconditions.CheckAsync(ExportHeader, SystemDatabase, IdentityDatabase)` returning `Task<List<string>>` of violations, empty when the import may proceed.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Time;
using Odin.Test.Helpers;

namespace Odin.Core.Storage.Tests.IdentityJsonExport;

public class IdentityImportPreconditionTests
{
    private const string IdentityDomain = "frodo.dotyou.cloud";

    private Guid _identityId;
    private string _tempFolder = "";
    private TestServices _services = null!;
    private ILifetimeScope _scope = null!;

    [SetUp]
    public void Setup()
    {
        _identityId = Guid.NewGuid();
        _tempFolder = TempDirectory.Create();
        _services = new TestServices();
    }

    [TearDown]
    public void TearDown()
    {
        _services?.Dispose();
        _services = null!;
        _scope = null!;
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private async Task<(SystemDatabase sys, IdentityDatabase id)> InitAsync()
    {
        _scope = await _services.RegisterServicesAsync(DatabaseType.Sqlite, _tempFolder, _identityId);
        return (_scope.Resolve<SystemDatabase>(), _scope.Resolve<IdentityDatabase>());
    }

    private async Task<ExportHeader> MatchingHeaderAsync(SystemDatabase sys, IdentityDatabase id)
    {
        return new ExportHeader
        {
            FormatVersion = IdentityExportFile.CurrentFormatVersion,
            IdentityId = _identityId,
            Domain = IdentityDomain,
            TableVersions = new Dictionary<string, Dictionary<string, long>>
            {
                [IdentityExportFile.DbSystem] = await sys.GetTableVersionsAsync(),
                [IdentityExportFile.DbIdentity] = await id.GetTableVersionsAsync(),
            },
        };
    }

    [Test]
    public async Task CheckAsync_PassesOnAnEmptyMatchingTarget()
    {
        var (sys, id) = await InitAsync();
        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations, Is.Empty);
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheIdentityIdAlreadyExists()
    {
        var (sys, id) = await InitAsync();
        await sys.Registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = _identityId,
            email = "a@b.c",
            primaryDomainName = "someone-else.dotyou.cloud",
            firstRunToken = Guid.NewGuid().ToString(),
            disabled = false,
            planId = "free",
            enablePublicWebPresence = false,
            json = "{}",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("identityId")), Is.True,
            "Expected an identityId collision. Got: " + string.Join(" | ", violations));
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheDomainAlreadyExists_IgnoringCase()
    {
        var (sys, id) = await InitAsync();
        await sys.Registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            email = "a@b.c",
            primaryDomainName = "FRODO.DOTYOU.CLOUD",
            firstRunToken = Guid.NewGuid().ToString(),
            disabled = false,
            planId = "free",
            enablePublicWebPresence = false,
            json = "{}",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("domain")), Is.True,
            "Expected a domain collision. Got: " + string.Join(" | ", violations));
    }

    [Test]
    public async Task CheckAsync_FailsOnALeftoverCertificateRowWithNoRegistration()
    {
        var (sys, id) = await InitAsync();
        await sys.Certificates.InsertAsync(new CertificatesRecord
        {
            domain = new OdinId(IdentityDomain),
            privateKey = "pk",
            certificate = "cert",
            expiration = UnixTimeUtc.Now(),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = "c",
            lastError = "",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("Certificates")), Is.True,
            "Expected a Certificates collision. Got: " + string.Join(" | ", violations));
    }

    // DkimKeys is keyed by (domain, selector), so like Certificates it outlives the
    // registration and neither of the two checks above would catch it.
    [Test]
    public async Task CheckAsync_FailsOnLeftoverDkimKeyRowsWithNoRegistration()
    {
        var (sys, id) = await InitAsync();
        await sys.DkimKeys.InsertAsync(new DkimKeysRecord
        {
            domain = new OdinId(IdentityDomain),
            selector = "s1",
            algorithm = "ed25519",
            publicKey = "pub",
            privateKey = "priv",
        });

        var violations = await IdentityImportPreconditions.CheckAsync(await MatchingHeaderAsync(sys, id), sys, id);
        Assert.That(violations.Any(v => v.Contains("DkimKeys")), Is.True,
            "Expected a DkimKeys collision. Got: " + string.Join(" | ", violations));
    }

    // Requirement 10: one differing table blocks everything, even though the rest match.
    [Test]
    public async Task CheckAsync_FailsWhenASingleTableVersionDiffers()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["Circle"] = 111111111111L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Circle")), Is.True,
            "Expected a Circle version mismatch. Got: " + string.Join(" | ", violations));
    }

    // Requirement 10: skipping a table on import does not exempt it from the check.
    [Test]
    public async Task CheckAsync_FailsWhenASkippedTablesVersionDiffers()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["Outbox"] = 111111111111L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Outbox")), Is.True,
            "Expected an Outbox version mismatch even though Outbox is skipped on import.");
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheHeaderIsMissingATableTheTargetHas()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity].Remove("Circle");

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Circle")), Is.True,
            "A table on the target but absent from the header is a mismatch.");
    }

    [Test]
    public async Task CheckAsync_FailsWhenTheHeaderHasATableTheTargetDoesNot()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["TableFromTheFuture"] = 202700000000L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("TableFromTheFuture")), Is.True,
            "A table in the header but absent from the target is a mismatch.");
    }

    [Test]
    public async Task CheckAsync_ReportsEveryDifferenceNotJustTheFirst()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.TableVersions[IdentityExportFile.DbIdentity]["Circle"] = 111111111111L;
        header.TableVersions[IdentityExportFile.DbIdentity]["Drives"] = 222222222222L;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("Circle")), Is.True);
        Assert.That(violations.Any(v => v.Contains("Drives")), Is.True);
    }

    [Test]
    public async Task CheckAsync_FailsWhenFormatVersionIsNewerThanThisBinary()
    {
        var (sys, id) = await InitAsync();
        var header = await MatchingHeaderAsync(sys, id);
        header.FormatVersion = IdentityExportFile.CurrentFormatVersion + 1;

        var violations = await IdentityImportPreconditions.CheckAsync(header, sys, id);
        Assert.That(violations.Any(v => v.Contains("formatVersion")), Is.True);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityImportPreconditionTests"`
Expected: compile error, `IdentityImportPreconditions` does not exist.

- [ ] **Step 3: Write the preconditions**

Create `src/core/Odin.Core.Storage/DatabaseImport/IdentityImportPreconditions.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// Everything that must hold before a single row is written.
//
// Returns every violation rather than throwing on the first, so one run tells the
// operator the full extent of the problem instead of making them re-run to discover
// the next one.
public static class IdentityImportPreconditions
{
    public static async Task<List<string>> CheckAsync(
        ExportHeader header,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase targetIdentityDatabase)
    {
        var violations = new List<string>();

        if (header.FormatVersion > IdentityExportFile.CurrentFormatVersion)
        {
            violations.Add(
                $"File formatVersion {header.FormatVersion} is newer than this binary understands "
                + $"({IdentityExportFile.CurrentFormatVersion}).");
        }

        // 1. Registrations: identityId or domain, either is a hard stop.
        var registrations = await targetSystemDatabase.Registrations.GetAllAsync();
        if (registrations.Any(r => r.identityId == header.IdentityId))
        {
            violations.Add($"Target already has a registration with identityId {header.IdentityId}.");
        }
        if (registrations.Any(r => r.primaryDomainName.Equals(header.Domain, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add($"Target already has a registration for domain {header.Domain}.");
        }

        // 2. Certificates is keyed by domain, so it survives independently of the
        //    registration. DataImporter.DeleteIdentityFromSystemDataAsync has to delete
        //    both rows for exactly this reason.
        var certificate = await targetSystemDatabase.Certificates.GetAsync(new OdinId(header.Domain));
        if (certificate != null)
        {
            violations.Add($"Target already has a Certificates row for domain {header.Domain}.");
        }

        // 3. DkimKeys is keyed by (domain, selector) and outlives the registration for
        //    the same reason Certificates does; DataImporter.DeleteIdentityFromSystemDataAsync
        //    deletes it by domain too. One identity owns several rows here, so count them
        //    rather than testing for a single row.
        var dkimKeys = await targetSystemDatabase.DkimKeys.GetByDomainAsync(new OdinId(header.Domain));
        if (dkimKeys.Count > 0)
        {
            violations.Add($"Target already has {dkimKeys.Count} DkimKeys row(s) for domain {header.Domain}.");
        }

        // 4. On Postgres every identity shares one set of physical tables, and
        //    DeleteRegistration never purges them, so identity rows can outlive the
        //    registration and checks 1 and 2 would both pass.
        var orphanRows = await targetIdentityDatabase.CountRowsForIdentityAsync(header.IdentityId);
        if (orphanRows > 0)
        {
            violations.Add(
                $"Target identity tables already hold {orphanRows} row(s) for identityId {header.IdentityId}.");
        }

        // 5. All-or-nothing table version match, in both directions.
        violations.AddRange(CompareTableVersions(
            IdentityExportFile.DbSystem,
            header.TableVersions.GetValueOrDefault(IdentityExportFile.DbSystem) ?? new Dictionary<string, long>(),
            await targetSystemDatabase.GetTableVersionsAsync()));

        violations.AddRange(CompareTableVersions(
            IdentityExportFile.DbIdentity,
            header.TableVersions.GetValueOrDefault(IdentityExportFile.DbIdentity) ?? new Dictionary<string, long>(),
            await targetIdentityDatabase.GetTableVersionsAsync()));

        return violations;
    }

    // Table sets must be identical, not merely overlapping. A table present on one
    // side and absent on the other is as much a mismatch as a differing version.
    private static IEnumerable<string> CompareTableVersions(
        string db,
        Dictionary<string, long> fromFile,
        Dictionary<string, long> onTarget)
    {
        foreach (var (table, fileVersion) in fromFile.OrderBy(kv => kv.Key))
        {
            if (!onTarget.TryGetValue(table, out var targetVersion))
            {
                yield return $"{db}.{table}: present in the file, absent on the target.";
            }
            else if (fileVersion != targetVersion)
            {
                yield return $"{db}.{table}: file version {fileVersion}, target version {targetVersion}.";
            }
        }

        foreach (var table in onTarget.Keys.Where(t => !fromFile.ContainsKey(t)).OrderBy(t => t))
        {
            yield return $"{db}.{table}: present on the target, absent from the file.";
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityImportPreconditionTests"`
Expected: 11 tests PASS.

If `CheckAsync_FailsOnALeftoverCertificateRowWithNoRegistration` fails to compile, check the real name of the single-row getter on `TableCertificates` with:
`grep -n "public async Task<CertificatesRecord>" src/core/Odin.Core.Storage/Database/System/Table/TableCertificates*.cs`
and use that, rather than changing the test's intent.

- [ ] **Step 5: Commit**

```bash
cd /workspace/odin-core
git add src/core/Odin.Core.Storage/DatabaseImport/IdentityImportPreconditions.cs \
        tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityImportPreconditionTests.cs
git commit -m "Add identity import preconditions

Refuses on an existing identityId or domain, leftover Certificates or
DkimKeys rows, orphaned identity rows (which outlive DeleteRegistration
on Postgres), and any table version mismatch in either direction. Reports
every violation rather than the first."
```

---

### Task 9: The importer, with the skip list

**Files:**
- Create: `src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonImporter.cs`
- Create: `tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityJsonRoundTripTests.cs`

**Interfaces:**
- Consumes: `IdentityImportPreconditions.CheckAsync` (Task 8), `ExportHeader` / `ExportRow` (Task 7), `IdentityDatabase.ImportRowAsync`, `IdentityDatabase.ExportableRecordTypes`
- Produces:
  - `IdentityJsonImporter.DefaultSkippedTables` (`IReadOnlySet<string>`)
  - `IdentityJsonImporter.ImportAsync(ILogger, Stream, SystemDatabase, IdentityDatabase, bool commit, IReadOnlySet<string>? skipTables = null)` returning `Task<ImportResult>`
  - `ImportResult` with `RowsImported` (`long`), `SkippedRowsByTable` (`Dictionary<string, long>`), `Header` (`ExportHeader`)

- [ ] **Step 1: Write the failing round-trip tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Tests.DatabaseImport;
using Odin.Test.Helpers;

namespace Odin.Core.Storage.Tests.IdentityJsonExport;

public class IdentityJsonRoundTripTests
{
    private const string IdentityDomain = "frodo.dotyou.cloud";

    private Guid _identityId;
    private string _sourceTempFolder = "";
    private string _targetTempFolder = "";
    private TestServices _sourceServices = null!;
    private TestServices _targetServices = null!;
    private ILifetimeScope _sourceScope = null!;
    private ILifetimeScope _targetScope = null!;

    [SetUp]
    public void Setup()
    {
        _identityId = Guid.NewGuid();
        _sourceTempFolder = TempDirectory.Create();
        _targetTempFolder = TempDirectory.Create();
        _sourceServices = new TestServices();
        _targetServices = new TestServices();
    }

    [TearDown]
    public void TearDown()
    {
        _sourceServices?.Dispose();
        _targetServices?.Dispose();
        _sourceServices = null!;
        _targetServices = null!;
        if (Directory.Exists(_sourceTempFolder))
            Directory.Delete(_sourceTempFolder, true);
        if (Directory.Exists(_targetTempFolder))
            Directory.Delete(_targetTempFolder, true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private async Task<MemoryStream> SeedSourceAndExportAsync(DatabaseType sourceType)
    {
        _sourceScope = await _sourceServices.RegisterServicesAsync(sourceType, _sourceTempFolder, _identityId);
        var sys = _sourceScope.Resolve<SystemDatabase>();
        var id = _sourceScope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(sys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(id);

        var logger = _sourceScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();
        var stream = new MemoryStream();
        await IdentityJsonExporter.ExportAsync(
            logger, stream, _identityId, IdentityDomain, sys, id,
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasFrozenIdentity: true);
        stream.Position = 0;
        return stream;
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Sqlite, DatabaseType.Postgres)]
#endif
    public async Task Import_RestoresEveryTableExceptTheSkippedOnes(
        DatabaseType sourceType, DatabaseType targetType)
    {
        var stream = await SeedSourceAndExportAsync(sourceType);
        _targetScope = await _targetServices.RegisterServicesAsync(targetType, _targetTempFolder, _identityId);

        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        var result = await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true);

        Assert.That(result.RowsImported, Is.GreaterThan(0));

        var circles = await tgtId.Circle.GetAllCirclesAsync();
        Assert.That(circles, Is.Not.Empty, "Circle rows should have been restored");
    }

    // Requirement 7: the regression DataImportPatcher exists to repair.
    [Test]
    public async Task Import_PreservesCreatedAndModifiedExactly()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        var srcId = _sourceScope.Resolve<IdentityDatabase>();
        var sourceDrives = await srcId.Drives.GetDrivesAsync(Int32.MaxValue, null);

        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true);

        var targetDrives = await tgtId.Drives.GetDrivesAsync(Int32.MaxValue, null);

        foreach (var source in sourceDrives.Results)
        {
            var target = targetDrives.Results.Single(d => d.DriveId == source.DriveId);
            Assert.That(target.created.milliseconds, Is.EqualTo(source.created.milliseconds),
                $"created was rewritten for drive {source.DriveId}");
            Assert.That(target.modified.milliseconds, Is.EqualTo(source.modified.milliseconds),
                $"modified was rewritten for drive {source.DriveId}");
        }
    }

    [Test]
    public async Task Import_SkipsInboxOutboxAndNonceByDefault()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        var result = await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true);

        Assert.That(result.SkippedRowsByTable.Keys, Does.Contain("Inbox"));
        Assert.That(result.SkippedRowsByTable.Keys, Does.Contain("Outbox"));
        Assert.That(result.SkippedRowsByTable.Keys, Does.Contain("Nonce"));
        Assert.That(result.SkippedRowsByTable["Outbox"], Is.GreaterThan(0),
            "Skipped tables must report a row count so the operator sees what was dropped");
    }

    [Test]
    public async Task Import_HonoursAnExplicitSkipListOverride()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        // Empty skip list: import everything, including Outbox.
        var result = await IdentityJsonImporter.ImportAsync(
            logger, stream, tgtSys, tgtId, commit: true, skipTables: new HashSet<string>());

        Assert.That(result.SkippedRowsByTable, Is.Empty);
    }

    [Test]
    public async Task Import_WritesNothingWhenCommitIsFalse()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: false);

        Assert.That(await tgtId.CountRowsForIdentityAsync(_identityId), Is.EqualTo(0),
            "Dry run must leave the target untouched");
    }

    // A brand new identity with no drives and no files still round-trips. Guards against
    // the exporter emitting a malformed array (a header and no rows) that the importer
    // then cannot read back.
    [Test]
    public async Task RoundTrip_HandlesAnIdentityWithNoData()
    {
        _sourceScope = await _sourceServices.RegisterServicesAsync(
            DatabaseType.Sqlite, _sourceTempFolder, _identityId);
        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var srcId = _sourceScope.Resolve<IdentityDatabase>();
        var logger = _sourceScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        // No seeding at all: empty identity, empty system tables.
        var stream = new MemoryStream();
        await IdentityJsonExporter.ExportAsync(
            logger, stream, _identityId, IdentityDomain, srcSys, srcId,
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasFrozenIdentity: true);
        stream.Position = 0;

        _targetScope = await _targetServices.RegisterServicesAsync(
            DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        var result = await IdentityJsonImporter.ImportAsync(
            logger, stream, tgtSys, tgtId, commit: true);

        Assert.That(result.RowsImported, Is.EqualTo(0));
        Assert.That(result.Header.Domain, Is.EqualTo(IdentityDomain),
            "The header must survive a round trip even with no rows");
    }

    [Test]
    public async Task Import_WritesZeroRowsWhenAPreconditionFails()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        // Import once so the identity exists, then import the same file again.
        await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true);
        var rowsAfterFirst = await tgtId.CountRowsForIdentityAsync(_identityId);

        stream.Position = 0;
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true));

        Assert.That(await tgtId.CountRowsForIdentityAsync(_identityId), Is.EqualTo(rowsAfterFirst),
            "A failed precondition must write zero additional rows");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityJsonRoundTripTests"`
Expected: compile error, `IdentityJsonImporter` does not exist.

If `GetAllCirclesAsync` or `GetDrivesAsync` do not resolve, find the real read methods with
`grep -n "public async Task" src/core/Odin.Core.Storage/Database/Identity/Table/TableCircle.cs src/core/Odin.Core.Storage/Database/Identity/Table/TableDrives.cs`
and substitute, keeping each assertion's intent.

- [ ] **Step 3: Write the importer**

Create `src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonImporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

public class ImportResult
{
    public ExportHeader Header { get; init; } = new();
    public long RowsImported { get; set; }
    public Dictionary<string, long> SkippedRowsByTable { get; } = new();
}

// Reads an identity export file and replays it into an empty target.
//
// The export is unconditional; this is where the decision about what to replay lives.
// The default is to import, so a table added to the generator flows through untouched.
public static class IdentityJsonImporter
{
    // Transient state that describes the SOURCE system's in-flight work rather than the
    // identity, and that ranges from useless to actively broken on the target.
    //
    //   Inbox  - rows reference staged files in the inbox folder, which are temp state and
    //            out of scope. Importing them guarantees "File does not exist <inbox key>".
    //   Nonce  - short-lived auth nonces; none is still valid by import time.
    //   Outbox - rows reference long-term files that ARE exported, so replay is structurally
    //            sound once payloads land. Skipped because we cannot verify payloads are
    //            present, nor whether the source is still live and also sending.
    public static readonly IReadOnlySet<string> DefaultSkippedTables =
        new HashSet<string> { "Inbox", "Outbox", "Nonce" };

    public static async Task<ImportResult> ImportAsync(
        ILogger logger,
        Stream input,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase targetIdentityDatabase,
        bool commit,
        IReadOnlySet<string>? skipTables = null)
    {
        var skip = skipTables ?? DefaultSkippedTables;

        using var document = await JsonDocument.ParseAsync(input);
        var elements = document.RootElement.EnumerateArray();

        if (!elements.MoveNext())
        {
            throw new InvalidOperationException("Export file is empty.");
        }

        var header = JsonSerializer.Deserialize<ExportHeader>(
            elements.Current.GetRawText(), OdinSystemSerializer.JsonSerializerOptions)
            ?? throw new InvalidOperationException("Export file has no readable header.");

        if (header.Kind != IdentityExportFile.KindHeader)
        {
            throw new InvalidOperationException(
                $"Expected the first element to be a header, found '{header.Kind}'.");
        }

        // Nothing is written until every precondition holds.
        var violations = await IdentityImportPreconditions.CheckAsync(
            header, targetSystemDatabase, targetIdentityDatabase);

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to import {header.Domain}. {violations.Count} precondition(s) failed:"
                + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(v => "  - " + v)));
        }

        var result = new ImportResult { Header = header };

        await using var systemTransaction = await targetSystemDatabase.BeginStackedTransactionAsync();
        await using var identityTransaction = await targetIdentityDatabase.BeginStackedTransactionAsync();

        while (elements.MoveNext())
        {
            var element = elements.Current;
            var table = element.GetProperty("table").GetString()
                ?? throw new InvalidOperationException("Row is missing its table name.");
            var db = element.GetProperty("db").GetString()
                ?? throw new InvalidOperationException($"Row for {table} is missing its db discriminator.");
            var data = element.GetProperty("data");

            if (skip.Contains(table))
            {
                result.SkippedRowsByTable.TryGetValue(table, out var soFar);
                result.SkippedRowsByTable[table] = soFar + 1;
                continue;
            }

            switch (db)
            {
                case IdentityExportFile.DbIdentity:
                    result.RowsImported += await targetIdentityDatabase.ImportRowAsync(
                        table, Deserialize(IdentityDatabase.ExportableRecordTypes, table, data));
                    break;

                case IdentityExportFile.DbSystem:
                    result.RowsImported += await targetSystemDatabase.ImportRowAsync(
                        table, Deserialize(SystemDatabase.ExportableRecordTypes, table, data));
                    break;

                default:
                    throw new InvalidOperationException($"Unknown db discriminator '{db}' for table {table}.");
            }
        }

        foreach (var (table, count) in result.SkippedRowsByTable.OrderBy(kv => kv.Key))
        {
            logger.LogInformation("  skipped {table}: {count} row(s)", table, count);
        }

        if (!commit)
        {
            logger.LogInformation("Dry run: rolling back {count} rows for {domain}",
                result.RowsImported, header.Domain);
        }
        else
        {
            logger.LogInformation("Imported {count} rows for {domain}", result.RowsImported, header.Domain);
            systemTransaction.Commit();
            identityTransaction.Commit();
        }

        return result;
    }

    private static object Deserialize(
        IReadOnlyDictionary<string, Type> recordTypes, string table, JsonElement data)
    {
        if (!recordTypes.TryGetValue(table, out var type))
        {
            throw new InvalidOperationException(
                $"Export file contains table '{table}', which this binary does not know about.");
        }

        return JsonSerializer.Deserialize(data.GetRawText(), type, OdinSystemSerializer.JsonSerializerOptions)
            ?? throw new InvalidOperationException($"Row for table '{table}' deserialized to null.");
    }
}
```

Note this uses `JsonDocument.ParseAsync` for now, which buys correctness and simplicity; Task 12 replaces it with a streaming reader.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityJsonRoundTripTests"`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
cd /workspace/odin-core
git add src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonImporter.cs \
        tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityJsonRoundTripTests.cs
git commit -m "Add identity JSON importer with default skip list

Skips Inbox, Outbox and Nonce by default, overridable. Defaults to import
so a table added to the generator flows through untouched. Preconditions
run before any write; everything else is one transaction with a dry-run
default."
```

---

### Task 10: Freeze and unfreeze an identity

**Files:**
- Modify: `src/services/Odin.Services/Registry/IIdentityRegistry.cs` (after `ToggleDisabled`, around line 94)
- Modify: `src/services/Odin.Services/Registry/FileSystemIdentityRegistry.cs` (add public methods; `StartBackgroundServices` / `StopBackgroundServices` are at lines 663 and 671)
- Modify: any other `IIdentityRegistry` implementation the build reveals

**Interfaces:**
- Consumes: existing `ToggleDisabled`, `StartBackgroundServices`, `StopBackgroundServices`
- Produces:
  - `Task<bool> FreezeIdentityAsync(string domain)` returning the identity's **previous** `Disabled` state
  - `Task UnfreezeIdentityAsync(string domain, bool restoreDisabledTo)`

- [ ] **Step 1: Find every implementation of the interface**

Run: `cd /workspace/odin-core && grep -rn "IIdentityRegistry" src/ tests/ --include="*.cs" | grep -i "class.*:.*IIdentityRegistry"`

Every type listed needs the two new methods, or the build breaks. Note them before editing.

- [ ] **Step 2: Add the interface methods**

In `IIdentityRegistry.cs`, immediately after `ToggleDisabled`:

```csharp
        /// <summary>
        /// Brings an identity to a full stop: disables it AND stops its background workers.
        /// Disabling alone only closes the HTTP front door (enforced solely in
        /// MultiTenantContainerMiddleware); the tenant's background workers do not check
        /// the Disabled flag and keep writing to the identity database.
        /// Returns the identity's previous Disabled state, to hand back to UnfreezeIdentityAsync.
        /// </summary>
        Task<bool> FreezeIdentityAsync(string domain);

        /// <summary>
        /// Restarts an identity's background workers and restores its Disabled flag to
        /// <paramref name="restoreDisabledTo"/>. Pass the value FreezeIdentityAsync returned:
        /// an identity may already have been disabled for unrelated reasons.
        /// </summary>
        Task UnfreezeIdentityAsync(string domain, bool restoreDisabledTo);
```

- [ ] **Step 3: Implement in `FileSystemIdentityRegistry`**

Add immediately after the existing `ToggleDisabled` method:

```csharp
    public async Task<bool> FreezeIdentityAsync(string domain)
    {
        var registration = await GetAsync(domain);
        if (registration == null)
        {
            throw new OdinClientException($"No such identity: {domain}");
        }

        var wasDisabled = registration.Disabled;

        // Order matters: close the front door before stopping the workers, so nothing
        // new arrives for a worker that is no longer running.
        await ToggleDisabled(domain, true);
        await StopBackgroundServices(registration);

        _logger.LogInformation("Froze identity {domain} (was disabled: {wasDisabled})", domain, wasDisabled);
        return wasDisabled;
    }

    public async Task UnfreezeIdentityAsync(string domain, bool restoreDisabledTo)
    {
        var registration = await GetAsync(domain);
        if (registration == null)
        {
            throw new OdinClientException($"No such identity: {domain}");
        }

        await StartBackgroundServices(registration);
        await ToggleDisabled(domain, restoreDisabledTo);

        _logger.LogInformation("Unfroze identity {domain} (disabled restored to: {disabled})",
            domain, restoreDisabledTo);
    }
```

- [ ] **Step 4: Add the methods to every other implementation found in Step 1**

For test doubles and mocks, throw `NotImplementedException` unless the test needs real behaviour. Do not silently no-op: a no-op freeze would make the export's safety check meaningless.

- [ ] **Step 5: Build**

Run: `cd /workspace/odin-core && dotnet build ./odin-core.sln`
Expected: build succeeded. Any missing implementation shows up here.

- [ ] **Step 6: Write the disabled-state test**

There is no existing test harness for `FileSystemIdentityRegistry`. The only registry
test is `tests/services/Odin.Services.Tests/Registry/Registration/IdentityRegistrationServiceTest.cs`,
which does not construct the registry. Building a harness that can observe
`IBackgroundServiceManager` would mean standing up a multi-tenant container, which is
a bigger lift than the code under test.

So this step covers the half that is cheaply testable, and Step 7 covers the other
half by hand. Do not skip Step 7 on the grounds that Step 6 passes: they test
different things.

Add to `tests/services/Odin.Services.Tests/Registry/FreezeIdentityTests.cs`:

```csharp
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Registry;

namespace Odin.Services.Tests.Registry;

// Covers the Disabled bookkeeping only. That freeze actually stops the six tenant
// background workers is verified by hand; see the plan's Task 10 Step 7.
public class FreezeIdentityTests
{
    // Unfreeze must restore the PREVIOUS state, not blindly enable. An identity may
    // already have been disabled for reasons unrelated to the export, and re-enabling
    // it would silently undo an operator's decision.
    [Test]
    public async Task Unfreeze_RestoresAPreviouslyDisabledIdentityToDisabled()
    {
        var registry = await TestRegistry.CreateWithIdentityAsync("frodo.dotyou.cloud", disabled: true);

        var wasDisabled = await registry.FreezeIdentityAsync("frodo.dotyou.cloud");
        Assert.That(wasDisabled, Is.True, "Freeze must report the prior state");

        await registry.UnfreezeIdentityAsync("frodo.dotyou.cloud", wasDisabled);

        var registration = await registry.GetAsync("frodo.dotyou.cloud");
        Assert.That(registration.Disabled, Is.True,
            "Unfreeze re-enabled an identity that was disabled before the freeze");
    }

    [Test]
    public async Task Unfreeze_RestoresAPreviouslyEnabledIdentityToEnabled()
    {
        var registry = await TestRegistry.CreateWithIdentityAsync("frodo.dotyou.cloud", disabled: false);

        var wasDisabled = await registry.FreezeIdentityAsync("frodo.dotyou.cloud");
        Assert.That(wasDisabled, Is.False);

        var duringFreeze = await registry.GetAsync("frodo.dotyou.cloud");
        Assert.That(duringFreeze.Disabled, Is.True, "Freeze must disable the identity");

        await registry.UnfreezeIdentityAsync("frodo.dotyou.cloud", wasDisabled);

        var afterUnfreeze = await registry.GetAsync("frodo.dotyou.cloud");
        Assert.That(afterUnfreeze.Disabled, Is.False);
    }
}
```

`TestRegistry.CreateWithIdentityAsync` does not exist yet. Before writing it, run:

```bash
cd /workspace/odin-core
grep -rn "new FileSystemIdentityRegistry" src/ tests/ --include="*.cs"
```

Use the constructor arguments that call site supplies. If standing the registry up needs
more than roughly 30 lines of setup, do not force it: delete this step's test file,
record in the commit message that freeze is covered only by the manual check in Step 7,
and raise it for review. A test harness larger than the feature is not worth it here,
but that trade-off is a reviewer's call, not one to make silently.

- [ ] **Step 7: Verify by hand that the workers actually stop**

The behaviour that matters, and the reason this task exists.

```bash
cd /workspace/odin-core
dotnet run --project src/apps/Odin.Hosting
```

With the dev hobbits running, in another shell trigger `identity-export` for
`frodo.dotyou.cloud` once Task 11 is done, or call `FreezeIdentityAsync` from a scratch
CLI verb. Then confirm in the host log that all six workers logged a shutdown:
`PeerOutboxProcessorBackgroundService`, `PeerInboxProcessorBackgroundService`,
`InboxOutboxReconciliationBackgroundService`, `TempFolderCleanUpBackgroundService`,
`InboxOrphanScanBackgroundService`, `SecurityHealthCheckBackgroundScheduler`.

Expected: six shutdown log lines, then six startup lines after the unfreeze. Record
what you saw in the commit message.

- [ ] **Step 8: Commit**

```bash
cd /workspace/odin-core
git add src/services/Odin.Services/Registry/ tests/services/Odin.Services.Tests/Registry/
git commit -m "Add FreezeIdentityAsync and UnfreezeIdentityAsync

Disabling an identity only gates inbound HTTP (enforced solely in
MultiTenantContainerMiddleware); none of the six tenant background workers
checks the flag. Freeze disables and stops the workers. Unfreeze restores
the previous disabled state rather than blindly enabling."
```

---

### Task 11: CLI verbs

**Files:**
- Create: `src/apps/Odin.Hosting/Cli/Commands/IdentityJsonTransfer.cs`
- Modify: `src/apps/Odin.Hosting/Cli/CommandLine.cs` (beside the `sqlite2pg-*` verbs, around line 320-390)

**Interfaces:**
- Consumes: `IdentityJsonExporter.ExportAsync`, `IdentityJsonImporter.ImportAsync`, `IIdentityRegistry.FreezeIdentityAsync` / `UnfreezeIdentityAsync`
- Produces: `IdentityJsonTransfer.ExportAsync(IServiceProvider, string domain, string filePath)` and `IdentityJsonTransfer.ImportAsync(IServiceProvider, string filePath, bool commit)`

- [ ] **Step 1: Confirm how to resolve both databases in a running host**

Unlike `Sqlite2Pg`, which builds explicit scopes because it reads arbitrary SQLite file
paths, these verbs run against the host's own configured databases. Two different
sources, so confirm both before writing code.

`SystemDatabase` is registered on the root container (`SystemServices.cs:309-312`).
`IdentityDatabase` is tenant-scoped, resolved the way `FileSystemIdentityRegistry`
does it (`FileSystemIdentityRegistry.cs:663-669`).

Run:

```bash
cd /workspace/odin-core
sed -n '305,315p' src/apps/Odin.Hosting/SystemServices.cs
sed -n '663,678p' src/services/Odin.Services/Registry/FileSystemIdentityRegistry.cs
grep -n "GetTenantScope\|GetOrAddTenantScope" src/services/Odin.Services/Tenant/Container/MultiTenantContainer.cs
```

Write down the exact type used to reach a tenant scope. The code below assumes
`IMultiTenantContainer.GetTenantScope(string domain)`. If the real name differs, use
the real one.

- [ ] **Step 2: Write the export command**

Create `src/apps/Odin.Hosting/Cli/Commands/IdentityJsonTransfer.cs`:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.DatabaseImport;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

public static class IdentityJsonTransfer
{
    internal static async Task ExportAsync(IServiceProvider services, string domain, string filePath)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var registry = services.GetRequiredService<IIdentityRegistry>();

        if (File.Exists(filePath))
        {
            logger.LogError("Refusing to overwrite existing file: {path}", filePath);
            return;
        }

        var registration = await registry.GetAsync(domain);
        if (registration == null)
        {
            logger.LogError("No such identity: {domain}", domain);
            return;
        }

        logger.LogWarning(
            "The export file contains this identity's password data, private keys, TLS "
            + "certificate private key and DKIM signing keys. Anyone holding it can become "
            + "this identity. Store it encrypted and delete it when the migration is done.");

        // Freeze before reading. Disabling alone only closes the HTTP front door; the
        // tenant's background workers do not check the flag and would keep writing.
        var wasDisabled = await registry.FreezeIdentityAsync(domain);
        try
        {
            var systemDatabase = services.GetRequiredService<SystemDatabase>();
            var systemMigrator = services.GetRequiredService<SystemMigrator>();

            var tenantScope = services.GetRequiredService<IMultiTenantContainer>().GetTenantScope(domain);
            var identityDatabase = tenantScope.Resolve<IdentityDatabase>();
            var identityMigrator = tenantScope.Resolve<IdentityMigrator>();

            await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write))
            {
                var rows = await IdentityJsonExporter.ExportAsync(
                    logger, stream, registration.Id, domain,
                    systemDatabase, identityDatabase,
                    await identityMigrator.GetCurrentVersionAsync(),
                    await systemMigrator.GetCurrentVersionAsync(),
                    callerHasFrozenIdentity: true);

                logger.LogInformation("Exported {rows} rows for {domain} to {path}", rows, domain, filePath);
            }

            // Owner-only. The file is the identity.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            // Always restore, even on failure: a frozen identity stays offline until
            // someone notices.
            try
            {
                await registry.UnfreezeIdentityAsync(domain, wasDisabled);
            }
            catch (Exception e)
            {
                logger.LogError(e,
                    "FAILED TO UNFREEZE {domain}. The identity is disabled and its background "
                    + "workers are stopped. Restart the host or unfreeze it manually.", domain);
                throw;
            }
        }
    }
}
```

**Operator note on `DkimKeys`.** The exported `DkimKeys.privateKey` values are AES-CBC
ciphertext under the server-wide `Email:DkimStorageKey` config value, not under anything
derived from the identity (`DkimStore.cs`). The import replays that ciphertext verbatim,
which is correct and faithful, but the target can only decrypt it if it is configured with
the **same** `Email:DkimStorageKey`. If the two hosts differ, the imported rows land intact
and unreadable, and `DkimStore` throws when the identity next signs mail. There is no way
for the importer to detect this: it never holds the storage key, and that key lives in
`Odin.Services` configuration which `Odin.Core.Storage` must not reference. The fix in that
case is to rotate the identity's DKIM keys and republish the DNS TXT records on the target,
which `MailActivationService` already does. Out of scope for this plan; call it out in the
runbook that goes with the CLI verbs.

- [ ] **Step 3: Write the import command**

Add to the same class. The header has to be read before the tenant scope can be built,
because the scope needs the identityId the file carries:

```csharp
    internal static async Task ImportAsync(IServiceProvider services, string filePath, bool commit)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();

        if (!File.Exists(filePath))
        {
            logger.LogError("Export file not found: {path}", filePath);
            return;
        }

        // Peek at the header to learn which identity this file is for. The importer
        // re-reads it and re-validates; this read is only to build the right scope.
        ExportHeader header;
        await using (var peek = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            using var document = await System.Text.Json.JsonDocument.ParseAsync(peek);
            var first = document.RootElement[0].GetRawText();
            header = System.Text.Json.JsonSerializer.Deserialize<ExportHeader>(
                first, Odin.Core.Serialization.OdinSystemSerializer.JsonSerializerOptions)
                ?? throw new InvalidOperationException("Export file has no readable header.");
        }

        var config = services.GetRequiredService<Odin.Services.Configuration.OdinConfiguration>();
        var workContainer = services.GetRequiredService<IMultiTenantContainer>();

        await using var targetScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(new Odin.Core.Identity.OdinIdentity(header.IdentityId, header.Domain))
              .SingleInstance();
            if (config.Database.Type == Odin.Core.Storage.Factory.DatabaseType.Postgres)
            {
                cb.AddPgsqlIdentityDatabaseServices(header.IdentityId, config.Database.ConnectionString);
            }
            else
            {
                cb.AddSqliteIdentityDatabaseServices(
                    header.IdentityId,
                    Odin.Services.Drives.FileSystem.Base.TenantPathManager
                        .GetIdentityDatabasePath(config, header.IdentityId));
            }
        });

        var targetIdentityDatabase = targetScope.Resolve<IdentityDatabase>();
        var targetSystemDatabase = services.GetRequiredService<SystemDatabase>();

        // A fresh identity has version -1 until its per-identity migrations run. Bring the
        // target to the latest schema before comparing table versions, exactly as
        // Sqlite2Pg.ImportIdentityAsync does.
        await targetScope.Resolve<IdentityMigrator>().MigrateAsync();

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var result = await IdentityJsonImporter.ImportAsync(
            logger, stream, targetSystemDatabase, targetIdentityDatabase, commit);

        logger.LogInformation("Imported {rows} rows for {domain} (commit: {commit})",
            result.RowsImported, result.Header.Domain, commit);
    }
```

`TenantPathManager.GetIdentityDatabasePath` may not exist under that name. Find the real
one with:

```bash
cd /workspace/odin-core
grep -rn "headers.*identity.db\|identity\.db" src/services/Odin.Services/Drives/FileSystem/Base/TenantPathManager.cs src/apps/Odin.Hosting/Cli/Commands/Sqlite2Pg.cs
```

`Sqlite2Pg.ImportAllAsync` derives the same path; use whatever it uses.

- [ ] **Step 4: Wire the verbs into `CommandLine.cs`**

Add beside the `sqlite2pg-*` block:

```csharp
        //
        // Command line: Export one identity's tables to a single JSON file
        //
        // Freezes the identity (disables it and stops its background workers), exports,
        // then unfreezes. The file contains key material; see the warning it prints.
        //
        // examples:
        //   dotnet run -- identity-export frodo.dotyou.cloud /path/to/frodo.json
        //
        if (args.Length >= 3 && args[0] == "identity-export")
        {
            IdentityJsonTransfer.ExportAsync(_serviceProvider, args[1], args[2]).BlockingWait();
            return (true, 0);
        }

        //
        // Command line: Import an identity export file
        //
        // Refuses unless the target is empty of this identity and every table version
        // matches. Dry run unless "commit" is passed.
        //
        // examples:
        //   dotnet run -- identity-import /path/to/frodo.json commit
        //
        if (args.Length >= 2 && args[0] == "identity-import")
        {
            var commit = args.Length >= 3 && args[2] == "commit";
            IdentityJsonTransfer.ImportAsync(_serviceProvider, args[1], commit).BlockingWait();
            return (true, 0);
        }
```

- [ ] **Step 5: Build**

Run: `cd /workspace/odin-core && dotnet build ./odin-core.sln`
Expected: build succeeded.

- [ ] **Step 6: Commit**

```bash
cd /workspace/odin-core
git add src/apps/Odin.Hosting/Cli/
git commit -m "Add identity-export and identity-import CLI verbs

Export freezes the identity, writes the file 0600, and unfreezes in a
finally so a failure does not leave the identity offline silently."
```

---

### Task 12: Replace whole-document parsing with a streaming read

Task 9 used `JsonDocument.ParseAsync`, which loads the entire file into memory. `DriveMainIndex` carries `hdrFileMetaData` and `hdrAppData` for every file the identity owns, so this is the first thing to fall over on a real identity. Split out so the round-trip behaviour is already proven before the read path changes.

**Files:**
- Modify: `src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonImporter.cs`
- Modify: `tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityJsonRoundTripTests.cs`

**Interfaces:**
- Consumes: everything from Task 9
- Produces: no signature change. `ImportAsync` keeps its exact signature; only the read strategy changes.

- [ ] **Step 1: Add a test that a large export imports without loading it all**

Add to `IdentityJsonRoundTripTests`:

```csharp
    // DriveMainIndex carries hdrFileMetaData and hdrAppData for every file the identity
    // owns, so whole-document parsing is the first thing to fall over on a real identity.
    [Test]
    public async Task Import_HandlesAnExportLargerThanIsComfortableInMemory()
    {
        _sourceScope = await _sourceServices.RegisterServicesAsync(
            DatabaseType.Sqlite, _sourceTempFolder, _identityId);
        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var srcId = _sourceScope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(srcSys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(srcId);

        // 2000 KeyValue rows with 4KB payloads: roughly 8MB of base64 in the file.
        for (var i = 0; i < 2000; i++)
        {
            await srcId.KeyValue.UpsertAsync(new KeyValueRecord
            {
                identityId = _identityId,
                key = Guid.NewGuid().ToByteArray(),
                data = new byte[4096],
            });
        }

        var logger = _sourceScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();
        var path = Path.Combine(_sourceTempFolder, "big.json");
        await using (var outFile = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            await IdentityJsonExporter.ExportAsync(
                logger, outFile, _identityId, IdentityDomain, srcSys, srcId,
                identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasFrozenIdentity: true);
        }

        _targetScope = await _targetServices.RegisterServicesAsync(
            DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        await using var inFile = new FileStream(path, FileMode.Open, FileAccess.Read);
        var result = await IdentityJsonImporter.ImportAsync(
            logger, inFile, tgtSys, tgtId, commit: true);

        Assert.That(result.RowsImported, Is.GreaterThan(2000));
    }
```

- [ ] **Step 2: Run it against the current implementation**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~Import_HandlesAnExportLargerThan"`
Expected: PASS, but slowly and with a large allocation spike. It documents the behaviour we are about to improve. Note the elapsed time from the test output to compare against after Step 3.

- [ ] **Step 3: Switch to `DeserializeAsyncEnumerable`**

Replace the `JsonDocument.ParseAsync` block in `ImportAsync`. The file is a top-level array, which is exactly what `DeserializeAsyncEnumerable` streams:

```csharp
        var enumerator = JsonSerializer
            .DeserializeAsyncEnumerable<JsonElement>(input, OdinSystemSerializer.JsonSerializerOptions)
            .GetAsyncEnumerator();

        try
        {
            if (!await enumerator.MoveNextAsync())
            {
                throw new InvalidOperationException("Export file is empty.");
            }

            var header = JsonSerializer.Deserialize<ExportHeader>(
                enumerator.Current.GetRawText(), OdinSystemSerializer.JsonSerializerOptions)
                ?? throw new InvalidOperationException("Export file has no readable header.");

            // ... preconditions and transactions exactly as before ...

            while (await enumerator.MoveNextAsync())
            {
                var element = enumerator.Current;
                // ... loop body unchanged ...
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
```

- [ ] **Step 4: Run the full test class**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj --filter "FullyQualifiedName~IdentityJsonRoundTripTests"`
Expected: all tests PASS, including the large one. Behaviour is unchanged; only memory profile improves.

- [ ] **Step 5: Run every test added by this plan**

Run:

```bash
cd /workspace/odin-core
dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj \
  --filter "FullyQualifiedName~IdentityJsonExport"
```

Expected: all PASS.

- [ ] **Step 6: Run the whole storage test project to check for regressions**

Run: `cd /workspace/odin-core && dotnet test ./tests/core/Odin.Core.Storage.Tests/Odin.Core.Storage.Tests.csproj`
Expected: no new failures versus `main`. If anything in `DataImporter*Tests` fails, the regenerated CRUD files changed existing behaviour, which they should not have. Investigate before committing.

- [ ] **Step 7: Commit**

```bash
cd /workspace/odin-core
git add src/core/Odin.Core.Storage/DatabaseImport/IdentityJsonImporter.cs \
        tests/core/Odin.Core.Storage.Tests/IdentityJsonExport/IdentityJsonRoundTripTests.cs
git commit -m "Stream the import instead of parsing the whole document

DeserializeAsyncEnumerable over the top-level array. A real identity's
DriveMainIndex rows make whole-document parsing the first thing to fail."
```

---

## Deferred, with reasons

Recorded in the spec's Open follow-ups and deliberately not in this plan:

- **Payload and thumbnail transfer.** The file covers tables only. An imported identity has file headers whose bytes are absent until payloads are copied by other means.
- **Stopping server-wide workers per identity.** `UpdateCertificatesBackgroundService` and `JobRunnerBackgroundService` are not tenant-scoped, so the freeze does not stop them. A lost certificate renewal self-heals on the target.
- **Switching `CopyRegistration` to use the new freeze.** It has the same gap, but changing it here would alter the behaviour of the existing export path.
- **Retiring `DataImportPatcher`** once the generated import path has proven its timestamp handling in practice.
- **Migrating `DataImporter` onto `ExportRowsAsync` / `ImportRowAsync`**, which would delete its 25-table enumeration, both source-scanning tests, and its `PageSize = 100` paging.
- **Fixing identity deletion.** `DeleteRegistration` leaving identity rows behind on Postgres is a known shortcoming; import precondition 3 guards against it rather than fixing it.
