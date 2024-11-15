using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.Tests
{
    public class DatabaseCommitTest
    {
        [Test]
        public async Task LogicCommitUnit1Test()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests001");
            using (var myc = db.CreateDisposableConnection())
            {
                Debug.Assert(myc._nestedCounter == 0);
                Debug.Assert(myc.TransactionCount() == 0);

                await myc.CreateCommitUnitOfWorkAsync(async () =>
                {
                    Debug.Assert(myc.TransactionCount() == 0);
                    Debug.Assert(myc._nestedCounter == 1);

                    await myc.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        Debug.Assert(myc.TransactionCount() == 0);
                        Debug.Assert(myc._nestedCounter == 2);
                        await Task.CompletedTask;
                    });

                    Debug.Assert(myc.TransactionCount() == 0);
                    Debug.Assert(myc._nestedCounter == 1);
                });
                Debug.Assert(myc._nestedCounter == 0);
                Debug.Assert(myc.TransactionCount() == 1);
            }
        }

        [Test]
        public async Task LogicCommitUnit1TestAsync()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests002");
            using (var myc = db.CreateDisposableConnection())
            {
                Debug.Assert(myc._nestedCounter == 0);
                Debug.Assert(myc.TransactionCount() == 0);

                await myc.CreateCommitUnitOfWorkAsync(async () =>
                {
                    Debug.Assert(myc.TransactionCount() == 0);
                    Debug.Assert(myc._nestedCounter == 1);

                    await myc.CreateCommitUnitOfWorkAsync(() =>
                    {
                        Debug.Assert(myc.TransactionCount() == 0);
                        Debug.Assert(myc._nestedCounter == 2);
                        return Task.CompletedTask;
                    });

                    Debug.Assert(myc.TransactionCount() == 0);
                    Debug.Assert(myc._nestedCounter == 1);
                });
                Debug.Assert(myc._nestedCounter == 0);
                Debug.Assert(myc.TransactionCount() == 1);

            }
        }


        [Test]
        public async Task LogicCommitUnit1TestAsyncWithException()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                try
                {
                    Debug.Assert(myc._nestedCounter == 0);
                    Debug.Assert(myc.TransactionCount() == 0);

                    await myc.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        Debug.Assert(myc.TransactionCount() == 0);
                        Debug.Assert(myc._nestedCounter == 1);

                        await myc.CreateCommitUnitOfWorkAsync(() =>
                        {
                            Debug.Assert(myc.TransactionCount() == 0);
                            Debug.Assert(myc._nestedCounter == 2);
                            throw new Exception("boom");
                        });

                        Debug.Assert(myc.TransactionCount() == 0);
                        Debug.Assert(myc._nestedCounter == 1);
                        throw new Exception("boom");
                    });
                    Debug.Assert(myc._nestedCounter == 0);
                    Debug.Assert(myc.TransactionCount() == 1);
                    throw new Exception("boom");
                }
                catch
                {
                }
                finally
                {
                    Debug.Assert(myc._nestedCounter == 0);
                    Debug.Assert(myc.TransactionCount() == 1);
                }
            }
        }

        /// <summary>
        /// Make sure each new thread will have it's own connection and own commit counter
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task LogicCommitUnit1TestWithMultipleTasks()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests004");
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                var task = Task.Run(async () =>
                {
                    using (var myc = db.CreateDisposableConnection())
                    {
                        Debug.Assert(myc._nestedCounter == 0, "Counter should be ready to commit initially.");
                        Debug.Assert(myc.TransactionCount() == 0, "Initial commits count should be zero.");

                        await myc.CreateCommitUnitOfWorkAsync(async () =>
                        {
                            Debug.Assert(myc.TransactionCount() == 0, "Commits count should be zero after creating unit of work.");
                            Debug.Assert(myc._nestedCounter == 1, "Counter should not be ready to commit after creating unit of work.");

                            await myc.CreateCommitUnitOfWorkAsync(async () =>
                            {
                                Debug.Assert(myc.TransactionCount() == 0, "Commits count should remain zero after nested unit of work.");
                                Debug.Assert(myc._nestedCounter == 2, "Counter should not be ready to commit in nested unit of work.");
                                await Task.CompletedTask;
                            });

                            Debug.Assert(myc.TransactionCount() == 0, "Commits count should still be zero after exiting nested unit of work.");
                            Debug.Assert(myc._nestedCounter == 1, "Counter should not be ready to commit after exiting nested unit of work.");
                        });
                        Debug.Assert(myc._nestedCounter == 0, "Counter should be ready to commit after all units of work.");
                        Debug.Assert(myc.TransactionCount() == 1, "Commits count should be one after completing unit of work.");
                    }

                });

                tasks.Add(task);
            }

            // Await all the tasks to complete
            await Task.WhenAll(tasks);
        }

        [Test]
        public async Task CreateCommitUnitOfWorkShouldRollbackOnException()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests005");
            await db.CreateDatabaseAsync(true);

            var kv = new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() };

            using var cn = db.CreateDisposableConnection();

            await db.tblKeyValue.InsertAsync(cn, kv);
            Assert.That(await db.tblKeyValue.GetCountDirtyAsync(cn), Is.EqualTo(1));

            // First make sure we can provoke a key violation
            var exception = Assert.ThrowsAsync<SqliteException>(async () => await db.tblKeyValue.InsertAsync(kv));

            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Lets add 3 some rows in two nested transactions
            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                await db.tblKeyValue.InsertAsync(cn,
                    new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                });
            });

            // Make sure they are committed (total row count == 4)
            Assert.That(await db.tblKeyValue.GetCountDirtyAsync(cn), Is.EqualTo(4));

            // Rollback Variant 1
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await cn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        await db.tblKeyValue.InsertAsync(cn, new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        await db.tblKeyValue.InsertAsync(cn, new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                        await db.tblKeyValue.InsertAsync(cn, kv);
                    });
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(await db.tblKeyValue.GetCountDirtyAsync(cn), Is.EqualTo(4));

            // Rollback Variant 2
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await cn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    });

                    await db.tblKeyValue.InsertAsync(cn,kv);
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(await db.tblKeyValue.GetCountDirtyAsync(cn), Is.EqualTo(4));

            // Rollback Variant 3
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn, new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await db.tblKeyValue.InsertAsync(cn, kv);

                    await cn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        await db.tblKeyValue.InsertAsync(cn, new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        await db.tblKeyValue.InsertAsync(cn, new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    });
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(await db.tblKeyValue.GetCountDirtyAsync(cn), Is.EqualTo(4));

            // Finally a single successful row for good measure
            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                await db.tblKeyValue.InsertAsync(cn,
                    new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
            });
            Assert.That(await db.tblKeyValue.GetCountDirtyAsync(cn), Is.EqualTo(5));
        }

        [Test]
        public async Task CreateCommitUnitOfWorkAsyncShouldRollbackOnException()
        {
            async Task<int> CountAsync(DatabaseConnection cn)
            {
                using var cmd = cn.db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM keyValue;";
                cmd.Connection = cn.Connection;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests006");
            await db.CreateDatabaseAsync(true);
            var kv = new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() };

            using var cn = db.CreateDisposableConnection();
            await db.tblKeyValue.InsertAsync(cn, kv);
            Assert.That(await CountAsync(cn), Is.EqualTo(1));

            // First make sure we can provoke a key violation
            var exception = Assert.ThrowsAsync<SqliteException>(async () => await db.tblKeyValue.InsertAsync(cn, kv));
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Lets add 3 some rows in two nested transactions
            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                await db.tblKeyValue.InsertAsync(cn,
                    new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                });
            });

            // Make sure they are committed (total row count == 4)
            Assert.That(await CountAsync(cn), Is.EqualTo(4));

            // Rollback Variant 1
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await cn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                        await db.tblKeyValue.InsertAsync(cn, kv);
                    });
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(await CountAsync(cn), Is.EqualTo(4));

            // Rollback Variant 2
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await cn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    });

                    await db.tblKeyValue.InsertAsync(cn,kv);
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(await CountAsync(cn), Is.EqualTo(4));

            // Rollback Variant 3
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await cn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db.tblKeyValue.InsertAsync(cn,
                        new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await db.tblKeyValue.InsertAsync(cn, kv);

                    await cn.CreateCommitUnitOfWorkAsync(async () =>
                    {
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        await db.tblKeyValue.InsertAsync(cn,
                            new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    });
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(await CountAsync(cn), Is.EqualTo(4));

            // Finally a single successful row for good measure
            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                await db.tblKeyValue.InsertAsync(cn,
                    new KeyValueRecord { identityId = db._identityId, key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
            });
            Assert.That(await CountAsync(cn), Is.EqualTo(5));
        }


        [Test]
        public async Task Test4()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests007");
            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();

                var wasCommitCallCount = myc.TransactionCount();

                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 0);

                await myc.CreateCommitUnitOfWorkAsync(async () =>
                {
                    // Add some data
                    await db.tblFollowsMe.InsertAsync(myc, new FollowsMeRecord() { identityId = db._identityId, identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    Debug.Assert(myc.TransactionCount() == wasCommitCallCount);
                });
                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 1);
                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 1);
            }
        }


        [Test]
        public async Task Test5()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseCommitTests008");
            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();

                var wasCommitCallCount = myc.TransactionCount();

                await myc.CreateCommitUnitOfWorkAsync(async () =>
                {
                    // Add some data
                    await db.tblFollowsMe.InsertAsync(myc, new FollowsMeRecord() { identityId = db._identityId, identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    Debug.Assert(myc.TransactionCount() == wasCommitCallCount);
                });
                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 1);
            }
        }
    }
}