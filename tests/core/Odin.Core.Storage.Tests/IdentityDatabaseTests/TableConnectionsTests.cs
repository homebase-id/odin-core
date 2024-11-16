# if false
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableConnectionsTests
    {
        [Test]
        public async Task ExampleTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableConnectionsTest001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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
                await db.tblConnections.UpsertAsync(item1);

                var item2 = new ConnectionsRecord()
                {
                    identity = new OdinId("samwise.gamgee.me"),
                    displayName = "Sam",
                    status = 43,
                    accessIsRevoked = 0,
                    data = g2.ToByteArray()
                };
                await db.tblConnections.UpsertAsync(item2);

                var item3 = new ConnectionsRecord()
                {
                    identity = new OdinId("gandalf.white.me"),
                    displayName = "G",
                    status = 44,
                    accessIsRevoked = 0,
                    data = g3.ToByteArray()
                };
                await db.tblConnections.UpsertAsync(item3);

                // We have three connections, get the first two in the first page, then the last page of one
                //
                var (r, outCursor) = await db.tblConnections.PagingByIdentityAsync(2, null);
                Debug.Assert(r.Count == 2);

                (r, outCursor) = await db.tblConnections.PagingByIdentityAsync(2, outCursor);
                Debug.Assert(r.Count == 1, message: "rdr.HasRows is the sinner");
                Debug.Assert(outCursor == null);


                // Try the filter ones
                (r, outCursor) = await db.tblConnections.PagingByIdentityAsync(2, 42, null);
                Debug.Assert(r.Count == 1);
            }
        }


        [Test]
        public async Task InsertValidConnectionTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableConnectionsTest002");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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

                await db.tblConnections.UpsertAsync(item);

                var r = await db.tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
                Debug.Assert(r.identity == "frodo.baggins.me");
                Debug.Assert(r.displayName == "Frodo Baggins");
                Debug.Assert(r.status == 42);
                Debug.Assert(r.accessIsRevoked == 0);
                Debug.Assert((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));
            }
        }


        [Test]
        public async Task DeleteValidConnectionTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableConnectionsTest003");
            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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

                var r = await db.tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
                Debug.Assert(r == null);
                await db.tblConnections.UpsertAsync(item);
                r = await db.tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
                Debug.Assert(r.identity == "frodo.baggins.me");
                Debug.Assert(r.displayName == "");
                Debug.Assert(r.status == 42);
                Debug.Assert(r.accessIsRevoked == 1);
                Debug.Assert((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));
            }
        }


        [Test]
        public async Task PagingByCreatedBothTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableConnectionsTest004");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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
                await db.tblConnections.UpsertAsync(item1);

                var item2 = new ConnectionsRecord()
                {
                    identity = new OdinId("samwise.gamgee.me"),
                    displayName = "Sam",
                    status = 43,
                    accessIsRevoked = 0,
                    data = g2.ToByteArray()
                };
                await db.tblConnections.UpsertAsync(item2);

                var item3 = new ConnectionsRecord()
                {
                    identity = new OdinId("gandalf.white.me"),
                    displayName = "G",
                    status = 42,
                    accessIsRevoked = 0,
                    data = g3.ToByteArray()
                };
                await db.tblConnections.UpsertAsync(item3);


                // Test the CRUD 

                // Get most recent (will be a different order)
                var (r, timeCursor) = await db.tblConnections.PagingByCreatedAsync(2, null);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(r[1].identity == "samwise.gamgee.me");
                Debug.Assert(timeCursor != null);
                (r, timeCursor) = await db.tblConnections.PagingByCreatedAsync(2, timeCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(timeCursor == null);


                // TEST THE HANDCODED
                (r, timeCursor) = await db.tblConnections.PagingByCreatedAsync(1, 42, null);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(timeCursor != null);
                (r, timeCursor) = await db.tblConnections.PagingByCreatedAsync(2, 42, timeCursor);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(timeCursor == null);
            }
        }


        [Test]
        public async Task GetConnectionsValidConnectionsTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableConnectionsTest005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
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
                await db.tblConnections.UpsertAsync(item1);

                var item2 = new ConnectionsRecord()
                {
                    identity = new OdinId("samwise.gamgee.me"),
                    displayName = "Sam",
                    status = 43,
                    accessIsRevoked = 0,
                    data = g2.ToByteArray()
                };
                await db.tblConnections.UpsertAsync(item2);

                var item3 = new ConnectionsRecord()
                {
                    identity = new OdinId("gandalf.white.me"),
                    displayName = "G",
                    status = 42,
                    accessIsRevoked = 0,
                    data = g3.ToByteArray()
                };
                await db.tblConnections.UpsertAsync(item3);


                var (r, outCursor) = await db.tblConnections.PagingByIdentityAsync(2, null);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(r[1].identity == "gandalf.white.me");

                (r, outCursor) = await db.tblConnections.PagingByIdentityAsync(2, outCursor);
                Debug.Assert(r.Count == 1, message: "rdr.HasRows is the sinner");
                Debug.Assert(r[0].identity == "samwise.gamgee.me");
                Debug.Assert(outCursor == null);

                // TEST HAND CODED STATUS FILTER
                (r, outCursor) = await db.tblConnections.PagingByIdentityAsync(1, 42, null);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "frodo.baggins.me");
                Debug.Assert(outCursor != null);
                (r, outCursor) = await db.tblConnections.PagingByIdentityAsync(1, 42, outCursor);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(outCursor == null);



                // Get most recent (will be a different order)
                (r, var timeCursor) = await db.tblConnections.PagingByCreatedAsync(2, null);
                Debug.Assert(r.Count == 2);
                Debug.Assert(r[0].identity == "gandalf.white.me");
                Debug.Assert(r[1].identity == "samwise.gamgee.me");
                Debug.Assert(timeCursor != null);

                // TEST THE HANDCODED
                (r, timeCursor) = await db.tblConnections.PagingByCreatedAsync(2, 43, null);
                Debug.Assert(r.Count == 1);
                Debug.Assert(r[0].identity == "samwise.gamgee.me");
                Debug.Assert(timeCursor == null);


                // PagingByCreated is NOT designed to be used with anything except the first page.
                // Hollow if you need pages and pages of 'most recent'. Hopefully just getting the
                // N you need is enough.
            }
        }
    }
}
#endif
