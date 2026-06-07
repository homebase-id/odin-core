using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableAppNotificationsCachedTests : IocTestBase
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
        var tableAppNotificationsCached = scope.Resolve<TableAppNotificationsCached>();

        var notificationId1 = Guid.Parse("11111111-AAAA-0000-0000-000000000000");
        var data1 = Guid.Parse("11111111-DDDD-0000-0000-000000000000").ToByteArray();
        var notificationId2 = Guid.Parse("22222222-AAAA-0000-0000-000000000000");
        var data2 = Guid.Parse("22222222-DDDD-0000-0000-000000000000").ToByteArray();
        var notificationId3 = Guid.Parse("33333333-AAAA-0000-0000-000000000000");
        var data3 = Guid.Parse("33333333-DDDD-0000-0000-000000000000").ToByteArray();

        {
            var record = await tableAppNotificationsCached.GetAsync(notificationId1, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(0));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(1));
        }

        await tableAppNotificationsCached.InsertAsync(
            new AppNotificationsRecord
            {
                notificationId = notificationId1, senderId = "frodo.com", unread = 1, data = data1
            });
        await tableAppNotificationsCached.InsertAsync(
            new AppNotificationsRecord
            {
                notificationId = notificationId2, senderId = "frodo.com", unread = 1, data = data2
            });
        await tableAppNotificationsCached.InsertAsync(
            new AppNotificationsRecord
            {
                notificationId = notificationId3, senderId = "frodo.com", unread = 1, data = data3
            });

        if (redisEnabled) WipeL1();

        {
            var record = await tableAppNotificationsCached.GetAsync(notificationId1, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(0));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(2));
        }

        if (redisEnabled) WipeL1();

        {
            List<AppNotificationsRecord> records;
            string cursor;

            //  NOTE: PagingByCreatedAsync returns records in descending order by created time (i.e. newest first)

            // Record 1 MISS
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId3));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(0));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(3));

            if (redisEnabled) WipeL1();

            // Record 1 HIT
            (records, cursor) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId3));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(1));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(3));

            if (redisEnabled) WipeL1();

            // Record 2 MISS
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(1));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(4));

            if (redisEnabled) WipeL1();

            // Record 2 HIT
            (records, cursor) = await tableAppNotificationsCached.PagingByCreatedAsync(1, cursor, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(4));
        }

        await tableAppNotificationsCached.DeleteAsync(notificationId3);

        if (redisEnabled) WipeL1();

        {
            var record = await tableAppNotificationsCached.GetAsync(notificationId3, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(5));
        }

        if (redisEnabled) WipeL1();

        {
            List<AppNotificationsRecord> records;

            // Record 1 MISS - DeleteAsync invalidated all Paging caches
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(6));

            if (redisEnabled) WipeL1();

            // Record 1 HIT
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(3));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(6));
        }

    }

    //

    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task ItShouldBulkUpdateUnreadAndInvalidateCaches(bool redisEnabled)
    {
        await RegisterServicesAsync(DatabaseType.Sqlite, redisEnabled: redisEnabled);
        await using var scope = Services.BeginLifetimeScope();
        var table = scope.Resolve<TableAppNotificationsCached>();

        var id1 = Guid.Parse("11111111-AAAA-0000-0000-000000000000");
        var id2 = Guid.Parse("22222222-AAAA-0000-0000-000000000000");
        var id3 = Guid.Parse("33333333-AAAA-0000-0000-000000000000");

        foreach (var id in new[] { id1, id2, id3 })
        {
            await table.InsertAsync(new AppNotificationsRecord
            {
                notificationId = id, senderId = "frodo.com", unread = 1, data = id.ToByteArray()
            });
        }

        // Prime per-record and paging caches.
        await table.GetAsync(id1, TimeSpan.FromMilliseconds(2000));
        await table.PagingByCreatedAsync(int.MaxValue, null, TimeSpan.FromMilliseconds(2000));

        // Bulk mark id1 and id2 as read; id3 left untouched. A missing id (id3 is
        // present, but pass a random absent one) must not throw.
        var affected = await table.UpdateUnreadAsync(
            new List<Guid> { id1, id2, Guid.NewGuid() }, unread: false);
        Assert.That(affected, Is.EqualTo(2));

        if (redisEnabled) WipeL1();

        // Affected per-record keys were invalidated -> reads reflect the new state.
        var rec1 = await table.GetAsync(id1, TimeSpan.FromMilliseconds(2000));
        var rec2 = await table.GetAsync(id2, TimeSpan.FromMilliseconds(2000));
        var rec3 = await table.GetAsync(id3, TimeSpan.FromMilliseconds(2000));
        Assert.That(rec1!.unread, Is.EqualTo(0));
        Assert.That(rec2!.unread, Is.EqualTo(0));
        Assert.That(rec3!.unread, Is.EqualTo(1));

        // Paging cache was invalidated too -> the page reflects the new state.
        var (records, _) = await table.PagingByCreatedAsync(int.MaxValue, null, TimeSpan.FromMilliseconds(2000));
        var unreadCount = records.FindAll(r => r.unread == 1).Count;
        Assert.That(unreadCount, Is.EqualTo(1));
    }

    //

    [Test]
    public async Task ItShouldReturnZeroForEmptyOrNullUpdateUnread()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var table = scope.Resolve<TableAppNotificationsCached>();

        Assert.That(await table.UpdateUnreadAsync(new List<Guid>(), unread: false), Is.EqualTo(0));
        Assert.That(await table.UpdateUnreadAsync(null!, unread: false), Is.EqualTo(0));
    }

    //

    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task ItShouldBulkDeleteAndInvalidateCaches(bool redisEnabled)
    {
        await RegisterServicesAsync(DatabaseType.Sqlite, redisEnabled: redisEnabled);
        await using var scope = Services.BeginLifetimeScope();
        var table = scope.Resolve<TableAppNotificationsCached>();

        var id1 = Guid.Parse("11111111-AAAA-0000-0000-000000000000");
        var id2 = Guid.Parse("22222222-AAAA-0000-0000-000000000000");
        var id3 = Guid.Parse("33333333-AAAA-0000-0000-000000000000");

        foreach (var id in new[] { id1, id2, id3 })
        {
            await table.InsertAsync(new AppNotificationsRecord
            {
                notificationId = id, senderId = "frodo.com", unread = 1, data = id.ToByteArray()
            });
        }

        // Prime per-record and paging caches.
        await table.GetAsync(id1, TimeSpan.FromMilliseconds(2000));
        await table.PagingByCreatedAsync(int.MaxValue, null, TimeSpan.FromMilliseconds(2000));

        // Bulk delete id1 and id2; id3 left intact. A missing id must not throw.
        var affected = await table.DeleteListAsync(new List<Guid> { id1, id2, Guid.NewGuid() });
        Assert.That(affected, Is.EqualTo(2));

        if (redisEnabled) WipeL1();

        // Deleted per-record keys were invalidated -> reads reflect the deletions.
        Assert.That(await table.GetAsync(id1, TimeSpan.FromMilliseconds(2000)), Is.Null);
        Assert.That(await table.GetAsync(id2, TimeSpan.FromMilliseconds(2000)), Is.Null);
        Assert.That(await table.GetAsync(id3, TimeSpan.FromMilliseconds(2000)), Is.Not.Null);

        // Paging cache was invalidated too -> the page reflects the deletions.
        var (records, _) = await table.PagingByCreatedAsync(int.MaxValue, null, TimeSpan.FromMilliseconds(2000));
        Assert.That(records.Count, Is.EqualTo(1));
        Assert.That(records[0].notificationId, Is.EqualTo(id3));
    }

    //

    [Test]
    public async Task ItShouldReturnZeroForEmptyOrNullDeleteList()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var table = scope.Resolve<TableAppNotificationsCached>();

        Assert.That(await table.DeleteListAsync(new List<Guid>()), Is.EqualTo(0));
        Assert.That(await table.DeleteListAsync(null!), Is.EqualTo(0));
    }

    //


}


