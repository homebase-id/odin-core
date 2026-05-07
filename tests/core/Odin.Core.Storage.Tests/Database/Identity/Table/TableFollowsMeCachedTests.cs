using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableFollowsMeCachedTests : IocTestBase
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
        var tableFollowsMeCached = scope.Resolve<TableFollowsMeCached>();

        var i1 = "odin.valhalla.com";
        var i2 = "thor.valhalla.com";
        var i3 = "freja.valhalla.com";
        var i4 = "heimdal.valhalla.com";
        var i5 = "loke.valhalla.com";
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();

        {
            var records = await tableFollowsMeCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(0));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(1));
        }

        if (redisEnabled) WipeL1();

        {
            var records = await tableFollowsMeCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(1));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(1));
        }

        if (redisEnabled) WipeL1();

        // Odin follows d1
        await tableFollowsMeCached.InsertAsync(new FollowsMeRecord { identity = i1, driveId = d1 });

        // Thor follows d1
        await tableFollowsMeCached.InsertAsync(new FollowsMeRecord { identity = i2, driveId = d1 });

        // Freja follows d1 & d2
        await tableFollowsMeCached.InsertAsync(new FollowsMeRecord { identity = i3, driveId = d1 });
        await tableFollowsMeCached.InsertAsync(new FollowsMeRecord { identity = i3, driveId = d2 });

        // Heimdal follows d2
        await tableFollowsMeCached.InsertAsync(new FollowsMeRecord { identity = i4, driveId = d2 });

        if (redisEnabled) WipeL1();

        {
            var records = await tableFollowsMeCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(1));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(2));
        }

        if (redisEnabled) WipeL1();

        {
            var records = await tableFollowsMeCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(2));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(2));
        }

        // Loke follows everything
        await tableFollowsMeCached.InsertAsync(new FollowsMeRecord { identity = i5, driveId = Guid.Empty });

        if (redisEnabled) WipeL1();

        {
            var records = await tableFollowsMeCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(2));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(3));
        }

        if (redisEnabled) WipeL1();

        {
            var records = await tableFollowsMeCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(3));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(3));
        }

        if (redisEnabled) WipeL1();

        List<string> followers;
        {
            (followers, _) = await tableFollowsMeCached.GetAllFollowersAsync(100, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(followers.Count, Is.EqualTo(5));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(3));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(4));
        }

        if (redisEnabled) WipeL1();

        {
            (followers, _) = await tableFollowsMeCached.GetAllFollowersAsync(100, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(followers.Count, Is.EqualTo(5));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(4));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(4));
        }

        if (redisEnabled) WipeL1();

        {
            (followers, _) = await tableFollowsMeCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(followers.Count, Is.EqualTo(4));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(4));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(5));
        }

        if (redisEnabled) WipeL1();

        {
            (followers, _) = await tableFollowsMeCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(followers.Count, Is.EqualTo(4));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(5));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(5));
        }

        await tableFollowsMeCached.DeleteAsync(new OdinId(i1), d1);

        if (redisEnabled) WipeL1();

        {
            (followers, _) = await tableFollowsMeCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(followers.Count, Is.EqualTo(3));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(5));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(6));
        }

        if (redisEnabled) WipeL1();

        {
            (followers, _) = await tableFollowsMeCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(followers.Count, Is.EqualTo(3));
            Assert.That(tableFollowsMeCached.Hits, Is.EqualTo(6));
            Assert.That(tableFollowsMeCached.Misses, Is.EqualTo(6));
        }

    }

    //

}


