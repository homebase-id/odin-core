using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database;

#nullable enable

//
// Cache-aside/invalidate-only pattern used below.
// This means that if there is a cache miss, we go to the database,
// and we only update the cache when we have a new/updated item.
// If an item is deleted or updated, we invalidate
// (aka remove) the cache entry.
//

public abstract class AbstractTableCaching
{
    private long _hits;
    public long Hits => Interlocked.Read(ref _hits);

    private long _misses;
    public long Misses => Interlocked.Read(ref _misses);

    private readonly IScopedConnectionFactory _scopedConnectionFactory;
    protected readonly ILevel2Cache Cache;

    private readonly string _tagAllItems;
    private readonly List<string> _tagsAllItems;

    protected virtual bool InDatabaseTransaction => _scopedConnectionFactory.HasTransaction;

    //

    protected AbstractTableCaching(ILevel2Cache cache, IScopedConnectionFactory scopedConnectionFactory)
    {
        Cache = cache;
        _scopedConnectionFactory = scopedConnectionFactory;
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

    protected virtual async Task<TValue> GetOrSetAsync<TValue>(
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

        // Bypass cache if connection is in a transaction
        if (InDatabaseTransaction)
        {
            return await factory(cancellationToken);
        }

        var hit = true;
        var result = await Cache.GetOrSetAsync(
            BuildCacheKey(key),
            async _ =>
            {
                hit = false;
                return await factory(cancellationToken);
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

    protected virtual async Task<TValue> GetOrSetAsync<TValue>(
        byte[] key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return await GetOrSetAsync(key.ToHexString(), factory, ttl, CombineAllTags(tags), cancellationToken);
    }

    //

    public virtual async Task InvalidateAllAsync()
    {
        if (InDatabaseTransaction)
        {
            _scopedConnectionFactory.AddPostCommitAction(async () => await Cache.RemoveByTagAsync(_tagsAllItems));
        }
        else
        {
            await Cache.RemoveByTagAsync(_tagsAllItems);
        }
    }

    //

    protected virtual async Task InvalidateAsync(string key)
    {
        if (InDatabaseTransaction)
        {
            _scopedConnectionFactory.AddPostCommitAction(async () => await Cache.RemoveAsync(BuildCacheKey(key)));
        }
        else
        {
            await Cache.RemoveAsync(BuildCacheKey(key));
        }
    }

    //

    protected virtual async Task InvalidateAsync(byte[] key)
    {
        await InvalidateAsync(key.ToHexString());
    }

    //

    protected virtual async Task InvalidateAsync(IEnumerable<Func<Task>> invalidateActions)
    {
        var actionsArray = invalidateActions.ToArray();

        if (actionsArray.Length == 0)
        {
            return;
        }

        if (InDatabaseTransaction)
        {
            _scopedConnectionFactory.AddPostCommitAction(async () =>
            {
                await Task.WhenAll(actionsArray.Select(a => a()));
            });
        }
        else
        {
            await Task.WhenAll(actionsArray.Select(a => a()));
        }
    }

    //

    protected virtual Task InvalidateAsync(IEnumerable<string> keys)
    {
        var uniqueKeys = new HashSet<string>(keys);

        if (uniqueKeys.Count == 0)
        {
            return Task.CompletedTask;
        }

        var actions = uniqueKeys.Select(key => new Func<Task>(() => Cache.RemoveAsync(BuildCacheKey(key)).AsTask()));

        return InvalidateAsync(actions);
    }

    //

    protected virtual Task InvalidateAsync(IEnumerable<byte[]> keys)
    {
        return InvalidateAsync(keys.Select(k => k.ToHexString()));
    }

    //

    protected virtual Task InvalidateByTagAsync(string tag)
    {
        return InvalidateByTagAsync([tag]);
    }

    //

    protected virtual Task InvalidateByTagAsync(IEnumerable<string> tags)
    {
        var uniqueTags = new HashSet<string>(tags);
        if (uniqueTags.Count == 0)
        {
            return Task.CompletedTask;
        };

        var actions = uniqueTags.Select(tag =>
            new Func<Task>(() => Cache.RemoveByTagAsync(CombineExplicitTags([tag])).AsTask()));

        return InvalidateAsync(actions);
    }

    //

}
