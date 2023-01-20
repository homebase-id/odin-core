﻿using System.Collections.Generic;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace IndexerTests.KeyValue
{
    public class TableOutboxTests
    {
        [TestCase()]
        public void InsertRowTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest1.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();

            var boxid = SequentialGuid.CreateGuid().ToByteArray();

            var tslo = UnixTimeUtc.Now();
            db.tblOutbox.InsertRow(boxid, f1, 0, v1);
            db.tblOutbox.InsertRow(boxid, f2, 10, v2);
            var tshi = UnixTimeUtc.Now();

            var r = db.tblOutbox.Get(f1);
            if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                Assert.Fail();
            if (r.priority != 0)
                Assert.Fail();

            r = db.tblOutbox.Get(f2);
            if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                Assert.Fail();
            if (r.priority != 10)
                Assert.Fail();
        }

        [TestCase()]
        public void PopTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest2.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();
            var f4 = SequentialGuid.CreateGuid().ToByteArray();
            var f5 = SequentialGuid.CreateGuid().ToByteArray();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var v3 = SequentialGuid.CreateGuid().ToByteArray();
            var v4 = SequentialGuid.CreateGuid().ToByteArray();
            var v5 = SequentialGuid.CreateGuid().ToByteArray();
            var boxid = SequentialGuid.CreateGuid().ToByteArray();

            var tslo = UnixTimeUtc.Now();
            db.tblOutbox.InsertRow(boxid, f1, 0, v1);
            db.tblOutbox.InsertRow(boxid, f2, 1, v2);
            db.tblOutbox.InsertRow(boxid, f3, 2, v3);
            db.tblOutbox.InsertRow(boxid, f4, 3, v4);
            db.tblOutbox.InsertRow(boxid, f5, 4, v5);
            var tshi = UnixTimeUtc.Now();

            // pop one item from the Outbox
            var r = db.tblOutbox.Pop(boxid, 1, out var popTimestamp);
            if (r.Count != 1)
                Assert.Fail();

            if (popTimestamp == null)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[0].fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[0].value, v1) != 0)
                Assert.Fail();
            if (r[0].priority != 0)
                Assert.Fail();
            if ((r[0].timeStamp < tslo) || (r[0].timeStamp > tshi))
                Assert.Fail();

            // pop all the remaining items from the Outbox
            r = db.tblOutbox.Pop(boxid, 10, out popTimestamp);
            if (r.Count != 4)
                Assert.Fail();

            if (popTimestamp == null)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[0].fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[0].value, v2) != 0)
                Assert.Fail();
            if (r[0].priority != 1)
                Assert.Fail();
            if ((r[0].timeStamp < tslo) || (r[0].timeStamp > tshi))
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[1].fileId, f3) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[1].value, v3) != 0)
                Assert.Fail();
            if (r[1].priority != 2)
                Assert.Fail();
            if ((r[1].timeStamp < tslo) || (r[1].timeStamp > tshi))
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[2].fileId, f4) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[2].value, v4) != 0)
                Assert.Fail();
            if (r[2].priority != 3)
                Assert.Fail();
            if ((r[2].timeStamp < tslo) || (r[2].timeStamp > tshi))
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[3].fileId, f5) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[3].value, v5) != 0)
                Assert.Fail();
            if (r[3].priority != 4)
                Assert.Fail();
            if ((r[3].timeStamp < tslo) || (r[3].timeStamp > tshi))
                Assert.Fail();

            // pop to make sure there are no more items
            r = db.tblOutbox.Pop(boxid, 1, out popTimestamp);
            if (r.Count != 0)
                Assert.Fail();
        }

        [TestCase()]
        public void PopCancelTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest3.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();
            var f4 = SequentialGuid.CreateGuid().ToByteArray();
            var f5 = SequentialGuid.CreateGuid().ToByteArray();
            var boxid = SequentialGuid.CreateGuid().ToByteArray();

            db.tblOutbox.InsertRow(boxid, f1, 0, null);
            db.tblOutbox.InsertRow(boxid, f2, 0, null);
            db.tblOutbox.InsertRow(boxid, f3, 10, null);
            db.tblOutbox.InsertRow(boxid, f4, 10, null);
            db.tblOutbox.InsertRow(boxid, f5, 20, null);

            var r1 = db.tblOutbox.Pop(boxid, 2, out var popTimestamp1);
            var r2 = db.tblOutbox.Pop(boxid, 3, out var popTimestamp2);

            db.tblOutbox.PopCancel(popTimestamp1);

            var r3 = db.tblOutbox.Pop(boxid, 10, out var popTimestamp3);

            if (r3.Count != 2)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r1[0].fileId, r3[0].fileId) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r1[1].fileId, r3[1].fileId) != 0)
                Assert.Fail();

            db.tblOutbox.PopCancel(popTimestamp3);
            db.tblOutbox.PopCancel(popTimestamp2);
            var r4 = db.tblOutbox.Pop(boxid, 10, out var popTimestamp4);

            if (r4.Count != 5)
                Assert.Fail();
        }


        [TestCase()]
        public void PopCommitTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest4.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();
            var f4 = SequentialGuid.CreateGuid().ToByteArray();
            var f5 = SequentialGuid.CreateGuid().ToByteArray();

            var boxid = SequentialGuid.CreateGuid().ToByteArray();

            db.tblOutbox.InsertRow(boxid, f1, 0, null);
            db.tblOutbox.InsertRow(boxid, f2, 0, null);
            db.tblOutbox.InsertRow(boxid, f3, 10, null);
            db.tblOutbox.InsertRow(boxid, f4, 10, null);
            db.tblOutbox.InsertRow(boxid, f5, 20, null);

            var r1 = db.tblOutbox.Pop(boxid, 2, out var popTimestamp1);
            db.tblOutbox.PopCommit(popTimestamp1);

            var r2 = db.tblOutbox.Pop(boxid, 10, out var popTimestamp2);
            if (r2.Count != 3)
                Assert.Fail();
        }

        [TestCase()]
        public void PopRecoverDeadTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest5.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();
            var f4 = SequentialGuid.CreateGuid().ToByteArray();
            var f5 = SequentialGuid.CreateGuid().ToByteArray();

            var boxid = SequentialGuid.CreateGuid().ToByteArray();

            db.tblOutbox.InsertRow(boxid, f1, 0, null);
            db.tblOutbox.InsertRow(boxid, f2, 0, null);
            db.tblOutbox.InsertRow(boxid, f3, 10, null);
            db.tblOutbox.InsertRow(boxid, f4, 10, null);
            db.tblOutbox.InsertRow(boxid, f5, 20, null);

            var r1 = db.tblOutbox.Pop(boxid, 2, out var popTimestamp1);

            // Recover all items older than the future (=all)
            db.tblOutbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(2));

            var r2 = db.tblOutbox.Pop(boxid, 10, out var popTimestamp2);
            if (r2.Count != 5)
                Assert.Fail();

            // Recover items older than long ago (=none)
            db.tblOutbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(-2));
            var r3 = db.tblOutbox.Pop(boxid, 10, out var popTimestamp3);
            if (r3.Count != 0)
                Assert.Fail();
        }


        [TestCase()]
        public void DualBoxTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest6.db");
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
            db.tblOutbox.InsertRow(b1.ToByteArray(), f1.ToByteArray(), 0, v1.ToByteArray());
            db.tblOutbox.InsertRow(b1.ToByteArray(), f2.ToByteArray(), 10, v1.ToByteArray());
            db.tblOutbox.InsertRow(b2.ToByteArray(), f3.ToByteArray(), 10, v1.ToByteArray());
            db.tblOutbox.InsertRow(b2.ToByteArray(), f4.ToByteArray(), 10, v1.ToByteArray());
            db.tblOutbox.InsertRow(b2.ToByteArray(), f5.ToByteArray(), 10, v1.ToByteArray());

            // Pop the oldest record from the Outbox 1
            var r1 = db.tblOutbox.Pop(b1.ToByteArray(), 1, out var popTimestamp1);
            var r2 = db.tblOutbox.Pop(b1.ToByteArray(), 10, out var popTimestamp2);
            if (r2.Count != 1)
                Assert.Fail();

            // Then pop 10 oldest record from the Outbox (only 2 are available now)
            var r3 = db.tblOutbox.Pop(b2.ToByteArray(), 10, out var popTimestamp3);
            if (r3.Count != 3)
                Assert.Fail();

            // The thread that popped the first record is now done.
            // Commit the pop
            db.tblOutbox.PopCommit(popTimestamp1);

            // Oh no, the second thread running on the second pop of records
            // encountered a terrible error. Undo the pop
            db.tblOutbox.PopCancel(popTimestamp2);

            var r4 = db.tblOutbox.Pop(b1.ToByteArray(), 10, out var popTimestamp4);
            if (r4.Count != 1)
                Assert.Fail();
        }


        [TestCase()]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest7.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();
            var v3 = SequentialGuid.CreateGuid().ToByteArray();
            var f4 = SequentialGuid.CreateGuid().ToByteArray();
            var v4 = SequentialGuid.CreateGuid().ToByteArray();
            var f5 = SequentialGuid.CreateGuid().ToByteArray();
            var v5 = SequentialGuid.CreateGuid().ToByteArray();

            var box1id = SequentialGuid.CreateGuid().ToByteArray();
            var box2id = SequentialGuid.CreateGuid().ToByteArray();

            // Insert three records into "Outbox1"
            // Insert two   records into "Outbox2"
            // An Outbox is simply a GUID. E.g. the DriveID.
            // A record has a fileId, priority and a custom value
            // The custom value could e.g. be a GUID or a JSON of { senderId, appId }
            db.tblOutbox.InsertRow(box1id, f1,  0, v1);
            db.tblOutbox.InsertRow(box1id, f2, 10, v2);
            db.tblOutbox.InsertRow(box1id, f3, 10, v3);
            db.tblOutbox.InsertRow(box2id, f4, 10, v4);
            db.tblOutbox.InsertRow(box2id, f5, 10, v5);

            // A thread1 pops one record from Outbox1 (it'll get the oldest one)
            // Popping the record "reserves it" for your thread but doesn't remove
            // it from the Outbox until the pop is committed or cancelled.
            var r1 = db.tblOutbox.Pop(box1id, 1, out var popTimestamp1);

            // Another thread2 then pops 10 records from Outbox1 (only 2 are available now)
            var r2 = db.tblOutbox.Pop(box1id, 10, out var popTimestamp2);

            // The thread1 that popped the first record is now done.
            // Commit the pop, which effectively deletes it from the Outbox
            // You of course call commit as the very final step when you're
            // certain the item has been saved correctly.
            db.tblOutbox.PopCommit(popTimestamp1);

            // Imagine that thread2 encountered a terrible error, e.g. out of disk space
            // Undo the pop and put the items back into the Outbox
            db.tblOutbox.PopCancel(popTimestamp2);

            // Thread3 pops 10 items from Outbox2 (will retrieve 2)
            var r3 = db.tblOutbox.Pop(box2id, 10, out var popTimestamp3);

            // Now imagine that there is a power outage, the server crashes.
            // The popped items are in "limbo" because they are not committed and not cancelled.
            // You can recover items popped for more than X seconds like this:
            db.tblOutbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(60*10));

            // That would recover all popped items that have not been committed or cancelled.
        }


        [TestCase()]
        public void PopCancelListTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest8.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();

            var b1 = SequentialGuid.CreateGuid().ToByteArray();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            db.tblOutbox.InsertRow(b1, f1, 0, v1);
            db.tblOutbox.InsertRow(b1, f2, 10, v1);
            db.tblOutbox.InsertRow(b1, f3, 10, v1);

            // Pop all records from the Outbox,be sure we get 3
            var r1 = db.tblOutbox.Pop(b1, 5, out var popTimestamp1);
            if (r1.Count != 3)
                Assert.Fail();

            // Cancel two of the three records
            db.tblOutbox.PopCancelList(popTimestamp1, new List<byte[]>() { f1, f2 });

            // Pop all the recods from the Outbox, but sure we get the two cancelled
            var r2 = db.tblOutbox.Pop(b1, 5, out var popTimestamp2);
            if (r2.Count != 2)
                Assert.Fail();

            // Cancel one of the two records
            db.tblOutbox.PopCancelList(popTimestamp2, new List<byte[]>() { f1 });

            // Pop all the recods from the Outbox, but sure we get the two cancelled
            var r3 = db.tblOutbox.Pop(b1, 5, out var popTimestamp3);
            if (r3.Count != 1)
                Assert.Fail();
        }


        [TestCase()]
        public void PopCommitListTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest9.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();

            var b1 = SequentialGuid.CreateGuid().ToByteArray();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            db.tblOutbox.InsertRow(b1, f1, 0, v1);
            db.tblOutbox.InsertRow(b1, f2, 10, v1);
            db.tblOutbox.InsertRow(b1, f3, 10, v1);

            // Pop all records from the Outbox,be sure we get 3
            var r1 = db.tblOutbox.Pop(b1, 5, out var popTimestamp1);
            if (r1.Count != 3)
                Assert.Fail();

            // Commit one of the three records
            db.tblOutbox.PopCommitList(popTimestamp1, new List<byte[]>() { f2 });

            // Cancel the rest (f1, f3)
            db.tblOutbox.PopCancel(popTimestamp1);

            // Pop all records from the Outbox,be sure we get 2 (f1 & f3)
            var r2 = db.tblOutbox.Pop(b1, 5, out var popTimestamp2);
            if (r2.Count != 2)
                Assert.Fail();

            // Commit all records
            db.tblOutbox.PopCommitList(popTimestamp2, new List<byte[]>() { f1, f3 });

            // Cancel nothing
            db.tblOutbox.PopCancel(popTimestamp2);
            // Get everything back
            db.tblOutbox.PopRecoverDead(new UnixTimeUtc());

            // Pop all records from the Outbox,be sure we get 2 (f1 & f3)
            var r3 = db.tblOutbox.Pop(b1, 5, out var popTimestamp3);
            if (r3.Count != 0)
                Assert.Fail();
        }


        [TestCase()]
        public void PopAllTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\Outboxtest42.db");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid().ToByteArray();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid().ToByteArray();
            var f4 = SequentialGuid.CreateGuid().ToByteArray();
            var f5 = SequentialGuid.CreateGuid().ToByteArray();
            var f6 = SequentialGuid.CreateGuid().ToByteArray();
            var f7 = SequentialGuid.CreateGuid().ToByteArray();
            var f8 = SequentialGuid.CreateGuid().ToByteArray();
            var f9 = SequentialGuid.CreateGuid().ToByteArray();
            var f10 = SequentialGuid.CreateGuid().ToByteArray();

            var box1id = SequentialGuid.CreateGuid().ToByteArray();
            var box2id = SequentialGuid.CreateGuid().ToByteArray();
            var box3id = SequentialGuid.CreateGuid().ToByteArray();

            db.tblOutbox.InsertRow(box1id, f1,  0, v1);
            db.tblOutbox.InsertRow(box1id, f2, 10, v1);
            db.tblOutbox.InsertRow(box1id, f3, 10, v1);
            db.tblOutbox.InsertRow(box2id, f4, 10, v1);
            db.tblOutbox.InsertRow(box2id, f5, 10, v1);
            db.tblOutbox.InsertRow(box3id, f6, 0, v1);
            db.tblOutbox.InsertRow(box3id, f7, 10, v1);
            db.tblOutbox.InsertRow(box3id, f8, 10, v1);
            db.tblOutbox.InsertRow(box3id, f9, 10, v1);
            db.tblOutbox.InsertRow(box3id, f10, 10, v1);

            var r = db.tblOutbox.PopAll(out var popStamp);

            if (r.Count != 3)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[0].fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[0].boxId, box1id) != 0)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[1].fileId, f4) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[1].boxId, box2id) != 0)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[2].fileId, f6) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[2].boxId, box3id) != 0)
                Assert.Fail();

            // That would recover all popped items that have not been committed or cancelled.
        }
    }
}