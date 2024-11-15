using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableFollowsMeTests
    {
        [Test]
        public async Task ExampleTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });

                // Thor follows d1
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });

                // Freja follows d1 & d2
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i3, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i3, driveId = d2 });

                // Heimdal follows d2
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i4, driveId = d2 });

                // Loke follows everything
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i5, driveId = Guid.Empty });

                // Now Frodo makes a new post to d1, which means we shouold get
                // everyone except Heimdal. Let's do a page size of 3
                //
                var (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(3, d1, null);
                Debug.Assert(r.Count == 3);
                Debug.Assert(nextCursor == r[2]); // Drive has 3 entires, so we got them all here.

                // Get the second page. Always use the last result as the cursor
                (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(3, d1, nextCursor);
                Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                             // but if we call again anyway, we get 0 back.
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");


                // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
                // So first page is all the data, and there is no more data
                (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(3, d2, null);
                Debug.Assert(r.Count == 3);
                Debug.Assert(nextCursor == null);
            }
        }


        [Test]
        public async Task InsertValidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest002");

            await db.CreateDatabaseAsync();
            var i1 = new OdinId("odin.valhalla.com");
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveId}
            await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g1 });
            await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g2 });
            await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = "thor.valhalla.com", driveId = g1 });

            var r = await db.tblFollowsMe.GetAsync(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveId, g2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[1].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, g2) == 0));

            // This is OK {odin.vahalla.com, {000000}}
            await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });
            r = await db.tblFollowsMe.GetAsync(i1);
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveId, Guid.Empty) == 0));
        }


        [Test]
        public async Task InsertInvalidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();

                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });

                bool ok = false;
                try
                {
                    // Can't insert duplicate
                    await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g1 });
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
                    await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = null, driveId = Guid.NewGuid() });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);

                ok = false;
                try
                {
                    await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = "", driveId = Guid.NewGuid() });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);


                ok = false;
                try
                {
                    await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = "", driveId = Guid.Empty });
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
                    await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);
            }
        }


        [Test]
        public async Task DeleteTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = new OdinId("odin.valhalla.com");
                var i2 = new OdinId("thor.valhalla.com");
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d2 });

                await db.tblFollowsMe.DeleteByIdentityAsync(i2);

                var r = await db.tblFollowsMe.GetAsync(i1);
                Debug.Assert(r.Count == 1);
                r = await db.tblFollowsMe.GetAsync(i2);
                Debug.Assert(r.Count == 0);
                await db.tblFollowsMe.DeleteByIdentityAsync(i1);
                r = await db.tblFollowsMe.GetAsync(i2);
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public async Task DeleteDriveTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest006");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = new OdinId("odin.valhalla.com");
                var i2 = new OdinId("thor.valhalla.com");
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });

                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d2 });

                await db.tblFollowsMe.DeleteAsync(i1, d2);
                var r = await db.tblFollowsMe.GetAsync(i1);
                Debug.Assert(r.Count == 1);

                await db.tblFollowsMe.DeleteAsync(i1, d1);
                r = await db.tblFollowsMe.GetAsync(i1);
                Debug.Assert(r.Count == 0);

                await db.tblFollowsMe.DeleteAsync(i2, d1);
                r = await db.tblFollowsMe.GetAsync(i2);
                Debug.Assert(r.Count == 2);

                await db.tblFollowsMe.DeleteAsync(i2, d2);
                r = await db.tblFollowsMe.GetAsync(i2);
                Debug.Assert(r.Count == 1);

                await db.tblFollowsMe.DeleteAsync(i2, Guid.Empty);
                r = await db.tblFollowsMe.GetAsync(i2);
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public async Task GetFollowersInvalidTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest007");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var d1 = Guid.NewGuid();

                bool ok = false;
                try
                {
                    await db.tblFollowsMe.GetFollowersAsync(0, d1, null);
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);
            }
        }


        [Test]
        public async Task GetFollowersTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest008");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d2 });

                // Get the all drive (only)
                var (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(100, d3, null);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                // Get all d1 (and empty) drive. 
                (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(100, d1, "");
                Debug.Assert(r.Count == 2);
                Debug.Assert(nextCursor == null);

                (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(100, d2, "");
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);
            }
        }


        [Test]
        public async Task GetFollowersPagedTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableFollowsMeTest009");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var i3 = "freja.valhalla.com";
                var i4 = "heimdal.valhalla.com";
                var i5 = "loke.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i3, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i4, driveId = d1 });
                await db.tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i5, driveId = Guid.Empty });

                var (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(2, d1, null);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i3);
                Debug.Assert(r[1] == i4);
                Debug.Assert(nextCursor == r[1]);

                (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(2, d1, r[1]);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i5);
                Debug.Assert(r[1] == i1);
                Debug.Assert(nextCursor == r[1]);

                (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(2, d1, r[1]);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                (r, nextCursor) = await db.tblFollowsMe.GetFollowersAsync(2, d1, r[0]);
                Debug.Assert(r.Count == 0);
                Debug.Assert(nextCursor == null);
            }
        }
    }
}