using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;

namespace Odin.Core.Storage.Database;

#nullable enable

public abstract class AbstractTableCaching
{
    private long _hits;
    public long Hits => _hits;

    private long _misses;
    public long Misses => _misses;

    protected readonly ILevel1Cache Cache;
    private readonly string[] _tags;

    //

    protected AbstractTableCaching(ILevel1Cache cache)
    {
        Cache = cache;
        _tags = [Cache.CacheKeyPrefix + ":" + GetType().Name];
    }

    //

    protected string GetCacheKey(string key)
    {
        return GetType().FullName + ":" + key;
    }

    //

    protected async ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new OdinSystemException("TTL must be positive.");            
        }

        var hit = true;
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(key),
            _ =>
            {
                hit = false;
                return factory(cancellationToken);
            },
            ttl,
            _tags,
            cancellationToken);

        if (hit)
        {
            Interlocked.Increment(ref _hits);
        }
        else
        {
            Interlocked.Increment(ref _misses);
        }

        return result;
    }

    //

    protected async ValueTask<TValue> GetOrSetAsync<TValue>(
        byte[] key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new OdinSystemException("TTL must be positive.");            
        }
        
        return await GetOrSetAsync(
            key.ToHexString(),
            factory,
            ttl,
            cancellationToken);
    }

    //

    protected async ValueTask SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new OdinSystemException("TTL must be positive.");            
        }
        
        await Cache.SetAsync(
            GetCacheKey(key),
            value,
            ttl,
            _tags,
            cancellationToken);
    }

    //

    protected async ValueTask SetAsync<TValue>(
        byte[] key,
        TValue value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        await SetAsync(
            key.ToHexString(),
            value,
            ttl,
            cancellationToken);
    }

    //

    protected async ValueTask RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Cache.RemoveAsync(
            GetCacheKey(key),
            cancellationToken);
    }

    //

    protected async ValueTask RemoveAsync(
        byte[] key,
        CancellationToken cancellationToken = default)
    {
        await RemoveAsync(
            key.ToHexString(),
            cancellationToken);
    }

    //

    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        await Cache.RemoveByTagAsync(
            _tags,
            cancellationToken);
    }

    //

}
