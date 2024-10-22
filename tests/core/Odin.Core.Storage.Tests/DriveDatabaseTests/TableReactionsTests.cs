using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    
    public class TableReactions
    {
        [Test]
        // Usage example
        public void ExampleUsageTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest001");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var p1 = Guid.NewGuid();

            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":smiley:" });

            int n = db.tblDriveReactions.GetIdentityPostReactions(new OdinId("frodo.baggins.me"), driveId, p1);
            Assert.IsTrue(n == 2); // Frodo made 2 reactions to post P1

            // Added: 3 lol, 2 wink, 1 smiley to post 'p1'

            // Now get the reactions to the post

            var (r, c) = db.tblDriveReactions.GetPostReactions(driveId, p1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");

            Int32? cursor = 0;
            var r2 = db.tblDriveReactions.PagingByRowid(db, 5, cursor, out cursor, driveId, p1);
            Debug.Assert(r2.Count == 5);
            Debug.Assert(cursor != null);

            r2 = db.tblDriveReactions.PagingByRowid(db, 5, cursor, out cursor, driveId, p1);
            Debug.Assert(r2.Count == 1);
            Debug.Assert(cursor == null, message: "rdr.HasRows is the sinner");

            // As a result we had 6 in total, 3 :lol:, 2 :wink: and 1 :smiley:
        }

        [Test]
        // Test we can insert rows as expected
        public void TheMissingOnes()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest002");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });

            var (ra1, ra2, n) = db.tblDriveReactions.GetPostReactionsWithDetails(driveId, k1);

        }

        [Test]
        // Test we can insert rows as expected
        public void InsertRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                db.tblDriveReactions.Insert(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
                db.tblDriveReactions.Insert(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
                db.tblDriveReactions.Insert(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            }
        }

        [Test]
        // Test we can insert rows as expected
        public void IdentityPostDetailsTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest004");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":smile:" });

            string[] array = { ":lol:", ":wink:", ":smile:" };
            var rs = db.tblDriveReactions.GetIdentityPostReactionDetails(new OdinId("frodo.baggins.me"), driveId, k1);
            Assert.IsTrue(array.Contains(rs[0]));
            Assert.IsTrue(array.Contains(rs[1]));
            Assert.IsTrue(array.Contains(rs[2]));
            Assert.IsTrue(rs[0] != rs[1]);
            Assert.IsTrue(rs[1] != rs[2]);

        }


        [Test]
        // Test we can insert and read two tagmembers
        public void InsertDuplicateFailTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();

                db.tblDriveReactions.Insert(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });

                bool ok = false;
                try
                {
                    // Insert duplicate, expect exception
                    db.tblDriveReactions.Insert(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
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
                    db.tblDriveReactions.Insert(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":l" });
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
                    db.tblDriveReactions.Insert(myc, new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId(new string('a', 257)), postId = k1, singleReaction = ":lol:" });
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
        public void DeleteTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest006");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            // The duplicate insert will fail unless the row was successfully deleted
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Delete(driveId, new OdinId("frodo.baggins.me"), k1, ":lol:");
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Delete(driveId, new OdinId("frodo.baggins.me"), k1, ":lol:");

            // The duplicate insert(s) will fail unless the rows were successfully deleted
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.tblDriveReactions.DeleteAllReactions(driveId, new OdinId("frodo.baggins.me"), k1);
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            int n = db.tblDriveReactions.GetIdentityPostReactions(new OdinId("frodo.baggins.me"), driveId, k1);
            Assert.IsTrue(n == 2);
        }


        [Test]
        // Test we can insert rows as expected
        public void GetPostReactionstest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest007");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });

            // 3 lol, 2 wink, 1 smiley

            var (r, c) = db.tblDriveReactions.GetPostReactions(driveId, k1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");
        }

        [Test]
        // Test we can insert rows as expected
        public void GetPostReactionsChopTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableReactionsTest008");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();

            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":skull:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":wagon:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":heart:" });
            db.tblDriveReactions.Insert(new DriveReactionsRecord { identityId = db._identityId, driveId = driveId, identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":cat:" });

            // 3 lol, 2 wink, 1 smiley, 4 additionals; total of 7 emojis, 10 reactions

            var (r, c) = db.tblDriveReactions.GetPostReactions(driveId, k1);
            Debug.Assert(c == 10);
            Debug.Assert(r.Count == 5);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            // It'll probably be fairly random which of the last ones are 'in' given they all have the same count
        }
    }
}