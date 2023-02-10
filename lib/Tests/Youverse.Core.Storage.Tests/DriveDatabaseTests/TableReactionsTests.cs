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

            db.TblReactions.InsertReaction("frodo.baggins.me", p1, ":lol:");
            db.TblReactions.InsertReaction("frodo.baggins.me", p1, ":wink:");
            db.TblReactions.InsertReaction("bilbo.baggins.me", p1, ":lol:");
            db.TblReactions.InsertReaction("bilbo.baggins.me", p1, ":wink:");
            db.TblReactions.InsertReaction("sam.gamgee.me", p1, ":lol:");
            db.TblReactions.InsertReaction("sam.gamgee.me", p1, ":smiley:");

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

            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":wink:");
            db.TblReactions.InsertReaction("bilbo.baggins.me", k1, ":lol:");
        }


        [Test]
        // Test we can insert and read two tagmembers
        public void InsertDuplicateFailTest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-03.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");

            bool ok = false;
            try
            {
                // Insert duplicate, expect exception
                db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
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
                db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":l");
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
                db.TblReactions.InsertReaction(new string('a', 257), k1, ":lol:");
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
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.DeleteReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.DeleteReaction("frodo.baggins.me", k1, ":lol:");

            // The duplicate insert(s) will fail unless the rows were successfully deleted
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":wink:");
            db.TblReactions.DeleteAllReactions("frodo.baggins.me", k1);
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":wink:");
        }


        [Test]
        // Test we can insert rows as expected
        public void GetPostReactionstest()
        {
            using var db = new DriveDatabase("URI=file:.\\tblReactions-05.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();

            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":wink:");
            db.TblReactions.InsertReaction("bilbo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("bilbo.baggins.me", k1, ":wink:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":lol:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":smiley:");

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

            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("frodo.baggins.me", k1, ":wink:");
            db.TblReactions.InsertReaction("bilbo.baggins.me", k1, ":lol:");
            db.TblReactions.InsertReaction("bilbo.baggins.me", k1, ":wink:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":lol:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":smiley:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":skull:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":wagon:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":heart:");
            db.TblReactions.InsertReaction("sam.gamgee.me", k1, ":cat:");

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