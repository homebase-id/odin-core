using System;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableCircleTests
    {
        [Test]
        public void InsertTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var c1 = SequentialGuid.CreateGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var c2 = SequentialGuid.CreateGuid();
                var d2 = Guid.NewGuid().ToByteArray();

                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai1", circleId = c1, data = d1 });
                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai2", circleId = c2, data = d2 });

                var r = db.tblCircle.PagingByCircleId(myc, 100, null, out var nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                // Result set is ordered
                Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
            }
        }


        [Test]
        public void DeleteCircleTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var c1 = SequentialGuid.CreateGuid();
                var c2 = SequentialGuid.CreateGuid();
                var d2 = Guid.NewGuid().ToByteArray();
                var d1 = Guid.NewGuid().ToByteArray();

                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai1", circleId = c1, data = d1 });
                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai2", circleId = c2, data = d2 });

                db.tblCircle.Delete(myc, c2);

                var r = db.tblCircle.PagingByCircleId(myc, 100, null, out var nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                // Result set is ordered
                Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
            }
        }


        [Test]
        public void GetTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var c1 = SequentialGuid.CreateGuid();
                var c2 = SequentialGuid.CreateGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var d2 = Guid.NewGuid().ToByteArray();

                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai", circleId = c1, data = d1 });
                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai", circleId = c2, data = d2 });

                var r = db.tblCircle.Get(myc, c1);
                Debug.Assert(ByteArrayUtil.muidcmp(r.circleId, c1) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r.data, d1) == 0);

                r = db.tblCircle.Get(myc, c2);
                Debug.Assert(ByteArrayUtil.muidcmp(r.circleId, c2) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r.data, d2) == 0);
            }
        }


        [Test]
        public void GetAllCirclesEmptyTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var r = db.tblCircle.PagingByCircleId(myc, 100, null, out var nextCursor);
                Debug.Assert(r.Count == 0);
                Debug.Assert(nextCursor == null);
            }
        }


        [Test]
        public void GetAllCirclesTest()
        {
            using var db = new IdentityDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var c1 = SequentialGuid.CreateGuid();
                var c2 = SequentialGuid.CreateGuid();
                var d1 = Guid.NewGuid().ToByteArray();
                var d2 = Guid.NewGuid().ToByteArray();

                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai", circleId = c1, data = d1 });
                db.tblCircle.Insert(myc, new CircleRecord() { circleName = "aiai", circleId = c2, data = d2 });

                var r = db.tblCircle.PagingByCircleId(myc, 100, null, out var nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                Debug.Assert(ByteArrayUtil.muidcmp(r[0].circleId, c1) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r[0].data, d1) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r[1].circleId, c2) == 0);
                Debug.Assert(ByteArrayUtil.muidcmp(r[1].data, d2) == 0);
            }
        }
    }
}