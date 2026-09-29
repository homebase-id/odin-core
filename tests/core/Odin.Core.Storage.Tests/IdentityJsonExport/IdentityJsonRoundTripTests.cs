using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Tests.DatabaseImport;
using Odin.Core.Util;
using Odin.Test.Helpers;
using Odin.Test.Helpers.Logging;
using Odin.Core.Logging.Statistics.Serilog;
using Serilog.Events;

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

    private async Task<MemoryStream> SeedSourceAndExportAsync(DatabaseType sourceType, RowRewriter rewriteRow = null)
    {
        _sourceScope = await _sourceServices.RegisterServicesAsync(sourceType, _sourceTempFolder, _identityId);
        var sys = _sourceScope.Resolve<SystemDatabase>();
        var id = _sourceScope.Resolve<IdentityDatabase>();

        await DataImporterSeedHelper.SeedAllSystemTablesAsync(sys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(id);

        var logger = _sourceScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();
        var stream = new MemoryStream();
        await IdentityJsonExporter.ExportAsync(
            stream, _identityId, IdentityDomain, sys, id,
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerCheckedIdentityIsStill: true, rewriteRow: rewriteRow);
        stream.Position = 0;
        return stream;
    }

    [Test]
    public async Task Export_WritesWhatTheRewriterReturnsAndLeavesOutWhatItDrops()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite, (db, table, record) => (db, table, record) switch
        {
            (IdentityExportFile.DbSystem, "DkimKeys", _) => null,
            (IdentityExportFile.DbSystem, "Certificates", CertificatesRecord c) => c with { privateKey = "rewritten on export" },
            _ => record
        });
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, _targetScope.Resolve<IdentityDatabase>(), commit: true);

        var certificate = await tgtSys.Certificates.GetAsync(new OdinId(IdentityDomain));
        Assert.That(certificate?.privateKey, Is.EqualTo("rewritten on export"));
        Assert.That(await tgtSys.DkimKeys.GetByDomainAsync(new OdinId(IdentityDomain)), Is.Empty);
    }

    [Test]
    public async Task Import_WritesWhatTheRewriterReturnsAndCountsWhatItDropsAsSkipped()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        var result = await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, _targetScope.Resolve<IdentityDatabase>(),
            commit: true, rewriteRow: (db, table, record) => (db, table, record) switch
            {
                (IdentityExportFile.DbSystem, "DkimKeys", _) => null,
                (IdentityExportFile.DbSystem, "Certificates", CertificatesRecord c) => c with { privateKey = "rewritten on import" },
                _ => record
            });

        var certificate = await tgtSys.Certificates.GetAsync(new OdinId(IdentityDomain));
        Assert.That(certificate?.privateKey, Is.EqualTo("rewritten on import"));
        Assert.That(await tgtSys.DkimKeys.GetByDomainAsync(new OdinId(IdentityDomain)), Is.Empty);
        Assert.That(result.SkippedRowsByTable.GetValueOrDefault("DkimKeys"), Is.GreaterThan(0),
            string.Join(", ", result.SkippedRowsByTable.Select(kv => $"{kv.Key}={kv.Value}")));
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

        var (circles, _) = await tgtId.Circle.PagingByRowIdAsync(Int32.MaxValue, null);
        Assert.That(circles, Is.Not.Empty, "Circle rows should have been restored");
    }

    // Requirement 7: the regression DataImportPatcher exists to repair.
    [Test]
    public async Task Import_PreservesCreatedAndModifiedExactly()
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        var srcId = _sourceScope.Resolve<IdentityDatabase>();
        var (sourceDrives, _) = await srcId.Drives.PagingByRowIdAsync(Int32.MaxValue, null);

        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        var logger = _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>();

        await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true);

        var (targetDrives, _) = await tgtId.Drives.PagingByRowIdAsync(Int32.MaxValue, null);

        foreach (var source in sourceDrives)
        {
            var target = targetDrives.Single(d => d.DriveId == source.DriveId);
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
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<IdentityJsonRoundTripTests>(logStore);

        var result = await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true);

        Assert.That(result.SkippedRowsByTable.Keys, Does.Contain("Inbox"));
        Assert.That(result.SkippedRowsByTable.Keys, Does.Contain("Outbox"));
        Assert.That(result.SkippedRowsByTable.Keys, Does.Contain("Nonce"));
        Assert.That(result.SkippedRowsByTable["Outbox"], Is.GreaterThan(0),
            "Skipped tables must report a row count so the operator sees what was dropped");

        // Queued messages that do not move are worth a warning; stale nonces are not
        var events = logStore.GetLogEvents();
        var warnings = events[LogEventLevel.Warning].Select(e => e.RenderMessage()).ToList();
        var all = string.Join(Environment.NewLine, events.SelectMany(kv => kv.Value.Select(e => $"{kv.Key}: {e.RenderMessage()}")));
        Assert.That(warnings.Count(w => w.Contains("Inbox") && w.Contains("queued")), Is.EqualTo(1), all);
        Assert.That(warnings.Count(w => w.Contains("Outbox") && w.Contains("queued")), Is.EqualTo(1), all);
        Assert.That(warnings.Any(w => w.Contains("Nonce")), Is.False, all);
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
            stream, _identityId, IdentityDomain, srcSys, srcId,
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerCheckedIdentityIsStill: true);
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
        Assert.ThrowsAsync<IdentityImportRefusedException>(async () =>
            await IdentityJsonImporter.ImportAsync(logger, stream, tgtSys, tgtId, commit: true));

        Assert.That(await tgtId.CountRowsForIdentityAsync(_identityId), Is.EqualTo(rowsAfterFirst),
            "A failed precondition must write zero additional rows");
    }

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
                outFile, _identityId, IdentityDomain, srcSys, srcId,
                identitySchemaVersion: 1, systemSchemaVersion: 1, callerCheckedIdentityIsStill: true);
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
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task Import_ClearsRowsAFailedEarlierImportLeftBehind(DatabaseType targetType)
    {
        // An import whose identity commit landed but whose system commit did not leaves identity
        // rows with no registration. A rerun has to succeed, and without duplicating anything.
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        var sourceRows = await _sourceScope.Resolve<IdentityDatabase>().CountRowsForIdentityAsync(_identityId);

        _targetScope = await _targetServices.RegisterServicesAsync(targetType, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(tgtId);
        Assert.That(await tgtId.CountRowsForIdentityAsync(_identityId), Is.GreaterThan(0), "sanity: leftovers seeded");

        var result = await IdentityJsonImporter.ImportAsync(
            _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>(), stream, tgtSys, tgtId, commit: true);

        var skipped = result.SkippedRowsByTable.Values.Sum();
        Assert.That(await tgtId.CountRowsForIdentityAsync(_identityId), Is.EqualTo(sourceRows - skipped),
            $"source {sourceRows} rows, skipped {skipped}");
        Assert.That(await tgtSys.Registrations.GetAsync(_identityId), Is.Not.Null);
    }

    [Test]
    public async Task Import_RefusesATruncatedFileAndWritesNothing()
    {
        // A copy cut short mid-row (a transfer that did not finish) fails part way through the rows
        var full = (await SeedSourceAndExportAsync(DatabaseType.Sqlite)).ToArray();
        var truncated = new MemoryStream(full[..(full.Length * 2 / 3)]);

        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        var e = Assert.ThrowsAsync<IdentityImportRefusedException>(() => IdentityJsonImporter.ImportAsync(
            _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>(), truncated, tgtSys, tgtId, commit: true));

        Assert.That(e!.Message, Does.Contain("not valid JSON"), e.Message);
        Assert.That(await tgtSys.Registrations.GetAsync(_identityId), Is.Null);
        Assert.That(await tgtId.CountRowsForIdentityAsync(_identityId), Is.EqualTo(0));
    }

    [Test]
    public void ReadHeader_RefusesAFileThatIsNotJson()
    {
        var e = Assert.ThrowsAsync<IdentityImportRefusedException>(() =>
            IdentityJsonImporter.ReadHeaderAsync(new MemoryStream("this is not an export"u8.ToArray())));
        Assert.That(e!.Message, Does.Contain("not valid JSON"), e.Message);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Import_RunsBeforeCommitInsideItsTransactions(bool commit)
    {
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Sqlite, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        var ran = false;
        await IdentityJsonImporter.ImportAsync(
            _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>(), stream, tgtSys, tgtId, commit,
            beforeCommit: async () =>
            {
                ran = true;
                Assert.That(await tgtSys.Registrations.GetAsync(_identityId), Is.Not.Null, "the rows are in when it runs");
                await tgtSys.Settings.UpsertAsync(new SettingsRecord { key = "import-marker", value = "x" });
            });

        Assert.That(ran, Is.True);
        var marker = await tgtSys.Settings.GetAsync("import-marker");
        Assert.That(marker != null, Is.EqualTo(commit), "the hook's write must share the import's fate");
    }

#if RUN_POSTGRES_TESTS
    [Test]
    public async Task Import_LandsNoRegistrationWhenTheIdentityCommitFails()
    {
        // The registration is the commit point: if the identity rows fail to commit, the system rows must
        // not commit either. A deferred constraint violated in the identity transaction makes exactly
        // that commit fail (Postgres only: SQLite enforces no deferred constraint here).
        var stream = await SeedSourceAndExportAsync(DatabaseType.Sqlite);
        _targetScope = await _targetServices.RegisterServicesAsync(DatabaseType.Postgres, _targetTempFolder, _identityId);
        var tgtSys = _targetScope.Resolve<SystemDatabase>();
        var tgtId = _targetScope.Resolve<IdentityDatabase>();

        Assert.CatchAsync(() => IdentityJsonImporter.ImportAsync(
            _targetScope.Resolve<ILogger<IdentityJsonRoundTripTests>>(), stream, tgtSys, tgtId, commit: true,
            beforeCommit: async () =>
            {
                await using var cn = await tgtId.CreateScopedConnectionAsync();
                await using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    "CREATE TEMP TABLE import_fail_parent (id int PRIMARY KEY);" +
                    "CREATE TEMP TABLE import_fail_child (parent int REFERENCES import_fail_parent (id) DEFERRABLE INITIALLY DEFERRED);" +
                    "INSERT INTO import_fail_child VALUES (1);";
                await cmd.ExecuteNonQueryAsync();
            }));

        Assert.That(await tgtSys.Registrations.GetAsync(_identityId), Is.Null, "the registration committed without its identity");
        Assert.That(await tgtId.CountRowsForIdentityAsync(_identityId), Is.EqualTo(0));
    }
#endif
}
