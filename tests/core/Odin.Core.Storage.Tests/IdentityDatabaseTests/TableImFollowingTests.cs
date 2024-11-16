# if false
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableImFollowingTests
    {
        [Test]
        public async Task ExampleTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests001");

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
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });

                // Thor follows d1
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });

                // Freja follows d1 & d2
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d2 });

                // Heimdal follows d2
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i4), driveId = d2 });

                // Loke follows everything
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

                // Now Frodo makes a new post to d1, which means we shouold get
                // everyone except Heimdal. Let's do a page size of 3
                // Get freja, loke, odin back. Still missing Thor
                var (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(3, d1, null);
                Debug.Assert(r.Count == 3, message: "rdr.HasRows is the sinner");
                Debug.Assert(nextCursor == r[2]);

                // Get the second page. Always use the last result as the cursor
                (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(3, d1, nextCursor);
                Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                             // but if we call again anyway, we get 0 back.
                Debug.Assert(nextCursor == null);


                // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
                //
                (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(3, d2, null);
                Debug.Assert(r.Count == 3);
                Debug.Assert(nextCursor == null);
            }
        }

        [Test]
        public async Task InsertValidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();
                var g2 = Guid.NewGuid();

                // This is OK {odin.vahalla.com, driveId}
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g2 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId("thor.valhalla.com"), driveId = g1 });

                var r = await db.tblImFollowing.GetAsync(new OdinId(i1));
                Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveId, g2) == 0));
                Debug.Assert((ByteArrayUtil.muidcmp(r[1].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, g2) == 0));

                // This is OK {odin.vahalla.com, {000000}}
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });
                r = await db.tblImFollowing.GetAsync(new OdinId(i1));
                Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveId, Guid.Empty) == 0));
            }
        }


        [Test]
        public async Task InsertInvalidFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();

                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });

                bool ok = false;
                try
                {
                    // Can't insert duplicate
                    await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
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
                    await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });
                }
                catch
                {
                    ok = true;
                }
                Debug.Assert(ok);
            }
        }


        [Test]
        public async Task DeleteFollowerTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

                await db.tblImFollowing.DeleteByIdentityAsync(new OdinId(i2));

                var r = await db.tblImFollowing.GetAsync(new OdinId(i1));
                Debug.Assert(r.Count == 1);
                r = await db.tblImFollowing.GetAsync(new OdinId(i2));
                Debug.Assert(r.Count == 0);
                await db.tblImFollowing.DeleteByIdentityAsync(new OdinId(i1));
                r = await db.tblImFollowing.GetAsync(new OdinId(i2));
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public async Task DeleteFollowerDriveTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();

                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

                await db.tblImFollowing.DeleteAsync(new OdinId(i1), d2);
                var r = await db.tblImFollowing.GetAsync(new OdinId(i1));
                Debug.Assert(r.Count == 1);

                await db.tblImFollowing.DeleteAsync(new OdinId(i1), d1);
                r = await db.tblImFollowing.GetAsync(new OdinId(i1));
                Debug.Assert(r.Count == 0);

                await db.tblImFollowing.DeleteAsync(new OdinId(i2), d1);
                r = await db.tblImFollowing.GetAsync(new OdinId(i2));
                Debug.Assert(r.Count == 2);

                await db.tblImFollowing.DeleteAsync(new OdinId(i2), d2);
                r = await db.tblImFollowing.GetAsync(new OdinId(i2));
                Debug.Assert(r.Count == 1);

                await db.tblImFollowing.DeleteAsync(new OdinId(i2), Guid.Empty);
                r = await db.tblImFollowing.GetAsync(new OdinId(i2));
                Debug.Assert(r.Count == 0);
            }
        }


        [Test]
        public async Task GetFollowersInvalidTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var d1 = Guid.NewGuid();

                bool ok = false;
                try
                {
                    await db.tblImFollowing.GetFollowersAsync(0, d1, null);
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests007");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var i1 = "odin.valhalla.com";
                var i2 = "thor.valhalla.com";
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

                var (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(100, d3, null);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

                (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(100, d1, "");
                Debug.Assert(r.Count == 2);
                Debug.Assert(nextCursor == null);

                (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(100, d2, "");
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);
            }
        }

        [Test]
        public async Task GetFollowersPagedTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests008");

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

                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i4), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

                var (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(2, d1, null);
                Debug.Assert(r.Count == 2, message: "rdr.HasRows is the sinner");
                Debug.Assert(r[0] == i3);
                Debug.Assert(r[1] == i4);
                Debug.Assert(nextCursor == r[1]);

                (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(2, d1, nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i5);
                Debug.Assert(r[1] == i1);
                Debug.Assert(nextCursor == r[1]);

                (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(2, d1, nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);

                nextCursor = r[0];

                (r, nextCursor) = await db.tblImFollowing.GetFollowersAsync(2, d1, r[0]);
                Debug.Assert(r.Count == 0);
                Debug.Assert(nextCursor == null);
            }
        }


        [Test]
        public async Task GetAllFollowersPagedTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableImFollowingTests009");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();

                var i1 = "odin.valhalla.com";     // 4
                var i2 = "thor.valhalla.com";     // 5
                var i3 = "freja.valhalla.com";    // 1
                var i4 = "heimdal.valhalla.com";  // 2
                var i5 = "loke.valhalla.com";     // 3
                var d1 = Guid.NewGuid();
                var d2 = Guid.NewGuid();
                var d3 = Guid.NewGuid();

                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d3 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i4), driveId = d1 });
                await db.tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

                var (r, nextCursor) = await db.tblImFollowing.GetAllFollowersAsync(2, null);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i3);
                Debug.Assert(r[1] == i4);
                Debug.Assert(nextCursor == r[1]);

                (r, nextCursor) = await db.tblImFollowing.GetAllFollowersAsync(2, nextCursor);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0] == i5, message: "rdr.HasRows is the sinner");
                Debug.Assert(r[1] == i1);
                Debug.Assert(nextCursor == r[1]);

                (r, nextCursor) = await db.tblImFollowing.GetAllFollowersAsync(2, nextCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0] == i2);
                Debug.Assert(nextCursor == null);

                (r, nextCursor) = await db.tblImFollowing.GetAllFollowersAsync(2, r[0]);
                Debug.Assert(r.Count == 0);
                Debug.Assert(nextCursor == null);
            }
        }
    }
}
#endif
