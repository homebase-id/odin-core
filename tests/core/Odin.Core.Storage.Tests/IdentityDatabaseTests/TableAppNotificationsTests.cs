# if false
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableAppNotificationsTest
    {
        [Test]
        public async Task InsertGetTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppNotificationsTest001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var nid = SequentialGuid.CreateGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var c2 = SequentialGuid.CreateGuid();
                var c3 = SequentialGuid.CreateGuid();
                var d2 = Guid.NewGuid().ToByteArray();

                var i = await db.tblAppNotificationsTable.InsertAsync(new AppNotificationsRecord() { notificationId = nid, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
                Debug.Assert(i == 1);
                var r = await db.tblAppNotificationsTable.GetAsync(nid);
                Debug.Assert(r != null);
                Debug.Assert(ByteArrayUtil.EquiByteArrayCompare(nid.ToByteArray(), r.notificationId.ToByteArray()) == true);
                Debug.Assert(r.senderId == "frodo.com");
            }
        }

        [Test]
        public async Task InsertPageTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppNotificationsTest002");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var nid = SequentialGuid.CreateGuid();
                var nid2 = SequentialGuid.CreateGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var c2 = SequentialGuid.CreateGuid();
                var c3 = SequentialGuid.CreateGuid();
                var d2 = Guid.NewGuid().ToByteArray();

                var i = await db.tblAppNotificationsTable.InsertAsync(new AppNotificationsRecord() { notificationId = nid, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
                Debug.Assert(i == 1);
                i = await db.tblAppNotificationsTable.InsertAsync(new AppNotificationsRecord() { notificationId = nid2, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
                Debug.Assert(i == 1);

                var (results, cursor2) = await db.tblAppNotificationsTable.PagingByCreatedAsync(1, null);

                Debug.Assert(results.Count == 1);
                Debug.Assert(cursor2 != null);
                Debug.Assert(cursor2.Value.uniqueTime == results[0].created.uniqueTime);

                (results, cursor2) = await db.tblAppNotificationsTable.PagingByCreatedAsync(1, cursor2);

                Debug.Assert(results.Count == 1);
                Debug.Assert(cursor2 == null);
            }
        }
    }
}
#endif
