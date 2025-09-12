using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Cache;

public class TableImFollowingCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableImFollowingCached = scope.Resolve<TableImFollowingCached>();

        var i1 = "odin.valhalla.com";
        var i2 = "thor.valhalla.com";
        var i3 = "freja.valhalla.com";
        var i4 = "heimdal.valhalla.com";
        var i5 = "loke.valhalla.com";
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();

        {
            var records = await tableImFollowingCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(0));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(1));
        }

        {
            var records = await tableImFollowingCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(1));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(1));
        }

        // Odin follows d1
        await tableImFollowingCached.InsertAsync(new ImFollowingRecord { identity = new OdinId(i1), driveId = d1 });

        // Thor follows d1
        await tableImFollowingCached.InsertAsync(new ImFollowingRecord { identity = new OdinId(i2), driveId = d1 });

        // Freja follows d1 & d2
        await tableImFollowingCached.InsertAsync(new ImFollowingRecord { identity = new OdinId(i3), driveId = d1 });
        await tableImFollowingCached.InsertAsync(new ImFollowingRecord { identity = new OdinId(i3), driveId = d2 });

        // Heimdal follows d2
        await tableImFollowingCached.InsertAsync(new ImFollowingRecord { identity = new OdinId(i4), driveId = d2 });

        {
            var records = await tableImFollowingCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(1));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(2));
        }

        {
            var records = await tableImFollowingCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(2));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(2));
        }

        // Loke follows everything
        await tableImFollowingCached.InsertAsync(new ImFollowingRecord { identity = new OdinId(i5), driveId = Guid.Empty });

        {
            var records = await tableImFollowingCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(2));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(3));
        }

        {
            var records = await tableImFollowingCached.GetAsync(new OdinId(i1), TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(3));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(3));
        }

        List<string> followers;
        {
            (followers, _) = await tableImFollowingCached.GetAllFollowersAsync(100, null, TimeSpan.FromMilliseconds(100));
            Assert.That(followers.Count, Is.EqualTo(5));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(3));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(4));
        }

        {
            (followers, _) = await tableImFollowingCached.GetAllFollowersAsync(100, null, TimeSpan.FromMilliseconds(100));
            Assert.That(followers.Count, Is.EqualTo(5));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(4));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(4));
        }

        {
            (followers, _) = await tableImFollowingCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(followers.Count, Is.EqualTo(4));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(4));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(5));
        }

        {
            (followers, _) = await tableImFollowingCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(followers.Count, Is.EqualTo(4));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(5));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(5));
        }

        await tableImFollowingCached.DeleteAsync(new OdinId(i1), d1);

        {
            (followers, _) = await tableImFollowingCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(followers.Count, Is.EqualTo(3));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(5));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(6));
        }

        {
            (followers, _) = await tableImFollowingCached.GetFollowersAsync(100, d1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(followers.Count, Is.EqualTo(3));
            Assert.That(tableImFollowingCached.Hits, Is.EqualTo(6));
            Assert.That(tableImFollowingCached.Misses, Is.EqualTo(6));
        }

    }

    //

}


