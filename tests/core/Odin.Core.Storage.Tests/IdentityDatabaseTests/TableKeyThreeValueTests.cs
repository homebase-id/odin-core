using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableKeyThreeValueTests
    {
        [Test]
        public void InsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableThreeKeyValueTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var k111 = Guid.NewGuid().ToByteArray();
                var k222 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.TblKeyThreeValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v2 });

                r = db.TblKeyThreeValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                var lr = db.TblKeyThreeValue.GetByKeyTwo(myc, k11);
                if (ByteArrayUtil.muidcmp(lr[0], v1) != 0)
                    Assert.Fail();

                lr = db.TblKeyThreeValue.GetByKeyThree(myc, k111);
                if (ByteArrayUtil.muidcmp(lr[0], v1) != 0)
                    Assert.Fail();
            }
        }


        // Test that inserting a duplicate throws an exception
        [Test]
        public void InsertDuplicateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableThreeKeyValueTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k111 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.TblKeyThreeValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });

                bool ok = false;

                try
                {
                    db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v2 });
                    ok = true;
                }
                catch
                {
                    ok = false;
                }

                Debug.Assert(ok == false);

                r = db.TblKeyThreeValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        [Test]
        public void UpdateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableThreeKeyValueTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k111 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.TblKeyThreeValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
                db.TblKeyThreeValue.Update(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v2 });

                r = db.TblKeyThreeValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();
            }
        }


        // Test updating non existing row just continues
        [Test]
        public void Update2Test()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableThreeKeyValueTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k111 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.TblKeyThreeValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });

                bool ok = false;

                try
                {
                    db.TblKeyThreeValue.Update(myc, new KeyThreeValueRecord() { key1 = k2, key2 = k11, key3 = k111, data = v2 });
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableThreeKeyValueTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var k11 = Guid.NewGuid().ToByteArray();
                var k22 = Guid.NewGuid().ToByteArray();
                var k111 = Guid.NewGuid().ToByteArray();
                var k222 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.TblKeyThreeValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v2 });

                r = db.TblKeyThreeValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                db.TblKeyThreeValue.Delete(myc, k1);
                r = db.TblKeyThreeValue.Get(myc, k1);
                Debug.Assert(r == null);
            }
        }


        [Test]
        public void UpsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableThreeKeyValueTests006");

            using (var myc = db.CreateDisposableConnection())
            {
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

                var r = db.TblKeyThreeValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.TblKeyThreeValue.Upsert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = k11, key3 = k111, data = v1 });
                db.TblKeyThreeValue.Upsert(myc, new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v2 });

                r = db.TblKeyThreeValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                r = db.TblKeyThreeValue.Get(myc, k2);
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                db.TblKeyThreeValue.Upsert(myc, new KeyThreeValueRecord() { key1 = k2, key2 = k22, key3 = k222, data = v3 });

                r = db.TblKeyThreeValue.Get(myc, k2);
                if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                    Assert.Fail();
            }
        }



        [Test]
        public void TableKeyThreeValueTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableThreeKeyValueTests007");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var i1 = Guid.NewGuid().ToByteArray();
                var i2 = Guid.NewGuid().ToByteArray();
                var u1 = Guid.NewGuid().ToByteArray();
                var u2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k1, key2 = i1, key3 = u1, data = v1 });
                db.TblKeyThreeValue.Insert(myc, new KeyThreeValueRecord() { key1 = k2, key2 = i1, key3 = u2, data = v2 });

                var r = db.TblKeyThreeValue.Get(myc, k1);
                if (r == null)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                var ra = db.TblKeyThreeValue.GetByKeyTwo(myc, i1);
                if (ra.Count != 2)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[0], v1) != 0)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[1], v2) != 0)
                    Assert.Fail();

                ra = db.TblKeyThreeValue.GetByKeyThree(myc, u1);
                if (ra.Count != 1)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[0], v1) != 0)
                    Assert.Fail();

                var singleRecord = db.TblKeyThreeValue.GetByKeyTwoThree(myc, i1, u2);
                Assert.NotNull(singleRecord);
                if (ByteArrayUtil.muidcmp(singleRecord.Single().data, v2) != 0)
                    Assert.Fail();

                db.TblKeyThreeValue.Update(myc, new KeyThreeValueRecord() { key1 = k1, key2 = i1, key3 = u1, data = v2 });
                r = db.TblKeyThreeValue.Get(myc, k1);

                if (r == null)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
                    Assert.Fail();

                db.TblKeyThreeValue.Delete(myc, k1);
                ra = db.TblKeyThreeValue.GetByKeyTwo(myc, i1);
                if (ra.Count != 1)
                    Assert.Fail();
                if (ByteArrayUtil.muidcmp(ra[0], v2) != 0)
                    Assert.Fail();

                r = db.TblKeyThreeValue.Get(myc, k1);

                if (r != null)
                    Assert.Fail();
            }
        }
    }
}