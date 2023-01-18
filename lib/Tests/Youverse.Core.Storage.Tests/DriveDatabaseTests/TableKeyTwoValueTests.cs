using System;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace IndexerTests.KeyValue
{

    public class TableKeyTwoValueTests
    {

        [Test]
        public void InsertTest()
        {
            using var db = new KeyValueDatabase("URI=file:.\\kv2tbltest1.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyTwoValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyTwoValue.InsertRow(k1, k11, v1);
            db.tblKeyTwoValue.InsertRow(k2, k22, v2);

            r = db.tblKeyTwoValue.Get(k1);
            if (ByteArrayUtil.muidcmp(r, v1) != 0)
                Assert.Fail();

            var lr = db.tblKeyTwoValue.GetByKeyTwo(k11);
            if (ByteArrayUtil.muidcmp(lr[0], v1) != 0)
                Assert.Fail();
        }


        // Test that inserting a duplicate throws an exception
        [Test]
        public void InsertDuplicateTest()
        {
            using var db = new KeyValueDatabase("URI=file:.\\kv2tbltest2.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyTwoValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyTwoValue.InsertRow(k1, k11, v1);

            bool ok = false;

            try
            {
                db.tblKeyTwoValue.InsertRow(k1, k11, v2);
                ok = true;
            }
            catch
            {
                ok = false;
            }

            Debug.Assert(ok == false);

            r = db.tblKeyTwoValue.Get(k1);
            if (ByteArrayUtil.muidcmp(r, v1) != 0)
                Assert.Fail();
        }


        [Test]
        public void UpdateTest()
        {
            using var db = new KeyValueDatabase("URI=file:.\\kv2tbltest3.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyTwoValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyTwoValue.InsertRow(k1, k11, v1);
            db.tblKeyTwoValue.UpdateRow(k1, v2);

            r = db.tblKeyTwoValue.Get(k1);
            if (ByteArrayUtil.muidcmp(r, v2) != 0)
                Assert.Fail();
        }


        // Test updating non existing row just continues
        [Test]
        public void Update2Test()
        {
            using var db = new KeyValueDatabase("URI=file:.\\kv2tbltest4.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyTwoValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyTwoValue.InsertRow(k1, k11, v1);

            bool ok = false;

            try
            {
                db.tblKeyTwoValue.UpdateRow(k2, v2);
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
            using var db = new KeyValueDatabase("URI=file:.\\kv2tbltest5.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyTwoValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyTwoValue.InsertRow(k1, k11, v1);
            db.tblKeyTwoValue.InsertRow(k2, k22, v2);

            r = db.tblKeyTwoValue.Get(k1);
            if (ByteArrayUtil.muidcmp(r, v1) != 0)
                Assert.Fail();

            db.tblKeyTwoValue.DeleteRow(k1);
            r = db.tblKeyTwoValue.Get(k1);
            Debug.Assert(r == null);
        }


        [Test]
        public void UpsertTest()
        {
            using var db = new KeyValueDatabase("URI=file:.\\kv2tbltest6.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();
            var v3 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyTwoValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyTwoValue.UpsertRow(k1, k11, v1);
            db.tblKeyTwoValue.UpsertRow(k2, k22, v2);

            r = db.tblKeyTwoValue.Get(k1);
            if (ByteArrayUtil.muidcmp(r, v1) != 0)
                Assert.Fail();

            r = db.tblKeyTwoValue.Get(k2);
            if (ByteArrayUtil.muidcmp(r, v2) != 0)
                Assert.Fail();

            db.tblKeyTwoValue.UpsertRow(k2, k22, v3);

            r = db.tblKeyTwoValue.Get(k2);
            if (ByteArrayUtil.muidcmp(r, v3) != 0)
                Assert.Fail();
        }




        [Test]
        public void TableKeyTwoValueTest1()
        {
            using var db = new KeyValueDatabase("URI=file:.\\ctest10.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var i1 = Guid.NewGuid().ToByteArray();
            var i2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            db.tblKeyTwoValue.InsertRow(k1, i1, v1);
            db.tblKeyTwoValue.InsertRow(k2, i1, v2);

            var r = db.tblKeyTwoValue.Get(k1);
            if (r == null)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r, v1) != 0)
                Assert.Fail();

            var ra = db.tblKeyTwoValue.GetByKeyTwo(i1);
            if (ra.Count != 2)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[0], v1) != 0)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[1], v2) != 0)
                Assert.Fail();


            db.tblKeyTwoValue.UpdateRow(k1, v2);
            r = db.tblKeyTwoValue.Get(k1);

            if (r == null)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(r, v2) != 0)
                Assert.Fail();

            db.tblKeyTwoValue.DeleteRow(k1);
            ra = db.tblKeyTwoValue.GetByKeyTwo(i1);
            if (ra.Count != 1)
                Assert.Fail();
            if (ByteArrayUtil.muidcmp(ra[0], v2) != 0)
                Assert.Fail();

            r = db.tblKeyTwoValue.Get(k1);

            if (r != null)                
                Assert.Fail();

        }
    }
}
