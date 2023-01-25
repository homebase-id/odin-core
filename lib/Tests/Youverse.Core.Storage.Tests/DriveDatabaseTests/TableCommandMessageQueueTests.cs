using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace IndexerTests
{
    
    public class TableCommandMessageQueueTests
    {
        [Test]
        // Usage example
        public void ExampleUsageTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblCmdMsg0.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var a1 = new List<Guid>();

            // t1 is oldest, t5 is newest
            var t1 = SequentialGuid.CreateGuid();
            var t2 = SequentialGuid.CreateGuid();
            var t3 = SequentialGuid.CreateGuid();
            var t4 = SequentialGuid.CreateGuid();
            var t5 = SequentialGuid.CreateGuid();

            // Add them in any order 
            a1.Add(t3);
            a1.Add(t2);
            a1.Add(t5);
            a1.Add(t1);
            a1.Add(t4);

            // We save the 5 fileIds (randomly shuffled for fun) to the CommandMessageQueue
            db.TblCmdMsgQueue.InsertRows(a1);

            // Now we get the oldest fileId from the queue
            var md = db.TblCmdMsgQueue.Get(1);
            Debug.Assert(md != null);
            Debug.Assert(md.Count == 1);
            if (ByteArrayUtil.muidcmp(md[0].fileId, t1) != 0)
                Assert.Fail();

            // We get the same one again, and it's still the same
            md = db.TblCmdMsgQueue.Get(1);
            Debug.Assert(md != null);
            Debug.Assert(md.Count == 1);
            if (ByteArrayUtil.muidcmp(md[0].fileId, t1) != 0)
                Assert.Fail();

            // We delete only the oldest one
            db.TblCmdMsgQueue.DeleteRow(new List<Guid>() { t1 });

            // We get all the rest
            md = db.TblCmdMsgQueue.Get(10);
            Debug.Assert(md != null);
            Debug.Assert(md.Count == 4);
            Debug.Assert(ByteArrayUtil.muidcmp(md[0].fileId, t2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(md[1].fileId, t3) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(md[2].fileId, t4) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(md[3].fileId, t5) == 0);
        }




        [Test]
        // Test we can insert and read a row
        public void InsertRowTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblCmdMsg1.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            var md = db.TblCmdMsgQueue.Get(1);

            if (md != null)
                Assert.Fail();

            db.TblCmdMsgQueue.InsertRows(a1);

            md = db.TblCmdMsgQueue.Get(1);

            if (md == null)
                Assert.Fail();

            if (md.Count != 1)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md[0].fileId, a1[0]) != 0)
                Assert.Fail();
        }

        [Test]
        // Test we can insert and read two tagmembers
        public void InsertDoubleRowTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblCmdMsg2.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(Guid.NewGuid());

            db.TblCmdMsgQueue.InsertRows(a1);

            var md = db.TblCmdMsgQueue.Get(5);

            if (md == null)
                Assert.Fail();

            if (md.Count != 2)
                Assert.Fail();

            // We don't know what order it comes back in :o) Quick hack.
            if (ByteArrayUtil.muidcmp(md[0].fileId, a1[0]) != 0)
            {
                if (ByteArrayUtil.muidcmp(md[0].fileId, a1[1]) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(md[1].fileId, a1[0]) != 0)
                    Assert.Fail();
            }
            else
            {
                if (ByteArrayUtil.muidcmp(md[1].fileId, a1[1]) != 0)
                    Assert.Fail();
            }
        }

        [Test]
        // Test we cannot insert the same tagmember key twice on the same key
        public void InsertDuplicatetagMemberTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblCmdMsg3.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(a1[0]);

            bool ok = false;
            try
            {
                db.TblCmdMsgQueue.InsertRows(a1);
                ok = false;
            }
            catch
            {
                ok = true;
            }

            if (!ok)
                Assert.Fail();
        }


        [Test]
        // Test we cannot insert the same key twice
        public void InsertDoubleKeyTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblCmdMsg5.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.TblCmdMsgQueue.InsertRows(a1);
            bool ok = false;
            try
            {
                db.TblCmdMsgQueue.InsertRows(a1);
                ok = false;
            }
            catch
            {
                ok = true;
            }

            if (!ok)
                Assert.Fail();
        }


        [Test]
        public void DeleteRowTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblCmdMsg6.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            var v1 = Guid.NewGuid();
            var v2 = Guid.NewGuid();

            a1.Add(v1);
            a1.Add(v2);

            db.TblCmdMsgQueue.InsertRows(a1);

            // Delete all tagmembers of the first key entirely
            db.TblCmdMsgQueue.DeleteRow(a1);

            // Check that k1 is now gone
            var md = db.TblCmdMsgQueue.Get(10);
            if (md != null)
                Assert.Fail();
        }
    }
}