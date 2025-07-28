using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Factory;

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

            // IsInOutbox
            n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId, null, null, null, isReadByRecipient: true);
            ClassicAssert.IsTrue(n == 1);
            r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].isReadByRecipient == true);

            n = await tbl.UpdateTransferHistoryRecordAsync(d1, f1, frodoId, null, null, null, isReadByRecipient: false);
            ClassicAssert.IsTrue(n == 1);
            r = await tbl.GetAsync(d1, f1);
            ClassicAssert.IsTrue(r[0].isReadByRecipient == false);

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
    }
}
