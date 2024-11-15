using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{

    public class TableKeyTwoValueTests
    {

        [Test]
        public async Task InsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTwoKeyValue001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyTwoValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

                r = await db.tblKeyTwoValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                var lr = await db.tblKeyTwoValue.GetByKeyTwoAsync(k11);
                if (ByteArrayUtil.muidcmp(lr[0].data, v1) != 0)
                    Assert.Fail();
            }
        }

        // Test that inserting a duplicate throws an exception
        [Test]
        public async Task InsertDuplicateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTwoKeyValue002");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyTwoValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });

                bool ok = false;

                try
                {
                    await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v2 });
                    ok = true;
                }
                catch
                {
                    ok = false;
                }

                Debug.Assert(ok == false);

                r = await db.tblKeyTwoValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        [Test]
        public async Task UpdateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTwoKeyValue003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyTwoValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                await db.tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v2 });

                r = await db.tblKeyTwoValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();
            }
        }


        // Test updating non existing row just continues
        [Test]
        public async Task Update2Test()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTwoKeyValue004");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyTwoValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });

                bool ok = false;

                try
                {
                    await db.tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = k2, data = v2 });
                    ok = true;
                }
                catch
                {
                    ok = false;
                }

                Debug.Assert(ok == true);
            }
        }


        [Test]
        public async Task DeleteTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTwoKeyValue005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyTwoValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

                r = await db.tblKeyTwoValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                await db.tblKeyTwoValue.DeleteAsync(k1);
                r = await db.tblKeyTwoValue.GetAsync(k1);
                Debug.Assert(r == null);
            }
        }


        [Test]
        public async Task UpsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTwoKeyValue006");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();
                var v3 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyTwoValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyTwoValue.UpsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                await db.tblKeyTwoValue.UpsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

                r = await db.tblKeyTwoValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                r = await db.tblKeyTwoValue.GetAsync(k2);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                await db.tblKeyTwoValue.UpsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v3 });

                r = await db.tblKeyTwoValue.GetAsync(k2);
                if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                    Assert.Fail();
            }
        }



        [Test]
        public async Task TableKeyTwoValueTest1()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTwoKeyValue007");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var i1 = Guid.NewGuid().ToByteArray();
                var i2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k1, key2 = i1, data = v1 });
                await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = k2, key2 = i1, data = v2 });

                var r = await db.tblKeyTwoValue.GetAsync(k1);
                if (r == null)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                var ra = await db.tblKeyTwoValue.GetByKeyTwoAsync(i1);
                if (ra.Count != 2)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[0].data, v1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[1].data, v2) != 0)
                    Assert.Fail();


                await db.tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = k1, key2 = i1, data = v2 });
                r = await db.tblKeyTwoValue.GetAsync(k1);

                if (r == null)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                await db.tblKeyTwoValue.DeleteAsync(k1);
                ra = await db.tblKeyTwoValue.GetByKeyTwoAsync(i1);
                if (ra.Count != 1)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[0].data, v2) != 0)
                    Assert.Fail();

                r = await db.tblKeyTwoValue.GetAsync(k1);

                if (r != null)
                    Assert.Fail();

            }
        }
    }
}
