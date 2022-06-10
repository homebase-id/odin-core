﻿using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Youverse.Core.SystemStorage.SqliteKeyValue;

namespace Youverse.Core.Tests.SystemStorageTests.KeyValue
{
    public class TableKeyThreeValueTests
    {
        [Test]
        public void InsertTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kv3tbltest1.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var k222 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.TblKeyThreeValue.Get(k1);
            Debug.Assert(r == null);

            db.TblKeyThreeValue.InsertRow(k1, k11, k111, v1);
            db.TblKeyThreeValue.InsertRow(k2, k22, k222, v2);

            r = db.TblKeyThreeValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            var lr = db.TblKeyThreeValue.GetByKeyTwo(k11);
            if (SequentialGuid.muidcmp(lr[0], v1) != 0)
                Assert.Fail();

            lr = db.TblKeyThreeValue.GetByKeyThree(k111);
            if (SequentialGuid.muidcmp(lr[0], v1) != 0)
                Assert.Fail();
        }


        // Test that inserting a duplicate throws an exception
        [Test]
        public void InsertDuplicateTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kv3tbltest2.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.TblKeyThreeValue.Get(k1);
            Debug.Assert(r == null);

            db.TblKeyThreeValue.InsertRow(k1, k11, k111, v1);

            bool ok = false;

            try
            {
                db.TblKeyThreeValue.InsertRow(k1, k11, k111, v2);
                ok = true;
            }
            catch
            {
                ok = false;
            }

            Debug.Assert(ok == false);

            r = db.TblKeyThreeValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();
        }


        [Test]
        public void UpdateTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kv3tbltest3.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.TblKeyThreeValue.Get(k1);
            Debug.Assert(r == null);

            db.TblKeyThreeValue.InsertRow(k1, k11, k111, v1);
            db.TblKeyThreeValue.UpdateRow(k1, v2);

            r = db.TblKeyThreeValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v2) != 0)
                Assert.Fail();
        }


        // Test updating non existing row just continues
        [Test]
        public void Update2Test()
        {
            var db = new KeyValueDatabase("URI=file:.\\kv3tbltest4.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.TblKeyThreeValue.Get(k1);
            Debug.Assert(r == null);

            db.TblKeyThreeValue.InsertRow(k1, k11, k111, v1);

            bool ok = false;

            try
            {
                db.TblKeyThreeValue.UpdateRow(k2, v2);
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
            var db = new KeyValueDatabase("URI=file:.\\kv3tbltest5.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var k222 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.TblKeyThreeValue.Get(k1);
            Debug.Assert(r == null);

            db.TblKeyThreeValue.InsertRow(k1, k11, k111, v1);
            db.TblKeyThreeValue.InsertRow(k2, k22, k222, v2);

            r = db.TblKeyThreeValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            db.TblKeyThreeValue.DeleteRow(k1);
            r = db.TblKeyThreeValue.Get(k1);
            Debug.Assert(r == null);
        }


        [Test]
        public void UpsertTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\kv3tbltest6.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k11 = Guid.NewGuid().ToByteArray();
            var k22 = Guid.NewGuid().ToByteArray();
            var k111 = Guid.NewGuid().ToByteArray();
            var k222 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();
            var v3 = Guid.NewGuid().ToByteArray();

            var r = db.TblKeyThreeValue.Get(k1);
            Debug.Assert(r == null);

            db.TblKeyThreeValue.UpsertRow(k1, k11, k111, v1);
            db.TblKeyThreeValue.UpsertRow(k2, k22, k222, v2);

            r = db.TblKeyThreeValue.Get(k1);
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            r = db.TblKeyThreeValue.Get(k2);
            if (SequentialGuid.muidcmp(r, v2) != 0)
                Assert.Fail();

            db.TblKeyThreeValue.UpsertRow(k2, k22, k222, v3);

            r = db.TblKeyThreeValue.Get(k2);
            if (SequentialGuid.muidcmp(r, v3) != 0)
                Assert.Fail();
        }


        [Test]
        public void TableKeyThreeValueTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\ctest31.db");
            db.CreateDatabase();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var i1 = Guid.NewGuid().ToByteArray();
            var i2 = Guid.NewGuid().ToByteArray();
            var u1 = Guid.NewGuid().ToByteArray();
            var u2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            db.TblKeyThreeValue.InsertRow(k1, i1, u1, v1);
            db.TblKeyThreeValue.InsertRow(k2, i1, u2, v2);

            var r = db.TblKeyThreeValue.Get(k1);
            if (r == null)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r, v1) != 0)
                Assert.Fail();

            var ra = db.TblKeyThreeValue.GetByKeyTwo(i1);
            if (ra.Count != 2)
                Assert.Fail();
            if (SequentialGuid.muidcmp(ra[0], v1) != 0)
                Assert.Fail();
            if (SequentialGuid.muidcmp(ra[1], v2) != 0)
                Assert.Fail();

            ra = db.TblKeyThreeValue.GetByKeyThree(u1);
            if (ra.Count != 1)
                Assert.Fail();
            if (SequentialGuid.muidcmp(ra[0], v1) != 0)
                Assert.Fail();

            ra = db.TblKeyThreeValue.GetByKeyTwoThree(i1, u2);
            Assert.True(ra.Count != 1);
            if (SequentialGuid.muidcmp(ra[0], v2) != 0)
                Assert.Fail();

            db.TblKeyThreeValue.UpdateRow(k1, v2);
            r = db.TblKeyThreeValue.Get(k1);

            if (r == null)
                Assert.Fail();
            if (SequentialGuid.muidcmp(r, v2) != 0)
                Assert.Fail();

            db.TblKeyThreeValue.DeleteRow(k1);
            ra = db.TblKeyThreeValue.GetByKeyTwo(i1);
            if (ra.Count != 1)
                Assert.Fail();
            if (SequentialGuid.muidcmp(ra[0], v2) != 0)
                Assert.Fail();

            r = db.TblKeyThreeValue.Get(k1);

            if (r != null)
                Assert.Fail();
        }
    }
}