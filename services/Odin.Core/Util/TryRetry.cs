using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;

public static class TryRetry
{
    private const int DefaultExponentialMs = 100;
    private static readonly Random Random = new();

    //
    // SYNC
    //

    public static void WithDelay(int attempts, int delayMs, Action action)
    {
        WithDelay<Exception>(attempts, (delayMs, delayMs), action);
    }

    public static void WithDelay(int attempts, ValueTuple<int, int> randomDelayMs, Action action)
    {
        WithDelay<Exception>(attempts, randomDelayMs, action);
    }

    public static void WithDelay<T>(int attempts, int delayMs, Action action) where T : Exception
    {
        WithDelay<T>(attempts, (delayMs, delayMs), action);
    }

    public static void WithDelay<T>(int attempts, ValueTuple<int, int> randomDelayMs, Action action) where T : Exception
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
                Thread.Sleep(Random.Next(randomDelayMs.Item1, randomDelayMs.Item2));
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

    public static void WithBackoff(int attempts, int exponentialMs, Action action)
    {
        WithBackoff<Exception>(attempts, exponentialMs, action);
    }

    public static void WithBackoff<T>(int attempts, int exponentialMs, Action action) where T : Exception
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
                var ms = (int)(Math.Pow(2, attempt - 1) * exponentialMs);
                Thread.Sleep(ms);
            }
        }
    }

    //
    // ASYNC
    //

    public static Task WithDelayAsync(int attempts, int delayMs, Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, (delayMs, delayMs), action);
    }

    public static Task WithDelayAsync(int attempts, ValueTuple<int, int> randomDelayMs, Func<Task> action)
    {
        return WithDelayAsync<Exception>(attempts, randomDelayMs, action);
    }

    public static Task WithDelayAsync<T>(int attempts, int delayMs, Func<Task> action) where T : Exception
    {
        return WithDelayAsync<T>(attempts, (delayMs, delayMs), action);
    }

    public static async Task WithDelayAsync<T>(int attempts, ValueTuple<int, int> randomDelayMs, Func<Task> action) where T : Exception
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
                await Task.Delay(Random.Next(randomDelayMs.Item1, randomDelayMs.Item2));
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

    public static Task WithBackoffAsync(int attempts, int exponentialMs, Func<Task> action)
    {
        return WithBackoffAsync<Exception>(attempts, exponentialMs, action);
    }

    public static async Task WithBackoffAsync<T>(int attempts, int exponentialMs, Func<Task> action) where T : Exception
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
                var ms = (int)(Math.Pow(2, attempt - 1) * exponentialMs);
                await Task.Delay(ms);
            }
        }
    }

    //

}

public class TryRetryException(string message, Exception innerException) : OdinSystemException(message, innerException);


