using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableFollowsMeTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task ExampleTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

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
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });

            // Thor follows d1
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });

            // Freja follows d1 & d2
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i3, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i3, driveId = d2 });

            // Heimdal follows d2
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i4, driveId = d2 });

            // Loke follows everything
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i5, driveId = Guid.Empty });

            // Now Frodo makes a new post to d1, which means we shouold get
            // everyone except Heimdal. Let's do a page size of 3
            //
            var (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(3, d1, null);
            ClassicAssert.IsTrue(r.Count == 3);
            ClassicAssert.IsTrue(nextCursor == r[2]); // Drive has 3 entires, so we got them all here.

            // Get the second page. Always use the last result as the cursor
            (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(3, d1, nextCursor);
            ClassicAssert.IsTrue(r.Count == 1);  // We know this is the last page because 1 < 3
                                         // but if we call again anyway, we get 0 back.
            ClassicAssert.IsTrue(nextCursor == null, message: "rdr.HasRows is the sinner");


            // Now Frodo does a post to d2 which means Freja, Heimdal, Loke gets it
            // So first page is all the data, and there is no more data
            (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(3, d2, null);
            ClassicAssert.IsTrue(r.Count == 3);
            ClassicAssert.IsTrue(nextCursor == null);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task InsertRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = new OdinId("odin.valhalla.com");
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveId}
            var item = new FollowsMeRecord() { identity = i1, driveId = g1 };
            var n = await tblFollowsMe.InsertAsync(item);
            ClassicAssert.That(n == 1);
            ClassicAssert.That(item.rowId > 0);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task TryInsertRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = new OdinId("odin.valhalla.com");
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            var item = new FollowsMeRecord() { identity = i1, driveId = g1 };
            var b = await tblFollowsMe.TryInsertAsync(item);
            ClassicAssert.That(b);
            ClassicAssert.That(item.rowId > 0);

            var n = item.rowId;

            // Now insert a duplicate
            b = await tblFollowsMe.TryInsertAsync(item);
            ClassicAssert.That(b == false);
            ClassicAssert.That(item.rowId == n); // It shouldn't have changed
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpsertRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = new OdinId("odin.valhalla.com");
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveId}
            var item = new FollowsMeRecord() { identity = i1, driveId = g1 };
            var n = await tblFollowsMe.UpsertAsync(item);
            ClassicAssert.That(n == 1);
            ClassicAssert.That(item.rowId > 0);

            item.rowId = -1;
            n = await tblFollowsMe.UpsertAsync(item);
            ClassicAssert.That(n == 1);
            ClassicAssert.That(item.rowId > 0);
        }



        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertValidFollowerTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = new OdinId("odin.valhalla.com");
            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveId}
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g2 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = "thor.valhalla.com", driveId = g1 });

            var r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r[0].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[0].driveId, g2) == 0));
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r[1].driveId, g1) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, g2) == 0));

            // This is OK {odin.vahalla.com, {000000}}
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });
            r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r[0].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[1].driveId, Guid.Empty) == 0) || (ByteArrayUtil.muidcmp(r[2].driveId, Guid.Empty) == 0));
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertInvalidFollowerTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

                var i1 = "odin.valhalla.com";
                var g1 = Guid.NewGuid();

                await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g1 });
                await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });

                bool ok = false;
                try
                {
                    // Can't insert duplicate
                    await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = g1 });
                }
                catch
                {
                    ok = true;
                }
                ClassicAssert.IsTrue(ok);


                ok = false;
                try
                {
                    // 
                    await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = null, driveId = Guid.NewGuid() });
                }
                catch
                {
                    ok = true;
                }
                ClassicAssert.IsTrue(ok);

                ok = false;
                try
                {
                    await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = "", driveId = Guid.NewGuid() });
                }
                catch
                {
                    ok = true;
                }
                ClassicAssert.IsTrue(ok);


                ok = false;
                try
                {
                    await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = "", driveId = Guid.Empty });
                }
                catch
                {
                    ok = true;
                }
                ClassicAssert.IsTrue(ok);


                ok = false;
                try
                {
                    // Can't insert duplicate, this is supposed to fail.
                    await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = Guid.Empty });
                }
                catch
                {
                    ok = true;
                }
                ClassicAssert.IsTrue(ok);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = new OdinId("odin.valhalla.com");
            var i2 = new OdinId("thor.valhalla.com");
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d2 });

            await tblFollowsMe.DeleteByIdentityAsync(i2);

            var r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.IsTrue(r.Count == 1);
            r = await tblFollowsMe.GetAsync(i2);
            ClassicAssert.IsTrue(r.Count == 0);
            await tblFollowsMe.DeleteByIdentityAsync(i1);
            r = await tblFollowsMe.GetAsync(i2);
            ClassicAssert.IsTrue(r.Count == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteDriveTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = new OdinId("odin.valhalla.com");
            var i2 = new OdinId("thor.valhalla.com");
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();

            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });

            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d2 });

            await tblFollowsMe.DeleteAsync(i1, d2);
            var r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.IsTrue(r.Count == 1);

            await tblFollowsMe.DeleteAsync(i1, d1);
            r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.IsTrue(r.Count == 0);

            await tblFollowsMe.DeleteAsync(i2, d1);
            r = await tblFollowsMe.GetAsync(i2);
            ClassicAssert.IsTrue(r.Count == 2);

            await tblFollowsMe.DeleteAsync(i2, d2);
            r = await tblFollowsMe.GetAsync(i2);
            ClassicAssert.IsTrue(r.Count == 1);

            await tblFollowsMe.DeleteAsync(i2, Guid.Empty);
            r = await tblFollowsMe.GetAsync(i2);
            ClassicAssert.IsTrue(r.Count == 0);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetFollowersInvalidTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var d1 = Guid.NewGuid();

            bool ok = false;
            try
            {
                await tblFollowsMe.GetFollowersAsync(0, d1, null);
            }
            catch
            {
                ok = true;
            }
            ClassicAssert.IsTrue(ok);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetFollowersTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = Guid.Empty });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d2 });

            // Get the all drive (only)
            var (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(100, d3, null);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0] == i2);
            ClassicAssert.IsTrue(nextCursor == null, message: "rdr.HasRows is the sinner");

            // Get all d1 (and empty) drive.
            (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(100, d1, "");
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(nextCursor == null);

            (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(100, d2, "");
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0] == i2);
            ClassicAssert.IsTrue(nextCursor == null);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetFollowersPagedTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblFollowsMe = scope.Resolve<TableFollowsMe>();

            var i1 = "odin.valhalla.com";
            var i2 = "thor.valhalla.com";
            var i3 = "freja.valhalla.com";
            var i4 = "heimdal.valhalla.com";
            var i5 = "loke.valhalla.com";
            var d1 = Guid.NewGuid();
            var d2 = Guid.NewGuid();
            var d3 = Guid.NewGuid();

            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i1, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i2, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i3, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i4, driveId = d1 });
            await tblFollowsMe.InsertAsync(new FollowsMeRecord() { identity = i5, driveId = Guid.Empty });

            var (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(2, d1, null);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(r[0] == i3);
            ClassicAssert.IsTrue(r[1] == i4);
            ClassicAssert.IsTrue(nextCursor == r[1]);

            (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(2, d1, r[1]);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(r[0] == i5);
            ClassicAssert.IsTrue(r[1] == i1);
            ClassicAssert.IsTrue(nextCursor == r[1]);

            (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(2, d1, r[1]);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0] == i2);
            ClassicAssert.IsTrue(nextCursor == null, message: "rdr.HasRows is the sinner");

            (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(2, d1, r[0]);
            ClassicAssert.IsTrue(r.Count == 0);
            ClassicAssert.IsTrue(nextCursor == null);

        }
    }
}
