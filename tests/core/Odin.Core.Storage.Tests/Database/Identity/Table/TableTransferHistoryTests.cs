using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableTransferHistoryTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableDriveTransferHistory>();

            var d1 = Guid.NewGuid();
            var f1 = Guid.NewGuid();
            var frodoId = new OdinId("frodobaggins.me");

            var n = await tbl.TryAddInitialRecordAsync(d1, f1, frodoId);
            ClassicAssert.IsTrue(n == true);

            var r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r.Count == 1);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpdateTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableDriveTransferHistory>();

            var d1 = Guid.NewGuid();
            var f1 = Guid.NewGuid();
            var frodoId = new OdinId("frodobaggins.me");

            var b = await tbl.TryAddInitialRecordAsync(d1, f1, frodoId);

            // IsInOutbox
            var n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId,  null, null, isInOutbox: true,  null);
            ClassicAssert.IsTrue(n == 1);
            var r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].isInOutbox == true);

            n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId, null, null, isInOutbox: false, null);
            ClassicAssert.IsTrue(n == 1);
            r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].isInOutbox == false);

            // IsReadByRecipient (now a timestamp: 0 = not read, >0 = read-at ms)
            var readAtMs = UnixTimeUtc.Now().milliseconds;
            n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId, null, null, null, readByRecipientTimestamp: readAtMs);
            ClassicAssert.IsTrue(n == 1);
            r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].isReadByRecipient.milliseconds == readAtMs);

            n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId, null, null, null, readByRecipientTimestamp: 0);
            ClassicAssert.IsTrue(n == 1);
            r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].isReadByRecipient.milliseconds == 0);

            // LatestTransferStatus
            n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId, latestTransferStatus: 42, null, null, null);
            ClassicAssert.IsTrue(n == 1);
            r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].latestTransferStatus == 42);

            // LatestTransferStatus
            var g = Guid.NewGuid();
            n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId, null, latestSuccessfullyDeliveredVersionTag: g, null, null);
            ClassicAssert.IsTrue(n == 1);
            r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].latestSuccessfullyDeliveredVersionTag == g);


        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PagingByRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableDriveTransferHistory>();

            var driveId = Guid.NewGuid();
            var f1 = Guid.NewGuid();
            var f2 = Guid.NewGuid();
            var f3 = Guid.NewGuid();
            var rid1 = new OdinId("frodo.baggins.me");
            var rid2 = new OdinId("sam.gamgee.me");
            var rid3 = new OdinId("gandalf.white.me");

            await tbl.InsertAsync(new DriveTransferHistoryRecord() { driveId = driveId, fileId = f1, remoteIdentityId = rid1, latestTransferStatus = 1, isInOutbox = false, isReadByRecipient = new UnixTimeUtc(0), latestSuccessfullyDeliveredVersionTag = null });
            await tbl.InsertAsync(new DriveTransferHistoryRecord() { driveId = driveId, fileId = f2, remoteIdentityId = rid2, latestTransferStatus = 2, isInOutbox = false, isReadByRecipient = new UnixTimeUtc(0), latestSuccessfullyDeliveredVersionTag = null });
            await tbl.InsertAsync(new DriveTransferHistoryRecord() { driveId = driveId, fileId = f3, remoteIdentityId = rid3, latestTransferStatus = 3, isInOutbox = false, isReadByRecipient = new UnixTimeUtc(0), latestSuccessfullyDeliveredVersionTag = null });

            var (page1, cursor1) = await tbl.PagingByRowIdAsync(2, null);
            Assert.That(page1.Count, Is.EqualTo(2));
            Assert.That(cursor1, Is.Not.Null);

            var (page2, cursor2) = await tbl.PagingByRowIdAsync(2, cursor1);
            Assert.That(page2.Count, Is.EqualTo(1));
            Assert.That(cursor2, Is.Null);

            var (all, allCursor) = await tbl.PagingByRowIdAsync(100, null);
            Assert.That(all.Count, Is.EqualTo(3));
            Assert.That(allCursor, Is.Null);
        }
    }
}
