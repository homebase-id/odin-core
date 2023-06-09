﻿using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Hosting.Tests.Performance
{

    public class DbPerformanceTests
    {

        // For the performance test
        private static readonly int MAXTHREADS = 20;
        private const int MAXITERATIONS = 50000;
        private const int KEYS = 100;

        private WebScaffold _scaffold;
        private IdentityDatabase _db;
        private SingleKeyValueStorage storage;
        private Guid[] _keys = new Guid[KEYS];
        public class Item
        {
            public string Name { get; set; }
            public byte[] Data { get; set; } = new byte[50];
        }


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
            _db = new IdentityDatabase("");
            _db.CreateDatabase();
            storage = new SingleKeyValueStorage(_db.tblKeyValue);

            for (int i = 0; i < KEYS; i++)
            {
                _keys[i] = Guid.NewGuid();
                var v1 = Guid.NewGuid().ToByteArray();

                // Create an instance of Item
                var item = new Item
                {
                    Name = $"Test Item {i}",
                    Data = new byte[] { (byte)(i % 256), 2, 3, 4, 5, /* ... */ } // This should contain 50 elements
                };

                storage.Upsert<Item>(_keys[i], item);
                // _db.tblKeyValue.Insert(new KeyValueRecord() { key = _keys[i], data = v1 });
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
            _db.Dispose();
        }

        /*
         *  TaskPerformanceTest_Ident
           Duration: 5.3 sec

          Standard Output: 
            2023-05-31 Host [SEMIBEASTII]
            Threads   : 1
            Iterations: 50,000
            Wall Time : 5,290ms
            Minimum   : 0ms
            Maximum   : 12ms
            Average   : 0ms
            Median    : 0ms
            Capacity  : 9,451 / second
            RSA Encryptions 0, Decryptions 8
            RSA Keys Created 4, Keys Expired 0
            DB Opened 5, Closed 0

        After DB cache

         TaskPerformanceTest_Db_SingleThread
            Source: DbPerformance.cs line 86
            Duration: 41 ms

            Standard Output: 
            2023-06-01 Host [SEMIBEASTII]
            Threads   : 1
            Iterations: 50,000
            Wall Time : 17ms
            Minimum   : 0ms
            Maximum   : 2ms
            Average   : 0ms
            Median    : 0ms
            Capacity  : 2,941,176 / second
            RSA Encryptions 0, Decryptions 8
            RSA Keys Created 4, Keys Expired 0
            DB Opened 5, Closed 0
         */
        [Test]
        public void TaskPerformanceTest_Db_SingleThread()
        {
            PerformanceFramework.ThreadedTest(1, MAXITERATIONS, DoDb);
            Assert.Pass();
        }

        /*
         * Before DB cache
         * 
           TaskPerformanceTest_Ident_MultiThread
               Duration: 1.1 min

              Standard Output: 
                2023-05-31 Host [SEMIBEASTII]
                Threads   : 20
                Iterations: 50,000
                Wall Time : 64,879ms
                Minimum   : 0ms
                Maximum   : 104ms
                Average   : 1ms
                Median    : 0ms
                Capacity  : 15,413 / second
                RSA Encryptions 0, Decryptions 8
                RSA Keys Created 4, Keys Expired 0
                DB Opened 5, Closed 0

        After DB cache

             TaskPerformanceTest_Db_MultiThread
                Duration: 118 ms

            Standard Output: 
                2023-06-01 Host [SEMIBEASTII]
                Threads   : 20
                Iterations: 50,000
                Wall Time : 77ms
                Minimum   : 0ms
                Maximum   : 16ms
                Average   : 0ms
                Median    : 0ms
                Capacity  : 12,987,012 / second
                RSA Encryptions 0, Decryptions 8
                RSA Keys Created 4, Keys Expired 0
                DB Opened 5, Closed 0
        */
        [Test]
        public void TaskPerformanceTest_Db_MultiThread()
        {
            PerformanceFramework.ThreadedTest(MAXTHREADS, MAXITERATIONS, DoDb);
            Assert.Pass();
        }

        public Task<(long, long[])> DoDb(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();

            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                var r = _db.tblKeyValue.Get(_keys[0]);
                Debug.Assert(r != null);

                timers[count] = sw.ElapsedMilliseconds;
            }

            return Task.FromResult((0L, timers));
        }


        /*
             TaskPerformanceTest_DbWrapper_SingleThread
               Source: DbPerformance.cs line 181
               Duration: 74 ms

              Standard Output: 
            2023-06-01 Host [SEMIBEASTII]
            Threads   : 1
            Iterations: 50,000
            Wall Time : 51ms
            Minimum   : 0ms
            Maximum   : 2ms
            Average   : 0ms
            Median    : 0ms
            Capacity  : 980,392 / second
            RSA Encryptions 0, Decryptions 8
            RSA Keys Created 4, Keys Expired 0
            DB Opened 5, Closed 0
         */
        [Test]
        public void TaskPerformanceTest_DbWrapper_SingleThread()
        {
            PerformanceFramework.ThreadedTest(1, MAXITERATIONS, DoWrapperDb);
            Assert.Pass();
        }

        /*
             TaskPerformanceTest_DbWrapper_MultiThread
               Source: DbPerformance.cs line 205
               Duration: 244 ms

              Standard Output: 
            2023-06-01 Host [SEMIBEASTII]
            Threads   : 20
            Iterations: 50,000
            Wall Time : 198ms
            Minimum   : 0ms
            Maximum   : 20ms
            Average   : 0ms
            Median    : 0ms
            Capacity  : 5,050,505 / second
            RSA Encryptions 0, Decryptions 8
            RSA Keys Created 4, Keys Expired 0
            DB Opened 5, Closed 0
         */
        [Test]
        public void TaskPerformanceTest_DbWrapper_MultiThread()
        {
            PerformanceFramework.ThreadedTest(MAXTHREADS, MAXITERATIONS, DoWrapperDb);
            Assert.Pass();
        }

        public Task<(long, long[])> DoWrapperDb(int threadno, int iterations)
        {

            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();

            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                // Retrieve the item
                var r = storage.Get<Item>(_keys[0]);
                Debug.Assert(r != null);

                timers[count] = sw.ElapsedMilliseconds;
            }

            return Task.FromResult((0L, timers));
        }
    }
}