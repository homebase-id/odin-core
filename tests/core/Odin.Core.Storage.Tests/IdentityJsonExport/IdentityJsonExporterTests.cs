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
using Odin.Core.Storage.Database.Identity.Table;
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
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerCheckedIdentityIsStill: true);
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

    // Requirement 9: the exporter has no way to see whether a host is still writing,
    // so it takes the caller's assertion and refuses a false one.
    [Test]
    public void ExportAsync_ThrowsWhenCallerHasNotCheckedTheIdentityIsStill()
    {
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            _scope = await _services.RegisterServicesAsync(DatabaseType.Sqlite, _tempFolder, _identityId);
            var logger = _scope.Resolve<ILogger<IdentityJsonExporterTests>>();
            await IdentityJsonExporter.ExportAsync(
                logger, new MemoryStream(), _identityId, IdentityDomain,
                _scope.Resolve<SystemDatabase>(), _scope.Resolve<IdentityDatabase>(),
                identitySchemaVersion: 1, systemSchemaVersion: 1, callerCheckedIdentityIsStill: false);
        });
    }
    [Test]
    public async Task ExportAsync_ReachesTheStreamRowByRow()
    {
        // Memory stays flat only if rows leave the writer as they are written. Serializing a value
        // into a Utf8JsonWriter flushes it; if that ever stops, the whole export would sit in memory
        // and reach the stream in one write at the end
        _scope = await _services.RegisterServicesAsync(DatabaseType.Sqlite, _tempFolder, _identityId);
        var sys = _scope.Resolve<SystemDatabase>();
        var id = _scope.Resolve<IdentityDatabase>();
        await DataImporterSeedHelper.SeedAllSystemTablesAsync(sys, IdentityDomain, _identityId);
        for (var i = 0; i < 8; i++)
        {
            await id.KeyValue.InsertAsync(new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = System.Security.Cryptography.RandomNumberGenerator.GetBytes(512 * 1024) });
        }

        var stream = new WriteRecordingStream();
        await IdentityJsonExporter.ExportAsync(
            _scope.Resolve<ILogger<IdentityJsonExporterTests>>(), stream, _identityId, IdentityDomain, sys, id,
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerCheckedIdentityIsStill: true);

        var writes = string.Join(", ", stream.Writes);
        const int oneRow = 1024 * 1024; // a 512 KB value, base64 encoded, plus its envelope
        Assert.That(stream.Length, Is.GreaterThan(4 * oneRow), $"export too small to test: {stream.Length} bytes");
        Assert.That(stream.Writes.Max(), Is.LessThan(oneRow), $"writes: {writes}");
    }

    [Test]
    public async Task ExportToFileAsync_WritesTheFileOnlyOnceComplete()
    {
        var path = Path.Combine(_tempFolder, "frodo.json");
        await SeedAndExportToFileAsync(path, callerCheckedIdentityIsStill: true);

        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.Exists(path + ".partial"), Is.False);
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }

        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.That(doc.RootElement.GetArrayLength(), Is.GreaterThan(1));
    }

    [Test]
    public void ExportToFileAsync_LeavesNothingBehindWhenTheExportFails()
    {
        var path = Path.Combine(_tempFolder, "frodo.json");
        Assert.ThrowsAsync<InvalidOperationException>(() => SeedAndExportToFileAsync(path, callerCheckedIdentityIsStill: false));

        Assert.That(File.Exists(path), Is.False);
        Assert.That(File.Exists(path + ".partial"), Is.False);
    }

    [Test]
    public async Task ExportToFileAsync_RefusesAPartialFileLeftByACrash()
    {
        var path = Path.Combine(_tempFolder, "frodo.json");
        await File.WriteAllTextAsync(path + ".partial", "left over");

        var e = Assert.ThrowsAsync<IOException>(() => SeedAndExportToFileAsync(path, callerCheckedIdentityIsStill: true));

        Assert.That(e!.Message, Does.Contain("left over from an export that did not finish"));
        Assert.That(await File.ReadAllTextAsync(path + ".partial"), Is.EqualTo("left over"), "the partial file was touched");
        Assert.That(File.Exists(path), Is.False);
    }

    private async Task SeedAndExportToFileAsync(string path, bool callerCheckedIdentityIsStill)
    {
        _scope = await _services.RegisterServicesAsync(DatabaseType.Sqlite, _tempFolder, _identityId);
        var sys = _scope.Resolve<SystemDatabase>();
        var id = _scope.Resolve<IdentityDatabase>();
        await DataImporterSeedHelper.SeedAllSystemTablesAsync(sys, IdentityDomain, _identityId);
        await DataImporterSeedHelper.SeedAllIdentityTablesAsync(id);

        await IdentityJsonExporter.ExportToFileAsync(
            _scope.Resolve<ILogger<IdentityJsonExporterTests>>(), path, _identityId, IdentityDomain, sys, id,
            identitySchemaVersion: 1, systemSchemaVersion: 1, callerCheckedIdentityIsStill);
    }

    // Records the size of every write the exporter makes (MemoryStream's async writes land in these)
    private sealed class WriteRecordingStream : MemoryStream
    {
        public List<int> Writes { get; } = [];

        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes.Add(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Writes.Add(buffer.Length);
            base.Write(buffer);
        }
    }
}
