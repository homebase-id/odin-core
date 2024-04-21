using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                Debug.Assert(myc._counter.ReadyToCommit() == true);
                Debug.Assert(myc.CommitsCount() == 0);

                using (myc.CreateCommitUnitOfWork())
                {
                    Debug.Assert(myc.CommitsCount() == 0);
                    Debug.Assert(myc._counter.ReadyToCommit() == false);

                    using (myc.CreateCommitUnitOfWork())
                    {
                        Debug.Assert(myc.CommitsCount() == 0);
                        Debug.Assert(myc._counter.ReadyToCommit() == false);
                    }

                    Debug.Assert(myc.CommitsCount() == 0);
                    Debug.Assert(myc._counter.ReadyToCommit() == false);
                }
                Debug.Assert(myc._counter.ReadyToCommit() == true);
                Debug.Assert(myc.CommitsCount() == 1);
            }
        }


        /// <summary>
        /// Make sure each new thread will have it's own connection and own commit counter
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task LogicCommitUnit1TestWithMultipleTasks()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                var task = Task.Run(() =>
                {
                    // Create database with 250ms commit timer trigger
                    // Task's logic
                    using (var myc = db.CreateDisposableConnection())
                    {
                        Debug.Assert(myc._counter.ReadyToCommit() == true, "Counter should be ready to commit initially.");
                        Debug.Assert(myc.CommitsCount() == 0, "Initial commits count should be zero.");

                        using (myc.CreateCommitUnitOfWork())
                        {
                            Debug.Assert(myc.CommitsCount() == 0, "Commits count should be zero after creating unit of work.");
                            Debug.Assert(myc._counter.ReadyToCommit() == false, "Counter should not be ready to commit after creating unit of work.");

                            using (myc.CreateCommitUnitOfWork())
                            {
                                Debug.Assert(myc.CommitsCount() == 0, "Commits count should remain zero after nested unit of work.");
                                Debug.Assert(myc._counter.ReadyToCommit() == false, "Counter should not be ready to commit in nested unit of work.");
                            }

                            Debug.Assert(myc.CommitsCount() == 0, "Commits count should still be zero after exiting nested unit of work.");
                            Debug.Assert(myc._counter.ReadyToCommit() == false, "Counter should not be ready to commit after exiting nested unit of work.");
                        }
                        Debug.Assert(myc._counter.ReadyToCommit() == true, "Counter should be ready to commit after all units of work.");
                        Debug.Assert(myc.CommitsCount() == 1, "Commits count should be one after completing unit of work.");
                    }

                });

                tasks.Add(task);
            }

            // Await all the tasks to complete
            await Task.WhenAll(tasks);
        }


        [Test]
        public void Test4()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);

                var wasCommitCallCount = myc.CommitsCount();
                myc.Commit();
                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 0);

                using (myc.CreateCommitUnitOfWork())
                {
                    // Add some data
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    Debug.Assert(myc.Commit() == false);
                    Debug.Assert(myc.CommitsCount() == wasCommitCallCount);
                }
                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 1);
                myc.Commit();
                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 1);
            }
        }


        [Test]
        public void Test5()
        {
            // Create database with 250ms commit timer trigger
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);

                var wasCommitCallCount = myc.CommitsCount();

                using (myc.CreateCommitUnitOfWork())
                {
                    // Add some data
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    myc.Commit();
                    Debug.Assert(myc.CommitsCount() == wasCommitCallCount);
                }
                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 1);
            }
        }
    }
}