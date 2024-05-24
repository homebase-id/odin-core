﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite;
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
                Debug.Assert(myc.TransactionCount() == 0);

                myc.CreateCommitUnitOfWork(() =>
                {
                    Debug.Assert(myc.TransactionCount() == 0);
                    Debug.Assert(myc._nestedCounter == 1);

                    myc.CreateCommitUnitOfWork(() =>
                    {
                        Debug.Assert(myc.TransactionCount() == 0);
                        Debug.Assert(myc._nestedCounter == 2);
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
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
            using var db = new IdentityDatabase(Guid.NewGuid(), "");

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
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                var task = Task.Run(() =>
                {
                    using (var myc = db.CreateDisposableConnection())
                    {
                        Debug.Assert(myc._nestedCounter == 0, "Counter should be ready to commit initially.");
                        Debug.Assert(myc.TransactionCount() == 0, "Initial commits count should be zero.");

                        myc.CreateCommitUnitOfWork(() =>
                        {
                            Debug.Assert(myc.TransactionCount() == 0, "Commits count should be zero after creating unit of work.");
                            Debug.Assert(myc._nestedCounter == 1, "Counter should not be ready to commit after creating unit of work.");

                            myc.CreateCommitUnitOfWork(() =>
                            {
                                Debug.Assert(myc.TransactionCount() == 0, "Commits count should remain zero after nested unit of work.");
                                Debug.Assert(myc._nestedCounter == 2, "Counter should not be ready to commit in nested unit of work.");
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
        public void CreateCommitUnitOfWorkShouldRollbackOnException()
        {
            int Count(DatabaseConnection cn)
            {
                using var cmd = cn.db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM keyValue;";
                cmd.Connection = cn.Connection;
                return Convert.ToInt32(cmd.ExecuteScalar());
            }

            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using var cn = db.CreateDisposableConnection();

            db.CreateDatabase(cn, true);
            var kv = new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() };

            db.tblKeyValue.Insert(cn, kv);
            Assert.That(Count(cn), Is.EqualTo(1));

            // First make sure we can provoke a key violation
            var exception = Assert.Throws<SqliteException>(() => db.tblKeyValue.Insert(cn, kv));
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Lets add 3 some rows in two nested transactions
            cn.CreateCommitUnitOfWork(() =>
            {
                db.tblKeyValue.Insert(cn,
                    new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                cn.CreateCommitUnitOfWork(() =>
                {
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                });
            });

            // Make sure they are committed (total row count == 4)
            Assert.That(Count(cn), Is.EqualTo(4));

            // Rollback Variant 1
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.Throws<SqliteException>(() =>
            {
                cn.CreateCommitUnitOfWork(() =>
                {
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    cn.CreateCommitUnitOfWork(() =>
                    {
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                        db.tblKeyValue.Insert(cn, kv);
                    });
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(Count(cn), Is.EqualTo(4));

            // Rollback Variant 2
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.Throws<SqliteException>(() =>
            {
                cn.CreateCommitUnitOfWork(() =>
                {
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    cn.CreateCommitUnitOfWork(() =>
                    {
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    });

                    db.tblKeyValue.Insert(cn, kv);
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(Count(cn), Is.EqualTo(4));

            // Rollback Variant 3
            // Lets add 3 more rows in two nested transactions
            // And then the fatal key violation that should rollback everything
            exception = Assert.Throws<SqliteException>(() =>
            {
                cn.CreateCommitUnitOfWork(() =>
                {
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    db.tblKeyValue.Insert(cn, kv);

                    cn.CreateCommitUnitOfWork(() =>
                    {
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    });
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(Count(cn), Is.EqualTo(4));

            // Finally a single successful row for good measure
            cn.CreateCommitUnitOfWork(() =>
            {
                db.tblKeyValue.Insert(cn,
                    new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
            });
            Assert.That(Count(cn), Is.EqualTo(5));

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

            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using var cn = db.CreateDisposableConnection(); // SEB:TODO make async variant

            db.CreateDatabase(cn, true); // SEB:TODO make async variant
            var kv = new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() };

            db.tblKeyValue.Insert(cn, kv); // SEB:TODO make async variant
            Assert.That(await CountAsync(cn), Is.EqualTo(1));

            // First make sure we can provoke a key violation
            var exception = Assert.Throws<SqliteException>(() => db.tblKeyValue.Insert(cn, kv));
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Lets add 3 some rows in two nested transactions
            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                db.tblKeyValue.Insert(cn,
                    new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                await cn.CreateCommitUnitOfWorkAsync(() =>
                {
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    return Task.CompletedTask;
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
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await cn.CreateCommitUnitOfWorkAsync(() =>
                    {
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                        db.tblKeyValue.Insert(cn, kv);

                        return Task.CompletedTask;
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
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    await cn.CreateCommitUnitOfWorkAsync(() =>
                    {
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                        return Task.CompletedTask;
                    });

                    db.tblKeyValue.Insert(cn, kv);
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
                    db.tblKeyValue.Insert(cn,
                        new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                    db.tblKeyValue.Insert(cn, kv);

                    await cn.CreateCommitUnitOfWorkAsync(() =>
                    {
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                        db.tblKeyValue.Insert(cn,
                            new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });

                        return Task.CompletedTask;
                    });
                });
            });
            Assert.That(exception!.SqliteErrorCode, Is.EqualTo(19));

            // Make sure we still only have 4 rows
            Assert.That(await CountAsync(cn), Is.EqualTo(4));

            // Finally a single successful row for good measure
            await cn.CreateCommitUnitOfWorkAsync(() =>
            {
                db.tblKeyValue.Insert(cn,
                    new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                return Task.CompletedTask;
            });
            Assert.That(await CountAsync(cn), Is.EqualTo(5));
        }

        //

        [Test]
        public Task NestedTransactionsMustHaveTheSameIsolationLevel()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using var cn = db.CreateDisposableConnection();
            db.CreateDatabase(cn, true);

            var exception = Assert.ThrowsAsync<OdinSystemException>(async () =>
            {
                await cn.CreateCommitUnitOfWorkAsync(IsolationLevel.Serializable, async () =>
                {
                    await cn.CreateCommitUnitOfWorkAsync(() => Task.CompletedTask);
                });
            });
            Assert.That(exception!.Message, Is.EqualTo("Nested transactions must have the same isolation level"));
            return Task.CompletedTask;
        }

        //

        [Test]
        public void Test4()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);

                var wasCommitCallCount = myc.TransactionCount();

                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 0);

                myc.CreateCommitUnitOfWork(() =>
                {
                    // Add some data
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    Debug.Assert(myc.TransactionCount() == wasCommitCallCount);
                });
                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 1);
                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 1);
            }
        }


        [Test]
        public void Test5()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase(myc);

                var wasCommitCallCount = myc.TransactionCount();

                myc.CreateCommitUnitOfWork(() =>
                {
                    // Add some data
                    db.tblFollowsMe.Insert(myc, new FollowsMeRecord() { identity = "odin.valhalla.com", driveId = Guid.NewGuid() });

                    Debug.Assert(myc.TransactionCount() == wasCommitCallCount);
                });
                Debug.Assert(myc.TransactionCount() == wasCommitCallCount + 1);
            }
        }
    }
}