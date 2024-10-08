﻿using System;
using System.Diagnostics;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableFollowsMeTests
    {
        [Test]
        public void ExampleTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
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
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = d1 });

                // Thor follows d1
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d1 });

                // Freja follows d1 & d2
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i3, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i3, driveId = d2 });

                // Heimdal follows d2
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i4, driveId = d2 });

                // Loke follows everything
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i5, driveId = Guid.Empty });

                // Now Frodo makes a new post to d1, which means we shouold get
                // everyone except Heimdal. Let's do a page size of 3
                //
                var r = db.tblFollowsMe.GetFollowers(myc, 3, d1, null, out var nextCursor);
                Debug.Assert(r.Count == 3);
                Debug.Assert(nextCursor == r[2]); // Drive has 3 entires, so we got them all here.

                // Get the second page. Always use the last result as the cursor
                r = db.tblFollowsMe.GetFollowers(myc, 3, d1, nextCursor, out nextCursor);
                Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                             // but if we call again anyway, we get 0 back.
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");


                // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
                // So first page is all the data, and there is no more data
                r = db.tblFollowsMe.GetFollowers(myc, 3, d2, null, out nextCursor);
                Debug.Assert(r.Count == 3);
                Debug.Assert(nextCursor == null);
            }
        }


        [Test]
        public void InsertValidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();
                var g2 = Guid.NewGuid();

                // This is OK {odin.vahalla.com, driveId}
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = g1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = g2 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "thor.valhalla.com", driveId = g1 });

                var r = db.tblFollowsMe.Get(myc, i1);
                Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveId, g2) == 0));
                Debug.Assert((ByteArrayUtil.muidcmp(r[1].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, g2) == 0));

                // This is OK {odin.vahalla.com, {000000}}
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });
                r = db.tblFollowsMe.Get(myc, i1);
                Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveId, Guid.Empty) == 0));

                // Test non ASCII
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "ødin.valhalla.com", driveId = g1 });
                r = db.tblFollowsMe.Get(myc, "ødin.valhalla.com");
                Debug.Assert(ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0);
            }
        }


        [Test]
        public void InsertInvalidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();

                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = g1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });

                bool ok = false;
                try
                {
                    // Can't insert duplicate
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = g1 });
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
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = null, driveId = Guid.NewGuid() });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);

                ok = false;
                try
                {
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "", driveId = Guid.NewGuid() });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);


                ok = false;
                try
                {
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "", driveId = Guid.Empty });
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
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);
            }
        }


        [Test]
        [Ignore("Later")]
        public void DeleteInvalidTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                int i = db.tblFollowsMe.DeleteByIdentity(myc, null);
                Debug.Assert(i == 0);

                i = db.tblFollowsMe.DeleteByIdentity(myc, "");
                Debug.Assert(i == 0);
            }
        }


        [Test]
        public void DeleteTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d2 });

                db.tblFollowsMe.DeleteByIdentity(myc, i2);

                var r = db.tblFollowsMe.Get(myc, i1);
                Debug.Assert(r.Count == 1);
                r = db.tblFollowsMe.Get(myc, i2);
                Debug.Assert(r.Count == 0);
                db.tblFollowsMe.DeleteByIdentity(myc, i1);
                r = db.tblFollowsMe.Get(myc, i2);
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public void DeleteDriveTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });

                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d2 });

                db.tblFollowsMe.Delete(myc, i1, d2);
                var r = db.tblFollowsMe.Get(myc, i1);
                Debug.Assert(r.Count == 1);

                db.tblFollowsMe.Delete(myc, i1, d1);
                r = db.tblFollowsMe.Get(myc, i1);
                Debug.Assert(r.Count == 0);

                db.tblFollowsMe.Delete(myc, i2, d1);
                r = db.tblFollowsMe.Get(myc, i2);
                Debug.Assert(r.Count == 2);

                db.tblFollowsMe.Delete(myc, i2, d2);
                r = db.tblFollowsMe.Get(myc, i2);
                Debug.Assert(r.Count == 1);

                db.tblFollowsMe.Delete(myc, i2, Guid.Empty);
                r = db.tblFollowsMe.Get(myc, i2);
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public void GetFollowersInvalidTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var d1 = Guid.NewGuid();

                bool ok = false;
                try
                {
                    db.tblFollowsMe.GetFollowers(myc, 0, d1, null, out var nc);
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d2 });

                // Get the all drive (only)
                var r = db.tblFollowsMe.GetFollowers(myc, 100, d3, null, out var nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                // Get all d1 (and empty) drive. 
                r = db.tblFollowsMe.GetFollowers(myc, 100, d1, "", out nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(nextCursor == null);

                r = db.tblFollowsMe.GetFollowers(myc, 100, d2, "", out nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);
            }
        }


        [Test]
        public void GetFollowersPagedTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var i3 = "freja.valhalla.com";
                var i4 = "heimdal.valhalla.com";
                var i5 = "loke.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i1, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i2, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i3, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i4, driveId = d1 });
                db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = i5, driveId = Guid.Empty });

                var r = db.tblFollowsMe.GetFollowers(myc, 2, d1, null, out var nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i3);
                Debug.Assert(r[1] == i4);
                Debug.Assert(nextCursor == r[1]);

                r = db.tblFollowsMe.GetFollowers(myc, 2, d1, r[1], out nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i5);
                Debug.Assert(r[1] == i1);
                Debug.Assert(nextCursor == r[1]);

                r = db.tblFollowsMe.GetFollowers(myc, 2, d1, r[1], out nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                r = db.tblFollowsMe.GetFollowers(myc, 2, d1, r[0], out nextCursor);
                Debug.Assert(r.Count == 0);
                Debug.Assert(nextCursor == null);
            }
        }
    }
}