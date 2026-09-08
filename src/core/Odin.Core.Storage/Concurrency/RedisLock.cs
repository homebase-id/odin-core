using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using StackExchange.Redis;

namespace Odin.Core.Storage.Concurrency;

#nullable enable

// Redlock implementation as described in https://redis.io/docs/latest/develop/use/patterns/distributed-locks/
public sealed class RedisLock(ILogger<RedisLock> logger, IConnectionMultiplexer connectionMultiplexer) : INodeLock
{
    private const string Prefix = "odin:lock:";
    private static readonly TimeSpan DefaultForcedRelease = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(10);

    // A lock held for longer than this is logged as a warning when it is finally released.
    // Nothing is wrong per se - some locks legitimately wrap slow work (ACME orders) - but it
    // is the number you want in the log when everything else is queueing behind it.
    private static readonly TimeSpan LongHoldWarningThreshold = TimeSpan.FromMinutes(1);

    private readonly IConnectionMultiplexer _connectionMultiplexer = connectionMultiplexer;

    //

    public async Task<IAsyncDisposable> LockAsync(
        NodeLockKey key,
        TimeSpan? timeout = null,         // Timeout after timespan. Only used for distributed locks.
        TimeSpan? forcedRelease = null,   // Force release lock after timespan. Only used for distributed locks.
        CancellationToken cancellationToken = default)
    {
        timeout ??= DefaultTimeout;
        forcedRelease ??= DefaultForcedRelease;

        if (timeout <= TimeSpan.Zero  )
        {
            throw new RedisLockException($"{nameof(timeout)} must be greater than zero");
        }

        if (forcedRelease <= TimeSpan.Zero  )
        {
            throw new RedisLockException($"{nameof(forcedRelease)} must be greater than zero");
        }

        if (timeout >= forcedRelease )
        {
            throw new RedisLockException($"{nameof(timeout)} must be less than {nameof(forcedRelease)}");
        }

        var timeoutTime = DateTimeOffset.UtcNow + timeout;
        var value = CreateOwnerToken();
        var redis = _connectionMultiplexer.GetDatabase();

        var redisKey = Prefix + key;
        var sw = Stopwatch.StartNew();

        while (DateTimeOffset.UtcNow < timeoutTime)
        {
            var didLock = await redis.StringSetAsync(redisKey, value, forcedRelease, When.NotExists);
            if (didLock)
            {
                if (sw.ElapsedMilliseconds > 0)
                {
                    logger.LogDebug("Acquired lock {redisKey} after waiting {elapsed}ms", redisKey, sw.ElapsedMilliseconds);
                }
                return new Releaser(this, logger, redisKey, value);
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }

        var (holder, holderTtl) = await DescribeHolderAsync(redis, redisKey);
        throw new RedisLockException(
            $"Could not acquire lock '{redisKey}'. Timeout after {timeout?.TotalSeconds}s. " +
            $"Held by {holder}, expires in {holderTtl}.");
    }

    //

    //
    // The lock value doubles as the owner token: the release script only deletes the key when
    // the value still matches, so it must be unique - but there is no reason for it to be
    // opaque. Making it self-describing means `GET odin:lock:<key>` in redis-cli names the
    // process that is holding things up, which is otherwise very hard to work out.
    //
    private static string CreateOwnerToken()
    {
        return $"{Environment.MachineName}|pid:{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}|{Guid.NewGuid()}";
    }

    //

    private static async Task<(string holder, string ttl)> DescribeHolderAsync(IDatabase redis, string redisKey)
    {
        try
        {
            var holder = await redis.StringGetAsync(redisKey);
            var ttl = await redis.KeyTimeToLiveAsync(redisKey);
            return (
                holder.IsNullOrEmpty ? "nobody (lock was released in the meantime)" : holder.ToString(),
                ttl == null ? "unknown" : $"{(int)ttl.Value.TotalSeconds}s");
        }
        catch (Exception e)
        {
            // Diagnostics must never be the reason a caller fails
            return ($"unknown ({e.Message})", "unknown");
        }
    }

    //

    private class Releaser(RedisLock redisLock, ILogger logger, string key, string value) : IAsyncDisposable
    {
        private readonly Stopwatch _heldFor = Stopwatch.StartNew();
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
            var result = await redis.ScriptEvaluateAsync(script, [key], [value]);

            if ((long)result == 0)
            {
                // We no longer owned the key: it was force-released (forcedRelease elapsed) while
                // we were still working, which means somebody else may have been running
                // concurrently with us.
                logger.LogWarning(
                    "Lock {key} was no longer held by us when released after {heldFor}s. " +
                    "It was probably force-released before the work finished.",
                    key, _heldFor.ElapsedMilliseconds / 1000.0);
            }
            else if (_heldFor.Elapsed > LongHoldWarningThreshold)
            {
                logger.LogWarning("Lock {key} was held for {heldFor}s", key, _heldFor.ElapsedMilliseconds / 1000.0);
            }
            else
            {
                logger.LogDebug("Released lock {key} after {heldFor}s", key, _heldFor.ElapsedMilliseconds / 1000.0);
            }
        }
    }

    //

}

//

public class RedisLockException(string message) : OdinSystemException(message);
