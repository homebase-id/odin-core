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

public interface ITransactionalCacheFactory
{
    public TransactionalCache Create(string keyPrefix, string rootTag);
}

public abstract class AbstractTransactionalCacheFactory(ILevel2Cache cache, IScopedConnectionFactory scopedConnectionFactory)
{
    public TransactionalCache Create(string keyPrefix, string rootTag)
    {
        return new TransactionalCache(cache, scopedConnectionFactory, keyPrefix, rootTag);
    }
}

//

public sealed class TransactionalCache
{
    private long _hits;
    public long Hits => Interlocked.Read(ref _hits);

    private long _misses;
    public long Misses => Interlocked.Read(ref _misses);

    private readonly ILevel2Cache _cache;
    private readonly IScopedConnectionFactory _scopedConnectionFactory;
    private readonly string _keyPrefix;

    private readonly List<string> _rootTag;
    private string RootTag { get; }

    private bool InDatabaseTransaction => _scopedConnectionFactory.HasTransaction;

    //

    public TransactionalCache(
        ILevel2Cache cache,
        IScopedConnectionFactory scopedConnectionFactory,
        string keyPrefix,
        string rootTag)
    {
        ArgumentNullException.ThrowIfNull(cache, nameof(cache));
        ArgumentNullException.ThrowIfNull(scopedConnectionFactory, nameof(scopedConnectionFactory));
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix, nameof(keyPrefix));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootTag, nameof(rootTag));

        _cache = cache;
        _scopedConnectionFactory = scopedConnectionFactory;
        _keyPrefix = keyPrefix;
        _rootTag = [_cache.CacheKeyPrefix + ":" + rootTag];
        RootTag = _rootTag.First();
    }

    //

    private string BuildCacheKey(string key)
    {
        return _keyPrefix + ":" + key;
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

    public async Task<TValue> GetOrSetAsync<TValue>(
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

    public async Task<TValue> GetOrSetAsync<TValue>(
        byte[] key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan ttl,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return await GetOrSetAsync(key.ToHexString(), factory, ttl, CombineAllTagsWithRoot(tags), cancellationToken);
    }

    //

    public Func<Task> CreateRemoveByKeyAction(string key)
    {
        return () => _cache.RemoveAsync(BuildCacheKey(key)).AsTask();
    }

    //

    public Func<Task> CreateRemoveByKeyAction(byte[] key)
    {
        return CreateRemoveByKeyAction(key.ToHexString());
    }

    //

    public Func<Task> CreateRemoveByTagsAction(IEnumerable<string> tags)
    {
        var explicitTags = CombineAllTagsWithoutRoot(tags.ToList());
        return () => _cache.RemoveByTagAsync(explicitTags).AsTask();
    }
    
    //

    public async Task InvalidateAllAsync()
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

    public async Task InvalidateAsync(IEnumerable<Func<Task>> invalidateActions)
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
