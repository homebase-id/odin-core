using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Test.Helpers;

namespace Odin.Core.Storage.Tests.DatabaseImport;

// End-to-end import tests. Spins up a SOURCE database pair and a TARGET database pair, seeds
// every table on the source via DataImporterSeedHelper, runs DataImporter, and asserts the
// target's row counts match (using SqlHelper.GetCountAsync + reflection over TableTypes).
//
// This is the runtime counterpart to DataImporterTests, which only does static source-code
// scanning. Together they catch (a) developers forgetting to add a new table to the importer
// and (b) actual data-copy bugs in the importer (paging, transactions, special-case filters).
//
// Both source and target are configured with the same identityId so the InsertAsync overrides
// on identity tables (which auto-fill identityId from the DI-injected OdinIdentity) produce
// matching counts on both sides.
public class DataImporterEndToEndTests
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
        _sourceScope = null!;
        _targetScope = null!;

        if (Directory.Exists(_sourceTempFolder))
            Directory.Delete(_sourceTempFolder, true);
        if (Directory.Exists(_targetTempFolder))
            Directory.Delete(_targetTempFolder, true);

        // SQLite holds file locks until the GC reclaims connection wrappers.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private async Task InitDatabasesAsync(DatabaseType sourceType, DatabaseType targetType)
    {
        _sourceScope = await _sourceServices.RegisterServicesAsync(sourceType, _sourceTempFolder, _identityId);
        _targetScope = await _targetServices.RegisterServicesAsync(targetType, _targetTempFolder, _identityId);
    }

    //
    // Tests
    //

    // Coverage guard. Fast: source-only, no import. Catches a developer adding a new table
    // without adding a corresponding seeder. Surfaces the failure with a single targeted
    // assertion instead of failing inside the heavier import tests below.
    [Test]
    public async Task SeedHelper_PopulatesEveryTable()
    {
        await InitDatabasesAsync(DatabaseType.Sqlite, DatabaseType.Sqlite);
        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var srcId = _sourceScope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(srcSys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(srcId);

        var idCounts = await GetTableCountsAsync(srcId, IdentityDatabase.TableTypes);
        var sysCounts = await GetTableCountsAsync(srcSys, SystemDatabase.TableTypes);

        var unseeded = idCounts.Concat(sysCounts)
            .Where(kv => kv.Value == 0)
            .Select(kv => kv.Key)
            .ToList();

        Assert.That(unseeded, Is.Empty,
            "DataImporterSeedHelper is missing seeders for: " + string.Join(", ", unseeded));
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Sqlite, DatabaseType.Postgres)]
#endif
    public async Task ImportIdentityAsync_CopiesEveryTable_WhenCommitTrue(DatabaseType sourceType, DatabaseType targetType)
    {
        await InitDatabasesAsync(sourceType, targetType);

        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var srcId = _sourceScope.Resolve<IdentityDatabase>();
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(srcSys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(srcId);

        var logger = _sourceScope.Resolve<ILogger<DataImporterEndToEndTests>>();
        await DataImporter.ImportIdentityAsync(logger, IdentityDomain, srcSys, tgtSys, srcId, tgtId, commit: true);

        // All identity tables should be copied 1:1
        await AssertTableCountsMatchAsync(srcId, tgtId, IdentityDatabase.TableTypes);

        // Of system tables, only Registrations + Certificates are imported per-identity
        var perIdentitySystemTables = new HashSet<string>
        {
            "Registrations",
            "Certificates",
        };
        await AssertSelectiveSystemCountsAsync(srcSys, tgtSys, perIdentitySystemTables);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Sqlite, DatabaseType.Postgres)]
#endif
    public async Task ImportIdentityAsync_LeavesTargetEmpty_WhenCommitFalse(
        DatabaseType sourceType, DatabaseType targetType)
    {
        await InitDatabasesAsync(sourceType, targetType);
        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var srcId = _sourceScope.Resolve<IdentityDatabase>();
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(srcSys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(srcId);

        var logger = _sourceScope.Resolve<ILogger<DataImporterEndToEndTests>>();
        await DataImporter.ImportIdentityAsync(
            logger, IdentityDomain, srcSys, tgtSys, srcId, tgtId, commit: false);

        // Dry run: target databases must be untouched
        await AssertAllTablesEmptyAsync(tgtId, IdentityDatabase.TableTypes);
        await AssertAllTablesEmptyAsync(tgtSys, SystemDatabase.TableTypes);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Sqlite, DatabaseType.Postgres)]
