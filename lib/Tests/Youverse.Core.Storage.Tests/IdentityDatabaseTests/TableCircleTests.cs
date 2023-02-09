using System;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

namespace IdentityDatabaseTests
{
    public class TableCircleTests
    {
        [Test]
        public void InsertInvalidCircleTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\circle-insert-01.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();

            // This should be OK
            db.tblCircle.InsertCircle(c1, d1);

            bool ok = false;
            try
            {
                db.tblCircle.InsertCircle(null, d1);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            try
            {
                db.tblCircle.InsertCircle(c1, null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            var hc = new byte[3] { 1,2,3 };

            try
            {
                db.tblCircle.InsertCircle(hc, null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            var ld = new byte[TableCircle.MAX_DATA_LENGTH+1];
            try
            {
                db.tblCircle.InsertCircle(c1, ld);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }

        [Test]
        public void InsertCircleTest()
        {   
            using var db = new IdentityDatabase("URI=file:.\\circle-insert-02.db");
            db.CreateDatabase();

            var c1 = SequentialGuid.CreateGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            db.tblCircle.InsertCircle(c1, d1);
            db.tblCircle.InsertCircle(c2, d2);

            var r = db.tblCircle.GetAllCircles();
            Debug.Assert(r.Count == 2);
            // Result set is ordered
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
        }


        [Test]
        public void DeleteInvalidCircleTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\circle-delete-01.db");
            db.CreateDatabase();

            bool ok = false;
            try
            {
                db.tblCircle.DeleteCircle(null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            var hi = new byte[3];
            try
            {
                db.tblCircle.DeleteCircle(hi);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }


        [Test]
        public void DeleteCircleTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\circle-delete-02.db");
            db.CreateDatabase();

            var c1 = SequentialGuid.CreateGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            db.tblCircle.InsertCircle(c1, d1);
            db.tblCircle.InsertCircle(c2, d2);

            db.tblCircle.DeleteCircle(c2);

            var r = db.tblCircle.GetAllCircles();
            Debug.Assert(r.Count == 1);
            // Result set is ordered
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
        }


        [Test]
        public void GetTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\circle-get-01.db");
            db.CreateDatabase();

            var c1 = SequentialGuid.CreateGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            db.tblCircle.InsertCircle(c1, d1);
            db.tblCircle.InsertCircle(c2, d2);

            var r = db.tblCircle.Get(c1);
            Debug.Assert(ByteArrayUtil.muidcmp(r.circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r.data, d1) == 0);

            r = db.tblCircle.Get(c2);
            Debug.Assert(ByteArrayUtil.muidcmp(r.circleId, c2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r.data, d2) == 0);
        }

        [Test]
        public void GetInvalidTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\circle-get-02.db");
            db.CreateDatabase();

            var c1 = Guid.NewGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();

            bool ok = false;
            try
            {
                db.tblCircle.Get(null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            var hc = new byte[3] { 1, 2, 3 };

            try
            {
                db.tblCircle.Get(hc);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }


        [Test]
        public void GetAllCirclesEmptyTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\circle-getall-01.db");
            db.CreateDatabase();

            var r = db.tblCircle.GetAllCircles();
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void GetAllCirclesTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\circle-getall-02.db");
            db.CreateDatabase();

            var c1 = SequentialGuid.CreateGuid().ToByteArray();
            var d1 = Guid.NewGuid().ToByteArray();
            var c2 = SequentialGuid.CreateGuid().ToByteArray();
            var d2 = Guid.NewGuid().ToByteArray();

            db.tblCircle.InsertCircle(c1, d1);
            db.tblCircle.InsertCircle(c2, d2);

            var r = db.tblCircle.GetAllCircles();
            Debug.Assert(r.Count == 2);

            Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
            Debug.Assert(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
        }

    }
}