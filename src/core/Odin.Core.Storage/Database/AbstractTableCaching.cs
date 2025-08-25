using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;

namespace Odin.Core.Storage.Database;

#nullable enable

public abstract class AbstractTableCaching
{
    private long _hits;
    public long Hits => Interlocked.Read(ref _hits);

    private long _misses;
    public long Misses => Interlocked.Read(ref _misses);

    protected readonly ILevel2Cache Cache;

    private readonly string _tagAllItems;
    private readonly List<string> _tagsAllItems;

    //

    protected AbstractTableCaching(ILevel2Cache cache)
    {
        Cache = cache;
        _tagAllItems = Cache.CacheKeyPrefix + ":" + GetType().Name;
        _tagsAllItems = [_tagAllItems];
    }

    //

    private string BuildCacheKey(string key)
    {
        return GetType().FullName + ":" + key;
    }

    //

    private List<string> BuildTags(List<string> tags)
    {
        if (tags.Count == 0)
        {
            return tags;
        }

        var result = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            result.Add(_tagAllItems + ":" + tag);
        }
        return result;
    }

    //

    private List<string> CombineAllTags(List<string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return _tagsAllItems;
        }

        var result = new List<string>(_tagsAllItems.Count + tags.Count);
        result.AddRange(_tagsAllItems);
        result.AddRange(BuildTags(tags));
        return result;
    }

    //

    private List<string> CombineExplicitTags(List<string> tags)
    {
        if (tags == null || tags.Count == 0)
        {
            throw new OdinSystemException("Tags cannot be null or empty.");
        }

        var result = new List<string>(tags.Count);
        result.AddRange(BuildTags(tags));
        return result;
    }

    //

    protected virtual async ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new OdinSystemException("TTL must be positive.");            
        }

        var hit = true;
        var result = await Cache.GetOrSetAsync(
            BuildCacheKey(key),
            _ =>
            {
                hit = false;
                return factory(cancellationToken);
            },
            ttl,
            CombineAllTags(tags),
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

    protected virtual async ValueTask<TValue> GetOrSetAsync<TValue>(
        byte[] key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return await GetOrSetAsync(key.ToHexString(), factory, ttl, CombineAllTags(tags), cancellationToken);
    }

    //

    protected virtual async ValueTask SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan ttl,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new OdinSystemException("TTL must be positive.");            
        }
        
        await Cache.SetAsync(
            BuildCacheKey(key),
            value,
            ttl,
            CombineAllTags(tags),
            cancellationToken);
    }

    //

    protected virtual async ValueTask SetAsync<TValue>(
        byte[] key,
        TValue value,
        TimeSpan ttl,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        await SetAsync(key.ToHexString(), value, ttl, CombineAllTags(tags), cancellationToken);
    }

    //

    protected virtual async ValueTask RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        await Cache.RemoveAsync(
            BuildCacheKey(key),
            cancellationToken);
    }

    //

    protected virtual async ValueTask RemoveAsync(
        byte[] key,
        CancellationToken cancellationToken = default)
    {
        await RemoveAsync(key.ToHexString(), cancellationToken);
    }

    //

    protected virtual async ValueTask RemoveByTagAsync(
        List<string> tags,
        CancellationToken cancellationToken = default)
    {

        await Cache.RemoveByTagAsync(
            CombineExplicitTags(tags),
            cancellationToken);
    }

    //

    public virtual async ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        await Cache.RemoveByTagAsync(
            _tagsAllItems,
            cancellationToken);
    }

    //

}
