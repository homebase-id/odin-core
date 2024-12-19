using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    
    public class TableReactions : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Usage example
        public async Task ExampleUsageTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var p1 = Guid.NewGuid();

            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":smiley:" });

            int n = await tblDriveReactions.GetIdentityPostReactionsAsync(new OdinId("frodo.baggins.me"), driveId, p1);
            Assert.IsTrue(n == 2); // Frodo made 2 reactions to post P1

            // Added: 3 lol, 2 wink, 1 smiley to post 'p1'

            // Now get the reactions to the post

            var (r, c) = await tblDriveReactions.GetPostReactionsAsync(driveId, p1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");

            Int32? cursor = 0;
            var (r2, nextCursor) = await tblDriveReactions.PagingByRowidAsync(5, cursor, driveId, p1);
            Debug.Assert(r2.Count == 5);
            Debug.Assert(nextCursor != null);

            (r2, nextCursor) = await tblDriveReactions.PagingByRowidAsync(5, nextCursor, driveId, p1);
            Debug.Assert(r2.Count == 1);
            Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

            // As a result we had 6 in total, 3 :lol:, 2 :wink: and 1 :smiley:
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert rows as expected
        public async Task TheMissingOnes(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });

            var (ra1, ra2, n) = await tblDriveReactions.GetPostReactionsWithDetailsAsync(driveId, k1);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert rows as expected
        public async Task InsertRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert rows as expected
        public async Task IdentityPostDetailsTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":smile:" });

            string[] array = { ":lol:", ":wink:", ":smile:" };
            var rs = await tblDriveReactions.GetIdentityPostReactionDetailsAsync(new OdinId("frodo.baggins.me"), driveId, k1);
            Assert.IsTrue(array.Contains(rs[0]));
            Assert.IsTrue(array.Contains(rs[1]));
            Assert.IsTrue(array.Contains(rs[2]));
            Assert.IsTrue(rs[0] != rs[1]);
            Assert.IsTrue(rs[1] != rs[2]);

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert and read two tagmembers
        public async Task InsertDuplicateFailTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });

            bool ok = false;
            try
            {
                // Insert duplicate, expect exception
                await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
                ok = false;
            }
            catch
            {
                ok = true;
            }

            Assert.IsTrue(ok);

            ok = false;
            try
            {
                // Insert invalid reaction
                await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":l" });
                ok = false;
            }
            catch
            {
                ok = true;
            }

            Assert.IsTrue(ok);

            ok = false;
            try
            {
                // Insert invalid identity
                await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId(new string('a', 257)), postId = k1, singleReaction = ":lol:" });
                ok = false;
            }
            catch
            {
                ok = true;
            }

            Assert.IsTrue(ok);

        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert rows as expected
        public async Task DeleteTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            // The duplicate insert will fail unless the row was successfully deleted
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.DeleteAsync(driveId, new OdinId("frodo.baggins.me"), k1, ":lol:");
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.DeleteAsync(driveId, new OdinId("frodo.baggins.me"), k1, ":lol:");

            // The duplicate insert(s) will fail unless the rows were successfully deleted
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.DeleteAllReactionsAsync(driveId, new OdinId("frodo.baggins.me"), k1);
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            int n = await tblDriveReactions.GetIdentityPostReactionsAsync(new OdinId("frodo.baggins.me"), driveId, k1);
            Assert.IsTrue(n == 2);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert rows as expected
        public async Task GetPostReactionstest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });

            // 3 lol, 2 wink, 1 smiley

            var (r, c) = await tblDriveReactions.GetPostReactionsAsync(driveId, k1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
        #if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
        #endif
        // Test we can insert rows as expected
        public async Task GetPostReactionsChopTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveReactions = scope.Resolve<TableDriveReactions>();
            var identityKey = scope.Resolve<IdentityKey>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":skull:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":wagon:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":heart:" });
            await tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = identityKey, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":cat:" });

            // 3 lol, 2 wink, 1 smiley, 4 additionals; total of 7 emojis, 10 reactions

            var (r, c) = await tblDriveReactions.GetPostReactionsAsync(driveId, k1);
            Debug.Assert(c == 10);
            Debug.Assert(r.Count == 5);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            // It'll probably be fairly random which of the last ones are 'in' given they all have the same count
        }
    }
}
