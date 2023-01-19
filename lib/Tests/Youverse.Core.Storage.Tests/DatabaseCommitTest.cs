using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace IndexerTests.KeyValue
{
    public class DatabaseCommitTest
    {
        [Test]
        public void CommitTimerDoesntFireTest()
        {
            // Create database with 250ms commit timer trigger
            using var db = new KeyValueDatabase("URI=file:.\\follower-example-01.db", 250);
            db.CreateDatabase();

            // Pass time
            Thread.Sleep(1000);

            Debug.Assert(db.TimerCount() == 0);
        }


        [Test]
        public void CommitTimerFireOnceTest()
        {
            // Create database with 250ms commit timer trigger
            using var db = new KeyValueDatabase("URI=file:.\\follower-example-01.db", 250);
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
            using var db = new KeyValueDatabase("URI=file:.\\follower-example-01.db", 250);
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
    }
}