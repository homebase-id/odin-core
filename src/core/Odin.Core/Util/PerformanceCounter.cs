using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Odin.Core.Util;

public static class PerformanceCounter
{
    private static readonly ConcurrentDictionary<string, List<long>> _timers = new();
    private static readonly ConcurrentDictionary<string, long> _counters = new();

    public static async Task<long> MeasureExecutionTime(string key, Func<Task> task)
    {
        var ms = await Benchmark.MillisecondsAsync(task);
        AddExecutionTime(key, ms);
        return ms;
    }

    public static void AddExecutionTime(string key, long milliseconds)
    {
#if DEBUG
        _timers.AddOrUpdate(key,
            k => [milliseconds],
            (k, existingList) =>
            {
                lock (existingList)
                {
                    existingList.Add(milliseconds);
                }

                return existingList;
            });
#endif
    }

    public static void IncrementCounter(string key)
    {
#if DEBUG
        _counters.AddOrUpdate(key, 1, (k, v) => v + 1);
#endif
    }

    public static double GetAverageTime(string key)
    {
#if DEBUG
        _timers.TryGetValue(key, out List<long> values);
        return values?.Average() ?? throw new Exception("Key not found");
#endif
    }

    public static long GetCounter(string key)
    {
#if DEBUG
        _counters.TryGetValue(key, out long value);
        return value;
#endif
    }

    public static void WriteCounters()
    {
#if DEBUG
        Console.WriteLine("Counters:");


        foreach (var (key, value) in _counters.OrderBy(kvp => kvp.Key))
        {
            Console.WriteLine($"\t{key}: {value}");
        }

        Console.WriteLine();

        Console.WriteLine("Timers:");

        foreach (var (key, values) in _timers.OrderBy(kvp => kvp.Key))
        {
            var average = values.Average();
            double sumOfSquaresOfDifferences = values.Select(val => (val - average) * (val - average)).Sum();
            double variance = sumOfSquaresOfDifferences / values.Count;

            Console.WriteLine($"\t{key} (cnt)     : {values.Count}");
            Console.WriteLine($"\t{key} (avg)     : {average:F2}ms");
            Console.WriteLine($"\t{key} (min)     : {values.Min()}ms");
            Console.WriteLine($"\t{key} (max)     : {values.Max()}ms");
            Console.WriteLine($"\t{key} (std-dev) : {Math.Sqrt(variance):F2}ms");
            Console.WriteLine();
        }
#endif
    }
}