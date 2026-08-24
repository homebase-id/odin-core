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
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Tests.DatabaseImport;
using Odin.Core.Util;
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
