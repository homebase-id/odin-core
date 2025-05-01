using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography.Crypto;

namespace Odin.Hosting.Tests.Performance
{

    public class PerformanceFramework
    {
        public static void PerformanceLog(int maxThreads, int maxIterations, long wallMilliseconds, long[] timerMsArray)
        {
            Console.WriteLine($"{DateTime.Today:yyyy-MM-dd} Host [{Dns.GetHostName()}]");
            Console.WriteLine($"Threads   : \t{maxThreads}");
            Console.WriteLine($"Iterations: \t{maxIterations:N0}");
            Console.WriteLine($"Wall Time : \t{wallMilliseconds:N0}\tms");
            Console.WriteLine($"Minimum   : \t{timerMsArray[0]:N0}\tms");
            Console.WriteLine($"Maximum   : \t{timerMsArray[maxThreads * maxIterations - 1]:N0}\tms");
            Console.WriteLine($"Average   : \t{timerMsArray.Sum() / (maxThreads * maxIterations):N0}\tms");
            Console.WriteLine($"Median    : \t{timerMsArray[(maxThreads * maxIterations) / 2]:N0}\tms");
            Console.WriteLine(
                $"Capacity  : \t{(1000 * maxIterations * maxThreads) / Math.Max(1, wallMilliseconds):N0}\t/ second");

            // Console.WriteLine($"RSA Encryptions {RsaKeyManagement.noEncryptions:N0}, Decryptions {RsaKeyManagement.noDecryptions:N0}");
            // Console.WriteLine($"RSA Keys Created {RsaKeyManagement.noKeysCreated:N0}, Keys Expired {RsaKeyManagement.noKeysExpired:N0}");
            // Console.WriteLine($"DB Opened {RsaKeyManagement.noDBOpened:N0}, Closed {RsaKeyManagement.noDBClosed:N0}");
        }


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
                    ClassicAssert.IsTrue(measurements.Length == iterations);
                    lock (timers)
                    {
                        timers.Add(measurements);
                        fileByteLength += bw;
                    }
                });
            }

            await Task.WhenAll(tasks);  // Wait for all tasks to complete

            sw.Stop();

            ClassicAssert.IsTrue(timers.Count == maxThreads);
            long[] oneDimensionalArray = timers.SelectMany(arr => arr).ToArray();
            ClassicAssert.IsTrue(oneDimensionalArray.Length == (maxThreads * iterations));

            Array.Sort(oneDimensionalArray);
            for (int i = 1; i < maxThreads * iterations; i++)
                ClassicAssert.IsTrue(oneDimensionalArray[i - 1] <= oneDimensionalArray[i]);

            PerformanceLog(maxThreads, iterations, sw.ElapsedMilliseconds, oneDimensionalArray);
            if (fileByteLength > 0)
                Console.WriteLine($"Bandwidth : \t{1000 * (fileByteLength / Math.Max(1, sw.ElapsedMilliseconds)):N0}\tbytes / second");
        }
    }
}