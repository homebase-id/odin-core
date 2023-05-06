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
        private static readonly int MAXTHREADS = 12;
        private const int MAXITERATIONS = 150;

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

        public static void PerformanceLog(int maxThreads, int maxIterations, long wallMilliseconds, long[] timerMsArray)
        {
            Console.WriteLine($"{DateTime.Today:yyyy-MM-dd} Host [{Dns.GetHostName()}]");
            Console.WriteLine($"Threads   : {maxThreads}");
            Console.WriteLine($"Iterations: {maxIterations:N0}");
            Console.WriteLine($"Wall Time : {wallMilliseconds:N0}ms");
            Console.WriteLine($"Minimum   : {timerMsArray[0]:N0}ms");
            Console.WriteLine($"Maximum   : {timerMsArray[maxThreads * maxIterations - 1]:N0}ms");
            Console.WriteLine($"Average   : {timerMsArray.Sum() / (maxThreads * maxIterations):N0}ms");
            Console.WriteLine($"Median    : {timerMsArray[(maxThreads * maxIterations) / 2]:N0}ms");
            Console.WriteLine(
                $"Capacity  : {(1000 * maxIterations * maxThreads) / Math.Max(1, wallMilliseconds):N0} / second");

            Console.WriteLine($"RSA Encryptions {RsaKeyManagement.noEncryptions:N0}, Decryptions {RsaKeyManagement.noDecryptions:N0}");
            Console.WriteLine($"RSA Keys Created {RsaKeyManagement.noKeysCreated:N0}, Keys Expired {RsaKeyManagement.noKeysExpired:N0}");
            Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened:N0}, Closed {RsaKeyManagement.noDBClosed:N0}");
        }

        /*
         TaskPerformanceTest_Ident
           Duration: 7.8 sec

          Standard Output: 
            2023-05-06 Host [SEMIBEASTII]
            Threads   : 12
            Iterations: 15,000
            Wall Time : 7,740ms
            Minimum   : 0ms
            Maximum   : 34ms
            Average   : 0ms
            Median    : 0ms
            Capacity  : 23,255 / second
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

            PerformanceLog(MAXTHREADS, MAXITERATIONS, sw.ElapsedMilliseconds, oneDimensionalArray);
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