using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    
    public class TableTagIndexTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert and read a row
        public async Task InsertRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            var md = await tblDriveTagIndex.GetAsync(driveId, k1);

            if (md.Count > 1)
                Assert.Fail();

            await tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);

            md = await tblDriveTagIndex.GetAsync(driveId, k1);

            if (md.Count == 0)
                Assert.Fail();

            if (md.Count != 1)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                Assert.Fail();
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert and read two tagmembers
        public async Task InsertDoubleRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(Guid.NewGuid());

            await tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);

            var md = await tblDriveTagIndex.GetAsync(driveId, k1);

            if (md == null)
                Assert.Fail();

            if (md.Count != 2)
                Assert.Fail();

            // We don't know what order it comes back in :o) Quick hack.
            if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
            {
                if (ByteArrayUtil.muidcmp(md[0], a1[1]) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(md[1], a1[0]) != 0)
                    Assert.Fail();
            }
            else
            {
                if (ByteArrayUtil.muidcmp(md[1], a1[1]) != 0)
                    Assert.Fail();
            }

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we cannot insert the same tagmember key twice on the same key
        public async Task InsertDuplicatetagMemberTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(a1[0]);

            bool ok = false;
            try
            {
                await tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
                ok = false;
            }
            catch
            {
                ok = true;
            }

            if (!ok)
                Assert.Fail();
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert the same tagmember on two different keys
        public async Task InsertDoubletagMemberTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
            await tblDriveTagIndex.InsertRowsAsync(driveId, k2, a1);

            var md = await tblDriveTagIndex.GetAsync(driveId, k1);
            if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                Assert.Fail();

            md = await tblDriveTagIndex.GetAsync(driveId, k2);
            if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                Assert.Fail();

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we cannot insert the same key twice
        public async Task InsertDoubleKeyTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
            bool ok = false;
            try
            {
                await tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
                ok = false;
            }
            catch
            {
                ok = true;
            }

            if (!ok)
                Assert.Fail();

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveTagIndex = scope.Resolve<TableDriveTagIndex>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            var v1 = Guid.NewGuid();
            var v2 = Guid.NewGuid();

            a1.Add(v1);
            a1.Add(v2);

            await tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
            await tblDriveTagIndex.InsertRowsAsync(driveId, k2, a1);

            // Delete all tagmembers of the first key entirely
            await tblDriveTagIndex.DeleteRowAsync(driveId, k1, a1);

            // Check that k1 is now gone
            var md = await tblDriveTagIndex.GetAsync(driveId, k1);
            if (md.Count != 0)
                Assert.Fail();

            // Remove one of the tagmembers from the list, delete it, and make sure we have the other one
            a1.RemoveAt(0); // Remove v1
            await tblDriveTagIndex.DeleteRowAsync(driveId, k2, a1);  // Delete v2

            // Check that we have one left
            md = await tblDriveTagIndex.GetAsync(driveId, k2);
            if (md.Count != 1)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md[0].ToByteArray(), v1.ToByteArray()) != 0)
                Assert.Fail();
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
            var tbl = scope.Resolve<TableDriveTagIndex>();

            var driveId = Guid.NewGuid();
            var f1 = Guid.NewGuid();
            var f2 = Guid.NewGuid();
            var f3 = Guid.NewGuid();
            var tagId = Guid.NewGuid();

            await tbl.InsertRowsAsync(driveId, f1, new List<Guid> { tagId });
            await tbl.InsertRowsAsync(driveId, f2, new List<Guid> { tagId });
            await tbl.InsertRowsAsync(driveId, f3, new List<Guid> { tagId });

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

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task LocalTagPagingByRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableDriveLocalTagIndex>();

            var driveId = Guid.NewGuid();
            var f1 = Guid.NewGuid();
            var f2 = Guid.NewGuid();
            var f3 = Guid.NewGuid();
            var tagId = Guid.NewGuid();

            await tbl.InsertRowsAsync(driveId, f1, new List<Guid> { tagId });
            await tbl.InsertRowsAsync(driveId, f2, new List<Guid> { tagId });
            await tbl.InsertRowsAsync(driveId, f3, new List<Guid> { tagId });

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
