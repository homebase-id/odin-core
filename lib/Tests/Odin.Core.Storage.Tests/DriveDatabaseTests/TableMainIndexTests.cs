using System;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.DriveDatabase;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.DriveDatabaseTests
{
    public class TableMainIndexTests
    {
        [Test]
        public void InsertRowTest()
        {
            using var db = new DriveDatabase("", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtcUnique.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            var md = db.TblMainIndex.Get(k1);

            if (md != null)
                Assert.Fail();

            db.TblMainIndex.Insert(new MainIndexRecord()
            {
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
                archivalStatus = 0,
                historyStatus = 1,
                requiredSecurityGroup = 44
            });

            var cts2 = UnixTimeUtcUnique.Now();

            md = db.TblMainIndex.Get(k1);

            if (md == null)
                Assert.Fail();

            Assert.IsTrue((md.created.ToUnixTimeUtc() >= cts1.ToUnixTimeUtc()) && (md.created.ToUnixTimeUtc() <= cts2.ToUnixTimeUtc()));

            if (md.modified != null)
                Assert.Fail();

            if (md.fileType != 7)
                Assert.Fail();

            if (md.dataType != 42)
                Assert.Fail();

            Assert.True(md.requiredSecurityGroup == 44);

            if (md.senderId.ToString() != sid1.ToString())
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md.groupId, tid1) != 0)
                Assert.Fail();

            if (md.userDate != ud1)
                Assert.Fail();

            if (md.archivalStatus != 0)
                Assert.Fail();

            if (md.historyStatus != 1)
                Assert.Fail();
        }

        [Test]
        public void InsertRowDuplicateTest()
        {
            using var db = new DriveDatabase("", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtcUnique.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            db.TblMainIndex.Insert(new MainIndexRecord()
            {
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
                archivalStatus = 0,
                historyStatus = 1,
                fileSystemType = 44
            });
            
            try
            {
                db.TblMainIndex.Insert(new MainIndexRecord()
                {
                    fileId = k1,
                    globalTransitId = Guid.NewGuid(),
                    created = cts1,
                    fileType = 7,
                    dataType = 42,
                    senderId = sid1.ToString(),
                    groupId = tid1,
                    uniqueId = Guid.NewGuid(),
                    userDate = ud1,
                    archivalStatus = 0,
                    historyStatus = 1,
                    fileSystemType = 44
                });
                Assert.Fail();
            }
            catch
            {
            }
        }


        [Test]
        public void UpdateRowTest()
        {
            using var db = new DriveDatabase("", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtcUnique.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            db.TblMainIndex.Insert(new MainIndexRecord()
            {
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
                archivalStatus = 0,
                historyStatus = 1,
                requiredSecurityGroup = 44
            });
            
            var md = db.TblMainIndex.Get(k1);
            if (md.modified != null)
                Assert.Fail();

            db.TblMainIndex.UpdateRow(k1, fileType: 8);
            md = db.TblMainIndex.Get(k1);
            if (md.fileType != 8)
                Assert.Fail();

            db.TblMainIndex.UpdateRow(k1, dataType: 43);
            md = db.TblMainIndex.Get(k1);
            if (md.dataType != 43)
                Assert.Fail();

            var sid2 = "frodo.baggins".ToUtf8ByteArray();
            db.TblMainIndex.UpdateRow(k1, senderId: sid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.EquiByteArrayCompare(sid2, md.senderId.ToUtf8ByteArray()) == false)
                Assert.Fail();

            var tid2 = Guid.NewGuid();
            db.TblMainIndex.UpdateRow(k1, groupId: tid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(tid2, md.groupId) != 0)
                Assert.Fail();

            var uid2 = Guid.NewGuid();
            db.TblMainIndex.UpdateRow(k1, uniqueId: uid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(uid2, md.uniqueId) != 0)
                Assert.Fail();

            var gtid2 = Guid.NewGuid();
            db.TblMainIndex.UpdateRow(k1, globalTransitId: gtid2);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(gtid2, md.globalTransitId) != 0)
                Assert.Fail();


            var ud2 = UnixTimeUtc.Now();
            db.TblMainIndex.UpdateRow(k1, userDate: ud2);
            md = db.TblMainIndex.Get(k1);
            if (ud2 != md.userDate)
                Assert.Fail();

            db.TblMainIndex.UpdateRow(k1, requiredSecurityGroup: 55);
            md = db.TblMainIndex.Get(k1);
            Assert.True(md.requiredSecurityGroup == 55);
            
            if (md.modified?.uniqueTime == 0)
                Assert.Fail();
        }
    }
}