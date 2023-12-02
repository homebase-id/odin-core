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
            using var db = new IdentityDatabase("");
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
            Debug.Assert(r.senderId == "frodo");
        }

    }
}