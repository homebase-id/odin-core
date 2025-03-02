using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
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
            Debug.Assert(i == 1);
            var r = await tblAppNotificationsTable.GetAsync(nid);
            Debug.Assert(r != null);
            Debug.Assert(ByteArrayUtil.EquiByteArrayCompare(nid.ToByteArray(), r.notificationId.ToByteArray()) == true);
            Debug.Assert(r.senderId == "frodo.com");
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
            Debug.Assert(i == 1);
            i = await tblAppNotificationsTable.InsertAsync(new AppNotificationsRecord() { notificationId = nid2, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
            Debug.Assert(i == 1);

            var (results, cursor) = await tblAppNotificationsTable.PagingByCreatedAsync(1, null);

            Debug.Assert(results.Count == 1);
            Debug.Assert(cursor != null);
            var c = TimeRowCursor.FromJsonOrOldString(cursor);
            Debug.Assert(c.time == results[0].created);
            Debug.Assert(c.rowId == 2);

            (results, cursor) = await tblAppNotificationsTable.PagingByCreatedAsync(1, cursor);

            Debug.Assert(results.Count == 1);
            Debug.Assert(cursor == null);
        }
    }
}
