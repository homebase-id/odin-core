using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;

// In TryRetry CancellationToken parameters must be explicit and action should always be last parameter
#pragma warning disable CA1068

public static class TryRetry
{
    private static readonly TimeSpan DefaultExponentialMs = TimeSpan.FromMilliseconds(100);
    private static readonly Random Random = new();

    //
    // SYNC
    //

    public static int WithDelay(
        int attempts,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Action action)
    {
        return WithDelay<Exception>(attempts, (delay, delay), cancellationToken, action);
    }

    public static int WithDelay(
        int attempts,
        ValueTuple<TimeSpan, TimeSpan> randomDelay,
        CancellationToken cancellationToken,
        Action action)
    {
        return WithDelay<Exception>(attempts, randomDelay, cancellationToken, action);
    }

    public static int WithDelay<T>(
        int attempts,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Action action) where T : Exception
    {
        return WithDelay<T>(attempts, (delay, delay), cancellationToken, action);
    }

    public static int WithDelay<T>(
        int attempts,
        ValueTuple<TimeSpan, TimeSpan> randomDelay,
        CancellationToken cancellationToken,
        Action action) where T : Exception
    {
        if (attempts < 1)
        {
            throw new ArgumentException("attempts must be greater than 0");
        }

        if (randomDelay.Item1 > randomDelay.Item2)
        {
            throw new ArgumentException("randomDelay.Item1 must be less than or equal to randomDelay.Item2");
        }

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempt++;
            try
            {
                action();
                return attempt;
            }
            catch(T e)
            {
                if (attempt == attempts)
                {
                    throw new TryRetryException($"{e.Message} (giving up after {attempts} attempt(s))", e);
                }
                var delay1 = (int)randomDelay.Item1.TotalMilliseconds;
                var delay2 = (int)randomDelay.Item2.TotalMilliseconds;
                Thread.Sleep(Random.Next(delay1, delay2));
            }
        }

    }

    public static int WithBackoff(
        int attempts,
        Action action,
        CancellationToken cancellationToken)
    {
        return WithBackoff<Exception>(attempts, DefaultExponentialMs, cancellationToken, action);
    }

    public static int WithBackoff<T>(
        int attempts,
        CancellationToken cancellationToken,
        Action action) where T : Exception
    {
        return WithBackoff<T>(attempts, DefaultExponentialMs, cancellationToken, action);
    }

    public static int WithBackoff(
        int attempts,
        TimeSpan exponentialBackoff,
        CancellationToken cancellationToken,
        Action action)
    {
        return WithBackoff<Exception>(attempts, exponentialBackoff, cancellationToken, action);
    }

    public static int WithBackoff<T>(
        int attempts,
        TimeSpan exponentialBackoff,
        CancellationToken cancellationToken,
        Action action) where T : Exception
    {
        if (attempts < 1)
        {
            throw new ArgumentException("attempts must be greater than 0");
        }

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempt++;
            try
            {
                action();
                return attempt;
            }
            catch(T e)
            {
                if (attempt == attempts)
                {
                    throw new TryRetryException($"{e.Message} (giving up after {attempts} attempt(s))", e);
                }
                var ms = (int)(Math.Pow(2, attempt - 1) * exponentialBackoff.TotalMilliseconds);
                Thread.Sleep(ms);
            }
        }
    }

    //
    // ASYNC
    //

    public static Task<int> WithDelayAsync(
        int attempts,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, (delay, delay), cancellationToken, action);
    }

    public static Task<int> WithDelayAsync(
        int attempts,
        ValueTuple<TimeSpan, TimeSpan> randomDelay,
        CancellationToken cancellationToken,
        Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, randomDelay, cancellationToken, action);
    }

    public static Task<int> WithDelayAsync<T>(
        int attempts,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Func<Task> action) where T : Exception
    {
        return WithDelayAsync<T>(attempts, (delay, delay), cancellationToken, action);
    }

    public static async Task<int> WithDelayAsync<T>(
        int attempts,
        ValueTuple<TimeSpan, TimeSpan> randomDelay,
        CancellationToken cancellationToken,
        Func<Task> action) where T : Exception
    {
        if (attempts < 1)
        {
            throw new ArgumentException("attempts must be greater than 0");
        }

        if (randomDelay.Item1 > randomDelay.Item2)
        {
            throw new ArgumentException("randomDelay.Item1 must be less than or equal to randomDelay.Item2");
        }

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempt++;
            try
            {
                await action();
                return attempt;
            }
            catch(T e)
            {
                if (attempt == attempts)
                {
                    throw new TryRetryException($"{e.Message} (giving up after {attempts} attempt(s))", e);
                }
                var delay1 = (int)randomDelay.Item1.TotalMilliseconds;
                var delay2 = (int)randomDelay.Item2.TotalMilliseconds;
                await Task.Delay(Random.Next(delay1, delay2), cancellationToken);
            }
        }
    }

    public static Task<int> WithBackoffAsync(
        int attempts,
        CancellationToken cancellationToken,
        Func<Task> action)
    {
        return WithBackoffAsync<Exception>(attempts, DefaultExponentialMs, cancellationToken, action);
    }

    public static Task<int> WithBackoffAsync<T>(
        int attempts,
        CancellationToken cancellationToken,
        Func<Task> action) where T : Exception
    {
        return WithBackoffAsync<T>(attempts, DefaultExponentialMs, cancellationToken, action);
    }

    public static Task<int> WithBackoffAsync(
        int attempts,
        TimeSpan exponentialBackoff,
        CancellationToken cancellationToken,
        Func<Task> action)
    {
        return WithBackoffAsync<Exception>(attempts, exponentialBackoff, cancellationToken, action);
    }

    public static async Task<int> WithBackoffAsync<T>(
        int attempts,
        TimeSpan exponentialBackoff,
        CancellationToken cancellationToken,
        Func<Task> action) where T : Exception
    {
        if (attempts < 1)
        {
            throw new ArgumentException("attempts must be greater than 0");
        }

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempt++;
            try
            {
                await action();
                return attempt;
            }
            catch(T e)
            {
                if (attempt == attempts)
                {
                    throw new TryRetryException($"{e.Message} (giving up after {attempts} attempt(s))", e);
                }
                var ms = (int)(Math.Pow(2, attempt - 1) * exponentialBackoff.TotalMilliseconds);
                await Task.Delay(ms, cancellationToken);
            }
        }
    }

    //

}
#pragma warning restore CA1068

public class TryRetryException(string message, Exception innerException) : OdinSystemException(message, innerException);