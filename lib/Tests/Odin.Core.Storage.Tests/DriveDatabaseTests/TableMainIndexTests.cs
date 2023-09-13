using System;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.DriveDatabase;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.DriveDatabaseTests
{
    public class TableMainIndexTests
    {
        [Test]
        public void GetSizeTest()
        {
            using var db = new DriveDatabase("", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            db.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            db.AddEntry(f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 2);
            db.AddEntry(f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 3);
            db.AddEntry(f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 4);
            db.AddEntry(f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 5);

            var (count, size) = db.TblMainIndex.GetDriveSize();
            Assert.AreEqual(count, 5);
            Assert.AreEqual(size, 1+2+3+4+5);
        }

        [Test]
        public void CannotInsertZeroSizeTest()
        {
            using var db = new DriveDatabase("", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToByteArray();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            try
            {
                db.AddEntry(f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 0);
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException))
                    Assert.Fail();
            }
        }



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
                requiredSecurityGroup = 44,
                byteCount = 7
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

            var kludge = new TableMainIndex.NullableGuid();
            kludge.uniqueId = Guid.NewGuid();
            db.TblMainIndex.UpdateRow(k1, kludgeUniqueId: kludge);
            md = db.TblMainIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(kludge.uniqueId, md.uniqueId) != 0)
                Assert.Fail();

            kludge.uniqueId = null;
            db.TblMainIndex.UpdateRow(k1, kludgeUniqueId: kludge);
            md = db.TblMainIndex.Get(k1);
            if (md.uniqueId != null)
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

            db.TblMainIndex.UpdateRow(k1, byteCount: 42);
            md = db.TblMainIndex.Get(k1);
            Assert.True(md.byteCount == 42);

            if (md.modified?.uniqueTime == 0)
                Assert.Fail();
        }
    }
}