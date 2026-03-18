using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableAppNotificationsTest : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertGetTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblAppNotificationsTable = scope.Resolve<TableAppNotifications>();

            var nid = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            var i = await tblAppNotificationsTable.InsertAsync(new AppNotificationsRecord() { notificationId = nid, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
            ClassicAssert.IsTrue(i == 1);
            var r = await tblAppNotificationsTable.GetAsync(nid);
            ClassicAssert.IsTrue(r != null);
            ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(nid.ToByteArray(), r.notificationId.ToByteArray()) == true);
            ClassicAssert.IsTrue(r.senderId == "frodo.com");
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertPageTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblAppNotificationsTable = scope.Resolve<TableAppNotifications>();

            var nid = SequentialGuid.CreateGuid();
            var nid2 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            var i = await tblAppNotificationsTable.InsertAsync(new AppNotificationsRecord() { notificationId = nid, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
            ClassicAssert.IsTrue(i == 1);
            i = await tblAppNotificationsTable.InsertAsync(new AppNotificationsRecord() { notificationId = nid2, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
            ClassicAssert.IsTrue(i == 1);

            var (results, cursor) = await tblAppNotificationsTable.PagingByCreatedAsync(1, null);

            ClassicAssert.IsTrue(results.Count == 1);
            ClassicAssert.IsTrue(cursor != null);
            var c = TimeRowCursor.FromJson(cursor);
            ClassicAssert.IsTrue(c.Time == results[0].created);
            ClassicAssert.IsTrue(c.rowId == 2);

            (results, cursor) = await tblAppNotificationsTable.PagingByCreatedAsync(1, cursor);

            ClassicAssert.IsTrue(results.Count == 1);
            ClassicAssert.IsTrue(cursor == null);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PagingByRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableAppNotifications>();

            var n1 = SequentialGuid.CreateGuid();
            var n2 = SequentialGuid.CreateGuid();
            var n3 = SequentialGuid.CreateGuid();

            await tbl.InsertAsync(new AppNotificationsRecord() { notificationId = n1, unread = 1, senderId = "frodo.baggins.me", timestamp = new Odin.Core.Time.UnixTimeUtc(1000), data = null });
            await tbl.InsertAsync(new AppNotificationsRecord() { notificationId = n2, unread = 1, senderId = "sam.gamgee.me", timestamp = new Odin.Core.Time.UnixTimeUtc(2000), data = null });
            await tbl.InsertAsync(new AppNotificationsRecord() { notificationId = n3, unread = 0, senderId = "gandalf.white.me", timestamp = new Odin.Core.Time.UnixTimeUtc(3000), data = null });

            var (page1, cursor1) = await tbl.PagingByRowIdAsync(2, null);
            Assert.That(page1.Count, Is.EqualTo(2));
            Assert.That(cursor1, Is.Not.Null);

            var (page2, cursor2) = await tbl.PagingByRowIdAsync(2, cursor1);
            Assert.That(page2.Count, Is.EqualTo(1));
            Assert.That(cursor2, Is.Null);

            var (all, allCursor) = await tbl.PagingByRowIdAsync(100, null);
            Assert.That(all.Count, Is.EqualTo(3));
            Assert.That(allCursor, Is.Null);
        }
    }
}
