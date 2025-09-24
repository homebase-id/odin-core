using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests.Anonymous.Ident;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests.Performance
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

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
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

            No change after adding database caching
          */

        [Test]
        public async Task TaskPerformanceTest_Ident()
        {
            await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, DoIdent);
            Assert.Pass();
        }

        [Test]
        [Explicit]
        public void TaskPerformanceTest_CreateHobbits()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;

            var clock = Stopwatch.StartNew();

            var testIdentities = new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Collab, TestIdentities.Pippin, TestIdentities.Merry,
              TestIdentities.TomBombadil };
            TestIdentities.SetCurrent(testIdentities);

            var _oldOwnerApi = new OwnerApiTestUtils(Guid.NewGuid());

            Parallel.ForEach(TestIdentities.InitializedIdentities.Values.Select(i => i.OdinId),
                odinId => { _oldOwnerApi.SetupOwnerAccount(odinId, true).GetAwaiter().GetResult(); });

            var elapsedTime = clock.Elapsed;

            Assert.Pass();
        }

        public async Task<(long, long[])> DoIdent(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            ClassicAssert.IsTrue(timers.Length == iterations);
            var sw = new Stopwatch();

            var anonClient = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Samwise.OdinId);

            var svc = RestService.For<IIdentHttpClient>(anonClient);


            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                var identResponse = await svc.GetIdent();
                var ident = identResponse.Content;
                ClassicAssert.IsFalse(string.IsNullOrEmpty(ident.OdinId));
                ClassicAssert.IsTrue(ident.Version == 1.0);

                timers[count] = sw.ElapsedMilliseconds;
            }

            return (0, timers);
        }


        /*
         *  TaskPerformanceTest_PingAsOwner
               Duration: 2.4 sec

              Standard Output: 
                2023-05-25 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 150
                Wall Time : 2,410ms
                Minimum   : 3ms
                Maximum   : 86ms
                Average   : 14ms
                Median    : 11ms
                Capacity  : 746 / second
                RSA Encryptions 0, Decryptions 8
                RSA Keys Created 4, Keys Expired 0
                DB Opened 4, Closed 0

        After adding Database caching

             TaskPerformanceTest_PingAsOwner
               Duration: 1.4 min

              Standard Output: 
                2023-06-01 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 15,000
                Wall Time : 81,118ms
                Minimum   : 1ms
                Maximum   : 60ms
                Average   : 4ms
                Median    : 2ms
                Capacity  : 2,218 / second
                RSA Encryptions 0, Decryptions 8
                RSA Keys Created 4, Keys Expired 0
                DB Opened 4, Closed 0
         */

        [Test, Explicit]
        public async Task TaskPerformanceTest_PingAsOwner()
        {
            await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, OwnerPing);
            Assert.Pass();
        }

        public async Task<(long, long[])> OwnerPing(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            ClassicAssert.IsTrue(timers.Length == iterations);
            var sw = new Stopwatch();

            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                var context = await ownerClient.Security.GetSecurityContext();
                ClassicAssert.IsFalse(string.IsNullOrEmpty(context.Caller.OdinId));

                timers[count] = sw.ElapsedMilliseconds;
            }

            return (0, timers);
        }


        [Test, Explicit]
        public async Task TaskPerformanceTest_PingHttpOnly()
        {
            await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, HttpOnlyPing);
            Assert.Pass();
        }

        public async Task<(long, long[])> HttpOnlyPing(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            ClassicAssert.IsTrue(timers.Length == iterations);
            var sw = new Stopwatch();

            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            HttpClient client = new HttpClient();
            string url = "http://frodo.dotyou.cloud/.well-known/acme-challenge/ping";

            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                try
                {
                    HttpResponseMessage response = await client.GetAsync(url);
                    //response.EnsureSuccessStatusCode();
                    // string responseBody = await response.Content.ReadAsStringAsync();

                    // TODO: Process the response body.
                    // Console.WriteLine(responseBody);
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine("\nException Caught!");
                    Console.WriteLine("Message: {0} ", e.Message);
                }

                timers[count] = sw.ElapsedMilliseconds;
            }

            return (0, timers);
        }


    }
}