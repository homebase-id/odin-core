# if false
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;

namespace Odin.Core.Storage.Tests.DriveDatabaseTests
{
    
    public class TableReactions
    {
        [Test]
        // Usage example
        public async Task ExampleUsageTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest001");

            await db.CreateDatabaseAsync();
            var driveId = Guid.NewGuid();

            var p1 = Guid.NewGuid();

            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":smiley:" });

            int n = await db.tblDriveReactions.GetIdentityPostReactionsAsync(new OdinId("frodo.baggins.me"), driveId, p1);
            Assert.IsTrue(n == 2); // Frodo made 2 reactions to post P1

            // Added: 3 lol, 2 wink, 1 smiley to post 'p1'

            // Now get the reactions to the post

            var (r, c) = await db.tblDriveReactions.GetPostReactionsAsync(driveId, p1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");

            Int32? cursor = 0;
            var (r2, nextCursor) = await db.tblDriveReactions.PagingByRowidAsync(db, 5, cursor, driveId, p1);
            Debug.Assert(r2.Count == 5);
            Debug.Assert(nextCursor != null);

            (r2, nextCursor) = await db.tblDriveReactions.PagingByRowidAsync(db, 5, nextCursor, driveId, p1);
            Debug.Assert(r2.Count == 1);
            Debug.Assert(nextCursor == null, message: "rdr.HasRows is the sinner");

            // As a result we had 6 in total, 3 :lol:, 2 :wink: and 1 :smiley:
        }

        [Test]
        // Test we can insert rows as expected
        public async Task TheMissingOnes()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest002");

            await db.CreateDatabaseAsync();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });

            var (ra1, ra2, n) = await db.tblDriveReactions.GetPostReactionsWithDetailsAsync(driveId, k1);

        }

        [Test]
        // Test we can insert rows as expected
        public async Task InsertRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                await db.tblDriveReactions.InsertAsync(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
                await db.tblDriveReactions.InsertAsync(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
                await db.tblDriveReactions.InsertAsync(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            }
        }

        [Test]
        // Test we can insert rows as expected
        public async Task IdentityPostDetailsTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest004");

            await db.CreateDatabaseAsync();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":smile:" });

            string[] array = { ":lol:", ":wink:", ":smile:" };
            var rs = await db.tblDriveReactions.GetIdentityPostReactionDetailsAsync(new OdinId("frodo.baggins.me"), driveId, k1);
            Assert.IsTrue(array.Contains(rs[0]));
            Assert.IsTrue(array.Contains(rs[1]));
            Assert.IsTrue(array.Contains(rs[2]));
            Assert.IsTrue(rs[0] != rs[1]);
            Assert.IsTrue(rs[1] != rs[2]);

        }


        [Test]
        // Test we can insert and read two tagmembers
        public async Task InsertDuplicateFailTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();

                await db.tblDriveReactions.InsertAsync(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });

                bool ok = false;
                try
                {
                    // Insert duplicate, expect exception
                    await db.tblDriveReactions.InsertAsync(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
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
                    await db.tblDriveReactions.InsertAsync(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":l" });
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
                    await db.tblDriveReactions.InsertAsync(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId(new string('a', 257)), postId = k1, singleReaction = ":lol:" });
                    ok = false;
                }
                catch
                {
                    ok = true;
                }

                Assert.IsTrue(ok);
            }
        }

        [Test]
        // Test we can insert rows as expected
        public async Task DeleteTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest006");

            await db.CreateDatabaseAsync();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            // The duplicate insert will fail unless the row was successfully deleted
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.DeleteAsync(driveId, new OdinId("frodo.baggins.me"), k1, ":lol:");
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.DeleteAsync(driveId, new OdinId("frodo.baggins.me"), k1, ":lol:");

            // The duplicate insert(s) will fail unless the rows were successfully deleted
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await db.tblDriveReactions.DeleteAllReactionsAsync(driveId, new OdinId("frodo.baggins.me"), k1);
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            int n = await db.tblDriveReactions.GetIdentityPostReactionsAsync(new OdinId("frodo.baggins.me"), driveId, k1);
            Assert.IsTrue(n == 2);
        }


        [Test]
        // Test we can insert rows as expected
        public async Task GetPostReactionstest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest007");

            await db.CreateDatabaseAsync();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });

            // 3 lol, 2 wink, 1 smiley

            var (r, c) = await db.tblDriveReactions.GetPostReactionsAsync(driveId, k1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");
        }

        [Test]
        // Test we can insert rows as expected
        public async Task GetPostReactionsChopTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest008");

            await db.CreateDatabaseAsync();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":skull:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":wagon:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":heart:" });
            await db.tblDriveReactions.InsertAsync(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":cat:" });

            // 3 lol, 2 wink, 1 smiley, 4 additionals; total of 7 emojis, 10 reactions

            var (r, c) = await db.tblDriveReactions.GetPostReactionsAsync(driveId, k1);
            Debug.Assert(c == 10);
            Debug.Assert(r.Count == 5);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            // It'll probably be fairly random which of the last ones are 'in' given they all have the same count
        }
    }
}
#endif