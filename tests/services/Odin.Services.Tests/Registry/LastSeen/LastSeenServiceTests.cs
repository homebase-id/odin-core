using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Registry;
using Odin.Services.Registry.LastSeen;
using Odin.Test.Helpers;

namespace Odin.Services.Tests.Registry.LastSeen;

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
    public async Task LastSeenNow_WithIdentityRegistration_SetsLastSeenToNow(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var now = UnixTimeUtc.Now();
        var registration = new IdentityRegistration { Id = Guid.NewGuid(), PrimaryDomainName = "test.domain" };

        await lastSeenService.LastSeenNowAsync(registration);

        var lastSeenId = await lastSeenService.GetLastSeenAsync(registration.Id);
        var lastSeenDomain = await lastSeenService.GetLastSeenAsync(registration.PrimaryDomainName);

        Assert.That(lastSeenId, Is.Not.Null);
        Assert.That(lastSeenDomain, Is.Not.Null);

        // Approximate check due to timing; in practice, use a mockable time provider for exactness
        Assert.That(lastSeenId!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
        Assert.That(lastSeenDomain!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task LastSeenNow_WithIdAndDomain_SetsLastSeenToNow(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var now = UnixTimeUtc.Now();
        var id = Guid.NewGuid();
        var domain = "test.domain";

        await lastSeenService.LastSeenNowAsync(id, domain);

        var lastSeenId = await lastSeenService.GetLastSeenAsync(id);
        var lastSeenDomain = await lastSeenService.GetLastSeenAsync(domain);

        Assert.That(lastSeenId, Is.Not.Null);
        Assert.That(lastSeenDomain, Is.Not.Null);
        Assert.That(lastSeenId!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
        Assert.That(lastSeenDomain!.Value.milliseconds, Is.GreaterThanOrEqualTo(now.milliseconds));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task PutLastSeen_WithIdentityRegistration_SetsSpecificLastSeen(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var lastSeen = UnixTimeUtc.Now().AddSeconds(-100);
        var registration = new IdentityRegistration { Id = Guid.NewGuid(), PrimaryDomainName = "test.domain" };

        await lastSeenService.PutLastSeenAsync(registration, lastSeen);

        Assert.That(await lastSeenService.GetLastSeenAsync(registration.Id), Is.EqualTo(lastSeen));
        Assert.That(await lastSeenService.GetLastSeenAsync(registration.PrimaryDomainName), Is.EqualTo(lastSeen));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task PutLastSeen_WithIdAndDomain_SetsSpecificLastSeen(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var lastSeen = UnixTimeUtc.Now().AddSeconds(-100);
        var id = Guid.NewGuid();
        var domain = "test.domain";

        await lastSeenService.PutLastSeenAsync(id, domain, lastSeen);

        Assert.That(await lastSeenService.GetLastSeenAsync(id), Is.EqualTo(lastSeen));
        Assert.That(await lastSeenService.GetLastSeenAsync(domain), Is.EqualTo(lastSeen));
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task PutLastSeen_WithRegistrationsRecord_SetsLastSeenIfNotNull(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var lastSeen = UnixTimeUtc.Now().AddSeconds(-100);
        var record = new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            primaryDomainName = "test.domain",
            lastSeen = lastSeen
        };

        await lastSeenService.PutLastSeenAsync(record);

        Assert.That(await lastSeenService.GetLastSeenAsync(record.identityId), Is.EqualTo(lastSeen));
        Assert.That(await lastSeenService.GetLastSeenAsync(record.primaryDomainName), Is.EqualTo(lastSeen));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task PutLastSeen_WithRegistrationsRecordNullLastSeen_DoesNothing(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var record = new RegistrationsRecord
        {
            identityId = Guid.NewGuid(),
            primaryDomainName = "test.domain",
            lastSeen = null
        };

        await lastSeenService.PutLastSeenAsync(record);

        Assert.That(await lastSeenService.GetLastSeenAsync(record.identityId), Is.Null);
        Assert.That(await lastSeenService.GetLastSeenAsync(record.primaryDomainName), Is.Null);
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

        var result = await lastSeenService.GetLastSeenAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task GetLastSeen_WithDomain_ReturnsNull(DatabaseType databaseType, bool enableRedis)
    {
        var services = await _testServices!.RegisterServicesAsync(databaseType, _tempDir, _identityId, true, enableRedis);
        var lastSeenService = services.Resolve<ILastSeenService>();

        var result = await lastSeenService.GetLastSeenAsync("foo.bar");

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

        var id = Guid.NewGuid();
        var domain = "test.domain";
        var initial = UnixTimeUtc.Now().AddSeconds(-200);
        var updated = UnixTimeUtc.Now().AddSeconds(-100);

        await lastSeenService.PutLastSeenAsync(id, domain, initial);
        await lastSeenService.PutLastSeenAsync(id, domain, updated);

        Assert.That(await lastSeenService.GetLastSeenAsync(id), Is.EqualTo(updated));
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

        var id = Guid.NewGuid();
        var domain = "test.domain";
        var initial = UnixTimeUtc.Now().AddSeconds(-2);
        var updated = UnixTimeUtc.Now().AddSeconds(-1);

        await lastSeenService.PutLastSeenAsync(id, domain, initial);
        await lastSeenService.PutLastSeenAsync(id, domain, updated);

        Assert.That(await lastSeenService.GetLastSeenAsync(id), Is.EqualTo(initial));
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
        var registrations = services.Resolve<TableRegistrations>();

        var identityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var domain = "test.domain";
        await registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = identityId,
            email = "frodo@baggins.com",
            primaryDomainName = domain
        });

        await lastSeenService.PutLastSeenAsync(identityId, domain, UnixTimeUtc.Now());

        var record = await registrations.GetLastSeenAsync(identityId);
        Assert.That(record, Is.Null);

        await lastSeenService.UpdateDatabaseAsync();
        record = await registrations.GetLastSeenAsync(identityId);
        Assert.That(record, Is.Not.Null);
    }
}