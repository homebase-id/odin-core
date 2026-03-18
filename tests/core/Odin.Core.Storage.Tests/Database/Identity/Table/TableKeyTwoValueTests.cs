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
    public class TableKeyTwoValueTests : IocTestBase
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
            var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyTwoValue.GetAsync(k1);
            ClassicAssert.IsTrue(r == null);

            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

            r = await tblKeyTwoValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            var lr = await tblKeyTwoValue.GetByKeyTwoAsync(k11);
            if (ByteArrayUtil.muidcmp(lr[0].data, v1) != 0)
                Assert.Fail();
        }

        // Test that inserting a duplicate throws an exception
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertDuplicateTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyTwoValue.GetAsync(k1);
            ClassicAssert.IsTrue(r == null);

            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });

            bool ok = false;

            try
            {
                await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v2 });
                ok = true;
            }
            catch
            {
                ok = false;
            }

            ClassicAssert.IsTrue(ok == false);

            r = await tblKeyTwoValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();
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
            var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyTwoValue.GetAsync(k1);
            ClassicAssert.IsTrue(r == null);

            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
            await tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v2 });

            r = await tblKeyTwoValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                Assert.Fail();
        }


        // Test updating non existing row just continues
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task Update2Test(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyTwoValue.GetAsync(k1);
            ClassicAssert.IsTrue(r == null);

            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });

            bool ok = false;

            try
            {
                await tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = k2, data = v2 });
                ok = true;
            }
            catch
            {
                ok = false;
            }

            ClassicAssert.IsTrue(ok == true);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyTwoValue.GetAsync(k1);
            ClassicAssert.IsTrue(r == null);

            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

            r = await tblKeyTwoValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            await tblKeyTwoValue.DeleteAsync(k1);
            r = await tblKeyTwoValue.GetAsync(k1);
            ClassicAssert.IsTrue(r == null);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task UpsertTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();
            var v3 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyTwoValue.GetAsync(k1);
            ClassicAssert.IsTrue(r == null);

            await tblKeyTwoValue.UpsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
            await tblKeyTwoValue.UpsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

            r = await tblKeyTwoValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            r = await tblKeyTwoValue.GetAsync(k2);
            if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                Assert.Fail();

            await tblKeyTwoValue.UpsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v3 });

            r = await tblKeyTwoValue.GetAsync(k2);
            if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                Assert.Fail();

        }



        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TableKeyTwoValueTest1(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyTwoValue = scope.Resolve<TableKeyTwoValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var i1 = Guid.NewGuid().ToByteArray();
            var i2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = i1, data = v1 });
            await tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = i1, data = v2 });

            var r = await tblKeyTwoValue.GetAsync(k1);
            if (r == null)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            var ra = await tblKeyTwoValue.GetByKeyTwoAsync(i1);
            if (ra.Count != 2)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[0].key1, k1) == 0)
            {
                if (ByteArrayUtil.muidcmp(ra[0].data, v1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[1].data, v2) != 0)
                    Assert.Fail();
            }
            else
            {
                if (ByteArrayUtil.muidcmp(ra[0].data, v2) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[1].data, v1) != 0)
                    Assert.Fail();
            }


            await tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = k1, key2 = i1, data = v2 });
            r = await tblKeyTwoValue.GetAsync(k1);

            if (r == null)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                Assert.Fail();

            await tblKeyTwoValue.DeleteAsync(k1);
            ra = await tblKeyTwoValue.GetByKeyTwoAsync(i1);
            if (ra.Count != 1)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[0].data, v2) != 0)
                Assert.Fail();

            r = await tblKeyTwoValue.GetAsync(k1);

            if (r != null)
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
            var tbl = scope.Resolve<TableKeyTwoValue>();

            var k1 = SequentialGuid.CreateGuid().ToByteArray();
            var k2 = SequentialGuid.CreateGuid().ToByteArray();
            var k3 = SequentialGuid.CreateGuid().ToByteArray();
            var key2 = SequentialGuid.CreateGuid().ToByteArray();

            await tbl.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = key2, data = null });
            await tbl.InsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = key2, data = null });
            await tbl.InsertAsync(new KeyTwoValueRecord() { key1 = k3, key2 = key2, data = null });

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

