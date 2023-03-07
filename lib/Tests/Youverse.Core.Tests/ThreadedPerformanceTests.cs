using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Youverse.Core.Tests
{
    public class ThreadedPerformanceTests
    {
        // For the performance test
        const int MAXTHREADS = 50;   // Should be at least 2 * your CPU cores. Can still be nice to test sometimes with lower. And not too high.
        const int MAXITERATIONS = 1000;  // A number high enough to get warmed up and reliable


        [Test]
        public void TaskPerformanceTest()
        {
            Task[] tasks = new Task[MAXTHREADS];
            List<long[]> timers = new List<long[]>();

            var sw = new Stopwatch();
            sw.Reset();
            sw.Start();

            for (var i = 0; i < MAXTHREADS; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    var measurements = DoSomeWork(i);
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


        public long[] DoSomeWork(int threadNo)
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
                // Take.Delay() is very inaccurate.
            }

            return timers;
        }
    }
}