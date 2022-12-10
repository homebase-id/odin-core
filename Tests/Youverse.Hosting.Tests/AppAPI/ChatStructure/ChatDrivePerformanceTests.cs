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
    public class ChatDrivePerformanceTests
    {
        // For the performance test
        const int MAXTHREADS = 8;
        const int MAXITERATIONS = 1000;


        [Test]
        public async Task PerformanceTest()
        {
            Task[] tasks = new Task[MAXTHREADS];
            List<long[]> timers = new List<long[]>();

            var sw = new Stopwatch();
            sw.Start();

            for (var i = 0; i < MAXTHREADS; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var measurements = await DoSomeWork(i);
                    Debug.Assert(measurements.Length == MAXITERATIONS);
                    timers.Add(measurements);
                });
            }

            sw.Stop();

            Task.WaitAll(tasks);

            long[] oneDimensionalArray = timers.SelectMany(arr => arr).ToArray();

            // timers.CopyTo(oneDimensionalArray, 0);
            Array.Sort(oneDimensionalArray);
            Console.WriteLine($"Total  : {oneDimensionalArray.Sum()}ms\n");
            Console.WriteLine($"Minimum: {oneDimensionalArray[0]}ms\n");
            Console.WriteLine($"Maximum: {oneDimensionalArray[MAXITERATIONS - 1]}ms\n");
            Console.WriteLine($"Average: {oneDimensionalArray.Sum() / (MAXTHREADS*MAXITERATIONS)}ms\n");
            Console.WriteLine($"\n");
            Console.WriteLine($"Median : {oneDimensionalArray[(MAXTHREADS*MAXITERATIONS)/2]}ms\n");
            if(sw.ElapsedMilliseconds>0)
            {
                Console.WriteLine($"Per sec: {(1000 * MAXITERATIONS * MAXTHREADS) / sw.ElapsedMilliseconds}ms\n");
            }
        }


        public async Task<long[]> DoSomeWork(int threadNo)
        {
            long[] timers = new long[MAXITERATIONS];
            Debug.Assert(timers.Length == MAXITERATIONS);
            var sw = new Stopwatch();

            for (int count = 0; count < MAXITERATIONS; count++)
            {
                sw.Start();
                // Do all the work here
                Thread.Sleep(10);
                // Finished doing all the work

                sw.Stop();
                timers[count] = sw.ElapsedMilliseconds;
            }

            return timers;
        }
    }
}