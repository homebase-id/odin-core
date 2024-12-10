using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Test.Helpers.Benchmark;
using Serilog.Events;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions
{
    public class DriveMainIndexPerformanceTests : IocTestBase
    {
        // For the performance test
        private const int MAXTHREADS = 10; // Should be at least 2 * your CPU cores. Can still be nice to test sometimes with lower. And not too high.
        private const int MAXITERATIONS = 500; // A number high enough to get warmed up and reliable

        private const int _performanceIterations = 5000; // Set to 5,000 when testing

        /// <summary>
        /// Test getting a non-existant item
        ///     00:00:01.03 : Got 5000000 non existing items from keyValue DB
        ///     Bandwidth: 4830917 rows / second
        ///     </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTestGetNone(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyValue = scope.Resolve<TableKeyValue>();

            var stopWatch = new Stopwatch();

            var g = Guid.NewGuid().ToByteArray();

            stopWatch.Start();
            for (int i = 1; i < _performanceIterations; i++)
            {
                await tblKeyValue.GetAsync(g);
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);
            var counters = scope.Resolve<DatabaseCounters>();

            TestBenchmark.StopWatchStatus($"Got {_performanceIterations} non existing items from keyValue DB", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// <summary>
        /// Test getting an existant item
        ///         00:00:01.05 : Got 5000000 existing items from keyValue DB
        ///         Bandwidth: 4725897 rows / second
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTestGetOne(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblKeyValue = scope.Resolve<TableKeyValue>();

            var stopWatch = new Stopwatch();

            var k1 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();

            await tblKeyValue.InsertAsync(new KeyValueRecord { key = k1, data = v1 } );

            stopWatch.Start();
            for (int i = 1; i < _performanceIterations; i++)
            {
                await tblKeyValue.GetAsync(k1);
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);
            var counters = scope.Resolve<DatabaseCounters>();

            TestBenchmark.StopWatchStatus($"Got {_performanceIterations} existing items from keyValue DB", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
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
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTest01(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var stopWatch = new Stopwatch();
            var myRnd = new Random();
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
                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);
            var counters = scope.Resolve<DatabaseCounters>();

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
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
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTest01B(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var stopWatch = new Stopwatch();
            var myRnd = new Random();

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
                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);
            var counters = scope.Resolve<DatabaseCounters>();

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
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
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTest02(DatabaseType databaseType) // Test batch of 100
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var stopWatch = new Stopwatch();
            var myRnd = new Random();

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
                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);
            var counters = scope.Resolve<DatabaseCounters>();

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
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
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTest03(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);

            Task[] tasks = new Task[MAXTHREADS];

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
                tasks[i] = Task.Run(async () =>
                {
                    await using var scope = Services.BeginLifetimeScope();
                    var metaIndex = scope.Resolve<MainIndexMeta>();
                    await WriteRowsAsync(i, MAXITERATIONS, metaIndex, driveId);
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
            var counters = Services.Resolve<DatabaseCounters>();

            Console.WriteLine($"Bandwidth: {(MAXTHREADS * MAXITERATIONS * 1000) / ms} rows / second");
            TestBenchmark.StopWatchStatus($"Added {MAXTHREADS * MAXITERATIONS} rows in mainindex, ACL, Tags", sw);
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        /// Multi-threading on a connection per thread
        /// SEB:NOTE this is a BAD idea with scoped connections, but I'll leave it for completeness
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTest03B(DatabaseType databaseType) // Just making sure multi-threaded doesn't give worse performance
        {
            await RegisterServicesAsync(databaseType, LogEventLevel.Verbose);

            // var logger = Services.Resolve<ILogger<ScopedIdentityConnectionFactory>>();

            Task[] tasks = new Task[MAXTHREADS];
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
                tasks[i] = Task.Run(async () =>
                {
                    await using var scope = Services.BeginLifetimeScope();
                    var metaIndex = scope.Resolve<MainIndexMeta>();
                    await WriteRowsAsync(i, MAXITERATIONS, metaIndex, driveId);
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
            var counters = Services.Resolve<DatabaseCounters>();

            Console.WriteLine($"Bandwidth: {(MAXTHREADS * MAXITERATIONS * 1000) / ms} rows / second");
            TestBenchmark.StopWatchStatus($"Added {MAXTHREADS * MAXITERATIONS} rows in mainindex, ACL, Tags", sw);
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private async Task<long[]> WriteRowsAsync(int threadno, int iterations, MainIndexMeta metaIndex, Guid driveId)
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
                // NOTE: synchroneous call
                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, Guid.NewGuid(), Guid.NewGuid(), myRnd.Next(0, 5), myRnd.Next(0, 5), Guid.NewGuid().ToString(), Guid.NewGuid(), Guid.NewGuid(), 42, new UnixTimeUtc(0), 55, tmpacllist, tmptaglist, 1);
            }

            return timers;
        }

        /// <summary>
        /// 2024-04-15 Standard Output, first with multi-connections
        /// Bandwidth: 4560 rows / second
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
        public async Task PerformanceTest10(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();

            var stopWatch = new Stopwatch();
            var myRnd = new Random();

            var driveId = Guid.NewGuid();

            stopWatch.Start();
            {
                for (int i = 1; i < _performanceIterations; i++)
                {
                    // using (var pragmaJournalModeCommand = _connection.CreateCommand())
                    // {
                    //     pragmaJournalModeCommand.CommandText = "PRAGMA journal_mode=WAL;";
                    //     pragmaJournalModeCommand.ExecuteNonQuery();
                    //     pragmaJournalModeCommand.CommandText = "PRAGMA synchronous=NORMAL;";
                    //     pragmaJournalModeCommand.ExecuteNonQuery();
                    // }

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
                    await tblDriveMainIndex.InsertAsync(r);
                    //_testDatabase.tblDriveMainIndex.Insert(r);
                }
            }
            stopWatch.Stop();
            int ms = (int)Math.Max(1, stopWatch.ElapsedMilliseconds);
            var counters = scope.Resolve<DatabaseCounters>();

            TestBenchmark.StopWatchStatus($"Added {_performanceIterations} rows in mainindex, ACL, Tags", stopWatch);
            Console.WriteLine($"Bandwidth: {(_performanceIterations * 1000L) / ms} rows / second");
            Console.WriteLine($"DB Opened {counters.NoDbOpened} , Closed  {counters.NoDbClosed}");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

    }
}
