using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Tests.Database.Identity;

// IdentityDataImporter.ImportAsync must explicitly call ImportTableAsync for each table
// in IdentityDatabase because the table record types don't share a common base type or interface,
// so there's no way to iterate over them generically at runtime. Each table has its own
// PagingByRowIdAsync and InsertAsync with table-specific record types, and some tables need
// special handling (e.g. Nonce skips expired records).
//
// The risk is that when a new table is added to IdentityDatabase, the developer forgets to add
// the corresponding ImportTableAsync call here, and the data migration silently drops that table.
//
// This test guards against that by using reflection to discover all table properties on
// IdentityDatabase (via the authoritative TableTypes list) and then scanning the
// IdentityDataImporter source code to verify each table is referenced. It reads the actual .cs
// file because there's no runtime artifact to inspect — the per-table calls are just sequential
// method invocations, not a data structure we can enumerate.
//
// The "real" fix would be to eliminate the per-table enumeration entirely:
//   1. Define a common interface like IMigratableTable<TRecord> exposing PagingByRowIdAsync
//      and InsertAsync with a shared signature.
//   2. Have each Table* class implement it (each record type would also need a common base
//      type or interface so the generic constraint works across tables).
//   3. Have IdentityDatabase expose an IEnumerable<IMigratableTable> of all its tables.
//   4. ImportAsync would then just iterate that list — no per-table calls, nothing to forget.
//   5. Tables needing special handling (e.g. Nonce filtering expired records) could override
//      a virtual migration method or be registered with a custom migration delegate.
// That refactor touches every table class and every record type, so this source-scanning test
// is the pragmatic stopgap until that investment is justified.
public class IdentityDataImporterTests
{
    [Test]
    public void ImportAsyncCoversAllTables()
    {
        // Get all table property names from IdentityDatabase using the authoritative TableTypes list
        var tableTypeSet = IdentityDatabase.TableTypes.ToHashSet();
        var tablePropertyNames = typeof(IdentityDatabase)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => tableTypeSet.Contains(p.PropertyType))
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        // Read the IdentityDataImporter source file
        var solutionRoot = FindSolutionRoot();
        var migratorSourcePath = Path.Combine(solutionRoot,
            "src", "core", "Odin.Core.Storage", "Database", "Identity", "IdentityDataImporter.cs");
        var sourceCode = File.ReadAllText(migratorSourcePath);

        // Verify each table property is referenced in the migrator
        var missingTables = tablePropertyNames
            .Where(name => !sourceCode.Contains($"sourceDb.{name}.PagingByRowIdAsync"))
            .ToList();

        Assert.That(missingTables, Is.Empty,
            $"IdentityDataImporter.ImportAsync is missing migration for table(s): {string.Join(", ", missingTables)}");
    }

    private static string FindSolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "odin-core.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Could not find solution root (odin-core.sln)");
    }
}
