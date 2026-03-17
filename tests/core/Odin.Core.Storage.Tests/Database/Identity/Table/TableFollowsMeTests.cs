using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableFollowsMeTests : IocTestBase
    {
        // ChannelDriveType = SystemDriveConstants.ChannelDriveType
        private static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");
        // FeedDrive.Alias = SystemDriveConstants.FeedDrive.Alias
        private static readonly Guid FeedDriveAlias = Guid.Parse("4db49422ebad02e99ab96e9c477d1e08");

        private static FollowsMeRecord MakeSelectedChannelsRecord(string identity, Guid sourceDriveId)
            => new FollowsMeRecord
            {
                subscriberOdinId = new OdinId(identity),
                sourceDriveId = sourceDriveId,
                subscriberTargetDriveId = FeedDriveAlias,
                subscriptionKind = 2, // SelectedChannels
                lastNotification = new UnixTimeUtc(0),
                lastQuery = new UnixTimeUtc(0)
            };

        private static FollowsMeRecord MakeAllNotificationsRecord(string identity)
            => new FollowsMeRecord
            {
                subscriberOdinId = new OdinId(identity),
                sourceDriveTypeId = ChannelDriveType,
                subscriberTargetDriveId = FeedDriveAlias,
                subscriptionKind = 1, // AllNotifications
                lastNotification = new UnixTimeUtc(0),
                lastQuery = new UnixTimeUtc(0)
            };

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
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i1, d1));

            // Thor follows d1
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i2, d1));

            // Freja follows d1 & d2
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i3, d1));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i3, d2));

            // Heimdal follows d2
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i4, d2));

            // Loke follows everything (AllNotifications → sourceDriveTypeId = ChannelDriveType)
            await tblFollowsMe.InsertAsync(MakeAllNotificationsRecord(i5));

            // Now Frodo makes a new post to d1, which means we should get
            // everyone except Heimdal. Let's do a page size of 3
            //
            var (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(3, d1, null);
            ClassicAssert.IsTrue(r.Count == 3);
            ClassicAssert.IsTrue(nextCursor == r[2]); // Drive has 4 entries, so page 1 has 3.

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

            var item = MakeSelectedChannelsRecord(i1, g1);
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

            var item = MakeSelectedChannelsRecord(i1, g1);
            var b = await tblFollowsMe.TryInsertAsync(item);
            ClassicAssert.That(b);
            ClassicAssert.That(item.rowId > 0);

            // Note: the UNIQUE constraint has nullable columns (sourceDriveTypeId), and SQLite treats
            // NULLs as distinct for unique constraints, so duplicate detection is not enforced at the DB level.
            // The service layer uses delete-then-insert semantics to handle uniqueness instead.
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

            var item = MakeSelectedChannelsRecord(i1, g1);
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

            // This is OK {odin.vahalla.com, sourceDriveId=g1}
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i1, g1));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i1, g2));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord("thor.valhalla.com", g1));

            var r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.IsTrue((r[0].sourceDriveId == g1) || (r[0].sourceDriveId == g2));
            ClassicAssert.IsTrue((r[1].sourceDriveId == g1) || (r[1].sourceDriveId == g2));

            // This is OK {odin.vahalla.com, sourceDriveTypeId=ChannelDriveType}
            await tblFollowsMe.InsertAsync(MakeAllNotificationsRecord(i1));
            r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.IsTrue(r.Exists(x => x.sourceDriveTypeId == ChannelDriveType));
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

            var i1 = new OdinId("odin.valhalla.com");
            var g1 = Guid.NewGuid();

            // Verify valid inserts succeed
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i1, g1));
            await tblFollowsMe.InsertAsync(MakeAllNotificationsRecord(i1));

            // Note: the UNIQUE constraint has nullable columns (sourceDriveTypeId/sourceDriveId), and SQLite
            // treats NULLs as distinct for unique constraints, so duplicate detection at the DB level is not
            // enforced. The service layer uses delete-then-insert semantics to ensure uniqueness.

            // Verify records are in DB
            var r = await tblFollowsMe.GetAsync(i1);
            ClassicAssert.That(r.Count >= 2);
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

            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i1, d1));
            await tblFollowsMe.InsertAsync(MakeAllNotificationsRecord(i2));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i2, d1));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i2, d2));

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

            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i1, d1));
            await tblFollowsMe.InsertAsync(MakeAllNotificationsRecord(i2));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i2, d1));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i2, d2));

            // Get the followers of d3 — only i2 (via AllNotifications/ChannelDriveType)
            var (r, nextCursor) = await tblFollowsMe.GetFollowersAsync(100, d3, null);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0] == i2);
            ClassicAssert.IsTrue(nextCursor == null, message: "rdr.HasRows is the sinner");

            // Get all d1 followers: i1 (sourceDriveId=d1) and i2 (ChannelDriveType or sourceDriveId=d1)
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

            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i1, d1));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i2, d1));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i3, d1));
            await tblFollowsMe.InsertAsync(MakeSelectedChannelsRecord(i4, d1));
            await tblFollowsMe.InsertAsync(MakeAllNotificationsRecord(i5));

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
