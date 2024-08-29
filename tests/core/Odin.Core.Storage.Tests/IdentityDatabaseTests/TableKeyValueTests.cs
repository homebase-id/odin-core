using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests

{
    public class TableKeyValueTests
    {

        [Test]
        public void InsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });
                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k2, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        // Test that inserting a duplicate throws an exception
        [Test]
        public void InsertDuplicateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests002");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });

                bool ok = false;

                try
                {
                    db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v2 });
                    ok = true;
                }
                catch
                {
                    ok = false;
                }

                Debug.Assert(ok == false);

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        [Test]
        public void UpdateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });
                db.tblKeyValue.Update(myc, new KeyValueRecord() { key = k1, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();
            }
        }


        // Test updating non existing row just continues
        [Test]
        public void Update2Test()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });

                bool ok = false;

                try
                {
                    db.tblKeyValue.Update(myc, new KeyValueRecord() { key = k2, data = v2 });
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
        public void DeleteTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });
                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k2, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                db.tblKeyValue.Delete(myc, k1);
                r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);
            }
        }


        [Test]
        public void UpsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();
                var v3 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Upsert(myc, new KeyValueRecord() { key = k1, data = v1 });
                db.tblKeyValue.Upsert(myc, new KeyValueRecord() { key = k2, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                r = db.tblKeyValue.Get(myc, k2);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                db.tblKeyValue.Upsert(myc, new KeyValueRecord() { key = k2, data = v3 });

                r = db.tblKeyValue.Get(myc, k2);
                if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                    Assert.Fail();
            }
        }



        [Test]
        public void LockingTest()
        {
            List<byte[]> Rows = new List<byte[]>();

            void writeDB(DatabaseConnection conn, IdentityDatabase db)
            {
                for (int i = 0; i < 100; i++)
                    db.tblKeyValue.Update(conn, new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() });
            }

            void readDB(DatabaseConnection conn, IdentityDatabase db)
            {
                for (int i = 0; i < 100; i++)
                    db.tblKeyValue.Get(conn, Rows[i]);
            }

            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests007");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                for (int i = 0; i < 100; i++)
                {
                    Rows.Add(Guid.NewGuid().ToByteArray());
                    db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() });
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
        public void CreateTableTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests008");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });

                var r = db.tblKeyValue.Get(myc, k1);

                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k2, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);

                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                r = db.tblKeyValue.Get(myc, k2);

                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                db.tblKeyValue.Update(myc, new KeyValueRecord() { key = k2, data = v1 });

                r = db.tblKeyValue.Get(myc, k2);

                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                db.tblKeyValue.Delete(myc, k2);

                r = db.tblKeyValue.Get(myc, k2);

                if (r != null)
                    Assert.Fail();
            }
        }


        // Test inserting two row´s in a transaction and reading their values
        [Test]
        public void CommitTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableKeyValueTests009");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                if (r != null)
                    Assert.Fail();
                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });
                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k2, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
                r = db.tblKeyValue.Get(myc, k2);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();
            }
        }
    }
}