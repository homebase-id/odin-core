using NUnit.Framework;
using Youverse.Core.SystemStorage.SqliteKeyValue;

namespace Youverse.Core.Tests.SystemStorageTests.KeyValue
{
    public class TableInboxTests
    {
        [TestCase()]
        public void InsertRowTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\inboxtest1.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid();
            var v2 = SequentialGuid.CreateGuid();

            var boxid = SequentialGuid.CreateGuid();

            var tslo = UnixTime.GetUnixTimeSeconds();
            db.tblInbox.InsertRow(boxid, f1, 0, v1);
            db.tblInbox.InsertRow(boxid, f2, 10, v2);
            var tshi = UnixTime.GetUnixTimeSeconds();

            var r = db.tblInbox.Get(f1);
            if (SequentialGuid.muidcmp(r.fileId, f1) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r.value, v1) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                Assert.Fail();
            if (r.priority != 0)
                Assert.Fail();

            r = db.tblInbox.Get(f2);
            if (SequentialGuid.muidcmp(r.fileId, f2) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r.value, v2) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                Assert.Fail();
            if (r.priority != 10)
                Assert.Fail();
        }

        [TestCase()]
        public void PopTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\inboxtest2.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid();
            var v2 = SequentialGuid.CreateGuid();
            var v3 = SequentialGuid.CreateGuid();
            var v4 = SequentialGuid.CreateGuid();
            var v5 = SequentialGuid.CreateGuid();
            var boxid = SequentialGuid.CreateGuid();

            var tslo = UnixTime.GetUnixTimeSeconds();
            db.tblInbox.InsertRow(boxid, f1, 0, v1);
            db.tblInbox.InsertRow(boxid, f2, 1, v2);
            db.tblInbox.InsertRow(boxid, f3, 2, v3);
            db.tblInbox.InsertRow(boxid, f4, 3, v4);
            db.tblInbox.InsertRow(boxid, f5, 4, v5);
            var tshi = UnixTime.GetUnixTimeSeconds();

            // pop one item from the inbox
            var r = db.tblInbox.Pop(boxid, 1, out var popTimestamp);
            if (r.Count != 1)
                Assert.Fail();

            if (popTimestamp == null)
                Assert.Fail();

