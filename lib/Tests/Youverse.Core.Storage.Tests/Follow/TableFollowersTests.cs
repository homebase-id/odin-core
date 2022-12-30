using System;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace IndexerTests.KeyValue
{
    public class TableFollowersTests
    {
        [Test]
        public void ExampleTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\follower-example-01.db");
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
            db.tblFollow.InsertFollower(i1, d1);

            // Thor follows d1
            db.tblFollow.InsertFollower(i2, d1);

            // Freja follows d1 & d2
            db.tblFollow.InsertFollower(i3, d1);
            db.tblFollow.InsertFollower(i3, d2);

            // Heimdal follows d2
            db.tblFollow.InsertFollower(i4, d2);

            // Loke follows everything
            db.tblFollow.InsertFollower(i5, null);


            // Now Frodo makes a new post to d1, which means we shouold get
            // everyone except Heimdal. Let's do a page size of 3
            //
            var r = db.tblFollow.GetFollowers(3, d1, null);
            Debug.Assert(r.Count == 3);

            // Get the second page. Always use the last result as the cursor
            r = db.tblFollow.GetFollowers(3, d1, r[2]);
            Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                         // but if we call again anyway, we get 0 back.


            // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
            //
            r = db.tblFollow.GetFollowers(3, d2, null);
            Debug.Assert(r.Count == 3);
            r = db.tblFollow.GetFollowers(3, d2, r[2]);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void InsertValidFollowerTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\followers-insert-01.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveid}
            db.tblFollow.InsertFollower(i1, g1);
            db.tblFollow.InsertFollower(i1, g2);
            db.tblFollow.InsertFollower("thor.valhalla.com", g1);

            var r = db.tblFollow.Get(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0], g1) == 0) || (ByteArrayUtil.muidcmp(r[0], g2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[1], g1) == 0) || (ByteArrayUtil.muidcmp(r[1], g2) == 0));

            // This is OK {odin.vahalla.com, {000000}}
            db.tblFollow.InsertFollower(i1, null);
            r = db.tblFollow.Get(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0], Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1], Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2], Guid.Empty) == 0));

            // Test non ASCII
            db.tblFollow.InsertFollower("ødin.valhalla.com", g1);
            r = db.tblFollow.Get("ødin.valhalla.com");
            Debug.Assert(ByteArrayUtil.muidcmp(r[0], g1) == 0);
        }


        [Test]
        public void InsertInvalidFollowerTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\followers-insert-02.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();

            db.tblFollow.InsertFollower(i1, g1);
            db.tblFollow.InsertFollower(i1, null);

            bool ok = false;
            try
            {
                // Can't insert duplicate
                db.tblFollow.InsertFollower(i1, g1);
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
                db.tblFollow.InsertFollower(null, Guid.NewGuid());
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            try
            {
                db.tblFollow.InsertFollower("", Guid.NewGuid());
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);


            ok = false;
            try
            {
                db.tblFollow.InsertFollower("", null);
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
                db.tblFollow.InsertFollower(i1, null);
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
            var db = new KeyValueDatabase("URI=file:.\\follower-delete-01.db");
            db.CreateDatabase();

            bool ok = false;
            try
            {
                db.tblFollow.DeleteFollower(null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            var hi = new byte[3];
            try
            {
                db.tblFollow.DeleteFollower("");
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
            var db = new KeyValueDatabase("URI=file:.\\follower-delete-02.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            db.tblFollow.InsertFollower(i1, d1);
            db.tblFollow.InsertFollower(i2, null);
            db.tblFollow.InsertFollower(i2, d1);
            db.tblFollow.InsertFollower(i2, d2);

            db.tblFollow.DeleteFollower(i2);

            var r = db.tblFollow.Get(i1);
            Debug.Assert(r.Count == 1);
            r = db.tblFollow.Get(i2);
            Debug.Assert(r.Count == 0);
            db.tblFollow.DeleteFollower(i1);
            r = db.tblFollow.Get(i2);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void DeleteFollowerDriveTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\follower-delete-03.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            db.tblFollow.InsertFollower(i1, d1);
            db.tblFollow.InsertFollower(i2, null);

            db.tblFollow.InsertFollower(i2, d1);
            db.tblFollow.InsertFollower(i2, d2);

            db.tblFollow.DeleteFollower(i1, d2);
            var r = db.tblFollow.Get(i1);
            Debug.Assert(r.Count == 1);

            db.tblFollow.DeleteFollower(i1, d1);
            r = db.tblFollow.Get(i1);
            Debug.Assert(r.Count == 0);

            db.tblFollow.DeleteFollower(i2, d1);
            r = db.tblFollow.Get(i2);
            Debug.Assert(r.Count == 2);

            db.tblFollow.DeleteFollower(i2, d2);
            r = db.tblFollow.Get(i2);
            Debug.Assert(r.Count == 1);

            db.tblFollow.DeleteFollower(i2, Guid.Empty);
            r = db.tblFollow.Get(i2);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void GetFollowersInvalidTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\follower-delete-04.db");
            db.CreateDatabase();

            var d1 = Guid.NewGuid();

            bool ok = false;
            try
            {
                db.tblFollow.GetFollowers(0, d1, null);
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
            var db = new KeyValueDatabase("URI=file:.\\follower-delete-05.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            db.tblFollow.InsertFollower(i1, d1);
            db.tblFollow.InsertFollower(i2, null);
            db.tblFollow.InsertFollower(i2, d1);
            db.tblFollow.InsertFollower(i2, d2);

            var r = db.tblFollow.GetFollowers(100, d3, null);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);

            r = db.tblFollow.GetFollowers(100, d1, "");
            Debug.Assert(r.Count == 2);

            r = db.tblFollow.GetFollowers(100, d2, "");
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
        }

        [Test]
        public void GetFollowersPagedTest()
        {
            var db = new KeyValueDatabase("URI=file:.\\follower-delete-06.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var i3 = "freja.valhalla.com";
            var i4 = "heimdal.valhalla.com";
            var i5 = "loke.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            db.tblFollow.InsertFollower(i1, d1);
            db.tblFollow.InsertFollower(i2, d1);
            db.tblFollow.InsertFollower(i3, d1);
            db.tblFollow.InsertFollower(i4, d1);
            db.tblFollow.InsertFollower(i5, null);

            var r = db.tblFollow.GetFollowers(2, d1, null);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i3);
            Debug.Assert(r[1] == i4);

            r = db.tblFollow.GetFollowers(2, d1, r[1]);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i5);
            Debug.Assert(r[1] == i1);

            r = db.tblFollow.GetFollowers(2, d1, r[1]);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);

            r = db.tblFollow.GetFollowers(2, d1, r[0]);
            Debug.Assert(r.Count == 0);
        }
    }
}