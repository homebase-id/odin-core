using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Test.Helpers.Benchmark;


namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class DriveIndexDatabasePerformanceTests
    {
        // For the performance test
        private const int MAXTHREADS = 5; // Should be at least 2 * your CPU cores. Can still be nice to test sometimes with lower. And not too high.
        private const int MAXITERATIONS = 1000; // A number high enough to get warmed up and reliable

        private const int _performanceIterations = 50000; // Set to 5,000 when testing



        /// <summary>
        /// 2023-01-10 SEMIBEASTII
        /// 00:00:15.10 : Added 5000 rows in mainindex, ACL, Tags
        /// Bandwidth: 330 rows / second
        /// 
        /// 2024-04-05 Bandwidth: 17301 rows / second
        /// REMOVED EVERYTHING TRANSACTION WRAPPER
        /// 2024-04-06 Bandwidth: 3918 rows / second
        /// </summary>
        [Test]
        public void PerformanceTest01()
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase($"");
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase(myc);
                var driveId = Guid.NewGuid();

                var tmpacllist = new List<Guid>();
                for (int j = 0; j < 1; j++)
                {
                    tmpacllist.Add(Guid.NewGuid());
                }

                var tmptaglist = new List<Guid>();
                for (int j = 0; j < 1; j++)
                {
                    tmptaglist.Add(Guid.NewGuid());
                }
                stopWatch.Start();
                for (int i = 1; i < _performanceIterations; i++)
                {
                    _testDatabase.AddEntry(myc, driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToByteArray(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
                }
                stopWatch.Stop();
            }
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000) / ms} rows / second");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// <summary>
        /// Repeats TEST01 as a transaction
        /// 
        /// 2023-01-10 SEMIBEASTII
        /// 00:00:15.10 : Added 5000 rows in mainindex, ACL, Tags
        /// Bandwidth: 330 rows / second
        /// 
        /// 2024-04-05 Bandwidth: 17301 rows / second
        /// REMOVED EVERYTHING TRANSACTION WRAPPER
        /// 2024-04-06 Bandwidth: 16578 rows / second
        /// </summary>
        [Test]
        public void PerformanceTest01B()
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase($"");
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase(myc);
                var driveId = Guid.NewGuid();

                var tmpacllist = new List<Guid>();
                for (int j = 0; j < 1; j++)
                {
                    tmpacllist.Add(Guid.NewGuid());
                }

                var tmptaglist = new List<Guid>();
                for (int j = 0; j < 1; j++)
                {
                    tmptaglist.Add(Guid.NewGuid());
                }

                stopWatch.Start();
                using (myc.CreateCommitUnitOfWork())
                {
                    for (int i = 1; i < _performanceIterations; i++)
                    {
                        _testDatabase.AddEntry(myc, driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToByteArray(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
                    }
                }
                stopWatch.Stop();
                int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

                TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
                Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000) / ms} rows / second");
                Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        /// 2023-01-10 SEMIBEASTII
        ///     00:00:00.41 : Added 5000 rows in mainindex, ACL, Tags
        ///     Bandwidth: 12019 rows / second
        ///     
        /// 2024-04-05 Bandwidth: 7733 rows / second
        /// REMOVED EVERYTHING TRANSACTION WRAPPER
        /// 2024-04-06 Bandwidth: Bandwidth: 3898 rows / second
        /// </summary>
        [Test]
        public void PerformanceTest02() // Test batch of 100
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase($"");
            var driveId = Guid.NewGuid();
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase(myc);
                var tmpacllist = new List<Guid>();
                for (int j = 0; j < 1; j++)
                {
                    tmpacllist.Add(Guid.NewGuid());
                }

                var tmptaglist = new List<Guid>();
                for (int j = 0; j < 1; j++)
                {
                    tmptaglist.Add(Guid.NewGuid());
                }

                stopWatch.Start();
                myc.BeginTransaction();
                for (int i = 1; i < _performanceIterations; i++)
                {
                    _testDatabase.AddEntry(myc, driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToByteArray(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
                    if (i % 100 == 0)
                    {
                        myc.Commit();
                    }
                }
                myc.Commit();
                stopWatch.Stop();
                int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

                TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
                Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000) / ms} rows / second");
                Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        ///    Threads   : 5
        ///    Iterations: 1000
        ///    Time      : 15667ms
        ///    Bandwidth: 319 rows / second
        ///    00:00:15.66 : Added 5000 rows in mainindex, ACL, Tags
        ///    
        /// 2024-04-05: Bandwidth: 8376 rows / second
        /// REMOVED EVERYTHING TRANSACTION WRAPPER
        /// 2024-04-06 Bandwidth: 8547 rows / second
        /// </summary>
        [Test]
        public void PerformanceTest03() // Just making sure multi-threaded doesn't give worse performance
        {
            Task[] tasks = new Task[MAXTHREADS];
            using var _testDatabase = new IdentityDatabase($"");
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase(myc);
                var driveId = Guid.NewGuid();
                var stopWatch = new Stopwatch();

                //
                // Now back to performance testing
                //
                var sw = new Stopwatch();
                sw.Reset();
                sw.Start();

                for (var i = 0; i < MAXTHREADS; i++)
                {
                    tasks[i] = Task.Run(() =>
                    {
                            WriteRows(i, MAXITERATIONS, _testDatabase, myc, driveId);
                    });
                }

                try
                {
                    Task.WaitAll(tasks);
                }
                catch (AggregateException ae)
                {
                    foreach (var ex in ae.InnerExceptions)
                    {
                        Console.WriteLine(ex.Message);
                    }

                    throw;
                }

                sw.Stop();

                Console.WriteLine($"Threads   : {MAXTHREADS}");
                Console.WriteLine($"Iterations: {MAXITERATIONS}");
                Console.WriteLine($"Time      : {sw.ElapsedMilliseconds}ms");
                long ms = Math.Max(1, sw.ElapsedMilliseconds);
                Console.WriteLine($"Bandwidth: {(MAXTHREADS * MAXITERATIONS * 1000) / ms} rows / second");
                TestBenchmark.StopWatchStatus($"Added {MAXTHREADS * MAXITERATIONS} rows in mainindex, ACL, Tags", sw);
                Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            _testDatabase.Dispose();
        }

        public long[] WriteRows(int threadno, int iterations, IdentityDatabase db, DatabaseBase.DatabaseConnection myc, Guid driveId)
        {
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var myRnd = new Random();
            var sw = new Stopwatch();


            var tmpacllist = new List<Guid>();
            for (int j = 0; j < 1; j++)
            {
                tmpacllist.Add(Guid.NewGuid());
            }

            var tmptaglist = new List<Guid>();
            for (int j = 0; j < 1; j++)
            {
                tmptaglist.Add(Guid.NewGuid());
            }

            //
            // I presume here we retrieve the file and download it
            //
            for (int count = 0; count < iterations; count++)
            {
                db.AddEntry(myc, driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToByteArray(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }

            return timers;
        }

        [Test]
        public void PerformanceTest10()
        {

            var s = new SqliteConnectionStringBuilder() { DataSource = "my.db", Pooling = true, Cache = SqliteCacheMode.Shared };
            var t = s.ConnectionString;
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase($"Data Source=mydatabase.db; Mode=ReadWriteCreate; Cache=Shared; Pooling=true");

            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase(myc);
                var driveId = Guid.NewGuid();

                stopWatch.Start();
                {
                    for (int i = 1; i < _performanceIterations; i++)
                    {
                        var _connection = new SqliteConnection("Data Source=mydatabase.db; Mode=ReadWriteCreate; Cache=Shared; Pooling=true");
                        _connection.Open();

                        using (var pragmaJournalModeCommand = _connection.CreateCommand())
                        {
                            pragmaJournalModeCommand.CommandText = "PRAGMA journal_mode=WAL;";
                            pragmaJournalModeCommand.ExecuteNonQuery();
                            pragmaJournalModeCommand.CommandText = "PRAGMA synchronous=NORMAL;";
                            pragmaJournalModeCommand.ExecuteNonQuery();
                        }

                        var r = new DriveMainIndexRecord()
                        {
                            driveId = driveId,
                            fileId = Guid.NewGuid(),
                            globalTransitId = Guid.NewGuid(),
                            fileType = myRnd.Next(0, 5),
                            dataType = myRnd.Next(0, 5),
                            senderId = "frodobaggins.me",
                            groupId = Guid.NewGuid(),
                            uniqueId = Guid.NewGuid(),
                            archivalStatus = 42,
                            userDate = new UnixTimeUtc(),
                            requiredSecurityGroup = 55
                        };
                        _testDatabase.tblDriveMainIndex.Insert(myc, r);
                        //_testDatabase.tblDriveMainIndex.Insert(r);
                        _connection.Close();
                    }
                }
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000) / ms} rows / second");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

    }
}