using System;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace IndexerTests
{
    public class TableMainIndexTests
    {
        [Test]
        public void InsertRowTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblmainindex1.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            var md = db.TblMainIndex.Get(k1);

            if (md != null)
                Assert.Fail();

            db.TblMainIndex.InsertRow(k1, Guid.NewGuid(), cts1, 7, 42, sid1, tid1, Guid.NewGuid(), ud1.milliseconds, false, true, 44);

            md = db.TblMainIndex.Get(k1);

            if (md == null)
                Assert.Fail();

            if (md.CreatedTimeStamp != cts1)
                Assert.Fail();

            if (md.UpdatedTimeStamp.uniqueTime != 0)
                Assert.Fail();

            if (md.FileType != 7)
                Assert.Fail();

            if (md.DataType != 42)
                Assert.Fail();

            Assert.True(md.RequiredSecurityGroup == 44);

            if (ByteArrayUtil.muidcmp(md.SenderId, sid1) != 0)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md.GroupId, tid1) != 0)
                Assert.Fail();

            if (md.UserDate != ud1.milliseconds)
                Assert.Fail();

            if (md.IsArchived != false)
                Assert.Fail();

            if (md.IsHistory != true)
                Assert.Fail();
        }

        [Test]
        public void InsertRowDuplicateTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblmainindex2.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            db.TblMainIndex.InsertRow(k1, Guid.NewGuid(), cts1, 7, 42, sid1, tid1, Guid.NewGuid(), ud1.milliseconds, false, true, 44);
            try
            {
                db.TblMainIndex.InsertRow(k1, Guid.NewGuid(), cts1, 7, 42, sid1, tid1, Guid.NewGuid(), ud1.milliseconds, false, true, 44);
                Assert.Fail();
            }
            catch
            {
            }
        }


        [Test]
        public void UpdateRowTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblmainindex3.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            db.TblMainIndex.InsertRow(k1, Guid.NewGuid(), cts1, 7, 42, sid1, tid1, Guid.NewGuid(), ud1.milliseconds, false, true, 44);
            var md = db.TblMainIndex.Get(k1);
            if (md.UpdatedTimeStamp.uniqueTime != 0)
                Assert.Fail();

            db.TblMainIndex.UpdateRow(k1, fileType: 8);
            md = db.TblMainIndex.Get(k1);
            if (md.FileType != 8)
                Assert.Fail();

            db.TblMainIndex.UpdateRow(k1, dataType: 43);
            md = db.TblMainIndex.Get(k1);
            if (md.DataType != 43)
                Assert.Fail();

            var sid2 = Guid.NewGuid().ToByteArray();
            db.TblMainIndex.UpdateRow(k1, senderId: sid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(sid2, md.SenderId) != 0)
                Assert.Fail();

            var tid2 = Guid.NewGuid();
            db.TblMainIndex.UpdateRow(k1, groupId: tid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(tid2, md.GroupId) != 0)
                Assert.Fail();

            var uid2 = Guid.NewGuid();
            db.TblMainIndex.UpdateRow(k1, uniqueId: uid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(uid2, md.uniqueId) != 0)
                Assert.Fail();

            var gtid2 = Guid.NewGuid();
            db.TblMainIndex.UpdateRow(k1, globalTransitId: gtid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(gtid2, md.GlobalTransitId) != 0)
                Assert.Fail();


            var ud2 = UnixTimeUtc.Now();
            db.TblMainIndex.UpdateRow(k1, userDate: ud2.milliseconds);
            md = db.TblMainIndex.Get(k1);
            if (ud2.milliseconds != md.UserDate)
                Assert.Fail();

            db.TblMainIndex.UpdateRow(k1, requiredSecurityGroup: 55);
            md = db.TblMainIndex.Get(k1);
            Assert.True(md.RequiredSecurityGroup == 55);
            
            if (md.UpdatedTimeStamp.uniqueTime == 0)
                Assert.Fail();
        }
    }
}