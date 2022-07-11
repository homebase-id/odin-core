using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Youverse.Core.SystemStorage.SqliteKeyValue;

namespace Youverse.Core.Tests.SystemStorageTests.KeyValue
{
    public class TableKeyValueTests
    {

        [Test]
        public void InsertTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest1.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyValue.InsertRow(k1, v1);
            db.tblKeyValue.InsertRow(k2, v2);

            r = db.tblKeyValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();
        }


        // Test that inserting a duplicate throws an exception
        [Test]
        public void InsertDuplicateTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest2.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyValue.InsertRow(k1, v1);

            bool ok = false;

            try
            {
                db.tblKeyValue.InsertRow(k1, v2);
                ok = true;
            }
            catch
            {
                ok = false;
            }

            Debug.Assert(ok == false);

            r = db.tblKeyValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();
        }


        [Test]
        public void UpdateTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest3.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyValue.InsertRow(k1, v1);
            db.tblKeyValue.UpdateRow(k1, v2);

            r = db.tblKeyValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v2) != 0)
                Assert.Fail();
        }


        // Test updating non existing row just continues
        [Test]
        public void Update2Test()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest4.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyValue.InsertRow(k1, v1);

            bool ok = false;

            try
            {
                db.tblKeyValue.UpdateRow(k2, v2);
                ok = true;
            }
            catch
            {
                ok = false;
            }

            Debug.Assert(ok == true);
        }



        [Test]
        public void DeleteTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest5.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyValue.InsertRow(k1, v1);
            db.tblKeyValue.InsertRow(k2, v2);

            r = db.tblKeyValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            db.tblKeyValue.DeleteRow(k1);
            r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);
        }


        [Test]
        public void UpsertTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest6.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();
            var v3 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyValue.UpsertRow(k1, v1);
            db.tblKeyValue.UpsertRow(k2, v2);

            r = db.tblKeyValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            r = db.tblKeyValue.Get(k2);
            if (SequentialGuid.muidcmp(r, v2) != 0)
                Assert.Fail();

            db.tblKeyValue.UpsertRow(k2, v3);

            r = db.tblKeyValue.Get(k2);
            if (SequentialGuid.muidcmp(r, v3) != 0)
                Assert.Fail();
        }



        [Test]
        public void LockingTest()
        {
            List<byte[]> Rows = new List<byte[]>();

            void writeDB(KeyValueDatabase db)
            {
                for (int i = 0; i < 100; i++)
                    db.tblKeyValue.UpdateRow(Rows[i], Guid.NewGuid().ToByteArray());
            }

            void readDB(KeyValueDatabase db)
            {
                for (int i = 0; i < 100; i++)
                    db.tblKeyValue.Get(Rows[i]);
            }

            var db = new KeyValueDatabase("URI=file:.\\kvtbltest10.db");
            db.CreateDatabase();

            for (int i = 0; i < 100; i++)
            {
                Rows.Add(Guid.NewGuid().ToByteArray());
                db.tblKeyValue.InsertRow(Rows[i], Guid.NewGuid().ToByteArray());
            }

            Thread tw = new Thread(() => writeDB(db));
            Thread tr = new Thread(() => readDB(db));

            tw.Start();
            tr.Start();

            tw.Join();
            tr.Join();
        }

        [Test]
        public void CreateTableTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest15.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            db.tblKeyValue.InsertRow(k1, v1);

            var r = db.tblKeyValue.Get(k1);

            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            db.tblKeyValue.InsertRow(k2, v2);

            r = db.tblKeyValue.Get(k1);

            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            r = db.tblKeyValue.Get(k2);

            if (SequentialGuid.muidcmp(r, v2) != 0)
                Assert.Fail();

            db.tblKeyValue.UpdateRow(k2, v1);

            r = db.tblKeyValue.Get(k2);

            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            db.tblKeyValue.DeleteRow(k2);

            r = db.tblKeyValue.Get(k2);

            if (r != null)
                Assert.Fail();

        }


        // Test inserting two row´s in a transaction and reading their values
        [Test]
        public void CommitTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kvtbltest22.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            db.BeginTransaction();
            var r = db.tblKeyValue.Get(k1);
            if (r != null)
                Assert.Fail();
            db.tblKeyValue.InsertRow(k1, v1);
            db.tblKeyValue.InsertRow(k2, v2);
            // If I query the DB here before commit for k1, I get v1.
            // Wonder if that's a bug or a feature
            db.Commit();

            r = db.tblKeyValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();
            r = db.tblKeyValue.Get(k2);
            if (SequentialGuid.muidcmp(r, v2) != 0)
                Assert.Fail();
        }
    }
}