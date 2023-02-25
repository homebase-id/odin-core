﻿using System;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

namespace IdentityDatabaseTests
{
    public class TableImFollowingTests
    {
        [Test]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-example-01.db");
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
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = d1 });

            // Thor follows d1
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d1 });

            // Freja follows d1 & d2
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i3, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i3, driveid = d2 });

            // Heimdal follows d2
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i4, driveid = d2 });

            // Loke follows everything
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i5, driveid = Guid.Empty});

            // Now Frodo makes a new post to d1, which means we shouold get
            // everyone except Heimdal. Let's do a page size of 3
            //
            var r = db.tblImFollowing.GetFollowers(3, d1, null, out var nextCursor);
            Debug.Assert(r.Count == 3);
            Debug.Assert(nextCursor == r[2]);

            // Get the second page. Always use the last result as the cursor
            r = db.tblImFollowing.GetFollowers(3, d1, nextCursor, out nextCursor);
            Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                         // but if we call again anyway, we get 0 back.
            Debug.Assert(nextCursor == null);


            // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
            //
            r = db.tblImFollowing.GetFollowers(3, d2, null, out nextCursor);
            Debug.Assert(r.Count == 3);
            Debug.Assert(nextCursor == r[2]);

            r = db.tblImFollowing.GetFollowers(3, d2, nextCursor, out nextCursor);
            Debug.Assert(r.Count == 0);
            Debug.Assert(nextCursor == null);
        }


        [Test]
        public void InsertValidFollowerTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-insert-01.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveid}
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = g1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = g2 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = "thor.valhalla.com", driveid = g1 });

            var r = db.tblImFollowing.Get(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveid, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveid, g2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[1].driveid, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveid, g2) == 0));

            // This is OK {odin.vahalla.com, {000000}}
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = Guid.Empty });
            r = db.tblImFollowing.Get(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveid, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveid, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveid, Guid.Empty) == 0));

            // Test non ASCII
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = "ødin.valhalla.com", driveid = g1 });
            r = db.tblImFollowing.Get("ødin.valhalla.com");
            Debug.Assert(ByteArrayUtil.muidcmp(r[0].driveid, g1) == 0);
        }


        [Test]
        public void InsertInvalidFollowerTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-insert-02.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();

            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = g1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = Guid.Empty });

            bool ok = false;
            try
            {
                // Can't insert duplicate
                db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = g1 });
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
                db.tblImFollowing.Insert(new ImFollowingItem() { identity = null, driveid = Guid.NewGuid() });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            ok = false;
            try
            {
                db.tblImFollowing.Insert(new ImFollowingItem() { identity = "", driveid = Guid.NewGuid() });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);


            ok = false;
            try
            {
                db.tblImFollowing.Insert(new ImFollowingItem() { identity = "", driveid = Guid.Empty });
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
                db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = Guid.Empty });
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
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-delete-01.db");
            db.CreateDatabase();

            bool ok = false;
            try
            {
                db.tblImFollowing.DeleteFollower(null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

            var hi = new byte[3];
            try
            {
                db.tblImFollowing.DeleteFollower("");
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
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-delete-02.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = Guid.Empty });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d2 });

            db.tblImFollowing.DeleteFollower(i2);

            var r = db.tblImFollowing.Get(i1);
            Debug.Assert(r.Count == 1);
            r = db.tblImFollowing.Get(i2);
            Debug.Assert(r.Count == 0);
            db.tblImFollowing.DeleteFollower(i1);
            r = db.tblImFollowing.Get(i2);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void DeleteFollowerDriveTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-delete-03.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = Guid.Empty });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d2 });

            db.tblImFollowing.Delete(i1, d2);
            var r = db.tblImFollowing.Get(i1);
            Debug.Assert(r.Count == 1);

            db.tblImFollowing.Delete(i1, d1);
            r = db.tblImFollowing.Get(i1);
            Debug.Assert(r.Count == 0);

            db.tblImFollowing.Delete(i2, d1);
            r = db.tblImFollowing.Get(i2);
            Debug.Assert(r.Count == 2);

            db.tblImFollowing.Delete(i2, d2);
            r = db.tblImFollowing.Get(i2);
            Debug.Assert(r.Count == 1);

            db.tblImFollowing.Delete(i2, Guid.Empty);
            r = db.tblImFollowing.Get(i2);
            Debug.Assert(r.Count == 0);
        }


        [Test]
        public void GetFollowersInvalidTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-delete-04.db");
            db.CreateDatabase();

            var d1 = Guid.NewGuid();

            bool ok = false;
            try
            {
                db.tblImFollowing.GetFollowers(0, d1, null, out var nextCursor);
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
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-delete-05.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = Guid.Empty });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d2 });

            var r = db.tblImFollowing.GetFollowers(100, d3, null, out var nextCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null);

            r = db.tblImFollowing.GetFollowers(100, d1, "", out nextCursor);
            Debug.Assert(r.Count == 2);
            Debug.Assert(nextCursor == null);

            r = db.tblImFollowing.GetFollowers(100, d2, "", out nextCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null);
        }

        [Test]
        public void GetFollowersPagedTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-delete-06.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var i3 = "freja.valhalla.com";
            var i4 = "heimdal.valhalla.com";
            var i5 = "loke.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i3, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i4, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i5, driveid = Guid.Empty });

            var r = db.tblImFollowing.GetFollowers(2, d1, null, out var nextCursor);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i3);
            Debug.Assert(r[1] == i4);
            Debug.Assert(nextCursor == r[1]);

            r = db.tblImFollowing.GetFollowers(2, d1, nextCursor, out nextCursor);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i5);
            Debug.Assert(r[1] == i1);
            Debug.Assert(nextCursor == r[1]);

            r = db.tblImFollowing.GetFollowers(2, d1, nextCursor, out nextCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null);

            nextCursor = r[0];

            r = db.tblImFollowing.GetFollowers(2, d1, r[0], out nextCursor);
            Debug.Assert(r.Count == 0);
            Debug.Assert(nextCursor == null);
        }


        [Test]
        public void GetAllFollowersPagedTest()
        {
            using var db = new IdentityDatabase("URI=file:.\\imfollowing-all-07.db");
            db.CreateDatabase();

            var i1 = "odin.valhalla.com";     // 4
            var i2 = "thor.valhalla.com";     // 5
            var i3 = "freja.valhalla.com";    // 1
            var i4 = "heimdal.valhalla.com";  // 2
            var i5 = "loke.valhalla.com";     // 3
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i1, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i2, driveid = d2 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i3, driveid = d3 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i4, driveid = d1 });
            db.tblImFollowing.Insert(new ImFollowingItem() { identity = i5, driveid = Guid.Empty });

            var r = db.tblImFollowing.GetAllFollowers(2, null, out var nextCursor);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i3);
            Debug.Assert(r[1] == i4);
            Debug.Assert(nextCursor == r[1]);

            r = db.tblImFollowing.GetAllFollowers(2, nextCursor, out nextCursor);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i5);
            Debug.Assert(r[1] == i1);
            Debug.Assert(nextCursor == r[1]);

            r = db.tblImFollowing.GetAllFollowers(2, nextCursor, out nextCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null);

            r = db.tblImFollowing.GetAllFollowers(2, r[0], out nextCursor);
            Debug.Assert(r.Count == 0);
            Debug.Assert(nextCursor == null);
        }
    }
}