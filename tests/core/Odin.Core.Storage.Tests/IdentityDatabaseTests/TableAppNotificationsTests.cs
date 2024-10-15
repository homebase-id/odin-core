using System;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableAppNotificationsTest
    {
        [Test]
        public void InsertGetTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppNotificationsTest001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var nid = SequentialGuid.CreateGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var c2 = SequentialGuid.CreateGuid();
                var c3 = SequentialGuid.CreateGuid();
                var d2 = Guid.NewGuid().ToByteArray();

                var i = db.tblAppNotificationsTable.Insert(new AppNotificationsRecord() { notificationId = nid, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
                Debug.Assert(i == 1);
                var r = db.tblAppNotificationsTable.Get(nid);
                Debug.Assert(r != null);
                Debug.Assert(ByteArrayUtil.EquiByteArrayCompare(nid.ToByteArray(), r.notificationId.ToByteArray()) == true);
                Debug.Assert(r.senderId == "frodo.com");
            }
        }

        [Test]
        public void InsertPageTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableAppNotificationsTest002");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var nid = SequentialGuid.CreateGuid();
                var nid2 = SequentialGuid.CreateGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var c2 = SequentialGuid.CreateGuid();
                var c3 = SequentialGuid.CreateGuid();
                var d2 = Guid.NewGuid().ToByteArray();

                var i = db.tblAppNotificationsTable.Insert(new AppNotificationsRecord() { notificationId = nid, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
                Debug.Assert(i == 1);
                i = db.tblAppNotificationsTable.Insert(new AppNotificationsRecord() { notificationId = nid2, senderId = (OdinId)"frodo.com", unread = 1, data = d1 });
                Debug.Assert(i == 1);

                var results = db.tblAppNotificationsTable.PagingByCreated(1, null, out var cursor2);

                Debug.Assert(results.Count == 1);
                Debug.Assert(cursor2 != null);
                Debug.Assert(cursor2.Value.uniqueTime == results[0].created.uniqueTime);

                results = db.tblAppNotificationsTable.PagingByCreated(1, cursor2, out cursor2);

                Debug.Assert(results.Count == 1);
                Debug.Assert(cursor2 == null);
            }
        }
    }
}