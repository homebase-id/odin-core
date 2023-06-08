using System;
using System.Net;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Youverse.Core.Cryptography.Crypto;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Threading;

namespace Youverse.Hosting.Tests.Performance
{

    public class PerformanceFramework
    {
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


        public static async Task<(long, long[])> ExecuteFunction(int threadno, int iterations, Func<int, int, Task<(long, long[])>> functionToExecute)
        {
            // Call the function with the provided parameters
            var (l, result) = await functionToExecute(threadno, iterations);

            // Perform any additional operations or transformations on the result
            // ...

            return (l, result);
        }


        public static void ThreadedTest(int maxThreads, int iterations, Func<int, int, Task<(long, long[])>> functionToExecute)
        {
            // var tasks = new Task[maxThreads];
            List<long[]> timers = new List<long[]>();
            long fileByteLength = 0;

            var sw = new Stopwatch();
            sw.Reset();
            sw.Start();

            Thread[] threads = new Thread[maxThreads];

            for (var i = 0; i < maxThreads; i++)
            {
                int threadIndex = i;

                threads[i] = new Thread(() =>
                {
                    var resultTuple = functionToExecute(threadIndex, iterations);
                    long bw = resultTuple.Result.Item1;
                    long[] measurements = resultTuple.Result.Item2;
                    Debug.Assert(measurements.Length == iterations);
                    lock (timers)
                    {
                        timers.Add(measurements);
                        fileByteLength += bw;
                    }
                });

                threads[i].Start();
            }

            // Join the threads
            for (var i = 0; i < maxThreads; i++)
            {
                threads[i].Join();
            }

            // [snip]

            sw.Stop();

            Debug.Assert(timers.Count == maxThreads);
            long[] oneDimensionalArray = timers.SelectMany(arr => arr).ToArray();
            Debug.Assert(oneDimensionalArray.Length == (maxThreads * iterations));

            Array.Sort(oneDimensionalArray);
            for (var i = 1; i < maxThreads * iterations; i++)
                Debug.Assert(oneDimensionalArray[i - 1] <= oneDimensionalArray[i]);

            PerformanceLog(maxThreads, iterations, sw.ElapsedMilliseconds, oneDimensionalArray);
            if (fileByteLength > 0)
                Console.WriteLine($"Bandwidth : {1000 * (fileByteLength / Math.Max(1, sw.ElapsedMilliseconds)):N0} bytes / second");
        }
    }
}