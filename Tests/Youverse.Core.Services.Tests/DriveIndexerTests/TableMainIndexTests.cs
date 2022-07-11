using System;
using NUnit.Framework;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    public class TableMainIndexTests
    {
        [Test]
        public void InsertRowTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblmainindex1.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = ZeroTime.GetZeroTimeSeconds();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid().ToByteArray();
            var ud1 = ZeroTime.ZeroTimeMillisecondsUnique();

            var md = db.TblMainIndex.Get(k1);

            if (md != null)
                Assert.Fail();

            db.TblMainIndex.InsertRow(k1, cts1, 7, 42, sid1, tid1, ud1, false, true, 44);

            md = db.TblMainIndex.Get(k1);

            if (md == null)
                Assert.Fail();

            if (md.CreatedTimeStamp != cts1)
                Assert.Fail();

            if (md.UpdatedTimeStamp != 0)
                Assert.Fail();

            if (md.FileType != 7)
                Assert.Fail();

            if (md.DataType != 42)
                Assert.Fail();

            Assert.True(md.RequiredSecurityGroup == 44);

            if (SequentialGuid.muidcmp(md.SenderId, sid1) != 0)
                Assert.Fail();

            if (SequentialGuid.muidcmp(md.ThreadId, tid1) != 0)
                Assert.Fail();

            if (md.UserDate != ud1)
                Assert.Fail();

            if (md.IsArchived != false)
                Assert.Fail();

            if (md.IsHistory != true)
                Assert.Fail();
        }

        [Test]
        public void InsertRowDuplicateTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblmainindex2.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = ZeroTime.GetZeroTimeSeconds();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid().ToByteArray();
            var ud1 = ZeroTime.ZeroTimeMillisecondsUnique();

            db.TblMainIndex.InsertRow(k1, cts1, 7, 42, sid1, tid1, ud1, false, true, 44);
            try
            {
                db.TblMainIndex.InsertRow(k1, cts1, 7, 42, sid1, tid1, ud1, false, true, 44);
                Assert.Fail();
            }
            catch
            {
            }
        }


        [Test]
        public void UpdateRowTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblmainindex3.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var cts1 = ZeroTime.GetZeroTimeSeconds();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid().ToByteArray();
            var ud1 = ZeroTime.ZeroTimeMillisecondsUnique();

            db.TblMainIndex.InsertRow(k1, cts1, 7, 42, sid1, tid1, ud1, false, true, 44);
            var md = db.TblMainIndex.Get(k1);
            if (md.UpdatedTimeStamp != 0)
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
            if (SequentialGuid.muidcmp(sid2, md.SenderId) != 0)
                Assert.Fail();

            var tid2 = Guid.NewGuid().ToByteArray();
            db.TblMainIndex.UpdateRow(k1, threadId: tid2);
            md = db.TblMainIndex.Get(k1);
            if (SequentialGuid.muidcmp(tid2, md.ThreadId) != 0)
                Assert.Fail();

            var ud2 = ZeroTime.ZeroTimeMillisecondsUnique();
            db.TblMainIndex.UpdateRow(k1, userDate: ud2);
            md = db.TblMainIndex.Get(k1);
            if (ud2 != md.UserDate)
                Assert.Fail();

            db.TblMainIndex.UpdateRow(k1, requiredSecurityGroup: 55);
            md = db.TblMainIndex.Get(k1);
            Assert.True(md.RequiredSecurityGroup == 55);
            
            if (md.UpdatedTimeStamp == 0)
                Assert.Fail();
        }
    }
}