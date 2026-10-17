using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Odin.Core.Storage.Cache;

#nullable enable

/// <summary>
/// Counts a cache's removes, so a fill can tell whether one happened while it read (#1909): it reads the count
/// before its query and again before and after storing, and keeps nothing if the count moved. Shaped like Redis
/// INCR and GET. Get answers null when the count cannot be read; a fill then keeps nothing.
/// </summary>
public interface IRemovalCounter
{
    void Increment();
    ValueTask IncrementAsync(CancellationToken cancellationToken = default);
    long? Get();
    ValueTask<long?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a count read before is known and still the same
    /// </summary>
    static bool Unchanged(long? before, long? now) => before != null && before == now;
}

/// <summary>
/// Makes a cache wrapper's remove counter: one counter for all nodes when the distributed cache is Redis, since
/// another node's remove must stop this node's fill too (for L1 as well: tag removes travel through the
/// distributed cache); this node's otherwise
/// </summary>
public interface IRemovalCounterFactory
{
    IRemovalCounter Create(string cacheKeyPrefix);
}

//

/// <summary>
/// This node's count, for a cache whose removes all go through this node
/// </summary>
public sealed class InMemoryRemovalCounter : IRemovalCounter
{
    private long _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public ValueTask IncrementAsync(CancellationToken cancellationToken = default)
    {
        Increment();
        return ValueTask.CompletedTask;
    }

    public long? Get() => Interlocked.Read(ref _count);
    public ValueTask<long?> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Get());
}

public sealed class InMemoryRemovalCounterFactory : IRemovalCounterFactory
{
    public IRemovalCounter Create(string cacheKeyPrefix) => new InMemoryRemovalCounter();
}

//

/// <summary>
/// One count in Redis for every node sharing the cache. A Redis error or timeout (a RedisTimeoutException is a
/// TimeoutException, not a RedisException) leaves an increment undone (the remove itself
/// still runs) and makes a read unknown, so a fill keeps nothing rather than an answer it cannot vouch for.
/// </summary>
public sealed class RedisRemovalCounter(IConnectionMultiplexer redis, string key) : IRemovalCounter
{
    private IDatabase Db => redis.GetDatabase();

    public void Increment()
    {
        try
        {
            Db.StringIncrement(key);
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            // The remove goes ahead; FusionCache reaches the other nodes the same way, or not at all
        }
    }

    public async ValueTask IncrementAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.StringIncrementAsync(key);
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            // As above
        }
    }

    public long? Get()
    {
        try
        {
            return (long?)Db.StringGet(key) ?? 0;
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            return null;
        }
    }

    public async ValueTask<long?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (long?)await Db.StringGetAsync(key) ?? 0;
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            return null;
        }
    }
}

public sealed class RedisRemovalCounterFactory(IConnectionMultiplexer redis) : IRemovalCounterFactory
{
    public IRemovalCounter Create(string cacheKeyPrefix) => new RedisRemovalCounter(redis, "odin:cache-removals:" + cacheKeyPrefix);
}
