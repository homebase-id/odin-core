using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests
{
    public class DatabaseCommitTest
    {
        [Test]
        public void LogicCommitUnit1Test()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            Debug.Assert(db._counter.ReadyToCommit() == true);
            Debug.Assert(db.CommitsCount() == 0);

            using (db.CreateCommitUnitOfWork())
            {
                Debug.Assert(db.CommitsCount() == 0);
                Debug.Assert(db._counter.ReadyToCommit() == false);

                using (db.CreateCommitUnitOfWork())
                {
                    Debug.Assert(db.CommitsCount() == 0);
                    Debug.Assert(db._counter.ReadyToCommit() == false);
                }

                Debug.Assert(db.CommitsCount() == 0);
                Debug.Assert(db._counter.ReadyToCommit() == false);
            }
            Debug.Assert(db._counter.ReadyToCommit() == true);
            Debug.Assert(db.CommitsCount() == 1);
        }


        [Test]
        public void Test4()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var wasCommitCallCount = db.CommitsCount();
            db.Commit();
            Debug.Assert(db.CommitsCount() == wasCommitCallCount + 0);

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                db.Commit();
                Debug.Assert(db.CommitsCount() == wasCommitCallCount);
            }
            Debug.Assert(db.CommitsCount() == wasCommitCallCount + 1);
            db.Commit();
            Debug.Assert(db.CommitsCount() == wasCommitCallCount + 1);
        }


        [Test]
        public void Test5()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var wasCommitCallCount = db.CommitsCount();

            using (db.CreateCommitUnitOfWork())
            {
                // Add some data
                db.tblFollowsMe.Insert(new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                db.Commit();
                Debug.Assert(db.CommitsCount() == wasCommitCallCount);
            }
            Debug.Assert(db.CommitsCount() == wasCommitCallCount + 1);
        }
    }
}