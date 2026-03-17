using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableImFollowingCachedTests : IocTestBase
{
    // ChannelDriveType = SystemDriveConstants.ChannelDriveType
    private static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");
    // FeedDrive.Alias = SystemDriveConstants.FeedDrive.Alias
    private static readonly Guid FeedDriveAlias = Guid.Parse("4db49422ebad02e99ab96e9c477d1e08");

    private static ImFollowingRecord MakeSelectedChannelsRecord(string identity, Guid sourceDriveId)
        => new ImFollowingRecord
        {
            sourceOdinId = new OdinId(identity),
            sourceDriveId = sourceDriveId,
            targetDriveId = FeedDriveAlias,
            subscriptionKind = 2, // SelectedChannels
            lastNotification = new UnixTimeUtc(0),
            lastQuery = new UnixTimeUtc(0)
        };

    private static ImFollowingRecord MakeAllNotificationsRecord(string identity)
        => new ImFollowingRecord
        {
            sourceOdinId = new OdinId(identity),
            sourceDriveTypeId = ChannelDriveType,
            targetDriveId = FeedDriveAlias,
            subscriptionKind = 1, // AllNotifications
            lastNotification = new UnixTimeUtc(0),
            lastQuery = new UnixTimeUtc(0)
        };

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
        await tableImFollowingCached.InsertAsync(MakeSelectedChannelsRecord(i1, d1));

        // Thor follows d1
        await tableImFollowingCached.InsertAsync(MakeSelectedChannelsRecord(i2, d1));

        // Freja follows d1 & d2
        await tableImFollowingCached.InsertAsync(MakeSelectedChannelsRecord(i3, d1));
        await tableImFollowingCached.InsertAsync(MakeSelectedChannelsRecord(i3, d2));

        // Heimdal follows d2
        await tableImFollowingCached.InsertAsync(MakeSelectedChannelsRecord(i4, d2));

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

        // Loke follows everything (AllNotifications)
        await tableImFollowingCached.InsertAsync(MakeAllNotificationsRecord(i5));

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

        // Delete all records for i1 (replaces the old per-record DeleteAsync)
        await tableImFollowingCached.DeleteByIdentityAsync(new OdinId(i1));

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


