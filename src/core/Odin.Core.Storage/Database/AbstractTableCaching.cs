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
    private readonly ILevel2Cache _cache;

    private readonly List<string> _rootTag;
    private string RootTag { get; }

    protected virtual bool InDatabaseTransaction => _scopedConnectionFactory.HasTransaction;

    //

    protected AbstractTableCaching(
        ILevel2Cache cache,
        IScopedConnectionFactory scopedConnectionFactory,
        string? shareableRootTag = null)
    {
        _cache = cache;
        _scopedConnectionFactory = scopedConnectionFactory;

        if (string.IsNullOrWhiteSpace(shareableRootTag))
        {
            _rootTag = [_cache.CacheKeyPrefix + ":" + GetType().Name];
        }
        else
        {
            _rootTag = [_cache.CacheKeyPrefix + ":" + shareableRootTag];
        }
        RootTag = _rootTag.First();
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
            result.Add(RootTag + ":" + tag);
        }
        return result;
    }

    //

    private List<string> CombineAllTagsWithRoot(List<string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return _rootTag;
        }

        var result = new List<string>(_rootTag.Count + tags.Count);
        result.AddRange(_rootTag);
        result.AddRange(BuildTags(tags));
        return result;
    }

    //

    private List<string> CombineAllTagsWithoutRoot(List<string> tags)
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
        var result = await _cache.GetOrSetAsync(
            BuildCacheKey(key),
            async _ =>
            {
                hit = false;
                return await factory(cancellationToken);
            },
            ttl,
            CombineAllTagsWithRoot(tags),
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
        return await GetOrSetAsync(key.ToHexString(), factory, ttl, CombineAllTagsWithRoot(tags), cancellationToken);
    }

    //

    protected virtual Func<Task> CreateRemoveByKeyAction(string key)
    {
        return () => _cache.RemoveAsync(BuildCacheKey(key)).AsTask();
    }

    //

    protected virtual Func<Task> CreateRemoveByKeyAction(byte[] key)
    {
        return CreateRemoveByKeyAction(key.ToHexString());
    }

    //

    protected Func<Task> CreateRemoveByTagsAction(IEnumerable<string> tags)
    {
        var explicitTags = CombineAllTagsWithoutRoot(tags.ToList());
        return () => _cache.RemoveByTagAsync(explicitTags).AsTask();
    }
    
    //

    public virtual async Task InvalidateAllAsync()
    {
        if (InDatabaseTransaction)
        {
            _scopedConnectionFactory.AddPostCommitAction(async () => await _cache.RemoveByTagAsync(_rootTag));
        }
        else
        {
            await _cache.RemoveByTagAsync(_rootTag);
        }
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

}
