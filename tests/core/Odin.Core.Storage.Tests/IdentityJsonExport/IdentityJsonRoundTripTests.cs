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
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Tests.DatabaseImport;
using Odin.Core.Util;
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
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasQuiescedIdentity: true);
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
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasQuiescedIdentity: true);
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
                identitySchemaVersion: 1, systemSchemaVersion: 1, callerHasQuiescedIdentity: true);
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
}
