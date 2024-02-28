using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;
#nullable enable

public static class RetryUtil
{
    public static T Retry<T>(Func<T> operation,
        int maxRetryCount,
        TimeSpan delayBetweenRetries,
        out int attempts)
    {
        var retryCount = 0;
        while (true)
        {
            try
            {
                attempts = retryCount + 1;
                return operation();
            }
            catch (Exception e)
            {
                retryCount++;
                if (retryCount >= maxRetryCount)
                {
                    var delay = (long)delayBetweenRetries.TotalMilliseconds;
                    throw new RetryUtilException(
                        $"Failed to execute operation after {maxRetryCount} attempts (delay:{delay}ms)", e);
                }
                
                Thread.Sleep(delayBetweenRetries);
            }
        }
    }

    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetryCount,
        TimeSpan delayBetweenRetries,
        CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException();
            }

            try
            {
                return await operation();
            }
            catch (Exception e)
            {
                retryCount++;
                if (retryCount >= maxRetryCount)
                {
                    var delay = delayBetweenRetries.TotalSeconds;
                    throw new RetryUtilException(
                        $"Failed to execute operation after {maxRetryCount} attempts (delay:{delay}s)", e);
                }

                await Task.Delay(delayBetweenRetries, cancellationToken);
            }
        }
    }
}

//

public class RetryUtilException(string message, Exception innerException) : OdinSystemException(message, innerException);
