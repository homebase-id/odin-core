using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Odin.Core.Cryptography.Crypto;
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

        private const int _performanceIterations = 5000; // Set to 5,000 when testing

        [SetUp]
        [TearDown]
        public void SetupTearDown()
        {
            // This will trigger any finalizers that are waiting to be run.
            // This is useful to verify that all db's are correctly disposed.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Test getting a non-existant item
        ///     00:00:01.03 : Got 5000000 non existing items from keyValue DB
        ///     Bandwidth: 4830917 rows / second
        ///     </summary>
        [Test]
        public void PerformanceTestGetNone()
        {
            var stopWatch = new Stopwatch();
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"diskoman1");
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase();

                var g = Guid.NewGuid().ToByteArray();

                stopWatch.Start();
                for (int i = 1; i < _performanceIterations; i++)
                {
                    _testDatabase.tblKeyValue.Get(g);
                }
                stopWatch.Stop();
            }
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            TestBenchmark.StopWatchStatus($"Got {_performanceIterations} non existing items from keyValue DB", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// <summary>
        /// Test getting an existant item
        ///         00:00:01.05 : Got 5000000 existing items from keyValue DB
        ///         Bandwidth: 4725897 rows / second
        /// </summary>
        [Test]
        public void PerformanceTestGetOne()
        {
            var stopWatch = new Stopwatch();
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"diskoman2");
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase();

                var k1 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();

                _testDatabase.tblKeyValue.Insert(new KeyValueRecord { key = k1, data = v1 } );

                stopWatch.Start();
                for (int i = 1; i < _performanceIterations; i++)
                {
                    _testDatabase.tblKeyValue.Get(k1);
                }
                stopWatch.Stop();
            }
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            TestBenchmark.StopWatchStatus($"Got {_performanceIterations} existing items from keyValue DB", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }



        /// <summary>
        /// 2023-01-10 SEMIBEASTII
        /// 00:00:15.10 : Added 5000 rows in mainindex, ACL, Tags
        /// Bandwidth: 330 rows / second
        /// 
        /// 2024-04-05 Bandwidth: 17301 rows / second
        /// REMOVED EVERYTHING TRANSACTION WRAPPER
        /// 2024-04-06 Bandwidth: 3918 rows / second
        /// 
        /// 2024-04-15 Standard Output, first with multi-connections
        /// Bandwidth: 4080 rows / second
        /// </summary>
        [Test]
        public void PerformanceTest01()
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"diskoman3");
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase();
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
                    _testDatabase.metaIndex.AddEntryPassalongToUpsert(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
                }
                stopWatch.Stop();
            }
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
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
        /// 
        /// 2024-04-15 Standard Output, first with multi-connections
        /// 00:00:02.71 : Added 50000 rows in mainindex, ACL, Tags
        /// Bandwidth: 18416 rows / second
        /// DB Opened 1, Closed 0
        /// </summary>
        [Test]
        public void PerformanceTest01B()
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"diskoman4");

                _testDatabase.CreateDatabase();
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
                    _testDatabase.metaIndex.AddEntryPassalongToUpsert(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
                }
                stopWatch.Stop();
                int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

                TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
                Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
                Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
                GC.Collect();
                GC.WaitForPendingFinalizers();
        }

        /// <summary>
        /// 2023-01-10 SEMIBEASTII
        ///     00:00:00.41 : Added 5000 rows in mainindex, ACL, Tags
        ///     Bandwidth: 12019 rows / second
        ///     
        /// 2024-04-05 Bandwidth: 7733 rows / second
        /// REMOVED EVERYTHING TRANSACTION WRAPPER
        /// 2024-04-06 Bandwidth: Bandwidth: 3898 rows / second
        /// 
        /// 2024-04-15 Standard Output, first with multi-connections
        /// Bandwidth: 3961 rows / second
        /// 
        /// </summary>
        [Test]
        public void PerformanceTest02() // Test batch of 100
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"PerformanceTest02");
            _testDatabase.CreateDatabase();

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
                _testDatabase.metaIndex.AddEntryPassalongToUpsert(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// Multi-threading on a single connection
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
        /// 
        /// 2024-04-15 Standard Output, first with multi-connections
        /// Bandwidth: 20491 rows / second
        /// </summary>
        [Test]
        [Ignore("no lock")]
        public void PerformanceTest03()
        {
            Task[] tasks = new Task[MAXTHREADS];
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"PerformanceTest03");

            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase();
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

        /// Multi-threading on a connection per thread
        [Test]
        public void PerformanceTest03B() // Just making sure multi-threaded doesn't give worse performance
        {
            Task[] tasks = new Task[MAXTHREADS];
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"memento03B.db");
            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase();
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
                        using (var cn = _testDatabase.CreateDisposableConnection())
                        {
                            WriteRows(i, MAXITERATIONS, _testDatabase, cn, driveId);
                        }
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

        public long[] WriteRows(int threadno, int iterations, IdentityDatabase db, DatabaseConnection myc, Guid driveId)
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
                db.metaIndex.AddEntryPassalongToUpsert(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }

            return timers;
        }

        /// <summary>
        /// 2024-04-15 Standard Output, first with multi-connections
        /// Bandwidth: 4560 rows / second
        /// </summary>
        [Test]
        public void PerformanceTest10()
        {
            var stopWatch = new Stopwatch();
            var myRnd = new Random();
            using var _testDatabase = new IdentityDatabase(Guid.NewGuid(), $"mydatabase.db");

            using (var myc = _testDatabase.CreateDisposableConnection())
            {
                _testDatabase.CreateDatabase();
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
                            requiredSecurityGroup = 55,
                            hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                            hdrVersionTag = SequentialGuid.CreateGuid(),
                            hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                            hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                            hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                            hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                            hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                            hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                            hdrTmpDriveType = SequentialGuid.CreateGuid()
                        };
                        _testDatabase.tblDriveMainIndex.Insert(r);
                        //_testDatabase.tblDriveMainIndex.Insert(r);
                        _connection.Close();
                    }
                }
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

    }
}