using System;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace IndexerTests.KeyValue
{
    public class TableFollowsMeTests
    {
        [Test]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\follower-example-01.db");
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
            db.tblFollowsMe.InsertFollower(i1, d1);

            // Thor follows d1
            db.tblFollowsMe.InsertFollower(i2, d1);

            // Freja follows d1 & d2
            db.tblFollowsMe.InsertFollower(i3, d1);
            db.tblFollowsMe.InsertFollower(i3, d2);

            // Heimdal follows d2
            db.tblFollowsMe.InsertFollower(i4, d2);

            // Loke follows everything
            db.tblFollowsMe.InsertFollower(i5, null);


            // Now Frodo makes a new post to d1, which means we shouold get
            // everyone except Heimdal. Let's do a page size of 3
            //
            var r = db.tblFollowsMe.GetFollowers(3, d1, null);
            Debug.Assert(r.Count == 3);

            // Get the second page. Always use the last result as the cursor
            r = db.tblFollowsMe.GetFollowers(3, d1, r[2]);
            Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                         // but if we call again anyway, we get 0 back.


            // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
            //
            r = db.tblFollowsMe.GetFollowers(3, d2, null);
            Debug.Assert(r.Count == 3);
            r = db.tblFollowsMe.GetFollowers(3, d2, r[2]);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void InsertValidFollowerTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\followers-insert-01.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveid}
            db.tblFollowsMe.InsertFollower(i1, g1);
            db.tblFollowsMe.InsertFollower(i1, g2);
            db.tblFollowsMe.InsertFollower("thor.valhalla.com", g1);

            var r = db.tblFollowsMe.Get(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveId, g2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[1].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, g2) == 0));

            // This is OK {odin.vahalla.com, {000000}}
            db.tblFollowsMe.InsertFollower(i1, null);
            r = db.tblFollowsMe.Get(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveId, Guid.Empty) == 0));

            // Test non ASCII
            db.tblFollowsMe.InsertFollower("ødin.valhalla.com", g1);
            r = db.tblFollowsMe.Get("ødin.valhalla.com");
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0);
        }


        [Test]
        public void InsertInvalidFollowerTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\followers-insert-02.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();

            db.tblFollowsMe.InsertFollower(i1, g1);
            db.tblFollowsMe.InsertFollower(i1, null);

            bool ok = false;
            try
            {
                // Can't insert duplicate
                db.tblFollowsMe.InsertFollower(i1, g1);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);


            ok = false;
            try
            {
                // 
                db.tblFollowsMe.InsertFollower(null, Guid.NewGuid());
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            try
            {
                db.tblFollowsMe.InsertFollower("", Guid.NewGuid());
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);


            ok = false;
            try
            {
                db.tblFollowsMe.InsertFollower("", null);
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
                db.tblFollowsMe.InsertFollower(i1, null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }

        [Test]
        public void DeleteFollowerInvalidTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\follower-delete-01.db");
            db.CreateDatabase();

            bool ok = false;
            try
            {
                db.tblFollowsMe.DeleteFollower(null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            var hi = new byte[3];
            try
            {
                db.tblFollowsMe.DeleteFollower("");
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }


        [Test]
        public void DeleteFollowerTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\follower-delete-02.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            db.tblFollowsMe.InsertFollower(i1, d1);
            db.tblFollowsMe.InsertFollower(i2, null);
            db.tblFollowsMe.InsertFollower(i2, d1);
            db.tblFollowsMe.InsertFollower(i2, d2);

            db.tblFollowsMe.DeleteFollower(i2);

            var r = db.tblFollowsMe.Get(i1);
            Debug.Assert(r.Count == 1);
            r = db.tblFollowsMe.Get(i2);
            Debug.Assert(r.Count == 0);
            db.tblFollowsMe.DeleteFollower(i1);
            r = db.tblFollowsMe.Get(i2);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void DeleteFollowerDriveTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\follower-delete-03.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            db.tblFollowsMe.InsertFollower(i1, d1);
            db.tblFollowsMe.InsertFollower(i2, null);

            db.tblFollowsMe.InsertFollower(i2, d1);
            db.tblFollowsMe.InsertFollower(i2, d2);

            db.tblFollowsMe.DeleteFollower(i1, d2);
            var r = db.tblFollowsMe.Get(i1);
            Debug.Assert(r.Count == 1);

            db.tblFollowsMe.DeleteFollower(i1, d1);
            r = db.tblFollowsMe.Get(i1);
            Debug.Assert(r.Count == 0);

            db.tblFollowsMe.DeleteFollower(i2, d1);
            r = db.tblFollowsMe.Get(i2);
            Debug.Assert(r.Count == 2);

            db.tblFollowsMe.DeleteFollower(i2, d2);
            r = db.tblFollowsMe.Get(i2);
            Debug.Assert(r.Count == 1);

            db.tblFollowsMe.DeleteFollower(i2, Guid.Empty);
            r = db.tblFollowsMe.Get(i2);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void GetFollowersInvalidTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\follower-delete-04.db");
            db.CreateDatabase();

            var d1 = Guid.NewGuid();

            bool ok = false;
            try
            {
                db.tblFollowsMe.GetFollowers(0, d1, null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);
        }


        [Test]
        public void GetFollowersTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\follower-delete-05.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            db.tblFollowsMe.InsertFollower(i1, d1);
            db.tblFollowsMe.InsertFollower(i2, null);
            db.tblFollowsMe.InsertFollower(i2, d1);
            db.tblFollowsMe.InsertFollower(i2, d2);

            var r = db.tblFollowsMe.GetFollowers(100, d3, null);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);

            r = db.tblFollowsMe.GetFollowers(100, d1, "");
            Debug.Assert(r.Count == 2);

            r = db.tblFollowsMe.GetFollowers(100, d2, "");
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
        }

        [Test]
        public void GetFollowersPagedTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\follower-delete-06.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var i3 = "freja.valhalla.com";
            var i4 = "heimdal.valhalla.com";
            var i5 = "loke.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            db.tblFollowsMe.InsertFollower(i1, d1);
            db.tblFollowsMe.InsertFollower(i2, d1);
            db.tblFollowsMe.InsertFollower(i3, d1);
            db.tblFollowsMe.InsertFollower(i4, d1);
            db.tblFollowsMe.InsertFollower(i5, null);

            var r = db.tblFollowsMe.GetFollowers(2, d1, null);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i3);
            Debug.Assert(r[1] == i4);

            r = db.tblFollowsMe.GetFollowers(2, d1, r[1]);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i5);
            Debug.Assert(r[1] == i1);

            r = db.tblFollowsMe.GetFollowers(2, d1, r[1]);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);

            r = db.tblFollowsMe.GetFollowers(2, d1, r[0]);
            Debug.Assert(r.Count == 0);
        }
    }
}