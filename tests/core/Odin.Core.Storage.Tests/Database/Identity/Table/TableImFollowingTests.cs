using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableImFollowingTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task ExampleTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

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
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });

            // Thor follows d1
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });

            // Freja follows d1 & d2
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d2 });

            // Heimdal follows d2
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i4), driveId = d2 });

            // Loke follows everything
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

            // Now Frodo makes a new post to d1, which means we shouold get
            // everyone except Heimdal. Let's do a page size of 3
            // Get freja, loke, odin back. Still missing Thor
            var (r, nextCursor) = await tblImFollowing.GetFollowersAsync(3, d1, null);
            Debug.Assert(r.Count == 3, message: "rdr.HasRows is the sinner");
            Debug.Assert(nextCursor == r[2]);

            // Get the second page. Always use the last result as the cursor
            (r, nextCursor) = await tblImFollowing.GetFollowersAsync(3, d1, nextCursor);
            Debug.Assert(r.Count == 1);  // We know this is the last page because 1 < 3
                                         // but if we call again anyway, we get 0 back.
            Debug.Assert(nextCursor == null);


            // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
            //
            (r, nextCursor) = await tblImFollowing.GetFollowersAsync(3, d2, null);
            Debug.Assert(r.Count == 3);
            Debug.Assert(nextCursor == null);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task InsertValidFollowerTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveId}
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g2 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId("thor.valhalla.com"), driveId = g1 });

            var r = await tblImFollowing.GetAsync(new OdinId(i1));
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveId, g2) == 0));
            Debug.Assert((ByteArrayUtil.muidcmp(r[1].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, g2) == 0));

            // This is OK {odin.vahalla.com, {000000}}
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });
            r = await tblImFollowing.GetAsync(new OdinId(i1));
            Debug.Assert((ByteArrayUtil.muidcmp(r[0].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveId, Guid.Empty) == 0));

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task InsertInvalidFollowerTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var i1 = "odin.valhalla.com";
            var g1 = Guid.NewGuid();

            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });

            bool ok = false;
            try
            {
                // Can't insert duplicate
                await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = g1 });
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
                await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = Guid.Empty });
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task DeleteFollowerTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

            await tblImFollowing.DeleteByIdentityAsync(new OdinId(i2));

            var r = await tblImFollowing.GetAsync(new OdinId(i1));
            Debug.Assert(r.Count == 1);
            r = await tblImFollowing.GetAsync(new OdinId(i2));
            Debug.Assert(r.Count == 0);
            await tblImFollowing.DeleteByIdentityAsync(new OdinId(i1));
            r = await tblImFollowing.GetAsync(new OdinId(i2));
            Debug.Assert(r.Count == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task DeleteFollowerDriveTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

            await tblImFollowing.DeleteAsync(new OdinId(i1), d2);
            var r = await tblImFollowing.GetAsync(new OdinId(i1));
            Debug.Assert(r.Count == 1);

            await tblImFollowing.DeleteAsync(new OdinId(i1), d1);
            r = await tblImFollowing.GetAsync(new OdinId(i1));
            Debug.Assert(r.Count == 0);

            await tblImFollowing.DeleteAsync(new OdinId(i2), d1);
            r = await tblImFollowing.GetAsync(new OdinId(i2));
            Debug.Assert(r.Count == 2);

            await tblImFollowing.DeleteAsync(new OdinId(i2), d2);
            r = await tblImFollowing.GetAsync(new OdinId(i2));
            Debug.Assert(r.Count == 1);

            await tblImFollowing.DeleteAsync(new OdinId(i2), Guid.Empty);
            r = await tblImFollowing.GetAsync(new OdinId(i2));
            Debug.Assert(r.Count == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task GetFollowersInvalidTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var d1 = Guid.NewGuid();

            bool ok = false;
            try
            {
                await tblImFollowing.GetFollowersAsync(0, d1, null);
            }
            catch
            {
                ok = true;
            }
            Debug.Assert(ok);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task GetFollowersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = Guid.Empty });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });

            var (r, nextCursor) = await tblImFollowing.GetFollowersAsync(100, d3, null);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

            (r, nextCursor) = await tblImFollowing.GetFollowersAsync(100, d1, "");
            Debug.Assert(r.Count == 2);
            Debug.Assert(nextCursor == null);

            (r, nextCursor) = await tblImFollowing.GetFollowersAsync(100, d2, "");
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task GetFollowersPagedTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var i3 = "freja.valhalla.com";
            var i4 = "heimdal.valhalla.com";
            var i5 = "loke.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i4), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

            var (r, nextCursor) = await tblImFollowing.GetFollowersAsync(2, d1, null);
            Debug.Assert(r.Count == 2, message: "rdr.HasRows is the sinner");
            Debug.Assert(r[0] == i3);
            Debug.Assert(r[1] == i4);
            Debug.Assert(nextCursor == r[1]);

            (r, nextCursor) = await tblImFollowing.GetFollowersAsync(2, d1, nextCursor);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i5);
            Debug.Assert(r[1] == i1);
            Debug.Assert(nextCursor == r[1]);

            (r, nextCursor) = await tblImFollowing.GetFollowersAsync(2, d1, nextCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null);

            nextCursor = r[0];

            (r, nextCursor) = await tblImFollowing.GetFollowersAsync(2, d1, r[0]);
            Debug.Assert(r.Count == 0);
            Debug.Assert(nextCursor == null);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        [TestCase(DatabaseType.Postgres)]
        public async Task GetAllFollowersPagedTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblImFollowing = scope.Resolve<TableImFollowing>();

            var i1 = "odin.valhalla.com";     // 4
            var i2 = "thor.valhalla.com";     // 5
            var i3 = "freja.valhalla.com";    // 1
            var i4 = "heimdal.valhalla.com";  // 2
            var i5 = "loke.valhalla.com";     // 3
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i1), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i2), driveId = d2 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i3), driveId = d3 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i4), driveId = d1 });
            await tblImFollowing.InsertAsync(new ImFollowingRecord() { identity = new OdinId(i5), driveId = Guid.Empty });

            var (r, nextCursor) = await tblImFollowing.GetAllFollowersAsync(2, null);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i3);
            Debug.Assert(r[1] == i4);
            Debug.Assert(nextCursor == r[1]);

            (r, nextCursor) = await tblImFollowing.GetAllFollowersAsync(2, nextCursor);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0] == i5, message: "rdr.HasRows is the sinner");
            Debug.Assert(r[1] == i1);
            Debug.Assert(nextCursor == r[1]);

            (r, nextCursor) = await tblImFollowing.GetAllFollowersAsync(2, nextCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0] == i2);
            Debug.Assert(nextCursor == null);

            (r, nextCursor) = await tblImFollowing.GetAllFollowersAsync(2, r[0]);
            Debug.Assert(r.Count == 0);
            Debug.Assert(nextCursor == null);

        }
    }
}
