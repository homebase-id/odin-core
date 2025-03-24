using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableInboxTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var v2 = SequentialGuid.CreateGuid().ToByteArray();

            var boxId = SequentialGuid.CreateGuid();

            var tslo = UnixTimeUtc.Now();
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f1, priority = 0, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f2, priority = 10, value = v2 });
            var tshi = UnixTimeUtc.Now();

            var r = await tblInbox.GetAsync(f1);
            if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                Assert.Fail();
            if (r.priority != 0)
                Assert.Fail();

            r = await tblInbox.GetAsync(f2);
            if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                Assert.Fail();
            if ((r.timeStamp < tslo) || (r.timeStamp > tshi))
                Assert.Fail();
            if (r.priority != 10)
                Assert.Fail();

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PopTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

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
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey,  boxId = boxId, fileId = f1, priority = 0, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f2, priority = 1, value = v2 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f3, priority = 2, value = v3 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f4, priority = 3, value = v4 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f5, priority = 4, value = v5 });
            var tshi = UnixTimeUtc.Now();

            var (tot, pop, poptime) = await tblInbox.PopStatusAsync();
            ClassicAssert.AreEqual(5, tot);
            ClassicAssert.AreEqual(0, pop);
            ClassicAssert.AreEqual(UnixTimeUtc.ZeroTime, poptime);


            // pop one item from the inbox
            var tbefore = new UnixTimeUtc();
            var r = await tblInbox.PopSpecificBoxAsync(boxId, 1);
            var tafter = new UnixTimeUtc();
            if (r.Count != 1)
                Assert.Fail();

            (tot, pop, poptime) = await tblInbox.PopStatusAsync();
            ClassicAssert.AreEqual(5, tot);
            ClassicAssert.AreEqual(1, pop);
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
            r = await tblInbox.PopSpecificBoxAsync(boxId, 10);
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
            r = await tblInbox.PopSpecificBoxAsync(boxId, 1);
            if (r.Count != 0)
                Assert.Fail();

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PopCancelTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();
            var boxId = SequentialGuid.CreateGuid();

            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f1, priority = 0, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f2, priority = 0, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f3, priority = 10, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f4, priority = 10, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f5, priority = 20, value = null });
            var r1 = await tblInbox.PopSpecificBoxAsync(boxId, 2);
            var r2 = await tblInbox.PopSpecificBoxAsync(boxId, 3);

            await tblInbox.PopCancelAllAsync((Guid)r1[0].popStamp);

            var r3 = await tblInbox.PopSpecificBoxAsync(boxId, 10);

            if (r3.Count != 2)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(r1[0].fileId, r3[0].fileId) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r1[1].fileId, r3[1].fileId) != 0)
                Assert.Fail();

            await tblInbox.PopCancelAllAsync((Guid)r3[0].popStamp);
            await tblInbox.PopCancelAllAsync((Guid)r2[0].popStamp);
            var r4 = await tblInbox.PopSpecificBoxAsync(boxId, 10);

            if (r4.Count != 5)
                Assert.Fail();

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PopCommitTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var boxId = SequentialGuid.CreateGuid();

            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f1, priority = 0, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f2, priority = 0, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f3, priority = 10, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f4, priority = 10, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f5, priority = 20, value = null });

            var r1 = await tblInbox.PopSpecificBoxAsync(boxId, 2);
            await tblInbox.PopCommitAllAsync((Guid)r1[0].popStamp);

            var r2 = await tblInbox.PopSpecificBoxAsync(boxId, 10);
            if (r2.Count != 3)
                Assert.Fail();

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PopCommitListTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            var b1 = SequentialGuid.CreateGuid();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f1, priority = 0, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f2, priority = 10, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f3, priority = 10, value = v1 });

            // Pop all records from the Inbox,be sure we get 3
            var r1 = await tblInbox.PopSpecificBoxAsync(b1, 5);
            if (r1.Count != 3)
                Assert.Fail();

            // Commit one of the three records
            await tblInbox.PopCommitListAsync((Guid)r1[0].popStamp, b1, new List<Guid>() { f2 });

            // Cancel the rest (f1, f3)
            await tblInbox.PopCancelAllAsync((Guid)r1[0].popStamp);

            // Pop all records from the Inbox,be sure we get 2 (f1 & f3)
            var r2 = await tblInbox.PopSpecificBoxAsync(b1, 5);
            if (r2.Count != 2)
                Assert.Fail();

            // Commit all records
            await tblInbox.PopCommitListAsync((Guid)r2[0].popStamp, b1, new List<Guid>() { f1, f3 });

            // Cancel nothing
            await tblInbox.PopCancelAllAsync((Guid)r2[0].popStamp);
            // Get everything back
            await tblInbox.PopRecoverDeadAsync(new UnixTimeUtc());

            // Pop all records from the Inbox,be sure we get 2 (f1 & f3)
            var r3 = await tblInbox.PopSpecificBoxAsync(b1, 5);
            if (r3.Count != 0)
                Assert.Fail();

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PopCancelListTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();

            var b1 = SequentialGuid.CreateGuid();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f1, priority = 0, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f2, priority = 10, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f3, priority = 10, value = v1 });

            // Pop all records from the Inbox,be sure we get 3
            var r1 = await tblInbox.PopSpecificBoxAsync(b1, 5);
            if (r1.Count != 3)
                Assert.Fail();

            // Cancel two of the three records
            await tblInbox.PopCancelListAsync((Guid)r1[0].popStamp, b1, new List<Guid>() { f1, f2 });

            // Pop all the recods from the Inbox, but sure we get the two cancelled
            var r2 = await tblInbox.PopSpecificBoxAsync(b1, 5);
            if (r2.Count != 2)
                Assert.Fail();

            // Cancel one of the two records
            await tblInbox.PopCancelListAsync((Guid)r2[0].popStamp, b1, new List<Guid>() { f1 });

            // Pop all the recods from the Inbox, but sure we get the two cancelled
            var r3 = await tblInbox.PopSpecificBoxAsync(b1, 5);
            if (r3.Count != 1)
                Assert.Fail();

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PopRecoverDeadTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var boxId = SequentialGuid.CreateGuid();

            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f1, priority = 0, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f2, priority = 0, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f3, priority = 10, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f4, priority = 10, value = null });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = boxId, fileId = f5, priority = 20, value = null });

            var r1 = await tblInbox.PopSpecificBoxAsync(boxId, 2);

            // Recover all items older than the future (=all)
            await tblInbox.PopRecoverDeadAsync(UnixTimeUtc.Now().AddSeconds(2));

            var r2 = await tblInbox.PopSpecificBoxAsync(boxId, 10);
            if (r2.Count != 5)
                Assert.Fail();

            // Recover items older than long ago (=none)
            await tblInbox.PopRecoverDeadAsync(UnixTimeUtc.Now().AddSeconds(-2));
            var r3 = await tblInbox.PopSpecificBoxAsync(boxId, 10);
            if (r3.Count != 0)
                Assert.Fail();
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DualBoxTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

            var f1 = SequentialGuid.CreateGuid();
            var v1 = SequentialGuid.CreateGuid().ToByteArray();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var b1 = SequentialGuid.CreateGuid();
            var b2 = SequentialGuid.CreateGuid();

            // Insert three records with fileId (f1), priority, and value (e.g. appId etc)
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f1, priority = 0, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b1, fileId = f2, priority = 10, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b2, fileId = f3, priority = 10, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b2, fileId = f4, priority = 10, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = b2, fileId = f5, priority = 10, value = v1 });

            var (tot, pop, poptime) = await tblInbox.PopStatusSpecificBoxAsync(b1);
            ClassicAssert.AreEqual(2, tot);
            ClassicAssert.AreEqual(0, pop);
            ClassicAssert.AreEqual(UnixTimeUtc.ZeroTime, poptime);
            var tbefore = new UnixTimeUtc();

            // Pop the oldest record from the inbox 1
            var r1 = await tblInbox.PopSpecificBoxAsync(b1, 1);
            var r2 = await tblInbox.PopSpecificBoxAsync(b1, 10);
            if (r2.Count != 1)
                Assert.Fail();

            var tafter = new UnixTimeUtc();
            (tot, pop, poptime) = await tblInbox.PopStatusSpecificBoxAsync(b1);
            ClassicAssert.AreEqual(2, tot);
            ClassicAssert.AreEqual(2, pop);
            if (poptime < tbefore) // We can't have popped before we popped
                Assert.Fail();
            if (poptime > tafter) // We can't have popped after we popped
                Assert.Fail();

            // Then pop 10 oldest record from the inbox (only 2 are available now)
            var r3 = await tblInbox.PopSpecificBoxAsync(b2, 10);
            if (r3.Count != 3)
                Assert.Fail();

            // The thread that popped the first record is now done.
            // Commit the pop
            await tblInbox.PopCommitAllAsync((Guid)r1[0].popStamp);

            // Oh no, the second thread running on the second pop of records
            // encountered a terrible error. Undo the pop
            await tblInbox.PopCancelAllAsync((Guid)r2[0].popStamp);

            var r4 = await tblInbox.PopSpecificBoxAsync(b1, 10);
            if (r4.Count != 1)
                Assert.Fail();
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task ExampleTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblInbox = scope.Resolve<TableInbox>();
            var identityKey = scope.Resolve<IdentityKey>();

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
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = box1id, fileId = f1, priority = 0, value = v1 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = box1id, fileId = f2, priority = 10, value = v2 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = box1id, fileId = f3, priority = 10, value = v3 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = box2id, fileId = f4, priority = 10, value = v4 });
            await tblInbox.InsertAsync(new InboxRecord() { identityId = identityKey, boxId = box2id, fileId = f5, priority = 10, value = v5 });

            // A thread1 pops one record from inbox1 (it'll get the oldest one)
            // Popping the record "reserves it" for your thread but doesn't remove
            // it from the inbox until the pop is committed or cancelled.
            var r1 = await tblInbox.PopSpecificBoxAsync(box1id, 1);

            // Another thread2 then pops 10 records from inbox1 (only 2 are available now)
            var r2 = await tblInbox.PopSpecificBoxAsync(box1id, 10);

            // The thread1 that popped the first record is now done.
            // Commit the pop, which effectively deletes it from the inbox
            // You of course call commit as the very final step when you're
            // certain the item has been saved correctly.
            await tblInbox.PopCommitAllAsync((Guid)r1[0].popStamp);

            // Imagine that thread2 encountered a terrible error, e.g. out of disk space
            // Undo the pop and put the items back into the inbox
            await tblInbox.PopCancelAllAsync((Guid)r2[0].popStamp);

            // Thread3 pops 10 items from inbox2 (will retrieve 2)
            var r3 = await tblInbox.PopSpecificBoxAsync(box2id, 10);

            // Now imagine that there is a power outage, the server crashes.
            // The popped items are in "limbo" because they are not committed and not cancelled.
            // You can recover items popped for more than X seconds like this:
            await tblInbox.PopRecoverDeadAsync(UnixTimeUtc.Now().AddSeconds(60 * 10));

            // That would recover all popped items that have not been committed or cancelled.
        }
    }
}

