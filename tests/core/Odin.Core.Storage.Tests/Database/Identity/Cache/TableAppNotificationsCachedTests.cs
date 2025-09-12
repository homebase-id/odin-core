using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Cache;

public class TableAppNotificationsCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableAppNotificationsCached = scope.Resolve<TableAppNotificationsCached>();

        var notificationId1 = Guid.Parse("11111111-AAAA-0000-0000-000000000000");
        var data1 = Guid.Parse("11111111-DDDD-0000-0000-000000000000").ToByteArray();
        var notificationId2 = Guid.Parse("22222222-AAAA-0000-0000-000000000000");
        var data2 = Guid.Parse("22222222-DDDD-0000-0000-000000000000").ToByteArray();
        var notificationId3 = Guid.Parse("33333333-AAAA-0000-0000-000000000000");
        var data3 = Guid.Parse("33333333-DDDD-0000-0000-000000000000").ToByteArray();

        {
            var record = await tableAppNotificationsCached.GetAsync(notificationId1, TimeSpan.FromMilliseconds(100));
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

        {
            var record = await tableAppNotificationsCached.GetAsync(notificationId1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(0));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(2));
        }

        {
            List<AppNotificationsRecord> records;
            string cursor;

            //  NOTE: PagingByCreatedAsync returns records in descending order by created time (i.e. newest first)

            // Record 1 MISS
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId3));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(0));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(3));

            // Record 1 HIT
            (records, cursor) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId3));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(1));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(3));

            // Record 2 MISS
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, cursor, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(1));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(4));

            // Record 2 HIT
            (records, cursor) = await tableAppNotificationsCached.PagingByCreatedAsync(1, cursor, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(4));
        }

        await tableAppNotificationsCached.DeleteAsync(notificationId3);

        {
            var record = await tableAppNotificationsCached.GetAsync(notificationId3, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Null);
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(5));
        }

        {
            List<AppNotificationsRecord> records;

            // Record 1 MISS - DeleteAsync invalidated all Paging caches
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(2));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(6));

            // Record 1 HIT
            (records, _) = await tableAppNotificationsCached.PagingByCreatedAsync(1, null, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].notificationId, Is.EqualTo(notificationId2));
            Assert.That(tableAppNotificationsCached.Hits, Is.EqualTo(3));
            Assert.That(tableAppNotificationsCached.Misses, Is.EqualTo(6));
        }

    }

    //


}


