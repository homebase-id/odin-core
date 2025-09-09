using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.LastSeen;
using Odin.Test.Helpers;

namespace Odin.Services.Tests.LastSeen;

[TestFixture]
public class LastSeenServiceTests
{
    private string _tempDir = "";
    private TestServices? _testServices;
    private Guid _identityId = Guid.NewGuid();

    [SetUp]
    public void Setup()
    {
        _tempDir = TempDirectory.Create();
        _testServices = new TestServices();
        _identityId = Guid.NewGuid();
    }

    [TearDown]
    public void TearDown()
    {
        _testServices?.Dispose();

        Directory.Delete(_tempDir, true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task LastSeenNow_SetsLastSeenToNow(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var now = UnixTimeUtc.Now();
        var domain = "frodo.me";

        await lastSeenService.LastSeenNowAsync(domain);

        var lastSeenId = await lastSeenService.GetLastSeenAsync(domain);

        Assert.That(lastSeenId, Is.Not.Null);
        Assert.That(lastSeenId!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task PutLastSeen_SetsSpecificLastSeen(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var lastSeen = UnixTimeUtc.Now().AddSeconds(-100);
        var domain = "frodo.me";

        await lastSeenService.PutLastSeenAsync(domain, lastSeen);

        Assert.That(await lastSeenService.GetLastSeenAsync(domain), Is.EqualTo(lastSeen));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task GetLastSeen_WithUnknownId_ReturnsNull(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var result = await lastSeenService.GetLastSeenAsync("gandalf.me");

        Assert.That(result, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task UpdatesOverwriteExistingValues(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var domain = "frodo.me";
        var initial = UnixTimeUtc.Now().AddSeconds(-200);
        var updated = UnixTimeUtc.Now().AddSeconds(-100);

        await lastSeenService.PutLastSeenAsync(domain, initial);
        await lastSeenService.PutLastSeenAsync(domain, updated);

        Assert.That(await lastSeenService.GetLastSeenAsync(domain), Is.EqualTo(updated));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task SmallUpdatesDontOverwriteExistingValues(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var domain = "frodo.me";
        var initial = UnixTimeUtc.Now().AddSeconds(-2);
        var updated = UnixTimeUtc.Now().AddSeconds(-1);

        await lastSeenService.PutLastSeenAsync(domain, initial);
        await lastSeenService.PutLastSeenAsync(domain, updated);

        Assert.That(await lastSeenService.GetLastSeenAsync(domain), Is.EqualTo(initial));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task ItShouldUpdateDatabaseOnDemand(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = (LastSeenService)services.Resolve<ILastSeenService>();
        var lastSeenTable = services.Resolve<TableLastSeen>();

        var identityId = "frodo.me";

        await lastSeenService.PutLastSeenAsync(identityId, UnixTimeUtc.Now());

        var record = await lastSeenTable.GetLastSeenAsync(identityId);
        Assert.That(record, Is.Null);

        await lastSeenService.UpdateDatabaseAsync();
        record = await lastSeenTable.GetLastSeenAsync(identityId);
        Assert.That(record, Is.Not.Null);
    }
}