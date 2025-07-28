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
    public class TableConnectionsTests : IocTestBase
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
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            var g3 = Guid.NewGuid();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };
            await tblConnections.UpsertAsync(item1);

            var item2 = new ConnectionsRecord()
            {
                identity = new OdinId("samwise.gamgee.me"),
                displayName = "Sam",
                status = 43,
                accessIsRevoked = 0,
                data = g2.ToByteArray()
            };
            await tblConnections.UpsertAsync(item2);

            var item3 = new ConnectionsRecord()
            {
                identity = new OdinId("gandalf.white.me"),
                displayName = "G",
                status = 44,
                accessIsRevoked = 0,
                data = g3.ToByteArray()
            };
            await tblConnections.UpsertAsync(item3);

            // We have three connections, get the first two in the first page, then the last page of one
            //
            var (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, null);
            ClassicAssert.IsTrue(r.Count == 2);

            (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, outCursor);
            ClassicAssert.IsTrue(r.Count == 1, message: "rdr.HasRows is the sinner");
            ClassicAssert.IsTrue(outCursor == null);


            // Try the filter ones
            (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, 42, null);
            ClassicAssert.IsTrue(r.Count == 1);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task InsertValidConnectionTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveid}
            var item = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo Baggins",
                status = 42,
                accessIsRevoked = 0,
                data = g1.ToByteArray()
            };

            await tblConnections.UpsertAsync(item);

            var r = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            ClassicAssert.IsTrue(r.identity == "frodo.baggins.me");
            ClassicAssert.IsTrue(r.displayName == "Frodo Baggins");
            ClassicAssert.IsTrue(r.status == 42);
            ClassicAssert.IsTrue(r.accessIsRevoked == 0);
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task DeleteValidConnectionTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();

            // This is OK {odin.vahalla.com, driveid}
            var item = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };

            var r = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            ClassicAssert.IsTrue(r == null);
            await tblConnections.UpsertAsync(item);
            r = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            ClassicAssert.IsTrue(r.identity == "frodo.baggins.me");
            ClassicAssert.IsTrue(r.displayName == "");
            ClassicAssert.IsTrue(r.status == 42);
            ClassicAssert.IsTrue(r.accessIsRevoked == 1);
            ClassicAssert.IsTrue((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PagingByCreatedBothTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            var g3 = Guid.NewGuid();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };
            await tblConnections.UpsertAsync(item1);

            var item2 = new ConnectionsRecord()
            {
                identity = new OdinId("samwise.gamgee.me"),
                displayName = "Sam",
                status = 43,
                accessIsRevoked = 0,
                data = g2.ToByteArray()
            };
            await tblConnections.UpsertAsync(item2);

            var item3 = new ConnectionsRecord()
            {
                identity = new OdinId("gandalf.white.me"),
                displayName = "G",
                status = 42,
                accessIsRevoked = 0,
                data = g3.ToByteArray()
            };
            await tblConnections.UpsertAsync(item3);


            // Test the CRUD

            // Get most recent (will be a different order)
            // Results should be reverse of insert
            //

            var (r, cursor) = await tblConnections.PagingByCreatedAsync(2, null);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(r[0].identity == "gandalf.white.me");
            ClassicAssert.IsTrue(r[1].identity == "samwise.gamgee.me");
            ClassicAssert.IsTrue(cursor != null);
            (r, cursor) = await tblConnections.PagingByCreatedAsync(2, cursor);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0].identity == "frodo.baggins.me");
            ClassicAssert.IsTrue(cursor == null);


            // TEST THE HANDCODED
            (r, cursor) = await tblConnections.PagingByCreatedAsync(1, 42, null);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0].identity == "gandalf.white.me");
            ClassicAssert.IsTrue(cursor != null);
            (r, cursor) = await tblConnections.PagingByCreatedAsync(2, 42, cursor);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0].identity == "frodo.baggins.me");
            ClassicAssert.IsTrue(cursor == null);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task GetConnectionsValidConnectionsTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblConnections = scope.Resolve<TableConnections>();

            var g1 = Guid.NewGuid();
            var g2 = Guid.NewGuid();
            var g3 = Guid.NewGuid();

            var item1 = new ConnectionsRecord()
            {
                identity = new OdinId("frodo.baggins.me"),
                displayName = "Frodo",
                status = 42,
                accessIsRevoked = 1,
                data = g1.ToByteArray()
            };
            await tblConnections.UpsertAsync(item1);

            var item2 = new ConnectionsRecord()
            {
                identity = new OdinId("samwise.gamgee.me"),
                displayName = "Sam",
                status = 43,
                accessIsRevoked = 0,
                data = g2.ToByteArray()
            };
            await tblConnections.UpsertAsync(item2);

            var item3 = new ConnectionsRecord()
            {
                identity = new OdinId("gandalf.white.me"),
                displayName = "G",
                status = 42,
                accessIsRevoked = 0,
                data = g3.ToByteArray()
            };
            await tblConnections.UpsertAsync(item3);


            var (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, null);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(r[0].identity == "frodo.baggins.me");
            ClassicAssert.IsTrue(r[1].identity == "gandalf.white.me");

            (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, outCursor);
            ClassicAssert.IsTrue(r.Count == 1, message: "rdr.HasRows is the sinner");
            ClassicAssert.IsTrue(r[0].identity == "samwise.gamgee.me");
            ClassicAssert.IsTrue(outCursor == null);

            // TEST HAND CODED STATUS FILTER
            (r, outCursor) = await tblConnections.PagingByIdentityAsync(1, 42, null);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0].identity == "frodo.baggins.me");
            ClassicAssert.IsTrue(outCursor != null);
            (r, outCursor) = await tblConnections.PagingByIdentityAsync(1, 42, outCursor);
            ClassicAssert.IsTrue(r[0].identity == "gandalf.white.me");
            ClassicAssert.IsTrue(outCursor == null);



            // Get most recent (will be a different order)
            (r, var cursor) = await tblConnections.PagingByCreatedAsync(2, null);
            ClassicAssert.IsTrue(r.Count == 2);
            ClassicAssert.IsTrue(r[0].identity == "gandalf.white.me");
            ClassicAssert.IsTrue(r[1].identity == "samwise.gamgee.me");
            ClassicAssert.IsTrue(cursor != null);

            // TEST THE HANDCODED
            (r, cursor) = await tblConnections.PagingByCreatedAsync(2, 43, null);
            ClassicAssert.IsTrue(r.Count == 1);
            ClassicAssert.IsTrue(r[0].identity == "samwise.gamgee.me");
            ClassicAssert.IsTrue(cursor == null);


            // PagingByCreated is NOT designed to be used with anything except the first page.
            // Hollow if you need pages and pages of 'most recent'. Hopefully just getting the
            // N you need is enough.

        }
    }
}
