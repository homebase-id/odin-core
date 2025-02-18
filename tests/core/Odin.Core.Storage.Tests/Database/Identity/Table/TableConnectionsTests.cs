using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
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
            Debug.Assert(r.Count == 2);

            (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, outCursor);
            Debug.Assert(r.Count == 1, message: "rdr.HasRows is the sinner");
            Debug.Assert(outCursor == null);


            // Try the filter ones
            (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, 42, null);
            Debug.Assert(r.Count == 1);

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
            Debug.Assert(r.identity == "frodo.baggins.me");
            Debug.Assert(r.displayName == "Frodo Baggins");
            Debug.Assert(r.status == 42);
            Debug.Assert(r.accessIsRevoked == 0);
            Debug.Assert((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));

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
            Debug.Assert(r == null);
            await tblConnections.UpsertAsync(item);
            r = await tblConnections.GetAsync(new OdinId("frodo.baggins.me"));
            Debug.Assert(r.identity == "frodo.baggins.me");
            Debug.Assert(r.displayName == "");
            Debug.Assert(r.status == 42);
            Debug.Assert(r.accessIsRevoked == 1);
            Debug.Assert((ByteArrayUtil.muidcmp(r.data, g1.ToByteArray()) == 0));

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
            var (r, timeCursor) = await tblConnections.PagingByCreatedAsync(2, null);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0].identity == "gandalf.white.me");
            Debug.Assert(r[1].identity == "samwise.gamgee.me");
            Debug.Assert(timeCursor != null);
            (r, timeCursor) = await tblConnections.PagingByCreatedAsync(2, timeCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0].identity == "frodo.baggins.me");
            Debug.Assert(timeCursor == null);


            // TEST THE HANDCODED
            (r, timeCursor) = await tblConnections.PagingByCreatedAsync(1, 42, null);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0].identity == "gandalf.white.me");
            Debug.Assert(timeCursor != null);
            (r, timeCursor) = await tblConnections.PagingByCreatedAsync(2, 42, timeCursor);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0].identity == "frodo.baggins.me");
            Debug.Assert(timeCursor == null);

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
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0].identity == "frodo.baggins.me");
            Debug.Assert(r[1].identity == "gandalf.white.me");

            (r, outCursor) = await tblConnections.PagingByIdentityAsync(2, outCursor);
            Debug.Assert(r.Count == 1, message: "rdr.HasRows is the sinner");
            Debug.Assert(r[0].identity == "samwise.gamgee.me");
            Debug.Assert(outCursor == null);

            // TEST HAND CODED STATUS FILTER
            (r, outCursor) = await tblConnections.PagingByIdentityAsync(1, 42, null);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0].identity == "frodo.baggins.me");
            Debug.Assert(outCursor != null);
            (r, outCursor) = await tblConnections.PagingByIdentityAsync(1, 42, outCursor);
            Debug.Assert(r[0].identity == "gandalf.white.me");
            Debug.Assert(outCursor == null);



            // Get most recent (will be a different order)
            (r, var timeCursor) = await tblConnections.PagingByCreatedAsync(2, null);
            Debug.Assert(r.Count == 2);
            Debug.Assert(r[0].identity == "gandalf.white.me");
            Debug.Assert(r[1].identity == "samwise.gamgee.me");
            Debug.Assert(timeCursor != null);

            // TEST THE HANDCODED
            (r, timeCursor) = await tblConnections.PagingByCreatedAsync(2, 43, null);
            Debug.Assert(r.Count == 1);
            Debug.Assert(r[0].identity == "samwise.gamgee.me");
            Debug.Assert(timeCursor == null);


            // PagingByCreated is NOT designed to be used with anything except the first page.
            // Hollow if you need pages and pages of 'most recent'. Hopefully just getting the
            // N you need is enough.

        }

        record DisplayNameCursor (Guid identityId, string displayName, long rowId);

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        public async Task PagingWithCursorBrainstormTest(DatabaseType databaseType)
        {
            //
            // Assumptions:
            // - We're trying to solve the problem of paging through a db table using
            //   a key-set,
            //   a sort order,
            //   a limit.
            //
            // Problem:
            // - How do we sort the items by e.g
            //   absolute position,
            //   modified time (with at least millisecond resolution),
            //   identity name
            //
            // Table requirements:
            // - Primary key must preferably be a single uuid, alternatively "something" globally unique
            //   ("globally unique" might not be doable here for legacy reasons).
            //   Whatever it is, it must created client side (i.e. not on the db server) to avoid unnecessary round
            //   trips to the db.
            // - All time stamps must be in UTC, have at least millisecond resolution and be created on the server.
            //   They cannot be generated on the client, because of the risk of time drift (single source of truth).
            // - The table must have a unique 64 bit integer field (e.g. "rowid") that is auto-incremented
            //   by the db, so that we can sort by absolute position in case of timestamp collisions.
            // - Any sortable field must have an index in the db.
            //

            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var db = scope.Resolve<IdentityDatabase>();
            var tblConnections = db.Connections;

            await tblConnections.UpsertAsync(new ConnectionsRecord
            {
                identity = new OdinId("frodo.me"),
                displayName = "hobbit",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            });

            await tblConnections.UpsertAsync(new ConnectionsRecord
            {
                identity = new OdinId("sam.me"),
                displayName = "hobbit",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            });

            await tblConnections.UpsertAsync(new ConnectionsRecord
            {
                identity = new OdinId("merry.me"),
                displayName = "hobbit",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            });

            await tblConnections.UpsertAsync(new ConnectionsRecord
            {
                identity = new OdinId("pippin.me"),
                displayName = "hobbit",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            });

            await tblConnections.UpsertAsync(new ConnectionsRecord
            {
                identity = new OdinId("gandalf.me"),
                displayName = "good wizard",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            });

            await tblConnections.UpsertAsync(new ConnectionsRecord
            {
                identity = new OdinId("saruman.me"),
                displayName = "bad wizard",
                status = 42,
                accessIsRevoked = 1,
                data = Guid.NewGuid().ToByteArray()
            });

            await using var cn = await db.CreateScopedConnectionAsync();

            //
            // Sanity check 1:
            // Order by rowid
            //
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT * FROM connections
                    ORDER by rowid;
                    """;

                await using var rdr = await cmd.ExecuteReaderAsync();
                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual(IdentityId, new Guid((byte[])rdr["identityid"]));
                Assert.AreEqual("frodo.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual(IdentityId, new Guid((byte[])rdr["identityid"]));
                Assert.AreEqual("sam.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual(IdentityId, new Guid((byte[])rdr["identityid"]));
                Assert.AreEqual("merry.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual(IdentityId, new Guid((byte[])rdr["identityid"]));
                Assert.AreEqual("pippin.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual(IdentityId, new Guid((byte[])rdr["identityid"]));
                Assert.AreEqual("gandalf.me", (string)rdr["identity"]);
                Assert.AreEqual("good wizard", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual(IdentityId, new Guid((byte[])rdr["identityid"]));
                Assert.AreEqual("saruman.me", (string)rdr["identity"]);
                Assert.AreEqual("bad wizard", (string)rdr["displayName"]);

                Assert.IsFalse(await rdr.ReadAsync());
            }

            // Sanity check 2:
            // Locate record by primary key
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT * FROM connections
                    WHERE identityId = @identityId
                    AND identity = @identity
                    """;

                var identityId = cmd.CreateParameter();
                identityId.ParameterName = "@identityId";
                identityId.Value = IdentityId.ToByteArray();
                cmd.Parameters.Add(identityId);

                var identity = cmd.CreateParameter();
                identity.ParameterName = "@identity";
                identity.Value = "merry.me";
                cmd.Parameters.Add(identity);

                await using var rdr = await cmd.ExecuteReaderAsync();
                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual("merry.me", (string)rdr["identity"]);

                Assert.IsFalse(await rdr.ReadAsync());
            }

            DisplayNameCursor cursor;

            //
            // Page by display name - FIRST PAGE (not using cursor)
            //
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT *, rowid 
                    FROM connections
                    WHERE identityId = @identityId
                    ORDER BY displayName asc, rowid
                    LIMIT 3;
                    """;

                var identityId = cmd.CreateParameter();
                identityId.ParameterName = "@identityId";
                identityId.Value = IdentityId.ToByteArray();
                cmd.Parameters.Add(identityId);

                await using var rdr = await cmd.ExecuteReaderAsync();

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual("saruman.me", (string)rdr["identity"]);
                Assert.AreEqual("bad wizard", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual("gandalf.me", (string)rdr["identity"]);
                Assert.AreEqual("good wizard", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual("frodo.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                cursor = new DisplayNameCursor(
                    new Guid((byte[])rdr["identityId"]),
                    (string)rdr["displayName"],
                    (long)rdr["rowid"]
                );

                Assert.IsFalse(await rdr.ReadAsync());
            }

            //
            // Page by display name - SECOND PAGE (using cursor)
            //
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT *, rowid 
                    FROM connections
                    WHERE identityId = @identityId
                    AND (displayName, rowid) > (@displayName, @rowid)  
                    ORDER BY displayName desc, rowid
                    LIMIT 2;
                    """;

                var displayName = cmd.CreateParameter();
                displayName.ParameterName = "@displayName";
                displayName.Value = cursor.displayName;
                cmd.Parameters.Add(displayName);

                var identityId = cmd.CreateParameter();
                identityId.ParameterName = "@identityId";
                identityId.Value = cursor.identityId.ToByteArray();
                cmd.Parameters.Add(identityId);

                var rowId = cmd.CreateParameter();
                rowId.ParameterName = "@rowid";
                rowId.Value = cursor.rowId;
                cmd.Parameters.Add(rowId);

                await using var rdr = await cmd.ExecuteReaderAsync();

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual("sam.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual("merry.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                cursor = new DisplayNameCursor(
                    new Guid((byte[])rdr["identityId"]),
                    (string)rdr["displayName"],
                    (long)rdr["rowid"]
                );

                Assert.IsFalse(await rdr.ReadAsync());
            }

            //
            // Page by display name - LAST PAGE (using cursor)
            //
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT *, rowid 
                    FROM connections
                    WHERE identityId = @identityId
                    AND (displayName, rowid) > (@displayName, @rowid)  
                    ORDER BY displayName desc, rowid
                    LIMIT 10;
                    """;

                var displayName = cmd.CreateParameter();
                displayName.ParameterName = "@displayName";
                displayName.Value = cursor.displayName;
                cmd.Parameters.Add(displayName);

                var identityId = cmd.CreateParameter();
                identityId.ParameterName = "@identityId";
                identityId.Value = cursor.identityId.ToByteArray();
                cmd.Parameters.Add(identityId);

                var rowId = cmd.CreateParameter();
                rowId.ParameterName = "@rowid";
                rowId.Value = cursor.rowId;
                cmd.Parameters.Add(rowId);

                await using var rdr = await cmd.ExecuteReaderAsync();

                Assert.IsTrue(await rdr.ReadAsync());
                Assert.AreEqual("pippin.me", (string)rdr["identity"]);
                Assert.AreEqual("hobbit", (string)rdr["displayName"]);

                cursor = new DisplayNameCursor(
                    new Guid((byte[])rdr["identityId"]),
                    (string)rdr["displayName"],
                    (long)rdr["rowid"]
                );

                Assert.IsFalse(await rdr.ReadAsync());
            }
        }

    }
}
