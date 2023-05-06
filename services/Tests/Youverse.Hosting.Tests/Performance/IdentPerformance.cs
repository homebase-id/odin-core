using System;
using System.Net;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Hosting.Tests.Anonymous.Ident;


namespace Youverse.Hosting.Tests.Performance
{

    public class IdentPerformanceTests
    {

        // For the performance test
        private static readonly int MAXTHREADS = 16; // Should be at least 2 * your CPU cores. Can still be nice to test sometimes with lower. And not too high.
        private const int MAXITERATIONS = 15000; // A number high enough to get warmed up and reliable

        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }


        /*
             TaskPerformanceTest_Ident
               Duration: 9.9 sec

              Standard Output: 
                2023-05-06 [SEMIBEASTII] Ident API test, anonymous
                Threads   : 16
                Iterations: 15000
                Time      : 9898ms
                Minimum   : 0ms
                Maximum   : 33ms
                Average   : 0ms
                Median    : 0ms
                Capacity  : 24247 / second
                RSA Encryptions 0, Decryptions 8
                RSA Keys Created 4, Keys Expired 0
                DB Opened 4, Closed 0    
          */

        [Test]
        public async Task TaskPerformanceTest_Ident()
        {
            Task[] tasks = new Task[MAXTHREADS];
            List<long[]> timers = new List<long[]>();

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
                    var measurements = await DoIdent(i, MAXITERATIONS);
                    Debug.Assert(measurements.Length == MAXITERATIONS);
                    lock (timers)
                    {
                        timers.Add(measurements);
                    }
                });
            }

            try
            {
                await Task.WhenAll(tasks);
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

            Debug.Assert(timers.Count == MAXTHREADS);
            long[] oneDimensionalArray = timers.SelectMany(arr => arr).ToArray();
            Debug.Assert(oneDimensionalArray.Length == (MAXTHREADS * MAXITERATIONS));

            Array.Sort(oneDimensionalArray);
            for (var i = 1; i < MAXTHREADS * MAXITERATIONS; i++)
                Debug.Assert(oneDimensionalArray[i - 1] <= oneDimensionalArray[i]);

            Console.WriteLine($"{DateTime.Today:yyyy-MM-dd} [{Dns.GetHostName()}] Ident API test, anonymous");
            Console.WriteLine($"Threads   : {MAXTHREADS}");
            Console.WriteLine($"Iterations: {MAXITERATIONS}");
            Console.WriteLine($"Time      : {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Minimum   : {oneDimensionalArray[0]}ms");
            Console.WriteLine($"Maximum   : {oneDimensionalArray[MAXTHREADS * MAXITERATIONS - 1]}ms");
            Console.WriteLine($"Average   : {oneDimensionalArray.Sum() / (MAXTHREADS * MAXITERATIONS)}ms");
            Console.WriteLine($"Median    : {oneDimensionalArray[(MAXTHREADS * MAXITERATIONS) / 2]}ms");

            Console.WriteLine(
                $"Capacity  : {(1000 * MAXITERATIONS * MAXTHREADS) / Math.Max(1, sw.ElapsedMilliseconds)} / second");

            Console.WriteLine($"RSA Encryptions {RsaKeyManagement.noEncryptions}, Decryptions {RsaKeyManagement.noDecryptions}");
            Console.WriteLine($"RSA Keys Created {RsaKeyManagement.noKeysCreated}, Keys Expired {RsaKeyManagement.noKeysExpired}");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened}, Closed {RsaKeyManagement.noDBClosed}");
        }


        public async Task<long[]> DoIdent(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();

            var anonClient = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Samwise.OdinId);

            var svc = RestService.For<IIdentHttpClient>(anonClient);


            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                var identResponse = await svc.GetIdent();
                var ident = identResponse.Content;
                Assert.IsFalse(string.IsNullOrEmpty(ident.OdinId));
                Assert.IsTrue(ident.Version == 1.0);

                timers[count] = sw.ElapsedMilliseconds;
            }

            return timers;
        }
    }
}