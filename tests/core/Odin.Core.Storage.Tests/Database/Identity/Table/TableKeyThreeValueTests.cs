using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableKeyThreeValueTests : IocTestBase
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
            var tblKeyThreeValue = scope.Resolve<TableKeyThreeValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var k222 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyThreeValue.GetAsync(k1);
            Debug.Assert(r == null);

            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v2 });

            r = await tblKeyThreeValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            var lr = await tblKeyThreeValue.GetByKeyTwoAsync(k11);
            if (ByteArrayUtil.muidcmp(lr[0], v1) != 0)
                Assert.Fail();

            lr = await tblKeyThreeValue.GetByKeyThreeAsync(k111);
            if (ByteArrayUtil.muidcmp(lr[0], v1) != 0)
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
            var tblKeyThreeValue = scope.Resolve<TableKeyThreeValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyThreeValue.GetAsync(k1);
            Debug.Assert(r == null);

            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });

            bool ok = false;

            try
            {
                await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v2 });
                ok = true;
            }
            catch
            {
                ok = false;
            }

            Debug.Assert(ok == false);

            r = await tblKeyThreeValue.GetAsync(k1);
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
            var tblKeyThreeValue = scope.Resolve<TableKeyThreeValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyThreeValue.GetAsync(k1);
            Debug.Assert(r == null);

            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
            await tblKeyThreeValue.UpdateAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v2 });

            r = await tblKeyThreeValue.GetAsync(k1);
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
            var tblKeyThreeValue = scope.Resolve<TableKeyThreeValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyThreeValue.GetAsync(k1);
            Debug.Assert(r == null);

            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });

            bool ok = false;

            try
            {
                await tblKeyThreeValue.UpdateAsync(new KeyThreeValueRecord() { key1 = k2, key2 = k11, key3 = k111, data = v2 });
                ok = true;
            }
            catch
            {
                ok = false;
            }

            Debug.Assert(ok == true);

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
            var tblKeyThreeValue = scope.Resolve<TableKeyThreeValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var k222 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyThreeValue.GetAsync(k1);
            Debug.Assert(r == null);

            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v2 });

            r = await tblKeyThreeValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            await tblKeyThreeValue.DeleteAsync(k1);
            r = await tblKeyThreeValue.GetAsync(k1);
            Debug.Assert(r == null);
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
            var tblKeyThreeValue = scope.Resolve<TableKeyThreeValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var k222 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();
            var v3 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyThreeValue.GetAsync(k1);
            Debug.Assert(r == null);

            await tblKeyThreeValue.UpsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
            await tblKeyThreeValue.UpsertAsync(new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v2 });

            r = await tblKeyThreeValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            r = await tblKeyThreeValue.GetAsync(k2);
            if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                Assert.Fail();

            await tblKeyThreeValue.UpsertAsync(new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v3 });

            r = await tblKeyThreeValue.GetAsync(k2);
            if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                Assert.Fail();

        }



        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task TableKeyThreeValueTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyThreeValue = scope.Resolve<TableKeyThreeValue>();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var i1 = Guid.NewGuid().ToByteArray();
            var i2 = Guid.NewGuid().ToByteArray();
            var u1 = Guid.NewGuid().ToByteArray();
            var u2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k1, key2 = i1, key3 = u1, data = v1 });
            await tblKeyThreeValue.InsertAsync(new KeyThreeValueRecord() { key1 = k2, key2 = i1, key3 = u2, data = v2 });

            var r = await tblKeyThreeValue.GetAsync(k1);
            if (r == null)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            var ra = await tblKeyThreeValue.GetByKeyTwoAsync(i1);
            if (ra.Count != 2)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[0], v1) == 0)
            {
                if (ByteArrayUtil.muidcmp(ra[0], v1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[1], v2) != 0)
                    Assert.Fail();
            }
            else
            {
                if (ByteArrayUtil.muidcmp(ra[0], v2) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[1], v1) != 0)
                    Assert.Fail();
            }

            ra = await tblKeyThreeValue.GetByKeyThreeAsync(u1);
            if (ra.Count != 1)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[0], v1) != 0)
                Assert.Fail();

            var singleRecord = await tblKeyThreeValue.GetByKeyTwoThreeAsync(i1, u2);
            ClassicAssert.NotNull(singleRecord);
            if (ByteArrayUtil.muidcmp(singleRecord.Single().data, v2) != 0)
                Assert.Fail();

            await tblKeyThreeValue.UpdateAsync(new KeyThreeValueRecord() { key1 = k1, key2 = i1, key3 = u1, data = v2 });
            r = await tblKeyThreeValue.GetAsync(k1);

            if (r == null)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                Assert.Fail();

            await tblKeyThreeValue.DeleteAsync(k1);
            ra = await tblKeyThreeValue.GetByKeyTwoAsync(i1);
            if (ra.Count != 1)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[0], v2) != 0)
                Assert.Fail();

            r = await tblKeyThreeValue.GetAsync(k1);

            if (r != null)
                Assert.Fail();

        }
    }
}
