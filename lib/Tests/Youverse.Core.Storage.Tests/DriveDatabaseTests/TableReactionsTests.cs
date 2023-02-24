using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Youverse.Core;
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

            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = p1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = p1, singlereaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "bilbo.baggins.me", postid = p1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "bilbo.baggins.me", postid = p1, singlereaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = p1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = p1, singlereaction = ":smiley:" });

            int n = db.TblReactions.GetIdentityPostReactions("frodo.baggins.me", p1);
            Assert.IsTrue(n == 2); // Frodo made 2 reactions to post P1

            // Added: 3 lol, 2 wink, 1 smiley to post 'p1'

            // Now get the reactions to the post

            var (r, c) = db.TblReactions.GetPostReactions(p1);
            Debug.Assert(c == 6);
            Debug.Assert(r.Count == 3);
            Debug.Assert(r[0] == ":lol:");
            Debug.Assert(r[1] == ":wink:");
            Debug.Assert(r[2] == ":smiley:");

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

            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "bilbo.baggins.me", postid = k1, singlereaction = ":lol:" });
        }


        [Test]
        // Test we can insert and read two tagmembers
        public void InsertDuplicateFailTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-03.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });

            bool ok = false;
            try
            {
                // Insert duplicate, expect exception
                db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
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
                db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":l" });
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
                db.TblReactions.Insert(new ReactionsItem { identity = new string('a', 257), postid = k1, singlereaction = ":lol:" });
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
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Delete("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Delete("frodo.baggins.me", k1, ":lol:");

            // The duplicate insert(s) will fail unless the rows were successfully deleted
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":wink:" });
            db.TblReactions.DeleteAllReactions("frodo.baggins.me", k1);
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":wink:" });
            int n = db.TblReactions.GetIdentityPostReactions("frodo.baggins.me", k1);
            Assert.IsTrue(n == 2);
        }


        [Test]
        // Test we can insert rows as expected
        public void GetPostReactionstest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-05.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "bilbo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "bilbo.baggins.me", postid = k1, singlereaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":smiley:" });

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

            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "frodo.baggins.me", postid = k1, singlereaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "bilbo.baggins.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "bilbo.baggins.me", postid = k1, singlereaction = ":wink:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":lol:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":smiley:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":skull:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":wagon:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":heart:" });
            db.TblReactions.Insert(new ReactionsItem { identity = "sam.gamgee.me", postid = k1, singlereaction = ":cat:" });

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