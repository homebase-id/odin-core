﻿using System;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{

    public class TableKeyTwoValueTests
    {

        [Test]
        public void InsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyTwoValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

                r = db.tblKeyTwoValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                var lr = db.tblKeyTwoValue.GetByKeyTwo(myc, k11);
                if (ByteArrayUtil.muidcmp(lr[0].data, v1) != 0)
                    Assert.Fail();
            }
        }

        // Test that inserting a duplicate throws an exception
        [Test]
        public void InsertDuplicateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var k1 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyTwoValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });

                bool ok = false;

                try
                {
                    db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v2 });
                    ok = true;
                }
                catch
                {
                    ok = false;
                }

                Debug.Assert(ok == false);

                r = db.tblKeyTwoValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        [Test]
        public void UpdateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var k1 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyTwoValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                db.tblKeyTwoValue.Update(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v2 });

                r = db.tblKeyTwoValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();
            }
        }


        // Test updating non existing row just continues
        [Test]
        public void Update2Test()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyTwoValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });

                bool ok = false;

                try
                {
                    db.tblKeyTwoValue.Update(myc, new KeyTwoValueRecord() { key1 = k2, data = v2 });
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyTwoValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

                r = db.tblKeyTwoValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                db.tblKeyTwoValue.Delete(myc, k1);
                r = db.tblKeyTwoValue.Get(myc, k1);
                Debug.Assert(r == null);
            }
        }


        [Test]
        public void UpsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();
                var v3 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyTwoValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyTwoValue.Upsert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = k11, data = v1 });
                db.tblKeyTwoValue.Upsert(myc, new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v2 });

                r = db.tblKeyTwoValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                r = db.tblKeyTwoValue.Get(myc, k2);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                db.tblKeyTwoValue.Upsert(myc, new KeyTwoValueRecord() { key1 = k2, key2 = k22, data = v3 });

                r = db.tblKeyTwoValue.Get(myc, k2);
                if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                    Assert.Fail();
            }
        }



        [Test]
        public void TableKeyTwoValueTest1()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var i1 = Guid.NewGuid().ToByteArray();
                var i2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k1, key2 = i1, data = v1 });
                db.tblKeyTwoValue.Insert(myc, new KeyTwoValueRecord() { key1 = k2, key2 = i1, data = v2 });

                var r = db.tblKeyTwoValue.Get(myc, k1);
                if (r == null)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                var ra = db.tblKeyTwoValue.GetByKeyTwo(myc, i1);
                if (ra.Count != 2)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[0].data, v1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[1].data, v2) != 0)
                    Assert.Fail();


                db.tblKeyTwoValue.Update(myc, new KeyTwoValueRecord() { key1 = k1, key2 = i1, data = v2 });
                r = db.tblKeyTwoValue.Get(myc, k1);

                if (r == null)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                db.tblKeyTwoValue.Delete(myc, k1);
                ra = db.tblKeyTwoValue.GetByKeyTwo(myc, i1);
                if (ra.Count != 1)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[0].data, v2) != 0)
                    Assert.Fail();

                r = db.tblKeyTwoValue.Get(myc, k1);

                if (r != null)
                    Assert.Fail();

            }
        }
    }
}
