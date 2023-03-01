using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace DriveDatabaseTests
{
    
    public class TableReactions
    {
        [Test]
        // Usage example
        public void ExampleUsageTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-01.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var p1 = Guid.NewGuid();

            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("bilbo.baggins.me"), postId = p1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = p1, singleReaction = ":smiley:" });

            int n = db.TblReactions.GetIdentityPostReactions(new OdinId("frodo.baggins.me"), p1);
            Assert.IsTrue(n == 2); // Frodo made 2 reactions to post P1

            // Added: 3 lol, 2 wink, 1 smiley to post 'p1'

            // Now get the reactions to the post

            var (r, c) = db.TblReactions.GetPostReactions(p1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");

            Int32? cursor = 0;
            var r2 = db.TblReactions.PagingByRowid(5, cursor, out cursor, p1);
            Debug.Assert(r2.Count == 5);
            Debug.Assert(cursor != null);

            r2 = db.TblReactions.PagingByRowid(5, cursor, out cursor, p1);
            Debug.Assert(r2.Count == 1);
            Debug.Assert(cursor == null);

            // As a result we had 6 in total, 3 :lol:, 2 :wink: and 1 :smiley:
        }


        [Test]
        // Test we can insert rows as expected
        public void InsertRowTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-02.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
        }


        [Test]
        // Test we can insert rows as expected
        public void IdentityPostDetailsTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-42.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":smile:" });

            string[] array = { ":lol:", ":wink:", ":smile:" };
            var rs = db.TblReactions.GetIdentityPostReactionDetails(new OdinId("frodo.baggins.me"), k1);
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
            using var db = new DriveDatabase("URI=file:.\\tblReactions-03.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });

            bool ok = false;
            try
            {
                // Insert duplicate, expect exception
                db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
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
                db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":l" });
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
                db.TblReactions.Insert(new ReactionsItem { identity = new OdinId(new string('a', 257)), postId = k1, singleReaction = ":lol:" });
                ok = false;
            }
            catch
            {
                ok = true;
            }

            Assert.IsTrue(ok);
        }

        [Test]
        // Test we can insert rows as expected
        public void DeleteTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-04.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            // The duplicate insert will fail unless the row was successfully deleted
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Delete(new OdinId("frodo.baggins.me"), k1, ":lol:");
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Delete(new OdinId("frodo.baggins.me"), k1, ":lol:");

            // The duplicate insert(s) will fail unless the rows were successfully deleted
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.TblReactions.DeleteAllReactions(new OdinId("frodo.baggins.me"), k1);
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            int n = db.TblReactions.GetIdentityPostReactions(new OdinId("frodo.baggins.me"), k1);
            Assert.IsTrue(n == 2);
        }


        [Test]
        // Test we can insert rows as expected
        public void GetPostReactionstest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-05.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });

            // 3 lol, 2 wink, 1 smiley

            var (r, c) = db.TblReactions.GetPostReactions(k1);
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
            using var db = new DriveDatabase("URI=file:.\\tblReactions-06.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("frodo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("bilbo.baggins.me"), postId = k1, singleReaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":smiley:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":skull:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":wagon:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":heart:" });
            db.TblReactions.Insert(new ReactionsItem { identity = new OdinId("sam.gamgee.me"), postId = k1, singleReaction = ":cat:" });

            // 3 lol, 2 wink, 1 smiley, 4 additionals; total of 7 emojis, 10 reactions

            var (r, c) = db.TblReactions.GetPostReactions(k1);
            Debug.Assert(c == 10);
            Debug.Assert(r.Count == 5);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            // It'll probably be fairly random which of the last ones are 'in' given they all have the same count
        }
    }
}