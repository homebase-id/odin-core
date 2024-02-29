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

    public static int WithDelay(int attempts, TimeSpan delay, Action action)
    {
        return WithDelay<Exception>(attempts, (delay, delay), action);
    }

    public static int WithDelay(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Action action)
    {
        return WithDelay<Exception>(attempts, randomDelay, action);
    }

    public static int WithDelay<T>(int attempts, TimeSpan delay, Action action) where T : Exception
    {
        return WithDelay<T>(attempts, (delay, delay), action);
    }

    public static int WithDelay<T>(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Action action) where T : Exception
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

        return attempt;
    }

    public static int WithBackoff(int attempts, Action action)
    {
        return WithBackoff<Exception>(attempts, DefaultExponentialMs, action);
    }

    public static int WithBackoff<T>(int attempts, Action action) where T : Exception
    {
        return WithBackoff<T>(attempts, DefaultExponentialMs, action);
    }

    public static int WithBackoff(int attempts, TimeSpan exponentialBackoff, Action action)
    {
        return WithBackoff<Exception>(attempts, exponentialBackoff, action);
    }

    public static int WithBackoff<T>(int attempts, TimeSpan exponentialBackoff, Action action) where T : Exception
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

        return attempt;
    }

    //
    // ASYNC
    //

    public static Task<int> WithDelayAsync(int attempts, TimeSpan delay, Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, (delay, delay), action);
    }

    public static Task<int> WithDelayAsync(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, randomDelay, action);
    }

    public static Task<int> WithDelayAsync<T>(int attempts, TimeSpan delay, Func<Task> action) where T : Exception
    {
        return WithDelayAsync<T>(attempts, (delay, delay), action);
    }

    public static async Task<int> WithDelayAsync<T>(int attempts, ValueTuple<TimeSpan, TimeSpan> randomDelay, Func<Task> action) where T : Exception
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

        return attempt;
    }

    public static Task<int> WithBackoffAsync(int attempts, Func<Task> action)
    {
        return WithBackoffAsync<Exception>(attempts, DefaultExponentialMs, action);
    }

    public static Task<int> WithBackoffAsync<T>(int attempts, Func<Task> action) where T : Exception
    {
        return WithBackoffAsync<T>(attempts, DefaultExponentialMs, action);
    }

    public static Task<int> WithBackoffAsync(int attempts, TimeSpan exponentialBackoff, Func<Task> action)
    {
        return WithBackoffAsync<Exception>(attempts, exponentialBackoff, action);
    }

    public static async Task<int> WithBackoffAsync<T>(int attempts, TimeSpan exponentialBackoff, Func<Task> action) where T : Exception
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

        return attempt;
    }

    //

}

public class TryRetryException(string message, Exception innerException) : OdinSystemException(message, innerException);


