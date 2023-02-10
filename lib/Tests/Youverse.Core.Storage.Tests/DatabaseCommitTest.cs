using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

namespace IdentityDatabaseTests
{
    public class DatabaseCommitTest
    {
        [Test]
        public void CommitTimerDoesntFireTest()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("URI=file:.\\commit-timer-01.db", 250);
            db.CreateDatabase();

            // Pass time
            Thread.Sleep(1000);

            Debug.Assert(db.TimerCount() == 0);
        }


        [Test]
        public void CommitTimerFireOnceTest()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("URI=file:.\\commit-timer-02.db", 250);
            db.CreateDatabase();

            // Add some data
            db.tblFollowsMe.InsertFollower("odin.valhalla.com", Guid.NewGuid());

            // Pass time, way more than 250ms, so we should trigger once, not twice
            Thread.Sleep(1000);
            Debug.Assert(db.TimerCount() == 1);
            Debug.Assert(db.TimerCommitCount() == 1);

            // Timer should be stopped
            Thread.Sleep(500);
            Debug.Assert(db.TimerCount() == 1);
            Debug.Assert(db.TimerCommitCount() == 1);
        }

        [Test]
        public void CommitTimerFireTwiceTest()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("URI=file:.\\commit-timer-01.db", 250);
            db.CreateDatabase();

            // Add some data
            db.tblFollowsMe.InsertFollower("odin.valhalla.com", Guid.NewGuid());

            // Pass time, so we should trigger once, not twice
            Thread.Sleep(500);
            Debug.Assert(db.TimerCount() == 1);
            Debug.Assert(db.TimerCommitCount() == 1);

            // Add some more data
            db.tblFollowsMe.InsertFollower("thor.valhalla.com", Guid.NewGuid());
            Thread.Sleep(500);
            Debug.Assert(db.TimerCount() == 2);
            Debug.Assert(db.TimerCommitCount() == 2);

            // Timer should be stopped
            Thread.Sleep(500);
            Debug.Assert(db.TimerCount() == 2);
            Debug.Assert(db.TimerCommitCount() == 2);
        }

        [Test]
        public void LogicCommitUnit1Test()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("URI=file:.\\commit-logic-01.db", 250);
            db.CreateDatabase();

            Debug.Assert(db._counter.ReadyToCommit() == true);

            using (db.CreateCommitUnitOfWork())
            {
                Debug.Assert(db._counter.ReadyToCommit() == false);

                using (db.CreateCommitUnitOfWork())
                {
                    Debug.Assert(db._counter.ReadyToCommit() == false);
                }
            }
            Debug.Assert(db._counter.ReadyToCommit() == true);

            Debug.Assert(db.TimerCount() == 0);
        }


        [Test]
        public void Test3()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("URI=file:.\\commit-logic-02.db", 250);
            db.CreateDatabase();

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.InsertFollower("odin.valhalla.com", Guid.NewGuid());

                // Pass time for 2 timer triggers (250+250+100), because we're in a logic commit unit, DB won't flush
                Thread.Sleep(600);
                Debug.Assert(db.TimerCount() == 2);  // Will have tried to flush twice
                Debug.Assert(db.TimerCommitCount() == 0);
                Debug.Assert(db.CommitCallCount() == 0);
                Debug.Assert(db.CommitFlushCount() == 0);
            }

            Thread.Sleep(300); // Wait for the timer  to trigger
            Debug.Assert(db.TimerCount() == 3);  // Timer will have triggered once more
            Debug.Assert(db.TimerCommitCount() == 1);
            Debug.Assert(db.CommitCallCount() == 1);
            Debug.Assert(db.CommitFlushCount() == 1);

            Thread.Sleep(300); // Timer shouldn't trigger because no data is pending

            // Check nothing changed
            Debug.Assert(db.TimerCount() == 3);
            Debug.Assert(db.TimerCommitCount() == 1);
            Debug.Assert(db.CommitCallCount() == 1);
            Debug.Assert(db.CommitFlushCount() == 1);
        }

        [Test]
        public void Test4()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("URI=file:.\\commit-logic-03.db", 5000);
            db.CreateDatabase();

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.InsertFollower("odin.valhalla.com", Guid.NewGuid());

                db.Commit();
                Debug.Assert(db.CommitCallCount() == 1);
                Debug.Assert(db.CommitFlushCount() == 0);
            }

            db.Commit();
            Debug.Assert(db.CommitCallCount() == 2);
            Debug.Assert(db.CommitFlushCount() == 1);
        }


        [Test]
        public void Test5()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("URI=file:.\\commit-logic-04.db", 250);
            db.CreateDatabase();

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.InsertFollower("odin.valhalla.com", Guid.NewGuid());

                db.Commit();
                Debug.Assert(db.CommitCallCount() == 1);
                Debug.Assert(db.CommitFlushCount() == 0);
            }

            Thread.Sleep(300);
            Debug.Assert(db.TimerCount() == 1);  // Timer will have triggered once more
            Debug.Assert(db.TimerCommitCount() == 1);
            Debug.Assert(db.CommitCallCount() == 2);
            Debug.Assert(db.CommitFlushCount() == 1);

            db.Commit();
            Debug.Assert(db.CommitCallCount() == 3);
            Debug.Assert(db.CommitFlushCount() == 1);
        }
    }
}