#endif
    public async Task ImportAllSystemDataAsync_CopiesEverySystemTable(
        DatabaseType sourceType, DatabaseType targetType)
    {
        await InitDatabasesAsync(sourceType, targetType);
        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var tgtSys = _targetScope.Resolve<SystemDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(srcSys, IdentityDomain, _identityId);

        var logger = _sourceScope.Resolve<ILogger<DataImporterEndToEndTests>>();
        await DataImporter.ImportAllSystemDataAsync(logger, srcSys, tgtSys, commit: true);

        await AssertTableCountsMatchAsync(srcSys, tgtSys, SystemDatabase.TableTypes);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Sqlite, DatabaseType.Postgres)]
#endif
    public async Task ImportAllSystemDataAsync_BailsOut_WhenTargetHasRegistrations(
        DatabaseType sourceType, DatabaseType targetType)
    {
        await InitDatabasesAsync(sourceType, targetType);
        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var tgtSys = _targetScope.Resolve<SystemDatabase>();

        // Source is fully populated; target is also pre-seeded with a different identity's
        // registration. The importer must refuse rather than silently merging the two.
        await DataImporterSeedHelper.SeedAllSystemTablesAsync(srcSys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllSystemTablesAsync(
            tgtSys, "other.dotyou.cloud", Guid.NewGuid());

        var preCounts = await GetTableCountsAsync(tgtSys, SystemDatabase.TableTypes);
        Assert.That(preCounts["Registrations"], Is.EqualTo(1),
            "Test setup: target should have one pre-existing registration to trigger the bailout");

        var logger = _sourceScope.Resolve<ILogger<DataImporterEndToEndTests>>();
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DataImporter.ImportAllSystemDataAsync(logger, srcSys, tgtSys, commit: true));

        Assert.That(ex!.Message, Does.Contain("registration").IgnoreCase);

        // Target must be exactly as it was before the failed call — no partial import.
        var postCounts = await GetTableCountsAsync(tgtSys, SystemDatabase.TableTypes);
        Assert.That(postCounts, Is.EqualTo(preCounts),
            "Target system database must be unchanged after a refused import");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Sqlite, DatabaseType.Postgres)]
