using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Util
{
    public static class Benchmark
    {
        public const long DefaultLogThresholdMs = 0;

        public static void StopWatchStatus(string s, Stopwatch stopWatch)
        {
            TimeSpan ts = stopWatch.Elapsed;

            // Format and display the TimeSpan value.
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                ts.Hours, ts.Minutes, ts.Seconds,
                ts.Milliseconds / 10);

            Console.WriteLine(elapsedTime + " : " + s);
            stopWatch.Reset();
        }

        public static void Log(ILogger logger, string context, long ms)
        {
            logger.LogInformation("Benchmark {Benchmark} {BenchmarkMilliseconds}ms", context, ms);
        }

        public static long Milliseconds(
            ILogger logger,
            string context,
            Action action,
            long logThresholdMs = DefaultLogThresholdMs)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            var elapsed = stopwatch.ElapsedMilliseconds;
            if (logger != null && context != null && elapsed >= logThresholdMs)
            {
                Log(logger, context, elapsed);
            }
            return elapsed;
        }

        public static long Milliseconds(Action action)
        {
            return Milliseconds(null, null, action);
        }

        public static T Milliseconds<T>(
            ILogger logger,
            string context,
            Func<T> func,
            long logThresholdMs = DefaultLogThresholdMs)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = func();
            var elapsed = stopwatch.ElapsedMilliseconds;
            if (logger != null && context != null && elapsed >= logThresholdMs)
            {
                Log(logger, context, elapsed);
            }
            return result;
        }

        public static async Task<long> MillisecondsAsync(
            ILogger logger,
            string context,
            Func<Task> task,
            long logThresholdMs = DefaultLogThresholdMs)
        {
            var stopwatch = Stopwatch.StartNew();
            await task();
            var elapsed = stopwatch.ElapsedMilliseconds;
            if (logger != null && context != null && elapsed >= logThresholdMs)
            {
                Log(logger, context, elapsed);
            }
            return elapsed;
        }

        public static Task<long> MillisecondsAsync(Func<Task> task, long logThresholdMs = DefaultLogThresholdMs)
        {
            return MillisecondsAsync(null, null, task, logThresholdMs);
        }

        public static async Task<T> MillisecondsAsync<T>(
            ILogger logger,
            string context,
            Func<Task<T>> task,
            long logThresholdMs = DefaultLogThresholdMs)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await task();
            var elapsed = stopwatch.ElapsedMilliseconds;
            if (logger != null && context != null && elapsed >= logThresholdMs)
            {
                Log(logger, context, elapsed);
            }
            return result;
        }
    }
}
