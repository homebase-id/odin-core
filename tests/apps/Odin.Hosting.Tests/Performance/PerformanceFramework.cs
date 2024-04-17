using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Hosting.Tests.Performance
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


/*        public static void ThreadedTest(int maxThreads, int iterations, Func<int, int, Task<(long, long[])>> functionToExecute)
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
*/

        public static async Task ThreadedTestAsync(int maxThreads, int iterations, Func<int, int, Task<(long, long[])>> functionToExecute)
        {
            List<long[]> timers = new List<long[]>();
            long fileByteLength = 0;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            Task[] tasks = new Task[maxThreads];

            for (int i = 0; i < maxThreads; i++)
            {
                int threadIndex = i;  // Capture the current index for use in the lambda

                tasks[i] = Task.Run(async () =>
                {
                    (long bw, long[] measurements) = await functionToExecute(threadIndex, iterations);
                    Debug.Assert(measurements.Length == iterations);
                    lock (timers)
                    {
                        timers.Add(measurements);
                        fileByteLength += bw;
                    }
                });
            }

            await Task.WhenAll(tasks);  // Wait for all tasks to complete

            sw.Stop();

            Debug.Assert(timers.Count == maxThreads);
            long[] oneDimensionalArray = timers.SelectMany(arr => arr).ToArray();
            Debug.Assert(oneDimensionalArray.Length == (maxThreads * iterations));

            Array.Sort(oneDimensionalArray);
            for (int i = 1; i < maxThreads * iterations; i++)
                Debug.Assert(oneDimensionalArray[i - 1] <= oneDimensionalArray[i]);

            PerformanceLog(maxThreads, iterations, sw.ElapsedMilliseconds, oneDimensionalArray);
            if (fileByteLength > 0)
                Console.WriteLine($"Bandwidth : {1000 * (fileByteLength / Math.Max(1, sw.ElapsedMilliseconds)):N0} bytes / second");
        }
    }
}