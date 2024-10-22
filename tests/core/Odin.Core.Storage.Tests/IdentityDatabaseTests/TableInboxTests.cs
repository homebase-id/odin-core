using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableInboxTests
    {
        [TestCase()]
        public void InsertRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var v2 = SequentialGuid.CreateGuid().ToByteArray();

                var boxId = SequentialGuid.CreateGuid();

                var tslo = UnixTimeUtc.Now();
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f1, priority = 0, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f2, priority = 10, value = v2 });
                var tshi = UnixTimeUtc.Now();

                var r = db.tblInbox.Get(f1);
                if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                    Assert.Fail();
                if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                    Assert.Fail();
                if (r.priority != 0)
                    Assert.Fail();

                r = db.tblInbox.Get(f2);
                if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                    Assert.Fail();
                if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                    Assert.Fail();
                if (r.priority != 10)
                    Assert.Fail();
            }
        }

        [TestCase()]
        public void PopTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests002");

            using (var myc = db.CreateDisposableConnection())
            {
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
                var boxId = SequentialGuid.CreateGuid();

                var tslo = UnixTimeUtc.Now();
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId,  boxId = boxId, fileId = f1, priority = 0, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f2, priority = 1, value = v2 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f3, priority = 2, value = v3 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f4, priority = 3, value = v4 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f5, priority = 4, value = v5 });
                var tshi = UnixTimeUtc.Now();

                var (tot, pop, poptime) = db.tblInbox.PopStatus();
                Assert.AreEqual(5, tot);
                Assert.AreEqual(0, pop);
                Assert.AreEqual(UnixTimeUtc.ZeroTime, poptime);


                // pop one item from the inbox
                var tbefore = new UnixTimeUtc();
                var r = db.tblInbox.PopSpecificBox(boxId, 1);
                var tafter = new UnixTimeUtc();
                if (r.Count != 1)
                    Assert.Fail();

                (tot, pop, poptime) = db.tblInbox.PopStatus();
                Assert.AreEqual(5, tot);
                Assert.AreEqual(1, pop);
                if (poptime < tbefore) // We can't have popped before we popped
                    Assert.Fail();
                if (poptime > tafter) // We can't have popped after we popped
                    Assert.Fail();


                if (ByteArrayUtil.muidcmp(r[0].fileId, f1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r[0].value, v1) != 0)
                    Assert.Fail();
                if (r[0].priority != 0)
                    Assert.Fail();
                if ((r[0].timeStamp < tslo) || (r[0].timeStamp > tshi))
                    Assert.Fail();

                // pop all the remaining items from the inbox
                r = db.tblInbox.PopSpecificBox(boxId, 10);
                if (r.Count != 4)
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
                r = db.tblInbox.PopSpecificBox(boxId, 1);
                if (r.Count != 0)
                    Assert.Fail();
            }
        }

        [TestCase()]
        public void PopCancelTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();
                var f4 = SequentialGuid.CreateGuid();
                var f5 = SequentialGuid.CreateGuid();
                var boxId = SequentialGuid.CreateGuid();

                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f1, priority = 0, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f2, priority = 0, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f3, priority = 10, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f4, priority = 10, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f5, priority = 20, value = null });
                var r1 = db.tblInbox.PopSpecificBox(boxId, 2);
                var r2 = db.tblInbox.PopSpecificBox(boxId, 3);

                db.tblInbox.PopCancelAll((Guid)r1[0].popStamp);

                var r3 = db.tblInbox.PopSpecificBox(boxId, 10);

                if (r3.Count != 2)
                    Assert.Fail();

                if (ByteArrayUtil.muidcmp(r1[0].fileId, r3[0].fileId) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r1[1].fileId, r3[1].fileId) != 0)
                    Assert.Fail();

                db.tblInbox.PopCancelAll((Guid)r3[0].popStamp);
                db.tblInbox.PopCancelAll((Guid)r2[0].popStamp);
                var r4 = db.tblInbox.PopSpecificBox(boxId, 10);

                if (r4.Count != 5)
                    Assert.Fail();
            }
        }


        [TestCase()]
        public void PopCommitTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();
                var f4 = SequentialGuid.CreateGuid();
                var f5 = SequentialGuid.CreateGuid();

                var boxId = SequentialGuid.CreateGuid();

                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f1, priority = 0, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f2, priority = 0, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f3, priority = 10, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f4, priority = 10, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f5, priority = 20, value = null });

                var r1 = db.tblInbox.PopSpecificBox(boxId, 2);
                db.tblInbox.PopCommitAll((Guid)r1[0].popStamp);

                var r2 = db.tblInbox.PopSpecificBox(boxId, 10);
                if (r2.Count != 3)
                    Assert.Fail();
            }
        }


        [TestCase()]
        public void PopCommitListTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var f1 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();

                var b1 = SequentialGuid.CreateGuid();

                // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f1, priority = 0, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f2, priority = 10, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f3, priority = 10, value = v1 });

                // Pop all records from the Inbox,be sure we get 3
                var r1 = db.tblInbox.PopSpecificBox(b1, 5);
                if (r1.Count != 3)
                    Assert.Fail();

                // Commit one of the three records
                db.tblInbox.PopCommitList((Guid)r1[0].popStamp, b1, new List<Guid>() { f2 });

                // Cancel the rest (f1, f3)
                db.tblInbox.PopCancelAll((Guid)r1[0].popStamp);

                // Pop all records from the Inbox,be sure we get 2 (f1 & f3)
                var r2 = db.tblInbox.PopSpecificBox(b1, 5);
                if (r2.Count != 2)
                    Assert.Fail();

                // Commit all records
                db.tblInbox.PopCommitList((Guid)r2[0].popStamp, b1, new List<Guid>() { f1, f3 });

                // Cancel nothing
                db.tblInbox.PopCancelAll((Guid)r2[0].popStamp);
                // Get everything back
                db.tblInbox.PopRecoverDead(new UnixTimeUtc());

                // Pop all records from the Inbox,be sure we get 2 (f1 & f3)
                var r3 = db.tblInbox.PopSpecificBox(b1, 5);
                if (r3.Count != 0)
                    Assert.Fail();
            }
        }

        [TestCase()]
        public void PopCancelListTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var f1 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();

                var b1 = SequentialGuid.CreateGuid();

                // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f1, priority = 0, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f2, priority = 10, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f3, priority = 10, value = v1 });

                // Pop all records from the Inbox,be sure we get 3
                var r1 = db.tblInbox.PopSpecificBox(b1, 5);
                if (r1.Count != 3)
                    Assert.Fail();

                // Cancel two of the three records
                db.tblInbox.PopCancelList((Guid)r1[0].popStamp, b1, new List<Guid>() { f1, f2 });

                // Pop all the recods from the Inbox, but sure we get the two cancelled
                var r2 = db.tblInbox.PopSpecificBox(b1, 5);
                if (r2.Count != 2)
                    Assert.Fail();

                // Cancel one of the two records
                db.tblInbox.PopCancelList((Guid)r2[0].popStamp, b1, new List<Guid>() { f1 });

                // Pop all the recods from the Inbox, but sure we get the two cancelled
                var r3 = db.tblInbox.PopSpecificBox(b1, 5);
                if (r3.Count != 1)
                    Assert.Fail();
            }
        }


        [TestCase()]
        public void PopRecoverDeadTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests007");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();
                var f4 = SequentialGuid.CreateGuid();
                var f5 = SequentialGuid.CreateGuid();

                var boxId = SequentialGuid.CreateGuid();

                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f1, priority = 0, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f2, priority = 0, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f3, priority = 10, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f4, priority = 10, value = null });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = boxId, fileId = f5, priority = 20, value = null });

                var r1 = db.tblInbox.PopSpecificBox(boxId, 2);

                // Recover all items older than the future (=all)
                db.tblInbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(2));

                var r2 = db.tblInbox.PopSpecificBox(boxId, 10);
                if (r2.Count != 5)
                    Assert.Fail();

                // Recover items older than long ago (=none)
                db.tblInbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(-2));
                var r3 = db.tblInbox.PopSpecificBox(boxId, 10);
                if (r3.Count != 0)
                    Assert.Fail();
            }
        }


        [TestCase()]
        public void DualBoxTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests008");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var f1 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();
                var f4 = SequentialGuid.CreateGuid();
                var f5 = SequentialGuid.CreateGuid();

                var b1 = SequentialGuid.CreateGuid();
                var b2 = SequentialGuid.CreateGuid();

                // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f1, priority = 0, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b1, fileId = f2, priority = 10, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b2, fileId = f3, priority = 10, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b2, fileId = f4, priority = 10, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = b2, fileId = f5, priority = 10, value = v1 });

                var (tot, pop, poptime) = db.tblInbox.PopStatusSpecificBox(b1);
                Assert.AreEqual(2, tot);
                Assert.AreEqual(0, pop);
                Assert.AreEqual(UnixTimeUtc.ZeroTime, poptime);
                var tbefore = new UnixTimeUtc();

                // Pop the oldest record from the inbox 1
                var r1 = db.tblInbox.PopSpecificBox(b1, 1);
                var r2 = db.tblInbox.PopSpecificBox(b1, 10);
                if (r2.Count != 1)
                    Assert.Fail();

                var tafter = new UnixTimeUtc();
                (tot, pop, poptime) = db.tblInbox.PopStatusSpecificBox(b1);
                Assert.AreEqual(2, tot);
                Assert.AreEqual(2, pop);
                if (poptime < tbefore) // We can't have popped before we popped
                    Assert.Fail();
                if (poptime > tafter) // We can't have popped after we popped
                    Assert.Fail();

                // Then pop 10 oldest record from the inbox (only 2 are available now)
                var r3 = db.tblInbox.PopSpecificBox(b2, 10);
                if (r3.Count != 3)
                    Assert.Fail();

                // The thread that popped the first record is now done.
                // Commit the pop
                db.tblInbox.PopCommitAll((Guid)r1[0].popStamp);

                // Oh no, the second thread running on the second pop of records
                // encountered a terrible error. Undo the pop
                db.tblInbox.PopCancelAll((Guid)r2[0].popStamp);

                var r4 = db.tblInbox.PopSpecificBox(b1, 10);
                if (r4.Count != 1)
                    Assert.Fail();
            }
        }


        [TestCase()]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableInboxTests009");

            using (var myc = db.CreateDisposableConnection())
            {
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

                // Insert three records into "inbox1"
                // Insert two   records into "inbox2"
                // An inbox is simply a GUID. E.g. the boxId.
                // A record has a fileId, priority and a custom value
                // The custom value could e.g. be a GUID or a JSON of { senderId, appId }
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = box1id, fileId = f1, priority = 0, value = v1 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = box1id, fileId = f2, priority = 10, value = v2 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = box1id, fileId = f3, priority = 10, value = v3 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = box2id, fileId = f4, priority = 10, value = v4 });
                db.tblInbox.Insert(new InboxRecord() { identityId = ((IdentityDatabase)myc.db)._identityId, boxId = box2id, fileId = f5, priority = 10, value = v5 });

                // A thread1 pops one record from inbox1 (it'll get the oldest one)
                // Popping the record "reserves it" for your thread but doesn't remove
                // it from the inbox until the pop is committed or cancelled.
                var r1 = db.tblInbox.PopSpecificBox(box1id, 1);

                // Another thread2 then pops 10 records from inbox1 (only 2 are available now)
                var r2 = db.tblInbox.PopSpecificBox(box1id, 10);

                // The thread1 that popped the first record is now done.
                // Commit the pop, which effectively deletes it from the inbox
                // You of course call commit as the very final step when you're
                // certain the item has been saved correctly.
                db.tblInbox.PopCommitAll((Guid)r1[0].popStamp);

                // Imagine that thread2 encountered a terrible error, e.g. out of disk space
                // Undo the pop and put the items back into the inbox
                db.tblInbox.PopCancelAll((Guid)r2[0].popStamp);

                // Thread3 pops 10 items from inbox2 (will retrieve 2)
                var r3 = db.tblInbox.PopSpecificBox(box2id, 10);

                // Now imagine that there is a power outage, the server crashes.
                // The popped items are in "limbo" because they are not committed and not cancelled.
                // You can recover items popped for more than X seconds like this:
                db.tblInbox.PopRecoverDead(UnixTimeUtc.Now().AddSeconds(60 * 10));

                // That would recover all popped items that have not been committed or cancelled.
            }
        }
    }
}