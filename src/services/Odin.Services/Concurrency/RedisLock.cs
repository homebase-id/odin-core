using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using StackExchange.Redis;

namespace Odin.Services.Concurrency;

#nullable enable

// Redlock implementation as described in https://redis.io/docs/latest/develop/use/patterns/distributed-locks/
public sealed class RedisLock(IConnectionMultiplexer connectionMultiplexer) : INodeLock
{
    private const string Prefix = "odin:lock:";
    private static readonly TimeSpan DefaultForcedRelease = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(10);
    private readonly IConnectionMultiplexer _connectionMultiplexer = connectionMultiplexer;

    //

    public async Task<IAsyncDisposable> LockAsync(
        string key,
        TimeSpan? timeout = null,         // Timeout after timespan. Only used for distributed locks.
        TimeSpan? forcedRelease = null,   // Force release lock after timespan. Only used for distributed locks.
        CancellationToken cancellationToken = default)
    {
        timeout ??= DefaultTimeout;
        forcedRelease ??= DefaultForcedRelease;

        var timeoutTime = DateTimeOffset.UtcNow + timeout;
        var value = Guid.NewGuid().ToString();
        var redis = _connectionMultiplexer.GetDatabase();

        key = Prefix + key;

        while (DateTimeOffset.UtcNow < timeoutTime)
        {
            var didLock = await redis.StringSetAsync(key, value, forcedRelease, When.NotExists);
            if (didLock)
            {
                return new Releaser(this, key, value);
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new RedisLockException($"Could not acquire lock '{key}'. Timeout after {timeout?.TotalSeconds}s.");
    }
    
    //
    
    private class Releaser(RedisLock redisLock, string key, string value) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);

            if (_disposed)
            {
                return;
            }

            _disposed = true;

            const string script =
                """
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                else
                    return 0
                end;
                """;

            var redis = redisLock._connectionMultiplexer.GetDatabase();
            await redis.ScriptEvaluateAsync(script, [key], [value]);
        }
    }

    //

}

//

public class RedisLockException(string message) : OdinSystemException(message);
