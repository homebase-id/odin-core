using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;

public static class TryRetry
{
    private static readonly TimeSpan DefaultExponentialMs = TimeSpan.FromMilliseconds(100);
    private static readonly Random Random = new();

    //
    // SYNC
    //

    public static void WithDelay(int attempts, TimeSpan delay, Action action)
    {
        WithDelay<Exception>(attempts, (delay, delay), action);
    }

    public static void WithDelay(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Action action)
    {
        WithDelay<Exception>(attempts, randomDelay, action);
    }

    public static void WithDelay<T>(int attempts, TimeSpan delay, Action action) where T : Exception
    {
        WithDelay<T>(attempts, (delay, delay), action);
    }

    public static void WithDelay<T>(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Action action) where T : Exception
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
        while (attempt < attempts)
        {
            attempt++;
            try
            {
                action();
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

    public static void WithBackoff(int attempts, Action action)
    {
        WithBackoff<Exception>(attempts, DefaultExponentialMs, action);
    }

    public static void WithBackoff<T>(int attempts, Action action) where T : Exception
    {
        WithBackoff<T>(attempts, DefaultExponentialMs, action);
    }

    public static void WithBackoff(int attempts, TimeSpan exponentialBackoff, Action action)
    {
        WithBackoff<Exception>(attempts, exponentialBackoff, action);
    }

    public static void WithBackoff<T>(int attempts, TimeSpan exponentialBackoff, Action action) where T : Exception
    {
        if (attempts < 1)
        {
            throw new ArgumentException("attempts must be greater than 0");
        }

        var attempt = 0;
        while (attempt < attempts)
        {
            attempt++;
            try
            {
                action();
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

    public static Task WithDelayAsync(int attempts, TimeSpan delay, Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, (delay, delay), action);
    }

    public static Task WithDelayAsync(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, randomDelay, action);
    }

    public static Task WithDelayAsync<T>(int attempts, TimeSpan delay, Func<Task> action) where T : Exception
    {
        return WithDelayAsync<T>(attempts, (delay, delay), action);
    }

    public static async Task WithDelayAsync<T>(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Func<Task> action) where T : Exception
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
        while (attempt < attempts)
        {
            attempt++;
            try
            {
                await action();
            }
            catch(T e)
            {
                if (attempt == attempts)
                {
                    throw new TryRetryException($"{e.Message} (giving up after {attempts} attempt(s))", e);
                }
                var delay1 = (int)randomDelay.Item1.TotalMilliseconds;
                var delay2 = (int)randomDelay.Item2.TotalMilliseconds;
                await Task.Delay(Random.Next(delay1, delay2));
            }
        }
    }

    public static Task WithBackoffAsync(int attempts, Func<Task> action)
    {
        return WithBackoffAsync<Exception>(attempts, DefaultExponentialMs, action);
    }

    public static Task WithBackoffAsync<T>(int attempts, Func<Task> action) where T : Exception
    {
        return WithBackoffAsync<T>(attempts, DefaultExponentialMs, action);
    }

    public static Task WithBackoffAsync(int attempts, TimeSpan exponentialBackoff, Func<Task> action)
    {
        return WithBackoffAsync<Exception>(attempts, exponentialBackoff, action);
    }

    public static async Task WithBackoffAsync<T>(int attempts, TimeSpan exponentialBackoff, Func<Task> action) where T : Exception
    {
        if (attempts < 1)
        {
            throw new ArgumentException("attempts must be greater than 0");
        }

        var attempt = 0;
        while (attempt < attempts)
        {
            attempt++;
            try
            {
                await action();
            }
            catch(T e)
            {
                if (attempt == attempts)
                {
                    throw new TryRetryException($"{e.Message} (giving up after {attempts} attempt(s))", e);
                }
                var ms = (int)(Math.Pow(2, attempt - 1) * exponentialBackoff.TotalMilliseconds);
                await Task.Delay(ms);
            }
        }
    }

    //

}

public class TryRetryException(string message, Exception innerException) : OdinSystemException(message, innerException);


