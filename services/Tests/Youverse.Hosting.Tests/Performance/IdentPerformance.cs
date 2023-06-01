using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
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
        public void TaskPerformanceTest_Ident()
        {
            PerformanceFramework.ThreadedTest(MAXTHREADS, MAXITERATIONS, DoIdent);
            Assert.Pass();
        }

        public async Task<(long, long[])> DoIdent(int threadno, int iterations)
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
        public void TaskPerformanceTest_PingAsOwner()
        {
            PerformanceFramework.ThreadedTest(MAXTHREADS, MAXITERATIONS, OwnerPing);
            Assert.Pass();
        }

        public async Task<(long, long[])> OwnerPing(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();

            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                var context = await ownerClient.Security.GetSecurityContext();
                Assert.IsFalse(string.IsNullOrEmpty(context.Caller.OdinId));

                timers[count] = sw.ElapsedMilliseconds;
            }

            return (0, timers);
        }
    }
}