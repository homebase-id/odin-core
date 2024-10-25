using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableOutboxTests
    {
        [TestCase()]
        public async Task InsertRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var v2 = SequentialGuid.CreateGuid().ToByteArray();
                var did1 = SequentialGuid.CreateGuid();

                var driveId = SequentialGuid.CreateGuid();

                var tslo = UnixTimeUtc.Now();
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, dependencyFileId = null, value = v1 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 10, dependencyFileId = did1, value = v2 });
                var tshi = UnixTimeUtc.Now();

                var r = await db.tblOutbox.GetAsync(driveId, f1, "frodo.baggins.me");
                if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                    Assert.Fail();
                if (r.dependencyFileId != null)
                    Assert.Fail();
                if (r.priority != 0)
                    Assert.Fail();

                r = await db.tblOutbox.GetAsync(driveId, f2, "frodo.baggins.me");
                if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.dependencyFileId, did1) != 0)
                    Assert.Fail();
                if (r.priority != 10)
                    Assert.Fail();
            }
        }

        [TestCase()]
        public async Task InsertCannotInsertDuplicateIdForSameRecipient()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var f1 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var v2 = SequentialGuid.CreateGuid().ToByteArray();
                var did1 = SequentialGuid.CreateGuid();

                var driveId = SequentialGuid.CreateGuid();

                try
                {
                    await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, dependencyFileId = null, value = v1 });
                    await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 10, dependencyFileId = did1, value = v2 });
                    Assert.Fail();
                }
                catch
                {
                    // Pass
                }
            }
        }

        [TestCase()]
        public async Task InsertCanInsertDuplicateIdForTwoRecipients()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var f1 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var v2 = SequentialGuid.CreateGuid().ToByteArray();
                var did1 = SequentialGuid.CreateGuid();

                var driveId = SequentialGuid.CreateGuid();

                try
                {
                    await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, dependencyFileId = null, value = v1 });
                    await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "sam.baggins.me", priority = 10, dependencyFileId = did1, value = v2 });
                    // Pass
                }
                catch
                {
                    Assert.Fail();
                }
            }
        }

        [TestCase()]
        public async Task GetByTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var v1 = SequentialGuid.CreateGuid().ToByteArray();
                var v2 = SequentialGuid.CreateGuid().ToByteArray();
                var did1 = SequentialGuid.CreateGuid();

                var driveId = SequentialGuid.CreateGuid();

                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, dependencyFileId = null, value = v1 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 10, dependencyFileId = did1, value = v2 });

                var r = await db.tblOutbox.GetAsync(driveId, f1);

                Assert.IsTrue(r.Count == 1);
            }
        }

        [TestCase()]
        public async Task PopTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 1, value = v2 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 2, value = v3 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 3, value = v4 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 4, value = v5 });
                var tshi = UnixTimeUtc.Now();

                // pop one item from the Outbox
                var r = await db.tblOutbox.CheckOutItemAsync();
                if (r == null)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.fileId, f1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v1) != 0)
                    Assert.Fail();
                if (r.priority != 0)
                    Assert.Fail();
                Assert.IsTrue(r.recipient == "frodo.baggins.me");

                var (ti, tp, nrt) = await db.tblOutbox.OutboxStatusAsync();
                Debug.Assert(ti == 5);
                Debug.Assert(tp == 1);
                var (ti1, tp1, nrt1) = await db.tblOutbox.OutboxStatusDriveAsync(driveId);
                Assert.IsTrue(ti == ti1);
                Assert.IsTrue(tp == tp1);
                Assert.IsTrue(nrt == nrt1);

                // pop all the remaining items from the Outbox
                r = await db.tblOutbox.CheckOutItemAsync();
                if (ByteArrayUtil.muidcmp(r.fileId, f2) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v2) != 0)
                    Assert.Fail();
                if (r.priority != 1)
                    Assert.Fail();
                Assert.IsTrue(r.recipient == "frodo.baggins.me");

                r = await db.tblOutbox.CheckOutItemAsync();
                if (ByteArrayUtil.muidcmp(r.fileId, f3) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v3) != 0)
                    Assert.Fail();
                if (r.priority != 2)
                    Assert.Fail();
                Assert.IsTrue(r.recipient == "frodo.baggins.me");

                r = await db.tblOutbox.CheckOutItemAsync();
                if (ByteArrayUtil.muidcmp(r.fileId, f4) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v4) != 0)
                    Assert.Fail();
                if (r.priority != 3)
                    Assert.Fail();
                Assert.IsTrue(r.recipient == "frodo.baggins.me");

                r = await db.tblOutbox.CheckOutItemAsync();
                if (ByteArrayUtil.muidcmp(r.fileId, f5) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.value, v5) != 0)
                    Assert.Fail();
                if (r.priority != 4)
                    Assert.Fail();
                Assert.IsTrue(r.recipient == "frodo.baggins.me");

                r = await db.tblOutbox.CheckOutItemAsync();
                if (r != null)
                    Assert.Fail();
            }
        }

        // Make sure priority takes precedence
        [TestCase()]
        public async Task PriorityTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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

                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 4, value = v1 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 1, value = v2 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 2, value = v3 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 3, value = v4 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 0, value = v5 });

                var r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.priority == 0);
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.priority == 1);
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.priority == 2);
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.priority == 3);
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.priority == 4);
            }
        }

        // With the same priority, make sure nextRunTime matters
        [TestCase()]
        public async Task NextRunTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests007");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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

                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "2frodo.baggins.me", priority = 0, value = v2 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "3frodo.baggins.me", priority = 0, value = v3 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "4frodo.baggins.me", priority = 0, value = v4 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "5frodo.baggins.me", priority = 0, value = v5 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "1frodo.baggins.me", priority = 0, value = v1 });

                var r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.recipient == "2frodo.baggins.me");
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.recipient == "3frodo.baggins.me");
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.recipient == "4frodo.baggins.me");
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.recipient == "5frodo.baggins.me");
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.recipient == "1frodo.baggins.me");
            }
        }


        // Make sure dependency works
        [TestCase()]
        public async Task DependencyTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests008");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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

                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", dependencyFileId = f3, priority = 0, value = v2 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", dependencyFileId = null, priority = 0, value = v3 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", dependencyFileId = f2, priority = 0, value = v4 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", dependencyFileId = f4, priority = 0, value = v5 });
                Thread.Sleep(2);
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", dependencyFileId = f5, priority = 0, value = v1 });

                var r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f3) == 0);
                var nr = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(nr == null);

                await db.tblOutbox.CompleteAndRemoveAsync((Guid)r.checkOutStamp);

                // Get the next one
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f2) == 0);
                nr = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(nr == null);
                await db.tblOutbox.CompleteAndRemoveAsync((Guid)r.checkOutStamp);

                // Get the next one
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f4) == 0);
                nr = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(nr == null);
                await db.tblOutbox.CompleteAndRemoveAsync((Guid)r.checkOutStamp);

                // Get the next one
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f5) == 0);
                nr = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(nr == null);
                await db.tblOutbox.CompleteAndRemoveAsync((Guid)r.checkOutStamp);

                // Get the next one
                r = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r.fileId, f1) == 0);
                nr = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(nr == null);
                await db.tblOutbox.CompleteAndRemoveAsync((Guid)r.checkOutStamp);
            }
        }


        [TestCase()]
        public async Task PopCancelTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests009");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();
                var f4 = SequentialGuid.CreateGuid();
                var f5 = SequentialGuid.CreateGuid();
                var driveId = SequentialGuid.CreateGuid();

                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 0, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 20, value = null });

                var r1 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r1.fileId, f1) == 0);

                var r2 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r2.fileId, f2) == 0);

                await db.tblOutbox.CheckInAsCancelledAsync((Guid)r1.checkOutStamp, UnixTimeUtc.Now().AddSeconds(2));

                var r3 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r3.fileId, f3) == 0);

                var r4 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r4.fileId, f4) == 0);

                var r5 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r5.fileId, f5) == 0);

                r1 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r1 == null);

                Thread.Sleep(3000); // Wait until it's available again

                r1 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(ByteArrayUtil.muidcmp(r1.fileId, f1) == 0);
            }
        }


        [TestCase()]
        public async Task PopCommitTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests010");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();
                var f3 = SequentialGuid.CreateGuid();
                var f4 = SequentialGuid.CreateGuid();
                var f5 = SequentialGuid.CreateGuid();

                var driveId = SequentialGuid.CreateGuid();

                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 0, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 20, value = null });

                var r1 = await db.tblOutbox.CheckOutItemAsync();
                await db.tblOutbox.CompleteAndRemoveAsync((Guid)r1.checkOutStamp);
                var (ti, tp, nrt) = await db.tblOutbox.OutboxStatusAsync();
                Debug.Assert(ti == 4);
                Debug.Assert(tp == 0);
            }
        }


        [TestCase()]
        public async Task NextRunTest2()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests011");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var f1 = SequentialGuid.CreateGuid();
                var f2 = SequentialGuid.CreateGuid();

                var driveId = SequentialGuid.CreateGuid();

                var tilo = UnixTimeUtc.Now();
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = null });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 0, value = null });
                var tihi = UnixTimeUtc.Now();

                var r = await db.tblOutbox.CheckOutItemAsync();
                var r2 = await db.tblOutbox.CheckOutItemAsync();
                Assert.IsTrue(r.nextRunTime >= tilo);
                Assert.IsTrue(r.nextRunTime <= tihi);

                var t = await db.tblOutbox.NextScheduledItemAsync();
                Assert.IsTrue(t == null); // There is no next item

                var nextTime = UnixTimeUtc.Now().AddHours(1);
                await db.tblOutbox.CheckInAsCancelledAsync((Guid)r.checkOutStamp, nextTime);
                await db.tblOutbox.CheckInAsCancelledAsync((Guid)r2.checkOutStamp, UnixTimeUtc.Now().AddDays(7));

                t = await db.tblOutbox.NextScheduledItemAsync();
                Assert.IsTrue(t == nextTime);
            }
        }



        [TestCase()]
        public async Task PopRecoverDeadTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests012");

            await db.CreateDatabaseAsync();
            var f1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid();

            var driveId = SequentialGuid.CreateGuid();

            await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = null });
            await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f2, recipient = "frodo.baggins.me", priority = 0, value = null });
            await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = null });
            await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = null });
            await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = driveId, fileId = f5, recipient = "frodo.baggins.me", priority = 20, value = null });

            var r1 = await db.tblOutbox.CheckOutItemAsync();
            var r2 = await db.tblOutbox.CheckOutItemAsync();

            var (ti, tp, nrt) = await db.tblOutbox.OutboxStatusAsync();
            Debug.Assert(ti == 5);
            Debug.Assert(tp == 2);

            // Recover all items older than the future (=all)
            await db.tblOutbox.RecoverCheckedOutDeadItemsAsync(UnixTimeUtc.Now().AddSeconds(2));

            (ti, tp, nrt) = await db.tblOutbox.OutboxStatusAsync();
            Debug.Assert(ti == 5);
            Debug.Assert(tp == 0);

            r1 = await db.tblOutbox.CheckOutItemAsync();
            r2 = await db.tblOutbox.CheckOutItemAsync();

            // Recover items older than long ago (=none)
            await db.tblOutbox.RecoverCheckedOutDeadItemsAsync(UnixTimeUtc.Now().AddSeconds(-2));
            (ti, tp, nrt) = await db.tblOutbox.OutboxStatusAsync();
            Debug.Assert(ti == 5);
            Debug.Assert(tp == 2);
        }

        [TestCase()]
        public async Task ExampleTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableOutboxTests013");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = box1id, fileId = f1, recipient = "frodo.baggins.me", priority = 0, value = v1 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = box1id, fileId = f2, recipient = "frodo.baggins.me", priority = 10, value = v2 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = box1id, fileId = f3, recipient = "frodo.baggins.me", priority = 10, value = v3 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = box2id, fileId = f4, recipient = "frodo.baggins.me", priority = 10, value = v4 });
                await db.tblOutbox.InsertAsync(new OutboxRecord() { driveId = box2id, fileId = f5, recipient = "frodo.baggins.me", priority = 10, value = v5 });

                // A thread1 checks out one record from Outbox1 (it'll get the highest priority & one with the oldest nextRunTime)
                // Checking out the record "reserves it" for your thread but doesn't remove
                // it from the Outbox until it is checked back in or removed. 
                var r1 = await db.tblOutbox.CheckOutItemAsync();

                // Another thread2 also checks out a record
                var r2 = await db.tblOutbox.CheckOutItemAsync();

                // The thread1 that popped the first record is now done.
                // Everything was fine, so the item is completed & removed.
                // You of course call commit as the very final step when you're
                // certain the item has been dealt with correctly.
                await db.tblOutbox.CompleteAndRemoveAsync((Guid)r1.checkOutStamp);

                // Imagine that thread2 encountered a terrible error, e.g. out of disk space
                // Undo the pop and put the items back into the Outbox. And specify when we
                // want to try to run it again. You can use the checkOutCount to incrementally
                // make the durations longer
                await db.tblOutbox.CheckInAsCancelledAsync((Guid)r2.checkOutStamp, UnixTimeUtc.Now().AddSeconds(r2.checkOutCount * 5));

                // Thread3 pops an items 
                var r3 = await db.tblOutbox.CheckOutItemAsync();

                // Now imagine that there is a power outage, the server crashes.
                // The popped items are in "limbo" because they are not committed and not cancelled.
                // You can recover items popped for more than X seconds like this:
                await db.tblOutbox.RecoverCheckedOutDeadItemsAsync(UnixTimeUtc.Now().AddSeconds(60 * 10));

                // That would recover all popped items that have not been committed or cancelled.
            }
        }
    }
}