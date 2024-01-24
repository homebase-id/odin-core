using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace Odin.Core.Util;

public static class RetryUtil
{
    public static T Retry<T>(Func<T> operation,
        int maxRetryCount,
        TimeSpan delayBetweenRetries, 
        out int attempts)
    {
        int retryCount = 0;
        while (true)
        {
            try
            {
                attempts = retryCount + 1;
                return operation();
            }
            catch
            {
                retryCount++;
                if (retryCount >= maxRetryCount)
                {
                    throw;
                }
                
                Thread.Sleep(delayBetweenRetries);
            }
        }
    }

    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetryCount,
        TimeSpan delayBetweenRetries)
    {
        int retryCount = 0;
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception)
            {
                retryCount++;
                if (retryCount >= maxRetryCount)
                {
                    throw;
                }

                await Task.Delay(delayBetweenRetries);
            }
        }
    }
}