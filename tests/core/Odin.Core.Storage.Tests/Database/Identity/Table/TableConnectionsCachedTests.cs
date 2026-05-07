using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableConnectionsCachedTests : IocTestBase
{
    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task ItShouldTestCachingFromAtoZ(bool redisEnabled)
    {
        await RegisterServicesAsync(DatabaseType.Sqlite, redisEnabled: redisEnabled);
        await using var scope = Services.BeginLifetimeScope();
        var tableConnectionsCached = scope.Resolve<TableConnectionsCached>();

        var item1 = new ConnectionsRecord
        {
            identity = new OdinId("frodo.baggins.me"),
            displayName = "Frodo",
            status = 42,
            accessIsRevoked = 1,
            data = Guid.NewGuid().ToByteArray()
        };

        var item2 = new ConnectionsRecord
        {
            identity = new OdinId("samwise.gamgee.me"),
            displayName = "Sam",
            status = 43,
            accessIsRevoked = 0,
            data = Guid.NewGuid().ToByteArray()
        };

        var item3 = new ConnectionsRecord
        {
            identity = new OdinId("gandalf.white.me"),
            displayName = "G",
            status = 44,
            accessIsRevoked = 0,
            data = Guid.NewGuid().ToByteArray()
        };

        {
            var record = await tableConnectionsCached.GetAsync(item1.identity, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(0));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(1));
        }

        if (redisEnabled) WipeL1();

        {
            var record = await tableConnectionsCached.GetAsync(item1.identity, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(1));
        }

        await tableConnectionsCached.UpsertAsync(item1);
        await tableConnectionsCached.UpsertAsync(item2);
        await tableConnectionsCached.UpsertAsync(item3);

        if (redisEnabled) WipeL1();

        {
            var record = await tableConnectionsCached.GetAsync(item1.identity, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(2));
        }

        if (redisEnabled) WipeL1();

        List<ConnectionsRecord> records;
        string cursor;

        {
            (records, _) = await tableConnectionsCached.PagingByIdentityAsync(2, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(3));
        }

        if (redisEnabled) WipeL1();

        {
            (records, cursor) = await tableConnectionsCached.PagingByIdentityAsync(2, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(2));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(3));
        }

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByIdentityAsync(2, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(2));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(4));
        }

        if (redisEnabled) WipeL1();

        {
            (records, cursor) = await tableConnectionsCached.PagingByIdentityAsync(2, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(3));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(4));
        }

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByIdentityAsync(2, 42, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(3));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(5));
        }

        if (redisEnabled) WipeL1();

        {
            (records, cursor) = await tableConnectionsCached.PagingByIdentityAsync(2, 42, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(4));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(5));
        }

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByCreatedAsync(2, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(4));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(6));
        }

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByCreatedAsync(2, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(5));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(6));
        }

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByCreatedAsync(2, 42, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(5));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(7));
        }

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByCreatedAsync(2, 42, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(6));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(7));
        }

        await tableConnectionsCached.DeleteAsync(item2.identity);
        await tableConnectionsCached.DeleteAsync(item3.identity);

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByIdentityAsync(2, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(6));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(8));
        }

        if (redisEnabled) WipeL1();

        {
            (records, _) = await tableConnectionsCached.PagingByCreatedAsync(2, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableConnectionsCached.Hits, Is.EqualTo(6));
            Assert.That(tableConnectionsCached.Misses, Is.EqualTo(9));
        }

    }

}
