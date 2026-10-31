using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Exceptions;
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

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai1", circleId = c1, CircleSlug = "c1", data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai2", circleId = c2, CircleSlug = "c2", data = d2 });

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
        // The share key is set once, by its own update, and no definition write can change or clear it.
        public async Task ShareKeyIsSetOnceAndKeptByUpserts(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var c1 = SequentialGuid.CreateGuid();
            await tblCircle.InsertAsync(new CircleRecord { circleName = "family", circleId = c1, CircleSlug = "family" });

            var key = new byte[] { 2, 1, 1, 1 };
            Assert.That(await tblCircle.SetShareKeyAsync(c1, key), Is.EqualTo(1));
            Assert.That(await tblCircle.SetShareKeyAsync(c1, new byte[] { 2, 9, 9 }), Is.EqualTo(0), "a second key replaced the first");
            Assert.That(await tblCircle.SetShareKeyAsync(SequentialGuid.CreateGuid(), key), Is.EqualTo(0), "a missing circle took a key");

            // A definition write carries no key, and one that names another is overruled
            await tblCircle.UpsertAsync(new CircleRecord { circleName = "renamed", circleId = c1, CircleSlug = "family" });
            await tblCircle.UpsertAsync(new CircleRecord
                { circleName = "renamed again", circleId = c1, CircleSlug = "family", appKeyLockedShareKey = new byte[] { 2, 7 } });

            var stored = await tblCircle.GetAsync(c1);
            Assert.That((stored.circleName, stored.appKeyLockedShareKey), Is.EqualTo(("renamed again", key)));

            // A new circle written by upsert has none
            var c2 = SequentialGuid.CreateGuid();
            await tblCircle.UpsertAsync(new CircleRecord { circleName = "friends", circleId = c2, CircleSlug = "friends" });
            Assert.That((await tblCircle.GetAsync(c2)).appKeyLockedShareKey, Is.Null);

            Assert.ThrowsAsync<OdinDatabaseValidationException>(() => tblCircle.SetShareKeyAsync(c2, []));
            Assert.ThrowsAsync<OdinDatabaseValidationException>(() => tblCircle.SetShareKeyAsync(c2, new byte[513]));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task CachedShareKeySetIsSeenByTheNextRead(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var cached = scope.Resolve<TableCircleCached>();

            var c1 = SequentialGuid.CreateGuid();
            await cached.InsertAsync(new CircleRecord { circleName = "family", circleId = c1, CircleSlug = "family" });
            Assert.That((await cached.GetAsync(c1))!.appKeyLockedShareKey, Is.Null);

            var key = new byte[] { 2, 3, 4 };
            await cached.SetShareKeyAsync(c1, key);
            Assert.That((await cached.GetAsync(c1))!.appKeyLockedShareKey, Is.EqualTo(key));
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // The GrantOn lookup the enrollment pass uses returns whole records, the share key with them.
        public async Task GetByGrantOnReadsEveryColumn(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblCircle = scope.Resolve<TableCircle>();

            var shareKey = new byte[] { 1, 2, 3, 4 };
            var c1 = SequentialGuid.CreateGuid();
            await tblCircle.InsertAsync(new CircleRecord
                { circleName = "enrolling", circleId = c1, CircleSlug = "c1", GrantOn = 1, appKeyLockedShareKey = shareKey });
            await tblCircle.InsertAsync(new CircleRecord { circleName = "other", circleId = SequentialGuid.CreateGuid(), CircleSlug = "c2" });

            var r = await tblCircle.GetByGrantOnAsync(1);

            Assert.That(r.Select(c => (c.circleId, c.circleName, c.appKeyLockedShareKey)),
                Is.EqualTo(new[] { (c1, "enrolling", shareKey) }));
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

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai1", circleId = c1, CircleSlug = "c1", data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai2", circleId = c2, CircleSlug = "c2", data = d2 });

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

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c1, CircleSlug = "c1", data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c2, CircleSlug = "c2", data = d2 });

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

            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c1, CircleSlug = "c1", data = d1 });
            await tblCircle.InsertAsync(new CircleRecord() { circleName = "aiai", circleId = c2, CircleSlug = "c2", data = d2 });

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

            await tbl.InsertAsync(new CircleRecord() { circleId = c1, CircleSlug = "c1", circleName = "Circle One", data = null });
            await tbl.InsertAsync(new CircleRecord() { circleId = c2, CircleSlug = "c2", circleName = "Circle Two", data = null });
            await tbl.InsertAsync(new CircleRecord() { circleId = c3, CircleSlug = "c3", circleName = "Circle Three", data = null });

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
