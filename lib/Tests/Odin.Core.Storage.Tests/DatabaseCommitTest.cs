using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests
{
    public class DatabaseCommitTest
    {
        [Test]
        public void CommitTimerDoesntFireTest()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("", 250);
            db.CreateDatabase();

            // Pass time
            Thread.Sleep(1000);

            Debug.Assert(db.TimerCount() == 0);
        }


        [Test]
        public void CommitTimerFireOnceTest()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("", 250);
            db.CreateDatabase();

            // Add some data
            db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

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
            using var db = new IdentityDatabase("", 250);
            db.CreateDatabase();

            // Add some data
            db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

            // Pass time, so we should trigger once, not twice
            Thread.Sleep(600);
            Debug.Assert(db.TimerCount() == 1);
            Debug.Assert(db.TimerCommitCount() == 1);

            // Add some more data
            db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "thor.valhalla.com", driveId = Guid.NewGuid() });
            Thread.Sleep(600);
            Debug.Assert(db.TimerCount() == 2);
            Debug.Assert(db.TimerCommitCount() == 2);

            // Timer should be stopped
            Thread.Sleep(600);
            Debug.Assert(db.TimerCount() == 2);
            Debug.Assert(db.TimerCommitCount() == 2);
        }

        [Test]
        public void LogicCommitUnit1Test()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("", 250);
            db.CreateDatabase();

            Debug.Assert(db._counter.ReadyToCommit() == true);

            using (db.CreateCommitUnitOfWork())
            {
                Debug.Assert(db._counter.ReadyToCommit() == false);

                using (db.CreateCommitUnitOfWork())
                {
                    Debug.Assert(db._counter.ReadyToCommit() == false);
                }

                Debug.Assert(db._counter.ReadyToCommit() == false);
            }
            Debug.Assert(db._counter.ReadyToCommit() == true);

            Thread.Sleep(300); // Wait for the timer  to trigger

            Debug.Assert(db.TimerCount() == 0);
        }


        [Test]
        public void Test3()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("", 250);
            db.CreateDatabase();

            var wasCommitCallCount = db.CommitCallCount();
            var wasCommitFlushCount = db.CommitFlushCount();

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                // Pass time for 2 timer triggers (250+250+100), because we're in a logic commit unit, DB won't flush
                Thread.Sleep(600);
                Debug.Assert(db.TimerCount() == 2);  // Will have tried to flush twice
                Debug.Assert(db.TimerCommitCount() == 0);
                Debug.Assert(db.CommitCallCount() == wasCommitCallCount);
                Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount);
            }

            Thread.Sleep(300); // Wait for the timer  to trigger
            Debug.Assert(db.TimerCount() == 3);  // Timer will have triggered once more
            Debug.Assert(db.TimerCommitCount() == 1);
            Debug.Assert(db.CommitCallCount() == wasCommitCallCount + 1);
            Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount + 1);

            Thread.Sleep(300); // Timer shouldn't trigger because no data is pending

            // Check nothing changed
            Debug.Assert(db.TimerCount() == 3);
            Debug.Assert(db.TimerCommitCount() == 1);
            Debug.Assert(db.CommitCallCount() == wasCommitCallCount + 1);
            Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount + 1);
        }


        [Test]
        public void Test4()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("", 5000);
            db.CreateDatabase();

            var wasCommitCallCount = db.CommitCallCount();
            var wasCommitFlushCount = db.CommitFlushCount();

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                db.Commit();
                Debug.Assert(db.CommitCallCount() == wasCommitCallCount + 1);
                Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount + 0);
            }

            db.Commit();
            Debug.Assert(db.CommitCallCount() == wasCommitCallCount + 2);
            Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount + 1);
        }


        [Test]
        public void Test5()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("", 250);
            db.CreateDatabase();

            var wasCommitCallCount = db.CommitCallCount();
            var wasCommitFlushCount = db.CommitFlushCount();

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                db.Commit();
                Debug.Assert(db.CommitCallCount() == wasCommitCallCount + 1);
                Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount + 0);
            }

            Thread.Sleep(300);
            Debug.Assert(db.TimerCount() == 1);  // Timer will have triggered once more
            Debug.Assert(db.TimerCommitCount() == 1);
            Debug.Assert(db.CommitCallCount() == wasCommitCallCount + 2);
            Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount + 1);

            db.Commit();
            Debug.Assert(db.CommitCallCount() == wasCommitCallCount + 3);
            Debug.Assert(db.CommitFlushCount() == wasCommitFlushCount + 1);
        }
    }
}