using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests

{
    public class TableKeyValueTests
    {

        [Test]
        public async Task InsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

                r = await db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        // Test that inserting a duplicate throws an exception
        [Test]
        public async Task InsertDuplicateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests002");
            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });

                bool ok = false;

                try
                {
                    await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v2 });
                    ok = true;
                }
                catch
                {
                    ok = false;
                }

                Debug.Assert(ok == false);

                r = await db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        [Test]
        public async Task UpdateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
                await db.tblKeyValue.UpdateAsync(new KeyValueRecord() { key = k1, data = v2 });

                r = await db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();
            }
        }


        // Test updating non existing row just continues
        [Test]
        public async Task Update2Test()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });

                bool ok = false;

                try
                {
                    await db.tblKeyValue.UpdateAsync(new KeyValueRecord() { key = k2, data = v2 });
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

                r = await db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                await db.tblKeyValue.DeleteAsync(k1);
                r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);
            }
        }


        [Test]
        public async Task UpsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();
                var v3 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyValue.UpsertAsync(new KeyValueRecord() { key = k1, data = v1 });
                await db.tblKeyValue.UpsertAsync(new KeyValueRecord() { key = k2, data = v2 });

                r = await db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                r = await db.tblKeyValue.GetAsync(k2);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                await db.tblKeyValue.UpsertAsync(new KeyValueRecord() { key = k2, data = v3 });

                r = await db.tblKeyValue.GetAsync(k2);
                if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                    Assert.Fail();
            }
        }



        [Test]
        public async Task LockingTest()
        {
            List<byte[]> Rows = new List<byte[]>();

            void writeDB(DatabaseConnection conn, IdentityDatabase db)
            {
                for (int i = 0; i < 100; i++)
                    db.tblKeyValue.UpdateAsync(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() }).Wait();
            }

            void readDB(DatabaseConnection conn, IdentityDatabase db)
            {
                for (int i = 0; i < 100; i++)
                    db.tblKeyValue.GetAsync(Rows[i]).Wait();
            }

            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests007");
            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                for (int i = 0; i < 100; i++)
                {
                    Rows.Add(Guid.NewGuid().ToByteArray());
                    await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() });
                }

                Thread tw = new Thread(() => writeDB(myc, db));
                Thread tr = new Thread(() => readDB(myc, db));

                tw.Start();
                tr.Start();

                tw.Join();
                tr.Join();
            }
        }

        [Test]
        public async Task CreateTableTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests008");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });

                var r = await db.tblKeyValue.GetAsync(k1);

                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

                r = await db.tblKeyValue.GetAsync(k1);

                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                r = await db.tblKeyValue.GetAsync(k2);

                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                await db.tblKeyValue.UpdateAsync(new KeyValueRecord() { key = k2, data = v1 });

                r = await db.tblKeyValue.GetAsync(k2);

                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                await db.tblKeyValue.DeleteAsync(k2);

                r = await db.tblKeyValue.GetAsync(k2);

                if (r != null)
                    Assert.Fail();
            }
        }


        // Test inserting two row´s in a transaction and reading their values
        [Test]
        public async Task CommitTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests009");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                if (r != null)
                    Assert.Fail();
                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

                r = await db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
                r = await db.tblKeyValue.GetAsync(k2);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();
            }
        }
    }
}