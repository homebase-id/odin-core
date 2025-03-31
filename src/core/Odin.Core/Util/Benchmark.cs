using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Odin.Core.Util;

public static class Benchmark
{
    private const LogLevel DefaultLogLevel = LogLevel.Debug;
    private const long DefaultLogThresholdMs = 0;

    //

    private static void Log(ILogger? logger, LogLevel level, string context, long ms)
    {
        if (logger != null && logger.IsEnabled(level))
        {
            logger.Log(level, "Benchmark {Benchmark} {BenchmarkMilliseconds}ms", context, ms);
        }
    }

    //

    public static long Milliseconds(
        ILogger? logger,
        string? context,
        Action action,
        LogLevel logLevel = DefaultLogLevel,
        long logThresholdMs = DefaultLogThresholdMs)
    {
        var stopwatch = Stopwatch.StartNew();
        action();
        var elapsed = stopwatch.ElapsedMilliseconds;
        if (context != null && elapsed >= logThresholdMs)
        {
            Log(logger, logLevel, context, elapsed);
        }
        return elapsed;
    }

    //

    public static long Milliseconds(Action action)
    {
        return Milliseconds(null, null, action);
    }

    //

    public static T Milliseconds<T>(
        ILogger? logger,
        string? context,
        Func<T> func,
        LogLevel logLevel = DefaultLogLevel,
        long logThresholdMs = DefaultLogThresholdMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = func();
        var elapsed = stopwatch.ElapsedMilliseconds;
        if (context != null && elapsed >= logThresholdMs)
        {
            Log(logger, logLevel, context, elapsed);
        }
        return result;
    }

    //

    public static async Task<long> MillisecondsAsync(
        ILogger? logger,
        string? context,
        Func<Task> task,
        LogLevel logLevel = DefaultLogLevel,
        long logThresholdMs = DefaultLogThresholdMs)
    {
        var stopwatch = Stopwatch.StartNew();
        await task();
        var elapsed = stopwatch.ElapsedMilliseconds;
        if (context != null && elapsed >= logThresholdMs)
        {
            Log(logger, logLevel, context, elapsed);
        }
        return elapsed;
    }

    //

    public static Task<long> MillisecondsAsync(Func<Task> task, long logThresholdMs = DefaultLogThresholdMs)
    {
        return MillisecondsAsync(null, null, task, DefaultLogLevel, logThresholdMs);
    }

    //

    public static async Task<T> MillisecondsAsync<T>(
        ILogger? logger,
        string? context,
        Func<Task<T>> task,
        LogLevel logLevel = DefaultLogLevel,
        long logThresholdMs = DefaultLogThresholdMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await task();
        var elapsed = stopwatch.ElapsedMilliseconds;
        if (context != null && elapsed >= logThresholdMs)
        {
            Log(logger, logLevel, context, elapsed);
        }
        return result;
    }
}