#endif
    public async Task ImportIdentityOnlyAsync_CopiesIdentityTables_LeavesSystemUntouched(
        DatabaseType sourceType, DatabaseType targetType)
    {
        await InitDatabasesAsync(sourceType, targetType);
        var srcSys = _sourceScope.Resolve<SystemDatabase>();
        var srcId = _sourceScope.Resolve<IdentityDatabase>();
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(srcSys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(srcId);

        var logger = _sourceScope.Resolve<ILogger<DataImporterEndToEndTests>>();
        await DataImporter.ImportIdentityOnlyAsync(
            logger, IdentityDomain, srcId, tgtId, commit: true);

        await AssertTableCountsMatchAsync(srcId, tgtId, IdentityDatabase.TableTypes);
        await AssertAllTablesEmptyAsync(tgtSys, SystemDatabase.TableTypes);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task DeleteIdentityFromSystemDataAsync_RemovesOnlyTheNamedIdentity(
        DatabaseType targetType)
    {
        // Source isn't used by this test, but InitDatabasesAsync requires both sides.
        await InitDatabasesAsync(DatabaseType.Sqlite, targetType);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();

        // First identity gets the full seed (Jobs/Settings/LastSeen + per-identity rows).
        var keepDomain = "frodo.dotyou.cloud";
        var keepIdentityId = Guid.NewGuid();
        await DataImporterSeedHelper.SeedAllSystemTablesAsync(tgtSys, keepDomain, keepIdentityId);

        // Second identity adds only its per-identity rows (Registrations + Certificates).
        // We can't call SeedAllSystemTablesAsync again because LastSeen/Settings have UNIQUE
        // constraints on hardcoded keys.
        var dropDomain = "sam.dotyou.cloud";
        var dropIdentityId = Guid.NewGuid();
        await tgtSys.Registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = dropIdentityId,
            email = "sam@example.com",
            primaryDomainName = dropDomain,
        });
        await tgtSys.Certificates.InsertAsync(new CertificatesRecord
        {
            domain = new OdinId(dropDomain),
            privateKey = "x",
            certificate = "x",
            expiration = UnixTimeUtc.Now().AddSeconds(3600),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = "x",
        });

        // Sanity: both identities are present before cleanup.
        var registrationsBefore = await tgtSys.Registrations.GetAllAsync();
        Assert.That(registrationsBefore.Select(r => r.primaryDomainName),
            Is.EquivalentTo(new[] { keepDomain, dropDomain }));

        var logger = _sourceScope.Resolve<ILogger<DataImporterEndToEndTests>>();
        await DataImporter.DeleteIdentityFromSystemDataAsync(
            logger, tgtSys, dropIdentityId, dropDomain);

        // Only the named identity's rows are gone; the other identity is untouched.
        var registrationsAfter = await tgtSys.Registrations.GetAllAsync();
        Assert.That(registrationsAfter.Select(r => r.primaryDomainName),
            Is.EquivalentTo(new[] { keepDomain }),
            "Cleanup should only delete the named identity's registration");

        await using (var cn = await tgtSys.CreateScopedConnectionAsync())
        {
            Assert.That(await SqlHelper.GetCountAsync(cn, "Certificates"), Is.EqualTo(1),
                "Cleanup should only delete the named identity's certificate");

            // Non-per-identity tables (Jobs, LastSeen, Settings) must be untouched.
            Assert.That(await SqlHelper.GetCountAsync(cn, "Jobs"), Is.EqualTo(1));
            Assert.That(await SqlHelper.GetCountAsync(cn, "LastSeen"), Is.EqualTo(1));
            Assert.That(await SqlHelper.GetCountAsync(cn, "Settings"), Is.EqualTo(1));
        }
    }

    //
    // Reflection-based count helpers
    //

    private static async Task AssertTableCountsMatchAsync<T>(
        T source, T target, IEnumerable<Type> tableTypes)
        where T : notnull
    {
        var srcCounts = await GetTableCountsAsync(source, tableTypes);
        var tgtCounts = await GetTableCountsAsync(target, tableTypes);

        var mismatches = new List<string>();
        foreach (var (tableName, srcCount) in srcCounts)
        {
            var tgtCount = tgtCounts[tableName];
            if (srcCount != tgtCount)
                mismatches.Add($"{tableName}: source={srcCount} target={tgtCount}");
        }

        Assert.That(mismatches, Is.Empty,
            "Row counts diverged after import: " + string.Join("; ", mismatches));

        // Sanity: every table should have at least one row, otherwise we're not actually
        // testing anything for that table.
        var emptyOnSource = srcCounts.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
        Assert.That(emptyOnSource, Is.Empty,
            "Seed helper produced no rows for: " + string.Join(", ", emptyOnSource));
    }

    private static async Task AssertAllTablesEmptyAsync<T>(
        T database, IEnumerable<Type> tableTypes)
        where T : notnull
    {
        var counts = await GetTableCountsAsync(database, tableTypes);
        var nonEmpty = counts.Where(kv => kv.Value != 0).Select(kv => $"{kv.Key}={kv.Value}").ToList();
        Assert.That(nonEmpty, Is.Empty,
            "Expected all tables to be empty, but found: " + string.Join(", ", nonEmpty));
    }

    // For per-identity import: tables whose name is in `expectedNonEmpty` should match the
    // source counts (rows for this identity). All others should be empty on the target.
    private static async Task AssertSelectiveSystemCountsAsync(
        SystemDatabase source, SystemDatabase target, ISet<string> expectedNonEmpty)
    {
        var srcCounts = await GetTableCountsAsync(source, SystemDatabase.TableTypes);
        var tgtCounts = await GetTableCountsAsync(target, SystemDatabase.TableTypes);

        var failures = new List<string>();
        foreach (var (tableName, srcCount) in srcCounts)
        {
            var tgtCount = tgtCounts[tableName];
            if (expectedNonEmpty.Contains(tableName))
            {
                if (srcCount == 0)
                    failures.Add($"{tableName}: source seed produced no rows (test setup bug)");
                if (tgtCount != srcCount)
                    failures.Add($"{tableName}: source={srcCount} target={tgtCount} (expected match)");
            }
            else
            {
                if (tgtCount != 0)
                    failures.Add($"{tableName}: target={tgtCount} (expected 0, not imported per-identity)");
            }
        }

        Assert.That(failures, Is.Empty,
            "Per-identity system import diverged: " + string.Join("; ", failures));
    }

    private static async Task<Dictionary<string, int>> GetTableCountsAsync<T>(
        T database, IEnumerable<Type> tableTypes)
        where T : notnull
    {
        var counts = new Dictionary<string, int>();
        var connection = await OpenConnectionAsync(database);
        await using (connection)
        {
            foreach (var tableType in tableTypes)
            {
                var tableName = ResolveTableName(database, tableType);
                counts[tableName] = await SqlHelper.GetCountAsync(connection, tableName);
            }
        }
        return counts;
    }

    private static Task<IConnectionWrapper> OpenConnectionAsync<T>(T database) where T : notnull
    {
        return database switch
        {
            IdentityDatabase id => id.CreateScopedConnectionAsync(),
            SystemDatabase sd => sd.CreateScopedConnectionAsync(),
            _ => throw new ArgumentException($"Unsupported database type: {database.GetType()}"),
        };
    }

    private static string ResolveTableName(object database, Type tableType)
    {
        var prop = database.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.PropertyType == tableType)
            ?? throw new InvalidOperationException(
                $"No property of type {tableType.Name} on {database.GetType().Name}");
        var instance = (TableBase)prop.GetValue(database)!;
        return instance.TableName;
    }
}
