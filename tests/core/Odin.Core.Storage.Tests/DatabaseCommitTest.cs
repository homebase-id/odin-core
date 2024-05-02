using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests
{
    public class DatabaseCommitTest
    {
        [Test]
        public void LogicCommitUnit1Test()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                Debug.Assert(myc._nestedCounter == 0);
                Debug.Assert(myc.CommitsCount() == 0);

                myc.CreateCommitUnitOfWork(() =>
                {
                    Debug.Assert(myc.CommitsCount() == 0);
                    Debug.Assert(myc._nestedCounter != 0);

                    myc.CreateCommitUnitOfWork(() =>
                    {
                        Debug.Assert(myc.CommitsCount() == 0);
                        Debug.Assert(myc._nestedCounter != 0);
                    });

                    Debug.Assert(myc.CommitsCount() == 0);
                    Debug.Assert(myc._nestedCounter != 0);
                });
                Debug.Assert(myc._nestedCounter == 0);
                Debug.Assert(myc.CommitsCount() == 1);
            }
        }

        [Test]
        public async Task LogicCommitUnit1TestAsync()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                Debug.Assert(myc._nestedCounter == 0);
                Debug.Assert(myc.CommitsCount() == 0);

                await myc.CreateCommitUnitOfWorkAsync(async () =>
                {
                    Debug.Assert(myc.CommitsCount() == 0);
                    Debug.Assert(myc._nestedCounter != 0);

                    await myc.CreateCommitUnitOfWorkAsync(() =>
                    {
                        Debug.Assert(myc.CommitsCount() == 0);
                        Debug.Assert(myc._nestedCounter != 0);
                        return Task.CompletedTask;
                    });

                    Debug.Assert(myc.CommitsCount() == 0);
                    Debug.Assert(myc._nestedCounter != 0);
                });
                Debug.Assert(myc._nestedCounter == 0);
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
                    using (var myc = db.CreateDisposableConnection())
                    {
                        Debug.Assert(myc._nestedCounter == 0, "Counter should be ready to commit initially.");
                        Debug.Assert(myc.CommitsCount() == 0, "Initial commits count should be zero.");

                        myc.CreateCommitUnitOfWork(() =>
                        {
                            Debug.Assert(myc.CommitsCount() == 0, "Commits count should be zero after creating unit of work.");
                            Debug.Assert(myc._nestedCounter != 0, "Counter should not be ready to commit after creating unit of work.");

                            myc.CreateCommitUnitOfWork(() =>
                            {
                                Debug.Assert(myc.CommitsCount() == 0, "Commits count should remain zero after nested unit of work.");
                                Debug.Assert(myc._nestedCounter != 0, "Counter should not be ready to commit in nested unit of work.");
                            });

                            Debug.Assert(myc.CommitsCount() == 0, "Commits count should still be zero after exiting nested unit of work.");
                            Debug.Assert(myc._nestedCounter != 0, "Counter should not be ready to commit after exiting nested unit of work.");
                        });
                        Debug.Assert(myc._nestedCounter == 0, "Counter should be ready to commit after all units of work.");
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);

                var wasCommitCallCount = myc.CommitsCount();

                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 0);

                myc.CreateCommitUnitOfWork(() =>
                {
                    // Add some data
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    Debug.Assert(myc.CommitsCount() == wasCommitCallCount);
                });
                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 1);
                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 1);
            }
        }


        [Test]
        public void Test5()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);

                var wasCommitCallCount = myc.CommitsCount();

                myc.CreateCommitUnitOfWork(() =>
                {
                    // Add some data
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    Debug.Assert(myc.CommitsCount() == wasCommitCallCount);
                });
                Debug.Assert(myc.CommitsCount() == wasCommitCallCount + 1);
            }
        }
    }
}