            if (SequentialGuid.muidcmp(r[0].fileId, f1) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r[0].value, v1) != 0)
                Assert.Fail();
            if (r[0].priority != 0)
                Assert.Fail();
            if ((r[0].timeStamp < tslo) || (r[0].timeStamp > tshi))
                Assert.Fail();

            // pop all the remaining items from the inbox
            r = db.tblInbox.Pop(boxid, 10, out popTimestamp);
            if (r.Count != 4)
                Assert.Fail();

            if (popTimestamp == null)
                Assert.Fail();

            if (SequentialGuid.muidcmp(r[0].fileId, f2) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r[0].value, v2) != 0)
                Assert.Fail();
            if (r[0].priority != 1)
                Assert.Fail();
            if ((r[0].timeStamp < tslo) || (r[0].timeStamp > tshi))
                Assert.Fail();

            if (SequentialGuid.muidcmp(r[1].fileId, f3) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r[1].value, v3) != 0)
                Assert.Fail();
            if (r[1].priority != 2)
                Assert.Fail();
            if ((r[1].timeStamp < tslo) || (r[1].timeStamp > tshi))
                Assert.Fail();

            if (SequentialGuid.muidcmp(r[2].fileId, f4) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r[2].value, v4) != 0)
                Assert.Fail();
            if (r[2].priority != 3)
                Assert.Fail();
            if ((r[2].timeStamp < tslo) || (r[2].timeStamp > tshi))
                Assert.Fail();

            if (SequentialGuid.muidcmp(r[3].fileId, f5) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r[3].value, v5) != 0)
                Assert.Fail();
            if (r[3].priority != 4)
                Assert.Fail();
            if ((r[3].timeStamp < tslo) || (r[3].timeStamp > tshi))
                Assert.Fail();

            // pop to make sure there are no more items
            r = db.tblInbox.Pop(boxid, 1, out popTimestamp);
            if (r.Count != 0)
                Assert.Fail();
        }

        [TestCase()]
        public void PopCancelTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\inboxtest3.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var boxid = SequentialGuid.CreateGuid();

            db.tblInbox.InsertRow(boxid, f1, 0, null);
            db.tblInbox.InsertRow(boxid, f2, 0, null);
            db.tblInbox.InsertRow(boxid, f3, 10, null);
            db.tblInbox.InsertRow(boxid, f4, 10, null);
            db.tblInbox.InsertRow(boxid, f5, 20, null);

            var r1 = db.tblInbox.Pop(boxid, 2, out var popTimestamp1);
            var r2 = db.tblInbox.Pop(boxid, 3, out var popTimestamp2);

            db.tblInbox.PopCancel(popTimestamp1);

            var r3 = db.tblInbox.Pop(boxid, 10, out var popTimestamp3);

            if (r3.Count != 2)
                Assert.Fail();

            if (SequentialGuid.muidcmp(r1[0].fileId, r3[0].fileId) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r1[1].fileId, r3[1].fileId) != 0)
                Assert.Fail();

            db.tblInbox.PopCancel(popTimestamp3);
            db.tblInbox.PopCancel(popTimestamp2);
            var r4 = db.tblInbox.Pop(boxid, 10, out var popTimestamp4);

            if (r4.Count != 5)
                Assert.Fail();
        }


        [TestCase()]
        public void PopCommitTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\inboxtest4.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var boxid = SequentialGuid.CreateGuid();

            db.tblInbox.InsertRow(boxid, f1, 0, null);
            db.tblInbox.InsertRow(boxid, f2, 0, null);
            db.tblInbox.InsertRow(boxid, f3, 10, null);
            db.tblInbox.InsertRow(boxid, f4, 10, null);
            db.tblInbox.InsertRow(boxid, f5, 20, null);

            var r1 = db.tblInbox.Pop(boxid, 2, out var popTimestamp1);
            db.tblInbox.PopCommit(popTimestamp1);

            var r2 = db.tblInbox.Pop(boxid, 10, out var popTimestamp2);
            if (r2.Count != 3)
                Assert.Fail();
        }

        [TestCase()]
        public void PopRecoverDeadTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\inboxtest5.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var boxid = SequentialGuid.CreateGuid();

            db.tblInbox.InsertRow(boxid, f1, 0, null);
            db.tblInbox.InsertRow(boxid, f2, 0, null);
            db.tblInbox.InsertRow(boxid, f3, 10, null);
            db.tblInbox.InsertRow(boxid, f4, 10, null);
            db.tblInbox.InsertRow(boxid, f5, 20, null);

            var r1 = db.tblInbox.Pop(boxid, 2, out var popTimestamp1);

            // Recover all items older than the future (=all)
            db.tblInbox.PopRecoverDead(UnixTime.GetUnixTimeSeconds()+2);

            var r2 = db.tblInbox.Pop(boxid, 10, out var popTimestamp2);
            if (r2.Count != 5)
                Assert.Fail();

            // Recover items older than long ago (=none)
            db.tblInbox.PopRecoverDead(UnixTime.GetUnixTimeSeconds() - 2);
            var r3 = db.tblInbox.Pop(boxid, 10, out var popTimestamp3);
            if (r3.Count != 0)
                Assert.Fail();
        }


        [TestCase()]
        public void DualBoxTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\inboxtest6.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var b1 = SequentialGuid.CreateGuid();
            var b2 = SequentialGuid.CreateGuid();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            db.tblInbox.InsertRow(b1, f1, 0, v1);
            db.tblInbox.InsertRow(b1, f2, 10, v1);
            db.tblInbox.InsertRow(b2, f3, 10, v1);
            db.tblInbox.InsertRow(b2, f4, 10, v1);
            db.tblInbox.InsertRow(b2, f5, 10, v1);

            // Pop the oldest record from the inbox 1
            var r1 = db.tblInbox.Pop(b1, 1, out var popTimestamp1);
            var r2 = db.tblInbox.Pop(b1, 10, out var popTimestamp2);
            if (r2.Count != 1)
                Assert.Fail();

            // Then pop 10 oldest record from the inbox (only 2 are available now)
            var r3 = db.tblInbox.Pop(b2, 10, out var popTimestamp3);
            if (r3.Count != 3)
                Assert.Fail();

            // The thread that popped the first record is now done.
            // Commit the pop
            db.tblInbox.PopCommit(popTimestamp1);

            // Oh no, the second thread running on the second pop of records
            // encountered a terrible error. Undo the pop
            db.tblInbox.PopCancel(popTimestamp2);

            var r4 = db.tblInbox.Pop(b1, 10, out var popTimestamp4);
            if (r4.Count != 1)
                Assert.Fail();
        }


        [TestCase()]
        public void ExampleTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\inboxtest7.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var v2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var v3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var v4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var v5 = SequentialGuid.CreateGuid();

            var box1id = SequentialGuid.CreateGuid();
            var box2id = SequentialGuid.CreateGuid();

            // Insert three records into "inbox1"
            // Insert two   records into "inbox2"
            // An inbox is simply a GUID. E.g. the DriveID.
            // A record has a fileId, priority and a custom value
            // The custom value could e.g. be a GUID or a JSON of { senderId, appId }
            db.tblInbox.InsertRow(box1id, f1,  0, v1);
            db.tblInbox.InsertRow(box1id, f2, 10, v2);
            db.tblInbox.InsertRow(box1id, f3, 10, v3);
            db.tblInbox.InsertRow(box2id, f4, 10, v4);
            db.tblInbox.InsertRow(box2id, f5, 10, v5);

            // A thread1 pops one record from inbox1 (it'll get the oldest one)
            // Popping the record "reserves it" for your thread but doesn't remove
            // it from the inbox until the pop is committed or cancelled.
            var r1 = db.tblInbox.Pop(box1id, 1, out var popTimestamp1);

            // Another thread2 then pops 10 records from inbox1 (only 2 are available now)
            var r2 = db.tblInbox.Pop(box1id, 10, out var popTimestamp2);

            // The thread1 that popped the first record is now done.
            // Commit the pop, which effectively deletes it from the inbox
            // You of course call commit as the very final step when you're
            // certain the item has been saved correctly.
            db.tblInbox.PopCommit(popTimestamp1);

            // Imagine that thread2 encountered a terrible error, e.g. out of disk space
            // Undo the pop and put the items back into the inbox
            db.tblInbox.PopCancel(popTimestamp2);

            // Thread3 pops 10 items from inbox2 (will retrieve 2)
            var r3 = db.tblInbox.Pop(box2id, 10, out var popTimestamp3);

            // Now imagine that there is a power outage, the server crashes.
            // The popped items are in "limbo" because they are not committed and not cancelled.
            // You can recover items popped for more than X seconds like this:
            db.tblInbox.PopRecoverDead(60*10);

            // That would recover all popped items that have not been committed or cancelled.
        }
    }
}