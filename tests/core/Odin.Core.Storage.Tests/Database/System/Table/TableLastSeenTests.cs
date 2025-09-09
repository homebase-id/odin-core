using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.System.Table;

public class TableLastSeenTests : IocTestBase
{
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldUpdateLastSeen(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var tableLastSeen = scope.Resolve<TableLastSeen>();

        var identityId1 = "frodo.me";
        var identityId2 = "sam.me";

        var now = UnixTimeUtc.Now();
        var lastSeen = new Dictionary<string, UnixTimeUtc>
        {
            { identityId1, now },
            { identityId2, now },
        };

        await tableLastSeen.UpdateLastSeenAsync(lastSeen);

        var all = await tableLastSeen.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all[0].timestamp, Is.EqualTo(now));
        Assert.That(all[1].timestamp, Is.EqualTo(now));

        var older = now.AddMilliseconds(-10000);
        lastSeen = new Dictionary<string, UnixTimeUtc>
        {
            { identityId1, older },
            { identityId2, older },
        };

        await tableLastSeen.UpdateLastSeenAsync(lastSeen);
        all = await tableLastSeen.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all[0].timestamp, Is.EqualTo(now));
        Assert.That(all[1].timestamp, Is.EqualTo(now));

        var newer = now.AddMilliseconds(10000);
        lastSeen = new Dictionary<string, UnixTimeUtc>
        {
            { identityId1, newer },
            { identityId2, newer },
        };

        await tableLastSeen.UpdateLastSeenAsync(lastSeen);
        all = await tableLastSeen.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(2));
        Assert.That(all[0].timestamp, Is.EqualTo(newer));
        Assert.That(all[1].timestamp, Is.EqualTo(newer));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldGetLastSeen(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var tableLastSeen = scope.Resolve<TableLastSeen>();

        var identityId1 = "frodo.me";
        var identityId2 = "sam.me";

        var now = UnixTimeUtc.Now();
        var then = now.AddMilliseconds(-5000);
        var lastSeen = new Dictionary<string, UnixTimeUtc>
        {
            { identityId1, now },
            { identityId2, then },
        };

        await tableLastSeen.UpdateLastSeenAsync(lastSeen);

        var seen1 = await tableLastSeen.GetLastSeenAsync(identityId1);
        Assert.That(seen1, Is.Not.Null);
        Assert.That(seen1.Value.milliseconds, Is.EqualTo(now.milliseconds));

        var seen2 = await tableLastSeen.GetLastSeenAsync(identityId2);
        Assert.That(seen2, Is.Not.Null);
        Assert.That(seen2.Value.milliseconds, Is.EqualTo(then.milliseconds));

        var notSeen = await tableLastSeen.GetLastSeenAsync("gandalf.me");
        Assert.That(notSeen, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldNotStoreInvalidDomain(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await using var scope = Services.BeginLifetimeScope();
        var tableLastSeen = scope.Resolve<TableLastSeen>();

        var domain = "a";

        var now = UnixTimeUtc.Now();
        var lastSeen = new Dictionary<string, UnixTimeUtc>
        {
            { domain, now },
        };

        await tableLastSeen.UpdateLastSeenAsync(lastSeen);

        var all = await tableLastSeen.GetAllAsync();
        Assert.That(all.Count, Is.EqualTo(0));
    }
}