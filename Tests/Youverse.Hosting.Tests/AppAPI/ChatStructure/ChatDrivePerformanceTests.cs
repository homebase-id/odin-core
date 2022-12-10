using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;
using Org.BouncyCastle.Security;
using Youverse.Core.Cryptography.Crypto;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Utilities.IO.Pem;
using System.Timers;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure
{
    public class ThreadedPerformanceTests
    {
        // For the performance test
        const int MAXTHREADS = 50;
        const int MAXITERATIONS = 1000;


        [Test]
        public async Task TaskPerformanceTest()
        {
            Task[] tasks = new Task[MAXTHREADS];
            List<long[]> timers = new List<long[]>();


            var sw = new Stopwatch();
            sw.Reset();
            sw.Start();

            for (var i = 0; i < MAXTHREADS; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var measurements = await DoSomeWork(i);
                    Debug.Assert(measurements.Length == MAXITERATIONS);
                    lock (timers) {
                        timers.Add(measurements);
                    }
                });
            }

            Task.WaitAll(tasks);
            sw.Stop();

            Debug.Assert(timers.Count == MAXTHREADS);
            long[] oneDimensionalArray = timers.SelectMany(arr => arr).ToArray();
            Debug.Assert(oneDimensionalArray.Length == (MAXTHREADS * MAXITERATIONS));

            Array.Sort(oneDimensionalArray);
            for (var i = 1; i < MAXTHREADS*MAXITERATIONS; i++)
                Debug.Assert(oneDimensionalArray[i-1] <= oneDimensionalArray[i]);

            Console.WriteLine($"Threads   : {MAXTHREADS}");
            Console.WriteLine($"Iterations: {MAXITERATIONS}");
            Console.WriteLine($"Time      : {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Minimum   : {oneDimensionalArray[0]}ms");
            Console.WriteLine($"Maximum   : {oneDimensionalArray[MAXTHREADS*MAXITERATIONS - 1]}ms");
            Console.WriteLine($"Average   : {oneDimensionalArray.Sum() / (MAXTHREADS*MAXITERATIONS)}ms");
            Console.WriteLine($"Median    : {oneDimensionalArray[(MAXTHREADS*MAXITERATIONS)/2]}ms");

            Console.WriteLine($"Capacity  : {(1000 * MAXITERATIONS * MAXTHREADS) / Math.Max(1, sw.ElapsedMilliseconds)} / second");
        }


        public async Task<long[]> DoSomeWork(int threadNo)
        {
            long[] timers = new long[MAXITERATIONS];
            Debug.Assert(timers.Length == MAXITERATIONS);
            var sw = new Stopwatch();

            for (int count = 0; count < MAXITERATIONS; count++)
            {
                sw.Restart();

                // Do all the work here

                //
                // Suggestion that you first simply try to load a static URL here.
                //


                // Finished doing all the work

                timers[count] = sw.ElapsedMilliseconds;
                //
                // If you want to introduce a delay be sure to use: await Task.Delay(1);
            }

            return timers;
        }
    }
}