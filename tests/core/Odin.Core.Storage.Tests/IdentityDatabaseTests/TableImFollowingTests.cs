using System;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableImFollowingTests
    {
        [Test]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                // Let's say that we're Frodo and we're followed by these 5 asir
                // We have 2 channels we post to.
                //
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var i3 = "freja.valhalla.com";
                var i4 = "heimdal.valhalla.com";
                var i5 = "loke.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                // Odin follows d1
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });

                // Thor follows d1
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });

                // Freja follows d1 & d2
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i3), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i3), driveId = d2 });

                // Heimdal follows d2
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i4), driveId = d2 });

                // Loke follows everything
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

                // Now Frodo makes a new post to d1, which means we shouold get
                // everyone except Heimdal. Let's do a page size of 3
                // Get freja, loke, odin back. Still missing Thor
                var r = db.tblImFollowing.GetFollowers(myc, 3, d1, null, out var nextCursor);
                Debug.Assert(r.Count == 3, message: "rdr.HasRows is the sinner");
                Debug.Assert(nextCursor == r[2]);

                // Get the second page. Always use the last result as the cursor
                r = db.tblImFollowing.GetFollowers(myc, 3, d1, nextCursor, out nextCursor);
                Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                             // but if we call again anyway, we get 0 back.
                Debug.Assert(nextCursor == null);


                // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
                //
                r = db.tblImFollowing.GetFollowers(myc, 3, d2, null, out nextCursor);
                Debug.Assert(r.Count == 3);
                Debug.Assert(nextCursor == null);
            }
        }

        [Test]
        public void InsertValidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();
                var g2 = Guid.NewGuid();

                // This is OK {odin.vahalla.com, driveId}
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = g2 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId("thor.valhalla.com"), driveId = g1 });

                var r = db.tblImFollowing.Get(myc, new OdinId(i1));
                Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveId, g2) == 0));
                Debug.Assert((ByteArrayUtil.muidcmp(r[1].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, g2) == 0));

                // This is OK {odin.vahalla.com, {000000}}
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });
                r = db.tblImFollowing.Get(myc, new OdinId(i1));
                Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveId, Guid.Empty) == 0));
            }
        }


        [Test]
        public void InsertInvalidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();

                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });

                bool ok = false;
                try
                {
                    // Can't insert duplicate
                    db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);



                ok = false;
                try
                {
                    // Can't insert duplicate, this is supposed to fail.
                    db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);
            }
        }


        [Test]
        public void DeleteFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

                db.tblImFollowing.DeleteByIdentity(myc, new OdinId(i2));

                var r = db.tblImFollowing.Get(myc, new OdinId(i1));
                Debug.Assert(r.Count == 1);
                r = db.tblImFollowing.Get(myc, new OdinId(i2));
                Debug.Assert(r.Count == 0);
                db.tblImFollowing.DeleteByIdentity(myc, new OdinId(i1));
                r = db.tblImFollowing.Get(myc, new OdinId(i2));
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public void DeleteFollowerDriveTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

                db.tblImFollowing.Delete(myc, new OdinId(i1), d2);
                var r = db.tblImFollowing.Get(myc, new OdinId(i1));
                Debug.Assert(r.Count == 1);

                db.tblImFollowing.Delete(myc, new OdinId(i1), d1);
                r = db.tblImFollowing.Get(myc, new OdinId(i1));
                Debug.Assert(r.Count == 0);

                db.tblImFollowing.Delete(myc, new OdinId(i2), d1);
                r = db.tblImFollowing.Get(myc, new OdinId(i2));
                Debug.Assert(r.Count == 2);

                db.tblImFollowing.Delete(myc, new OdinId(i2), d2);
                r = db.tblImFollowing.Get(myc, new OdinId(i2));
                Debug.Assert(r.Count == 1);

                db.tblImFollowing.Delete(myc, new OdinId(i2), Guid.Empty);
                r = db.tblImFollowing.Get(myc, new OdinId(i2));
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public void GetFollowersInvalidTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var d1 = Guid.NewGuid();

                bool ok = false;
                try
                {
                    db.tblImFollowing.GetFollowers(myc, 0, d1, null, out var nextCursor);
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);
            }
        }


        [Test]
        public void GetFollowersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests007");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

                var r = db.tblImFollowing.GetFollowers(myc, 100, d3, null, out var nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                r = db.tblImFollowing.GetFollowers(myc, 100, d1, "", out nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(nextCursor == null);

                r = db.tblImFollowing.GetFollowers(myc, 100, d2, "", out nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);
            }
        }

        [Test]
        public void GetFollowersPagedTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests008");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();

                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var i3 = "freja.valhalla.com";
                var i4 = "heimdal.valhalla.com";
                var i5 = "loke.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i3), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i4), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

                var r = db.tblImFollowing.GetFollowers(myc, 2, d1, null, out var nextCursor);
                Debug.Assert(r.Count == 2, message: "rdr.HasRows is the sinner");
                Debug.Assert(r[0] == i3);
                Debug.Assert(r[1] == i4);
                Debug.Assert(nextCursor == r[1]);

                r = db.tblImFollowing.GetFollowers(myc, 2, d1, nextCursor, out nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i5);
                Debug.Assert(r[1] == i1);
                Debug.Assert(nextCursor == r[1]);

                r = db.tblImFollowing.GetFollowers(myc, 2, d1, nextCursor, out nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);

                nextCursor = r[0];

                r = db.tblImFollowing.GetFollowers(myc, 2, d1, r[0], out nextCursor);
                Debug.Assert(r.Count == 0);
                Debug.Assert(nextCursor == null);
            }
        }


        [Test]
        public void GetAllFollowersPagedTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests009");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();

                var i1 = "odin.valhalla.com";     // 4
                var i2 = "thor.valhalla.com";     // 5
                var i3 = "freja.valhalla.com";    // 1
                var i4 = "heimdal.valhalla.com";  // 2
                var i5 = "loke.valhalla.com";     // 3
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i3), driveId = d3 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i4), driveId = d1 });
                db.tblImFollowing.Insert(myc, new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

                var r = db.tblImFollowing.GetAllFollowers(myc, 2, null, out var nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i3);
                Debug.Assert(r[1] == i4);
                Debug.Assert(nextCursor == r[1]);

                r = db.tblImFollowing.GetAllFollowers(myc, 2, nextCursor, out nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i5, message: "rdr.HasRows is the sinner");
                Debug.Assert(r[1] == i1);
                Debug.Assert(nextCursor == r[1]);

                r = db.tblImFollowing.GetAllFollowers(myc, 2, nextCursor, out nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);

                r = db.tblImFollowing.GetAllFollowers(myc, 2, r[0], out nextCursor);
                Debug.Assert(r.Count == 0);
                Debug.Assert(nextCursor == null);
            }
        }
    }
}