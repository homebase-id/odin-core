using System;
using System.Collections.Generic;
using NUnit.Framework;
using Org.BouncyCastle.Cms;
using Youverse.Core;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace IdentityDatabaseTests
{
    public class TableFeedDistributionOutboxTests
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

            var driveId = SequentialGuid.CreateGuid();

            var tslo = UnixTimeUtc.Now();
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", value = v2 });
            var tshi = UnixTimeUtc.Now();

            var r = db.tblFeedDistributionOutbox.Get(f1, driveId);
            if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                Assert.Fail();

            r = db.tblFeedDistributionOutbox.Get(f2, driveId);
            if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
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
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", value = v2 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", value = v3 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", value = v4 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", value = v5 });
            var tshi = UnixTimeUtc.Now();

            // pop one item from the Outbox
            var r = db.tblFeedDistributionOutbox.Pop(1);
            if (r.Count != 1)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[0].fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[0].value, v1) != 0)
                Assert.Fail();
            if ((r[0].timeStamp < tslo) || (r[0].timeStamp > tshi))
                Assert.Fail();
            Assert.IsTrue(r[0].recipient == "frodo.baggins.me");

            // pop all the remaining items from the Outbox
            r = db.tblFeedDistributionOutbox.Pop(10);
            if (r.Count != 4)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r[0].fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[0].value, v2) != 0)
                Assert.Fail();
            if ((r[0].timeStamp < tslo) || (r[0].timeStamp > tshi))
                Assert.Fail();
            Assert.IsTrue(r[0].recipient == "frodo.baggins.me");

            if (ByteArrayUtil.muidcmp(r[1].fileId, f3) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[1].value, v3) != 0)
                Assert.Fail();
            if ((r[1].timeStamp < tslo) || (r[1].timeStamp > tshi))
                Assert.Fail();
            Assert.IsTrue(r[1].recipient == "frodo.baggins.me");

            if (ByteArrayUtil.muidcmp(r[2].fileId, f4) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[2].value, v4) != 0)
                Assert.Fail();
            if ((r[2].timeStamp < tslo) || (r[2].timeStamp > tshi))
                Assert.Fail();
            Assert.IsTrue(r[2].recipient == "frodo.baggins.me");

            if (ByteArrayUtil.muidcmp(r[3].fileId, f5) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r[3].value, v5) != 0)
                Assert.Fail();
            if ((r[3].timeStamp < tslo) || (r[3].timeStamp > tshi))
                Assert.Fail();
            Assert.IsTrue(r[3].recipient == "frodo.baggins.me");

            // pop to make sure there are no more items
            r = db.tblFeedDistributionOutbox.Pop(1);
            if (r.Count != 0)
                Assert.Fail();
        }

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

            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", value = null });

            var r1 = db.tblFeedDistributionOutbox.Pop(2);
            var r2 = db.tblFeedDistributionOutbox.Pop(3);

            db.tblFeedDistributionOutbox.PopCancelAll((Guid)r1[0].popStamp);

            var r3 = db.tblFeedDistributionOutbox.Pop(10);

            if (r3.Count != 2)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r1[0].fileId, r3[0].fileId) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r1[1].fileId, r3[1].fileId) != 0)
                Assert.Fail();

            db.tblFeedDistributionOutbox.PopCancelAll((Guid)r3[0].popStamp);
            db.tblFeedDistributionOutbox.PopCancelAll((Guid)r2[0].popStamp);
            var r4 = db.tblFeedDistributionOutbox.Pop(10);

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

            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", value = null });

            var r1 = db.tblFeedDistributionOutbox.Pop(2);
            db.tblFeedDistributionOutbox.PopCommitAll((Guid)r1[0].popStamp);

            var r2 = db.tblFeedDistributionOutbox.Pop(10);
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

            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", value = null });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", value = null });

            var r1 = db.tblFeedDistributionOutbox.Pop(2);

            // Recover all items older than the future (=all)
            db.tblFeedDistributionOutbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(2));

            var r2 = db.tblFeedDistributionOutbox.Pop(10);
            if (r2.Count != 5)
                Assert.Fail();

            // Recover items older than long ago (=none)
            db.tblFeedDistributionOutbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(-2));
            var r3 = db.tblFeedDistributionOutbox.Pop(10);
            if (r3.Count != 0)
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
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box1id, fileId = f1, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box1id, fileId = f2, recipient = "frodo.baggins.me", value = v2 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box1id, fileId = f3, recipient = "frodo.baggins.me", value = v3 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box2id, fileId = f4, recipient = "frodo.baggins.me", value = v4 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box2id, fileId = f5, recipient = "frodo.baggins.me", value = v5 });

            // A thread1 pops one record from Outbox1 (it'll get the oldest one)
            // Popping the record "reserves it" for your thread but doesn't remove
            // it from the Outbox until the pop is committed or cancelled.
            var r1 = db.tblFeedDistributionOutbox.Pop(1);

            // Another thread2 then pops 10 records from Outbox1 (only 2 are available now)
            var r2 = db.tblFeedDistributionOutbox.Pop(10);

            // The thread1 that popped the first record is now done.
            // Commit the pop, which effectively deletes it from the Outbox
            // You of course call commit as the very final step when you're
            // certain the item has been saved correctly.
            db.tblFeedDistributionOutbox.PopCommitAll((Guid) r1[0].popStamp);

            // Imagine that thread2 encountered a terrible error, e.g. out of disk space
            // Undo the pop and put the items back into the Outbox
            db.tblFeedDistributionOutbox.PopCancelAll((Guid)r2[0].popStamp);

            // Thread3 pops 10 items from Outbox2 (will retrieve 2)
            var r3 = db.tblFeedDistributionOutbox.Pop(10);

            // Now imagine that there is a power outage, the server crashes.
            // The popped items are in "limbo" because they are not committed and not cancelled.
            // You can recover items popped for more than X seconds like this:
            db.tblFeedDistributionOutbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(60*10));

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
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = b1, fileId = f1, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = b1, fileId = f2, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = b1, fileId = f3, recipient = "frodo.baggins.me", value = v1 });

            // Pop all records from the Outbox,be sure we get 3
            var r1 = db.tblFeedDistributionOutbox.Pop(5);
            if (r1.Count != 3)
                Assert.Fail();

            // Cancel two of the three records
            db.tblFeedDistributionOutbox.PopCancelList((Guid)r1[0].popStamp, new List<Guid>() { f1, f2 });

            // Pop all the recods from the Outbox, but sure we get the two cancelled
            var r2 = db.tblFeedDistributionOutbox.Pop(5);
            if (r2.Count != 2)
                Assert.Fail();

            // Cancel one of the two records
            db.tblFeedDistributionOutbox.PopCancelList((Guid)r2[0].popStamp, new List<Guid>() { f1 });

            // Pop all the recods from the Outbox, but sure we get the two cancelled
            var r3 = db.tblFeedDistributionOutbox.Pop(5);
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
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = b1, fileId = f1, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = b1, fileId = f2, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = b1, fileId = f3, recipient = "frodo.baggins.me", value = v1 });

            // Pop all records from the Outbox,be sure we get 3
            var r1 = db.tblFeedDistributionOutbox.Pop(5);
            if (r1.Count != 3)
                Assert.Fail();

            // Commit one of the three records
            db.tblFeedDistributionOutbox.PopCommitList((Guid)r1[0].popStamp, new List<Guid>() { f2 });

            // Cancel the rest (f1, f3)
            db.tblFeedDistributionOutbox.PopCancelAll((Guid)r1[0].popStamp);

            // Pop all records from the Outbox,be sure we get 2 (f1 & f3)
            var r2 = db.tblFeedDistributionOutbox.Pop(5);
            if (r2.Count != 2)
                Assert.Fail();

            // Commit all records
            db.tblFeedDistributionOutbox.PopCommitList((Guid)r2[0].popStamp, new List<Guid>() { f1, f3 });

            // Cancel nothing
            db.tblFeedDistributionOutbox.PopCancelAll((Guid)r2[0].popStamp);
            // Get everything back
            db.tblFeedDistributionOutbox.PopRecoverDead(new UnixTimeUtc());

            // Pop all records from the Outbox,be sure we get 2 (f1 & f3)
            var r3 = db.tblFeedDistributionOutbox.Pop(5);
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

            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box1id, fileId = f1, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box1id, fileId = f2, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box1id, fileId = f3, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box2id, fileId = f4, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box2id, fileId = f5, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box3id, fileId = f6, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box3id, fileId = f7, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box3id, fileId = f8, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box3id, fileId = f9, recipient = "frodo.baggins.me", value = v1 });
            db.tblFeedDistributionOutbox.Insert(new FeedDistributionOutboxRecord() { driveId = box3id, fileId = f10, recipient = "frodo.baggins.me", value = v1 });

            var (tot, pop, poptime) = db.tblFeedDistributionOutbox.PopStatus();
            Assert.AreEqual(10, tot);
            Assert.AreEqual( 0, pop);
            Assert.AreEqual(UnixTimeUtc.ZeroTime, poptime);

            var tbefore = new UnixTimeUtc();
            var r = db.tblFeedDistributionOutbox.Pop(1000);
            var tafter = new UnixTimeUtc();

            (tot, pop, poptime) = db.tblFeedDistributionOutbox.PopStatus();
            Assert.AreEqual(10, tot);
            Assert.AreEqual(10, pop);
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
    }
}