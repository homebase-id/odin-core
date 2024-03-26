using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableOutboxTests
    {
        [TestCase()]
        public void InsertRowTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var did1 = SequentialGuid.CreateGuid();

            var driveId = SequentialGuid.CreateGuid();

            var tslo = UnixTimeUtc.Now();
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, dependencyFileId = null, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 10, dependencyFileId = did1, value = v2 });
            var tshi = UnixTimeUtc.Now();

            var r = db.tblOutbox.Get(driveId, f1, "frodo.baggins.me");
            if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                Assert.Fail();
            if (r.dependencyFileId != null)
                Assert.Fail();
            if (r.priority != 0)
                Assert.Fail();

            r = db.tblOutbox.Get(driveId, f2, "frodo.baggins.me");
            if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.dependencyFileId, did1) != 0)
                Assert.Fail();
            if (r.priority != 10)
                Assert.Fail();
        }

        [TestCase()]
        public void PopTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var v3 = SequentialGuid.CreateGuid().ToByteArray();
            var v4 = SequentialGuid.CreateGuid().ToByteArray();
            var v5 = SequentialGuid.CreateGuid().ToByteArray();
            var driveId = SequentialGuid.CreateGuid();

            var tslo = UnixTimeUtc.Now();
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 1, value = v2 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 2, value = v3 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 3, value = v4 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 4, value = v5 });
            var tshi = UnixTimeUtc.Now();

            // pop one item from the Outbox
            var r = db.tblOutbox.CheckOutItem();
            if (r == null)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                Assert.Fail();
            if (r.priority != 0)
                Assert.Fail();
            Assert.IsTrue(r.recipient == "frodo.baggins.me");

            var (ti,tp,nrt)  = db.tblOutbox.OutboxStatus();
            Debug.Assert(ti == 5);
            Debug.Assert(tp == 1);

            // pop all the remaining items from the Outbox
            r = db.tblOutbox.CheckOutItem();
            if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                Assert.Fail();
            if (r.priority != 1)
                Assert.Fail();
            Assert.IsTrue(r.recipient == "frodo.baggins.me");

            r = db.tblOutbox.CheckOutItem();
            if (ByteArrayUtil.muidcmp(r.fileId, f3) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v3) != 0)
                Assert.Fail();
            if (r.priority != 2)
                Assert.Fail();
            Assert.IsTrue(r.recipient == "frodo.baggins.me");

            r = db.tblOutbox.CheckOutItem();
            if (ByteArrayUtil.muidcmp(r.fileId, f4) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v4) != 0)
                Assert.Fail();
            if (r.priority != 3)
                Assert.Fail();
            Assert.IsTrue(r.recipient == "frodo.baggins.me");

            r = db.tblOutbox.CheckOutItem();
            if (ByteArrayUtil.muidcmp(r.fileId, f5) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v5) != 0)
                Assert.Fail();
            if (r.priority != 4)
                Assert.Fail();
            Assert.IsTrue(r.recipient == "frodo.baggins.me");

            r = db.tblOutbox.CheckOutItem();
            if (r != null)
                Assert.Fail();
        }

        // Make sure priority takes precedence
        [TestCase()]
        public void PriorityTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var v3 = SequentialGuid.CreateGuid().ToByteArray();
            var v4 = SequentialGuid.CreateGuid().ToByteArray();
            var v5 = SequentialGuid.CreateGuid().ToByteArray();
            var driveId = SequentialGuid.CreateGuid();

            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 4, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 1, value = v2 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 2, value = v3 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 3, value = v4 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 0, value = v5 });

            var r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.priority == 0);
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.priority == 1);
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.priority == 2);
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.priority == 3);
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.priority == 4);
        }

        // With the same priority, make sure nextRunTime matters
        [TestCase()]
        public void NextRunTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var v3 = SequentialGuid.CreateGuid().ToByteArray();
            var v4 = SequentialGuid.CreateGuid().ToByteArray();
            var v5 = SequentialGuid.CreateGuid().ToByteArray();
            var driveId = SequentialGuid.CreateGuid();

            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "2frodo.baggins.me", priority = 0, value = v2 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "3frodo.baggins.me", priority = 0, value = v3 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "4frodo.baggins.me", priority = 0, value = v4 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "5frodo.baggins.me", priority = 0, value = v5 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "1frodo.baggins.me", priority = 0, value = v1 });

            var r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.recipient == "2frodo.baggins.me");
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.recipient == "3frodo.baggins.me");
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.recipient == "4frodo.baggins.me");
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.recipient == "5frodo.baggins.me");
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(r.recipient == "1frodo.baggins.me");
        }


        // Make sure dependency works
        [TestCase()]
        public void DependencyTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var v3 = SequentialGuid.CreateGuid().ToByteArray();
            var v4 = SequentialGuid.CreateGuid().ToByteArray();
            var v5 = SequentialGuid.CreateGuid().ToByteArray();
            var driveId = SequentialGuid.CreateGuid();

            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", dependencyFileId = f3,  priority = 0, value = v2 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", dependencyFileId = null, priority = 0, value = v3 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", dependencyFileId = f2, priority = 0, value = v4 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", dependencyFileId = f4, priority = 0, value = v5 });
            Task.Delay(2);
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", dependencyFileId = f5, priority = 0, value = v1 });

            var r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f3) == 0);
            var nr = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(nr == null);

            db.tblOutbox.CompleteAndRemove((Guid) r.checkOutStamp);

            // Get the next one
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f2) == 0);
            nr = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(nr == null);
            db.tblOutbox.CompleteAndRemove((Guid)r.checkOutStamp);

            // Get the next one
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f4) == 0);
            nr = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(nr == null);
            db.tblOutbox.CompleteAndRemove((Guid)r.checkOutStamp);

            // Get the next one
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f5) == 0);
            nr = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(nr == null);
            db.tblOutbox.CompleteAndRemove((Guid)r.checkOutStamp);

            // Get the next one
            r = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f1) == 0);
            nr = db.tblOutbox.CheckOutItem();
            Assert.IsTrue(nr == null);
            db.tblOutbox.CompleteAndRemove((Guid)r.checkOutStamp);
        }


        /*
        [TestCase()]
        public void PopCancelTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var driveId = SequentialGuid.CreateGuid();

            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 0, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 20, value = null });

            var r1 = db.tblOutbox.CheckOutItem();
            var r2 = db.tblOutbox.CheckOutItem();

            db.tblOutbox.CheckInAsCancelled((Guid)r1.checkOutStamp, UnixTimeUtc.Now().AddHours(1));

            var r3 = db.tblOutbox.CheckOutItem();

            if (ByteArrayUtil.muidcmp(r1.fileId, r3.fileId) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r1[1].fileId, r3[1].fileId) != 0)
                Assert.Fail();

            db.tblOutbox.CheckInAsCancelled((Guid)r3[0].checkOutStamp);
            db.tblOutbox.CheckInAsCancelled((Guid)r2[0].checkOutStamp);
            var r4 = db.tblOutbox.CheckOutItem(driveId, 10);

            if (r4.Count != 5)
                Assert.Fail();
        }
        

        [TestCase()]
        public void PopCommitTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var driveId = SequentialGuid.CreateGuid();

            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 0, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 20, value = null });

            var r1 = db.tblOutbox.CheckOutItem(driveId, 2);
            db.tblOutbox.CompleteAndRemove((Guid)r1[0].checkOutStamp);

            var r2 = db.tblOutbox.CheckOutItem(driveId, 10);
            if (r2.Count != 3)
                Assert.Fail();
        }

        [TestCase()]
        public void PopRecoverDeadTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var driveId = SequentialGuid.CreateGuid();

            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 0, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = null });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 20, value = null });

            var r1 = db.tblOutbox.CheckOutItem(driveId, 2);

            // Recover all items older than the future (=all)
            db.tblOutbox.RecoverCheckedOutDeadItems(UnixTimeUtc.Now().AddSeconds(2));

            var r2 = db.tblOutbox.CheckOutItem(driveId, 10);
            if (r2.Count != 5)
                Assert.Fail();

            // Recover items older than long ago (=none)
            db.tblOutbox.RecoverCheckedOutDeadItems(UnixTimeUtc.Now().AddSeconds(-2));
            var r3 = db.tblOutbox.CheckOutItem(driveId, 10);
            if (r3.Count != 0)
                Assert.Fail();
        }


        [TestCase()]
        public void DualBoxTest()
        {
            using var db = new IdentityDatabase("");
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
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1.ToByteArray() });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f2, recipient = "frodo.baggins.me", priority = 10, value = v1.ToByteArray() });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b2, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = v1.ToByteArray() });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b2, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = v1.ToByteArray() });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b2, fileId = f5, recipient = "frodo.baggins.me", priority = 10, value = v1.ToByteArray() });

            // Pop the oldest record from the Outbox 1
            var r1 = db.tblOutbox.CheckOutItem(b1, 1);
            var r2 = db.tblOutbox.CheckOutItem(b1, 10);
            if (r2.Count != 1)
                Assert.Fail();

            // Then pop 10 oldest record from the Outbox (only 2 are available now)
            var r3 = db.tblOutbox.CheckOutItem(b2, 10);
            if (r3.Count != 3)
                Assert.Fail();

            // The thread that popped the first record is now done.
            // Commit the pop
            db.tblOutbox.CompleteAndRemove((Guid)r1[0].checkOutStamp);

            // Oh no, the second thread running on the second pop of records
            // encountered a terrible error. Undo the pop
            db.tblOutbox.CheckInAsCancelled((Guid)r2[0].checkOutStamp);

            var r4 = db.tblOutbox.CheckOutItem(b1, 10);
            if (r4.Count != 1)
                Assert.Fail();
        }


        [TestCase()]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();
            var f3 = SequentialGuid.CreateGuid();
            var v3 = SequentialGuid.CreateGuid().ToByteArray();
            var f4 = SequentialGuid.CreateGuid();
            var v4 = SequentialGuid.CreateGuid().ToByteArray();
            var f5 = SequentialGuid.CreateGuid();
            var v5 = SequentialGuid.CreateGuid().ToByteArray();

            var box1id = SequentialGuid.CreateGuid();
            var box2id = SequentialGuid.CreateGuid();

            // Insert three records into "Outbox1"
            // Insert two   records into "Outbox2"
            // An Outbox is simply a GUID. E.g. the DriveID.
            // A record has a fileId, priority and a custom value
            // The custom value could e.g. be a GUID or a JSON of { senderId, appId }
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box1id, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box1id, fileId = f2, recipient = "frodo.baggins.me", priority = 10, value = v2 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box1id, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = v3 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box2id, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = v4 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box2id, fileId = f5, recipient = "frodo.baggins.me", priority = 10, value = v5 });

            // A thread1 pops one record from Outbox1 (it'll get the oldest one)
            // Popping the record "reserves it" for your thread but doesn't remove
            // it from the Outbox until the pop is committed or cancelled.
            var r1 = db.tblOutbox.CheckOutItem(box1id, 1);

            // Another thread2 then pops 10 records from Outbox1 (only 2 are available now)
            var r2 = db.tblOutbox.CheckOutItem(box1id, 10);

            // The thread1 that popped the first record is now done.
            // Commit the pop, which effectively deletes it from the Outbox
            // You of course call commit as the very final step when you're
            // certain the item has been saved correctly.
            db.tblOutbox.CompleteAndRemove((Guid) r1[0].checkOutStamp);

            // Imagine that thread2 encountered a terrible error, e.g. out of disk space
            // Undo the pop and put the items back into the Outbox
            db.tblOutbox.CheckInAsCancelled((Guid)r2[0].checkOutStamp);

            // Thread3 pops 10 items from Outbox2 (will retrieve 2)
            var r3 = db.tblOutbox.CheckOutItem(box2id, 10);

            // Now imagine that there is a power outage, the server crashes.
            // The popped items are in "limbo" because they are not committed and not cancelled.
            // You can recover items popped for more than X seconds like this:
            db.tblOutbox.RecoverCheckedOutDeadItems(UnixTimeUtc.Now().AddSeconds(60*10));

            // That would recover all popped items that have not been committed or cancelled.
        }


        [TestCase()]
        public void PopCancelListTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            var b1 = SequentialGuid.CreateGuid();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f2, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = v1 });

            // Pop all records from the Outbox,be sure we get 3
            var r1 = db.tblOutbox.CheckOutItem(b1, 5);
            if (r1.Count != 3)
                Assert.Fail();

            // Cancel two of the three records
            db.tblOutbox.CheckInAsCancelledList((Guid)r1[0].checkOutStamp, new List<Guid>() { f1, f2 });

            // Pop all the recods from the Outbox, but sure we get the two cancelled
            var r2 = db.tblOutbox.CheckOutItem(b1, 5);
            if (r2.Count != 2)
                Assert.Fail();

            // Cancel one of the two records
            db.tblOutbox.CheckInAsCancelledList((Guid)r2[0].checkOutStamp, new List<Guid>() { f1 });

            // Pop all the recods from the Outbox, but sure we get the two cancelled
            var r3 = db.tblOutbox.CheckOutItem(b1, 5);
            if (r3.Count != 1)
                Assert.Fail();
        }


        [TestCase()]
        public void PopCommitListTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            var b1 = SequentialGuid.CreateGuid();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f2, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = b1, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = v1 });

            // Pop all records from the Outbox,be sure we get 3
            var r1 = db.tblOutbox.CheckOutItem(b1, 5);
            if (r1.Count != 3)
                Assert.Fail();

            // Commit one of the three records
            db.tblOutbox.CompleteAndRemoveList((Guid)r1[0].checkOutStamp, new List<Guid>() { f2 });

            // Cancel the rest (f1, f3)
            db.tblOutbox.CheckInAsCancelled((Guid)r1[0].checkOutStamp);

            // Pop all records from the Outbox,be sure we get 2 (f1 & f3)
            var r2 = db.tblOutbox.CheckOutItem(b1, 5);
            if (r2.Count != 2)
                Assert.Fail();

            // Commit all records
            db.tblOutbox.CompleteAndRemoveList((Guid)r2[0].checkOutStamp, new List<Guid>() { f1, f3 });

            // Cancel nothing
            db.tblOutbox.CheckInAsCancelled((Guid)r2[0].checkOutStamp);
            // Get everything back
            db.tblOutbox.RecoverCheckedOutDeadItems(new UnixTimeUtc());

            // Pop all records from the Outbox,be sure we get 2 (f1 & f3)
            var r3 = db.tblOutbox.CheckOutItem(b1, 5);
            if (r3.Count != 0)
                Assert.Fail();
        }


        [TestCase()]
        public void PopAnyBoxTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var f6 = SequentialGuid.CreateGuid();
            var f7 = SequentialGuid.CreateGuid();
            var f8 = SequentialGuid.CreateGuid();
            var f9 = SequentialGuid.CreateGuid();
            var f10 = SequentialGuid.CreateGuid();

            var box1id = SequentialGuid.CreateGuid();
            var box2id = SequentialGuid.CreateGuid();
            var box3id = SequentialGuid.CreateGuid();

            db.tblOutbox.Insert(new OutboxRecord() { driveId = box1id, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box1id, fileId = f2, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box1id, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box2id, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box2id, fileId = f5, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box3id, fileId = f6, recipient = "frodo.baggins.me", priority = 0, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box3id, fileId = f7, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box3id, fileId = f8, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box3id, fileId = f9, recipient = "frodo.baggins.me", priority = 10, value = v1 });
            db.tblOutbox.Insert(new OutboxRecord() { driveId = box3id, fileId = f10, recipient = "frodo.baggins.me", priority = 10, value = v1 });

            var (tot, pop, poptime) = db.tblOutbox.OutboxStatus();
            Assert.AreEqual(10, tot);
            Assert.AreEqual( 0, pop);
            Assert.AreEqual(UnixTimeUtc.ZeroTime, poptime);

            (tot, pop, poptime) = db.tblOutbox.OutboxStatusSpecificBox(box1id);
            Assert.AreEqual(3, tot);
            Assert.AreEqual(0, pop);
            Assert.AreEqual(UnixTimeUtc.ZeroTime, poptime);

            var tbefore = new UnixTimeUtc();
            var r = db.tblOutbox.CheckOutItem(box1id, 1000);
            var tafter = new UnixTimeUtc();

            if (r.Count != 3)
                Assert.Fail();

            (tot, pop, poptime) = db.tblOutbox.OutboxStatus();
            Assert.AreEqual(10, tot);
            Assert.AreEqual( 3, pop);
            if (poptime < tbefore) // We can't have popped before we popped
                Assert.Fail();
            if (poptime > tafter) // We can't have popped after we popped
                Assert.Fail();

            (tot, pop, poptime) = db.tblOutbox.OutboxStatusSpecificBox(box1id);
            Assert.AreEqual(3, tot);
            Assert.AreEqual(3, pop);
            if (poptime < tbefore) // We can't have popped before we popped
                Assert.Fail();
            if (poptime > tafter) // We can't have popped after we popped
                Assert.Fail();


            if (ByteArrayUtil.muidcmp(r[0].fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[0].driveId, box1id) != 0)
                Assert.Fail();
            Assert.IsTrue(r[0].recipient == "frodo.baggins.me");

            if (ByteArrayUtil.muidcmp(r[1].fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[1].driveId, box1id) != 0)
                Assert.Fail();
            Assert.IsTrue(r[1].recipient == "frodo.baggins.me");

            if (ByteArrayUtil.muidcmp(r[2].fileId, f3) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[2].driveId, box1id) != 0)
                Assert.Fail();
            Assert.IsTrue(r[2].recipient == "frodo.baggins.me");

            // That would recover all popped items that have not been committed or cancelled.
        }
*/
    }
}