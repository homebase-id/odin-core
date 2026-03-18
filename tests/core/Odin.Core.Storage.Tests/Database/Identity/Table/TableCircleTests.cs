using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableCircleTests : IocTestBase
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
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai1", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai2", circleId = c2, data = d2 });

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(nextCursor == null, message: "rdr.HasRows is the sinner");

            // Result set is ordered
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteCircleTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var c2 = SequentialGuid.CreateGuid();
            var d2 = Guid.NewGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai1", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai2", circleId = c2, data = d2 });

            await tblCircle.DeleteAsync(c2);

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(nextCursor == null, message: "rdr.HasRows is the sinner");

            // Result set is ordered
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var c2 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c2, data = d2 });

            var r = await tblCircle.GetAsync(c1);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r.circleId, c1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r.data, d1) == 0);

            r = await tblCircle.GetAsync(c2);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r.circleId, c2) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r.data, d2) == 0);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetAllCirclesEmptyTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            ClassicAssert.IsTrue(r.Count == 0);
            ClassicAssert.IsTrue(nextCursor == null);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetAllCirclesTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var c2 = SequentialGuid.CreateGuid();
            var d1 = Guid.NewGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c1, data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c2, data = d2 });

            var (r, nextCursor) = await tblCircle.PagingByCircleIdAsync(100, null);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(nextCursor == null, message: "rdr.HasRows is the sinner");

            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
            ClassicAssert.IsTrue(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
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
            var tbl = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            var c2 = SequentialGuid.CreateGuid();
            var c3 = SequentialGuid.CreateGuid();

            await tbl.InsertAsync(new CircleRecord() { circleId = c1, circleName = "Circle One", data = null });
            await tbl.InsertAsync(new CircleRecord() { circleId = c2, circleName = "Circle Two", data = null });
            await tbl.InsertAsync(new CircleRecord() { circleId = c3, circleName = "Circle Three", data = null });

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
